# SDF Rendering (Sprites & Fonts)

NoZ uses multi-channel signed distance fields (MSDF) for resolution-independent sprite and font rendering with sharp corners. The implementation is a faithful port of [msdfgen](https://github.com/Chlumsky/msdfgen) by Viktor Chlumsky, located in `noz/editor/src/msdf/`.

Conceptually these are "SDF sprites" and "SDF fonts" — the fact that the underlying algorithm is multi-channel (MSDF) is an implementation detail. The codebase uses `IsSDF` as the flag, `sprite_sdf` / `texture_sdf` as shader names, etc.

## Why MSDF

Single-channel SDF encodes distance in one channel (R only). This produces rounded artifacts at sharp corners because the minimum distance forms a circular iso-contour around vertices.

MSDF solves this by encoding distance across three channels (R, G, B). Each edge is assigned a color channel. At sharp corners, the channels disagree about which edge is closest, and `median(r, g, b)` reconstruction in the shader preserves the sharp boundary.

## Source Files (`noz/editor/src/msdf/`)

| File | Description |
|------|-------------|
| `Msdf.Math.cs` | Vector math, equation solvers (quadratic/cubic), `GetOrthonormal` |
| `Msdf.SignedDistance.cs` | Distance + dot product for closest-edge comparison |
| `Msdf.EdgeColor.cs` | `EdgeColor` flags enum (RED, GREEN, BLUE, CYAN, MAGENTA, YELLOW, WHITE) |
| `Msdf.EdgeSegments.cs` | `LinearSegment`, `QuadraticSegment`, `CubicSegment` with signed distance, scanline intersection, bounds, split |
| `Msdf.Contour.cs` | Closed edge loop with shoelace winding calculation |
| `Msdf.Shape.cs` | Contour collection with validate, normalize, orient contours |
| `Msdf.EdgeColoring.cs` | `EdgeColoring.ColorSimple` — assigns R/G/B to edges at sharp corners |
| `Msdf.ShapeClipper.cs` | Clipper2 boolean operations (union, difference) — flattens curves and merges/carves contours |
| `Msdf.Generator.cs` | `GenerateMSDF` (OverlappingContourCombiner), `DistanceSignCorrection`, `ErrorCorrection` |
| `Msdf.Sprite.cs` | Bridge: converts NoZ sprite paths to msdf shapes via `AppendContour` + draw-order subtract processing, runs generation |
| `Msdf.Font.cs` | Bridge: converts TTF glyph contours to msdf shapes and runs generation |

## Generation Pipeline

### 1. Shape Conversion

**Sprites** (`MsdfSprite.RasterizeMSDF`): Sprite paths are processed in draw order. Each path is converted to a contour via `AppendContour` — a lightweight raw conversion (no Clipper2, no normalize, no edge coloring per path). Winding is normalized to positive for the NonZero fill rule. Add paths accumulate contours on the shape. When a subtract path is encountered, it triggers an immediate `ShapeClipper.Difference` against the accumulated shape. After all paths are processed, one final `ShapeClipper.Union` + `Normalize` + `EdgeColoring.ColorSimple` prepares the shape for generation. This avoids redundant Clipper2 work that previously dominated timing.

**Fonts** (`MsdfFont.FromGlyph`): TTF glyph contours are converted similarly. TTF uses Y-up coordinates; coordinates are kept in native Y-up space and the shape is marked with `inverseYAxis = true` so the generator flips output rows for screen Y-down rendering. This preserves natural contour windings.

### 2. Clipper2 Boolean Operations (`ShapeClipper`)

Before MSDF generation, shapes are passed through Clipper2 to resolve overlapping, self-intersecting, and subtract contours.

**Why this is needed**: TTF fonts often use overlapping contours as a design technique (e.g., a separate contour for the stem and arch of "n" that overlap where they join). Some glyphs have self-intersecting single contours. Sprites use subtract paths to carve holes (e.g., a half-moon cut from a circle). MSDF generation produces artifacts at overlap/intersection boundaries because the distance field is ambiguous there.

**How it works**:
1. **Flatten curves**: All `QuadraticSegment` and `CubicSegment` edges are tessellated to polylines (16 uniform samples per curve). Clipper2 only operates on line segments.
2. **Boolean operation**: `Clipper.BooleanOp(ClipType.Union, FillRule.NonZero)` merges all paths, resolving overlaps and self-intersections into clean non-overlapping outer contours and holes. For sprites with subtract paths, `Clipper.BooleanOp(ClipType.Difference, ...)` carves the subtract regions.
3. **PolyTree traversal**: The `PolyTreeD` output is recursively converted back to `Shape` contours with `LinearSegment` edges.
4. **Winding correction**: All output contours are reversed because Clipper2's winding convention (positive area = CCW in Y-up) is opposite to our MSDF generator's convention (positive winding = CW via shoelace `(b.X-a.X)*(a.Y+b.Y)`).

**Key details**:
- Runs for ALL shapes, including single-contour glyphs (which may self-intersect)
- Only skipped for empty shapes (0 contours)
- Output is always all-linear (no curves) — this is fine for MSDF since `LinearSegment` distance computation is exact
- Uses `ClipperPrecision = 6` (6 decimal places, giving sub-pixel accuracy for glyph coordinates in the 0-2048 range)
- The `FillRule.NonZero` matches TTF's non-zero winding fill rule
- `DefaultStepsPerCurve = 16` provides smooth linearization for curved sprite paths

### 3. Shape Preparation

- **Normalize** (`Shape.Normalize`): Single-edge contours are split into thirds so edge coloring has enough edges to assign distinct colors.
- **Edge coloring** (`EdgeColoring.ColorSimple`): Edges are assigned R/G/B channel colors. At sharp corners (where `dot(dir_a, dir_b) <= 0` or `|cross(dir_a, dir_b)| > sin(3.0)`), adjacent edges get different colors.

Note: `OrientContours` is no longer needed since the Clipper2 union already produces correctly-oriented contours.

### 4. MSDF Generation (`GenerateMSDF`)

Both fonts and sprites use the same generator: `MsdfGenerator.GenerateMSDF` — a faithful port of msdfgen's OverlappingContourCombiner with `MultiDistanceSelector` / `PerpendicularDistanceSelectorBase`.

**How OverlappingContourCombiner works**: Each contour's MSDF is computed independently (per-contour `MultiDistanceSelector`). Then contours are combined based on winding number:
- Positive-winding contours with positive median distance → "inner" selectors (inside outer shape)
- Negative-winding contours with negative median distance → "outer" selectors (inside holes)
- The combiner picks the correct per-contour result based on which winding context dominates at each pixel

This correctly handles **nested contours** (holes from Clipper2 Difference, letter counters like "A", "O") without cross-contour channel interference. The perpendicular distance extension at shared edge endpoints provides precise channel separation.

The `shape.inverseYAxis` flag controls row flipping: the generator writes to `row = flipY ? h-1-y : y`, producing screen Y-down output from TTF Y-up coordinates.

### 5. Error Correction

After generation, both fonts and sprites run two correction passes:

1. **`DistanceSignCorrection`** — scanline-based sign verification using non-zero winding fill rule. For each row, computes scanline intersections with all edges to determine the correct inside/outside state, then flips pixels where the MSDF sign disagrees.
2. **`ErrorCorrection`** — modern artifact detection with corner and edge protection. Protects texels near shape corners and edges, then detects linear and diagonal interpolation artifacts and replaces error-flagged texels with single-channel median. Sub-steps: `ProtectCorners` → `ProtectEdges` → `FindErrors` → `ApplyCorrection`.

### 6. Subtract Path Handling (Sprites Only)

Subtract paths carve holes out of additive paths using Clipper2 boolean difference. Paths are processed in draw order so that subtract paths only affect add paths that precede them:

1. Add paths accumulate raw contours on the shape (no per-path Clipper2)
2. When a subtract path is encountered, `ShapeClipper.Difference(shape, subShape)` immediately carves it from the accumulated shape
3. After all paths: one final `ShapeClipper.Union` + normalize + edge coloring

This inline approach preserves draw ordering — a subtract between two add groups only carves the first group. An add path after a subtract is unaffected by it.

**Mesh slot distribution**: Subtract paths apply to all mesh slots created *before* them in draw order. When `GetMeshSlots` encounters a subtract path, it appends that path's index to every slot that already exists. Slots created after the subtract are unaffected. This means a subtract path between a white shape and a blue shape carves holes in both, but a shape added after the subtract is untouched.

## Coordinate Mapping

The generator maps pixel coordinates to shape-space:
```
shapePos = (pixel + 0.5) / scale - translate
```
Where `scale` and `translate` are provided by the caller. This matches msdfgen's `Projection::unproject()`. The distance range is symmetric around 0 and normalized to [0, 1] in the output.

## Data Flow

```
Sprite paths / TTF glyph contours
  |
  +--- Sprites: AppendContour per path (raw conversion, no Clipper2)
  |      Subtract paths → immediate ShapeClipper.Difference
  |      Final: ShapeClipper.Union → Normalize → EdgeColoring.ColorSimple
  |
  +--- Fonts: FromGlyph → ShapeClipper.Union → Normalize → EdgeColoring.ColorSimple
  |
  +--- Both: GenerateMSDF (OverlappingContourCombiner + PerpendicularDistanceSelector)
  |
  v
DistanceSignCorrection + ErrorCorrection
  |
  v
RGBA8 atlas (R=ch0, G=ch1, B=ch2, A=255)
  |
  v
Binary asset (.sprite / .font) with IsSDF flag
  |
  v
Runtime: sprite_sdf.wgsl / text.wgsl — median(r,g,b) reconstruction
  |
  v
Screen: sharp edges at any scale
```

## Shaders

All SDF content (sprites and fonts) uses MSDF under the hood. There is one set of SDF shaders, not separate SDF/MSDF variants.

### Runtime: `sprite_sdf.wgsl`

Uses `texture_2d_array` (same atlas texture array as normal sprites). Fragment shader:

```wgsl
fn median(r: f32, g: f32, b: f32) -> f32 {
    return max(min(r, g), min(max(r, g), b));
}

let dist = median(msd.r, msd.g, msd.b);
let edgeWidth = 0.7 * length(vec2<f32>(dpdx(dist), dpdy(dist)));
let alpha = smoothstep(0.5 - edgeWidth, 0.5 + edgeWidth, dist);
output = vec4(vertexColor.rgb, alpha * vertexColor.a);
```

The `dpdx`/`dpdy` derivatives adapt antialiasing width to the screen-space pixel density — sharp at high zoom, wider at low zoom.

### Editor: `texture_sdf.wgsl`

Same fragment logic but uses `texture_2d` instead of `texture_2d_array` for the editor's single-atlas rendering path.

### Text: `text.wgsl`

Same median reconstruction for fonts, with additional per-vertex outline support (outline color, width, softness).

## Runtime

### Binary Format

The sprite binary includes an `IsSDF` byte (`0` = normal, `1` = SDF). When `IsSDF` is true, each mesh includes a `FillColor` field (4 bytes RGBA).

Font binary version 6 uses RGBA8 atlas format (4 bytes per pixel, RGB = MSDF channels, A = 255).

### Sprite.IsSDF

A simple `bool` property on `Sprite`. Both legacy single-channel SDF (value 1) and MSDF (value 2) are treated as SDF=true when loading. There is no `SdfMode` enum — the distinction between single-channel and multi-channel is purely internal to the generator.

### Draw Path

In `Graphics.Draw.cs`, sprite draw checks `sprite.IsSDF`:
1. **Shader**: `GetSpriteShader()` returns `_spriteSdfShader` if `IsSDF`, otherwise `_spriteShader`
2. **Texture filter**: SDF requires `Linear` filtering (smooth distance interpolation). Normal sprites use `Point`.
3. **Per-mesh color**: Sets `Graphics.Color` to `mesh.FillColor` before drawing each mesh quad

### Color-Aware Mesh Slots

SDF encodes distance, not color. Each fill color gets its own mesh slot with its own atlas region. A sprite with 5 colors uses 5x the atlas space.

## Edge Coloring Details

`EdgeColoring.ColorSimple` (ported from msdfgen):

- **No corners**: All edges get the same two-channel color (e.g., CYAN)
- **One corner** ("teardrop"): Three color regions assigned via `symmetricalTrichotomy`, with edge splitting if fewer than 3 edges
- **Multiple corners**: Colors switch at each corner using seed-based `switchColor`, with the last color constrained to differ from the initial to avoid wrap-around conflicts

The angle threshold of 3.0 radians (~172 degrees) means any junction sharper than ~172 degrees triggers a color change.

## Clipper2 Winding Convention

Clipper2 and our MSDF generator use opposite winding conventions:

| Convention | Positive area | Outer contour direction (Y-up) |
|-----------|--------------|-------------------------------|
| Clipper2 | `(a.Y+b.Y) * (a.X-b.X)` | CCW |
| Our MSDF (shoelace) | `(b.X-a.X) * (a.Y+b.Y)` | CW |

These are negations of each other. After any Clipper2 operation, all contours must be reversed to match MSDF expectations. This is handled automatically in `ShapeClipper.Union()` and `ShapeClipper.Difference()`.

## Performance

Sprite SDF generation runs every time you leave the sprite editor, so it needs to be fast enough for interactive use.

### Parallelization

All pixel-level passes use `Parallel.For` on rows:

- **`GenerateMSDF`**: Each row's pixels are independent (per-contour selectors are thread-local)
- **`DistanceSignCorrection`**: Sequential (scanline intersection state is cumulative per-row). Uses a pooled intersection list (cleared per row, not reallocated) and binary search for winding lookup on sorted intersections.
- **`ProtectEdges`**: Single combined `Parallel.For` per row — checks horizontal, vertical, and diagonal neighbors in one pass. Writes are `|= STENCIL_PROTECTED` (same bit OR), so cross-row races are benign.
- **`FindErrors`**: Each pixel writes only to its own stencil entry, safe to parallelize.

### Sprite Shape Construction

The main optimization is in `MsdfSprite.RasterizeMSDF`: paths are converted to raw contours via `AppendContour` (no Clipper2 per path), with subtract paths carved inline via `Difference`. Only one final `Union` + `Normalize` + `EdgeColoring.ColorSimple` at the end. This avoids the previous pattern of repeated union/normalize/color per batch, which dominated total time (~21ms of a 32ms total for a 33-path sprite).

### LinearSegment Fast Path

After Clipper2 union, all edges are `LinearSegment`. The `GenerateMSDF` hot loop (edge scanning) was dominated by virtual dispatch overhead — 7 virtual calls per edge per pixel through `EdgeSegment.GetSignedDistance()`, `Point()`, `Direction()`, totaling ~32M virtual dispatches for a typical 142x143 sprite with 225 edges.

The `LinearEdgeData` struct and `PrecomputeLinearEdges()` method pre-compute all per-edge data once before the parallel loop:

- Endpoint coordinates (`p0`, `p1`)
- Direction vector (`ab`) and `invAbLenSq` (replaces per-pixel division with multiplication)
- Orthonormal vector (replaces per-pixel `GetOrthonormal` call with sqrt)
- Normalized direction (replaces per-pixel `Normalize` with sqrt)
- Corner bisectors (`NormalizeAllowZero(prevNd + curNd)`) — eliminates 2 normalizations per edge per pixel

The inner loop then inlines `LinearSegment.GetSignedDistance` and the perpendicular distance extensions using these pre-computed values, with zero virtual dispatch. Falls back to the generic virtual-dispatch path if any edge is not `LinearSegment`.

**Result**: ~10x speedup on edge scanning (2240ms → 240ms CPU time), ~6x on total GenerateMSDF wall time (~110ms → ~19ms). No artifacts — the math is identical, just pre-computed.

**Why spatial culling doesn't work with MSDF**: Both narrow-band (skipping far pixels) and contour AABB culling (skipping far contours) were attempted and produced visible seam artifacts. The OverlappingContourCombiner requires accurate per-channel distances from ALL contours at every pixel for correct channel combination. Skipping any contour creates per-channel discontinuities because R, G, B encode distances to different colored edges. This is a fundamental constraint of multi-channel SDF — single-channel SDF can use spatial culling, but MSDF cannot.

### Editor SDF Preview

The sprite editor renders a live MSDF preview when `IsSDF` is enabled. Per mesh slot (grouped by layer/bone/fillColor), an MSDF is generated into the editor's `_image` buffer and drawn with `texture_sdf.wgsl` + `TextureFilter.Linear` + per-slot fill color from the palette. The non-SDF raster path is unchanged.

## Future Optimizations

Potential further improvements to explore, roughly ordered by expected impact:

1. **Reduce Clipper2 linearization segments** — Currently 16 segments per curve (`DefaultStepsPerCurve`). Adaptive tessellation based on curve curvature or a lower fixed count (e.g., 8) would halve edge count for curved sprites. Fewer edges = proportionally less work in the hot loop. Requires quality validation.

2. **SIMD vectorization of distance computation** — The inner loop computes point-to-segment distance for each edge sequentially. With `System.Runtime.Intrinsics`, 4 edges could be processed simultaneously using AVX2 (4×double). Requires restructuring edge data into SoA (struct-of-arrays) layout.

3. **Per-contour edge grid index** — Instead of checking all edges within a contour, partition edges into a spatial grid. For each pixel, only check edges in nearby cells. This is safe (unlike contour-level culling) because we still evaluate every contour — we just find the closest edge faster within each contour. Benefit scales with edges-per-contour; for contours with 20+ edges, could skip 80%+ of distance computations.

4. **Combine phase optimization** — Currently 5% of time. The `PerpendicularDistanceSelectorBase.ComputeDistance` calls `nearEdge.DistanceToPerpendicularDistance()` via virtual dispatch for the combine phase. Could store pre-computed edge data index instead of edge reference and inline this too.

5. **Bitmap allocation pooling** — `MsdfBitmap` is allocated per `RasterizeMSDF` call. A thread-local pool or reusable bitmap (resized as needed) would reduce GC pressure during rapid editing.

## References

- [msdfgen by Viktor Chlumsky](https://github.com/Chlumsky/msdfgen) — the reference C++ implementation this port is based on
- [Clipper2 by Angus Johnson](https://github.com/AngusJohnson/Clipper2) — polygon boolean operations used for contour merging
- Chlumsky, V. (2015). "Shape Decomposition for Multi-channel Distance Fields" — the original thesis
- [Valve SDF paper](https://steamcdn-a.akamaihd.net/apps/valve/2007/SIGGRAPH2007_AlphaTestedMagnification.pdf) — the original single-channel SDF technique
