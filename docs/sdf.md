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
| `Msdf.Generator.cs` | `PerpendicularDistanceSelectorBase`, `MultiDistanceSelector`, `GenerateMSDF` (OverlappingContourCombiner), `GenerateMSDFSimple`, `DistanceSignCorrection`, and `ErrorCorrection` |
| `Msdf.Sprite.cs` | Bridge: converts NoZ sprite paths to msdf shapes and runs generation |
| `Msdf.Font.cs` | Bridge: converts TTF glyph contours to msdf shapes and runs generation |

## Generation Pipeline

### 1. Shape Conversion

**Sprites** (`MsdfSprite.FromSpritePaths`): NoZ sprite paths are converted to `Shape`/`Contour`/`EdgeSegment` objects. Linear segments become `LinearSegment`, quadratic curves become `QuadraticSegment`. Each path becomes one contour. Sprite coordinates are already in screen-space (Y-down).

**Fonts** (`MsdfFont.FromGlyph`): TTF glyph contours are converted similarly. TTF uses Y-up coordinates, so Y values are negated during conversion (`flipY`) to produce screen-space Y-down coordinates. This ensures correct winding direction for the MSDF generator without needing `shape.inverseYAxis`.

### 2. Shape Preparation

- **Normalize** (`Shape.Normalize`): Single-edge contours are split into thirds so edge coloring has enough edges to assign distinct colors.
- **Orient contours** (`Shape.OrientContours`): Ensures all outer contours have consistent winding direction using scanline intersection analysis. Used for fonts; sprites skip this since the OverlappingContourCombiner handles winding natively.
- **Edge coloring** (`EdgeColoring.ColorSimple`): Edges are assigned R/G/B channel colors. At sharp corners (where `dot(dir_a, dir_b) <= 0` or `|cross(dir_a, dir_b)| > sin(3.0)`), adjacent edges get different colors.

### 3. MSDF Generation

Two generators are available:

- **`GenerateMSDFSimple`**: Simple nearest-edge-per-channel approach. Iterates all edges across all contours and picks the closest per channel. Suitable when contour winding is correct (after `OrientContours`). Used for **fonts**.

- **`GenerateMSDF`**: Uses the **OverlappingContourCombiner** algorithm. Computes per-contour distances separately, then classifies contours as inner/outer based on winding direction and resolves which contour "owns" each pixel. Has an `invertWinding` parameter for cases where Y-negation reverses computed windings. Used for **sprites**.

Both generators use the full `PerpendicularDistanceSelectorBase` / `MultiDistanceSelector` system ported from msdfgen, which tracks three distances per channel:
- `minTrueDistance` — the closest edge by signed distance
- `minNegativePerpendicularDistance` — closest negative perpendicular from any edge endpoint
- `minPositivePerpendicularDistance` — closest positive perpendicular from any edge endpoint

Edge iteration uses prev/next edge context for perpendicular extension at edge junctions (bisector directions).

### 4. Scanline Sign Correction (`MsdfGenerator.DistanceSignCorrection`)

**Fonts only.** After MSDF generation, a scanline-based sign correction pass fixes distance signs for overlapping contours. For each pixel, scanline intersection determines the correct fill state (non-zero winding rule). If the MSDF median disagrees with the fill state, all three channels are flipped (`1.0 - value`). An ambiguity resolution pass uses neighbor voting for pixels exactly at the 0.5 boundary.

This matches msdfgen's `distanceSignCorrection` / `multiDistanceSignCorrection` in `rasterization.cpp`.

### 5. Error Correction (`MsdfGenerator.ErrorCorrection`)

**Fonts only.** After sign correction, a legacy error correction pass detects "clashing" texels where bilinear interpolation between adjacent pixels would produce incorrect median values. These texels are converted to single-channel (all RGB set to median), which eliminates interpolation artifacts at the cost of losing sharp corners at those specific texels.

Sprites do not currently use error correction or sign correction.

### 6. Subtract Path Handling (Sprites Only)

Subtract paths are handled by generating a separate MSDF and compositing:
1. Additive paths produce an MSDF where inside > 0.5
2. Subtract paths produce their own MSDF
3. The subtract MSDF is inverted (`1 - value`) so its inside becomes outside
4. The two are intersected per-channel via `min(add, inverted_sub)`

## Coordinate Mapping

The generator maps pixel coordinates to shape-space:
```
shapePos = (pixel + 0.5) / scale - translate
```
Where `scale` and `translate` are provided by the caller. This matches msdfgen's `Projection::unproject()`. The distance range is symmetric around 0 and normalized to [0, 1] in the output.

## msdfgen Pipeline Comparison

msdfgen has two main modes for handling overlapping contours:

| Mode | OrientContours | Generator | Scanline Pass | Error Correction |
|------|---------------|-----------|---------------|-----------------|
| **NO_PREPROCESS** (default without Skia) | No | OverlappingContourCombiner | Yes (`distanceSignCorrection`) | After sign correction |
| **WINDING_PREPROCESS** | Yes | SimpleContourCombiner (`overlapSupport=false`) | No | After generation |
| **FULL_PREPROCESS** (Skia) | Yes (via `resolveShapeGeometry`) | SimpleContourCombiner | No | After generation |

**Key insight**: `OrientContours` and the `OverlappingContourCombiner` are **mutually exclusive** strategies in msdfgen. The combiner relies on natural winding to classify inner/outer contours; `OrientContours` rewrites windings which breaks that classification.

Our pipelines:

| Asset | OrientContours | Generator | Scanline Pass | Error Correction |
|-------|---------------|-----------|---------------|-----------------|
| **Fonts** | Yes | `GenerateMSDFSimple` | Yes | Yes |
| **Sprites** | No | `GenerateMSDF` (OverlappingContourCombiner) | No | No |

The font pipeline is a hybrid of WINDING_PREPROCESS (orient + simple combiner) with the scanline pass from NO_PREPROCESS (to fix overlapping contours that `OrientContours` can't fully resolve). The sprite pipeline matches NO_PREPROCESS minus the scanline pass, since sprites typically have well-formed non-overlapping paths.

## msdfgen Code Correspondence

The C# port was verified against the C++ reference. Key correspondences:

| msdfgen C++ | NoZ C# |
|-------------|--------|
| `Vector2` (`core/Vector2.hpp`) | `Vector2Double` (`engine/src/math/Vector2Double.cs`) — same semantics, component-wise operators |
| `MultiDistanceSelector` (`core/edge-selectors.h`) | `MultiDistanceSelector` (`Msdf.Generator.cs`) |
| `PerpendicularDistanceSelectorBase` (`core/edge-selectors.h`) | `PerpendicularDistanceSelectorBase` (`Msdf.Generator.cs`) |
| `OverlappingContourCombiner` (`core/contour-combiners.cpp`) | Inline in `GenerateMSDF` (`Msdf.Generator.cs`) |
| `generateDistanceField` (`core/msdfgen.cpp`) | `GenerateMSDF` / `GenerateMSDFSimple` (`Msdf.Generator.cs`) |
| `multiDistanceSignCorrection` (`core/rasterization.cpp`) | `DistanceSignCorrection` (`Msdf.Generator.cs`) |
| `msdfErrorCorrection` (`core/MSDFErrorCorrection.cpp`) | `ErrorCorrection` (`Msdf.Generator.cs`) — legacy clash detection only |
| `Projection::unproject` (`core/Projection.cpp`) | Inline: `(pixel + 0.5) / scale - translate` |

**Minor difference**: The C# `QuadraticSegment` constructor has a degenerate control point check (pushes collinear control point to midpoint) that the C++ constructor lacks. In msdfgen, the factory method `EdgeSegment::create` handles this by returning a `LinearSegment` instead. This is cosmetic — our `FromGlyph` creates segments directly, so the constructor guard is extra safety.

## Known Issue: Overlapping Contour Artifacts

**Status**: Glyphs with overlapping contours (e.g. "A" in some fonts) show visible seam artifacts at overlap boundaries. The current pipeline (OrientContours + GenerateMSDFSimple + DistanceSignCorrection) handles most cases correctly but the transition at overlap boundaries is not perfectly smooth.

**Root cause**: The simple combiner computes distances relative to the nearest edge across ALL contours. In overlap regions, the nearest edge may belong to a different contour than the one that "owns" the pixel. When the scanline pass flips the sign (`1.0 - value`), the flipped distance doesn't smoothly transition to the non-flipped distance in the adjacent pixel, creating a visible seam.

**Approaches tried**:
1. `GenerateMSDF` (OverlappingContourCombiner) without OrientContours — inverted glyphs because Y-negation reverses all windings and the combiner sees everything as "outside"
2. `GenerateMSDF` with `invertWinding=true` + scanline — staircase/grid artifacts at overlap boundaries (scanline fights the combiner)
3. `GenerateMSDF` with `invertWinding=true`, no scanline — hollow/patchy glyphs (combiner alone insufficient)
4. Current: `GenerateMSDFSimple` + OrientContours + scanline — best results so far, minor seam artifacts remain

**Potential next steps**:
- Try `shape.inverseYAxis = true` instead of Y negation in `FromGlyph`, with `GenerateMSDF` (no OrientContours, no invertWinding). This would let the combiner see correct natural windings and use msdfgen's `reorient` logic for row flipping. Requires adjusting the translate coordinates computed by the caller (which currently assume Y-negated space).
- Compare byte-for-byte output of the OverlappingContourCombiner against msdfgen for a simple test glyph to isolate where the distance values diverge.

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

## Data Flow

```
Sprite paths / TTF glyph contours
  |
  v
Shape conversion (FromSpritePaths / FromGlyph)
  |
  v
Normalize → OrientContours (fonts only) → EdgeColoring.ColorSimple
  |
  v
GenerateMSDFSimple (fonts) / GenerateMSDF (sprites)
  |  (fonts only)
  v
DistanceSignCorrection (scanline-based sign fix for overlapping contours)
  |  (fonts only)
  v
ErrorCorrection (legacy clash detection)
  |  (sprites only)
  v
Subtract compositing (invert + min)
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

## References

- [msdfgen by Viktor Chlumsky](https://github.com/Chlumsky/msdfgen) — the reference C++ implementation this port is based on
- Chlumsky, V. (2015). "Shape Decomposition for Multi-channel Distance Fields" — the original thesis
- [Valve SDF paper](https://steamcdn-a.akamaihd.net/apps/valve/2007/SIGGRAPH2007_AlphaTestedMagnification.pdf) — the original single-channel SDF technique
