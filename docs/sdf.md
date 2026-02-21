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
| `Msdf.ShapeClipper.cs` | Clipper2 boolean union — flattens curves and merges overlapping/self-intersecting contours |
| `Msdf.Generator.cs` | `PerpendicularDistanceSelectorBase`, `MultiDistanceSelector`, `GenerateMSDF` (OverlappingContourCombiner), `GenerateMSDFSimple`, `DistanceSignCorrection`, and `ErrorCorrection` |
| `Msdf.GeneratorRemora.cs` | Bridge: converts our Shape types to CSGL's MSDFGen types and calls their generator |
| `Msdf.Sprite.cs` | Bridge: converts NoZ sprite paths to msdf shapes and runs generation |
| `Msdf.Font.cs` | Bridge: converts TTF glyph contours to msdf shapes and runs generation |
| `csgl/` | Third-party CSGL MSDFGen library (used via `MsdfGeneratorRemora`) |

## Generation Pipeline

### 1. Shape Conversion

**Sprites** (`MsdfSprite.FromSpritePaths`): NoZ sprite paths are converted to `Shape`/`Contour`/`EdgeSegment` objects. Linear segments become `LinearSegment`, quadratic curves become `QuadraticSegment`. Each path becomes one contour. Sprite coordinates are already in screen-space (Y-down).

**Fonts** (`MsdfFont.FromGlyph`): TTF glyph contours are converted similarly. TTF uses Y-up coordinates; coordinates are kept in native Y-up space and the shape is marked with `inverseYAxis = true` so the generator flips output rows for screen Y-down rendering. This preserves natural contour windings.

### 2. Clipper2 Boolean Union (`ShapeClipper.Union`)

Before MSDF generation, all shapes are passed through `ShapeClipper.Union()` which uses the [Clipper2](https://github.com/AngusJohnson/Clipper2) library to resolve overlapping and self-intersecting contours.

**Why this is needed**: TTF fonts often use overlapping contours as a design technique (e.g., a separate contour for the stem and arch of "n" that overlap where they join). Some glyphs have self-intersecting single contours. MSDF generation produces artifacts at overlap/intersection boundaries because the distance field is ambiguous there.

**How it works**:
1. **Flatten curves**: All `QuadraticSegment` and `CubicSegment` edges are tessellated to polylines (8 uniform samples per curve). Clipper2 only operates on line segments.
2. **Boolean union**: `Clipper.BooleanOp(ClipType.Union, FillRule.NonZero)` merges all paths, resolving overlaps and self-intersections into clean non-overlapping outer contours and holes.
3. **PolyTree traversal**: The `PolyTreeD` output is recursively converted back to `Shape` contours with `LinearSegment` edges.
4. **Winding correction**: All output contours are reversed because Clipper2's winding convention (positive area = CCW in Y-up) is opposite to our MSDF generator's convention (positive winding = CW via shoelace `(b.X-a.X)*(a.Y+b.Y)`).

**Key details**:
- Runs for ALL shapes, including single-contour glyphs (which may self-intersect)
- Only skipped for empty shapes (0 contours)
- Output is always all-linear (no curves) — this is fine for MSDF since `LinearSegment` distance computation is exact
- Uses `ClipperPrecision = 6` (6 decimal places, giving sub-pixel accuracy for glyph coordinates in the 0-2048 range)
- The `FillRule.NonZero` matches TTF's non-zero winding fill rule

### 3. Shape Preparation

- **Normalize** (`Shape.Normalize`): Single-edge contours are split into thirds so edge coloring has enough edges to assign distinct colors.
- **Edge coloring** (`EdgeColoring.ColorSimple`): Edges are assigned R/G/B channel colors. At sharp corners (where `dot(dir_a, dir_b) <= 0` or `|cross(dir_a, dir_b)| > sin(3.0)`), adjacent edges get different colors.

Note: `OrientContours` is no longer needed since the Clipper2 union already produces correctly-oriented contours.

### 4. MSDF Generation

Both fonts and sprites use the CSGL MSDFGen library via `MsdfGeneratorRemora`. The bridge converts our `Shape` to CSGL's `MSDFGen.Shape`, calls `MSDFGen.MSDF.GenerateMSDF()`, and copies the result back to our `MsdfBitmap`.

CSGL internally performs its own `Normalize()` and `EdgeColoringSimple()` on the converted shape.

The `shape.InverseYAxis` flag is passed through so CSGL flips output rows for fonts (TTF Y-up to screen Y-down).

### 5. Error Correction

**Fonts only.** After generation, `MsdfGeneratorRemora.CorrectErrors` runs CSGL's pixel clash detection to fix bilinear interpolation artifacts.

Sprites do not currently use error correction.

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

## Data Flow

```
Sprite paths / TTF glyph contours
  |
  v
Shape conversion (FromSpritePaths / FromGlyph)
  |
  v
ShapeClipper.Union (Clipper2 boolean union — resolves overlaps + self-intersections)
  |
  v
Normalize → EdgeColoring.ColorSimple
  |
  v
MsdfGeneratorRemora.GenerateMSDF (via CSGL library)
  |  (fonts only)
  v
MsdfGeneratorRemora.CorrectErrors (pixel clash detection)
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

These are negations of each other. After Clipper2 union, all contours must be reversed to match MSDF expectations. This is handled automatically in `ShapeClipper.Union()`.

## References

- [msdfgen by Viktor Chlumsky](https://github.com/Chlumsky/msdfgen) — the reference C++ implementation this port is based on
- [Clipper2 by Angus Johnson](https://github.com/AngusJohnson/Clipper2) — polygon boolean operations used for contour merging
- Chlumsky, V. (2015). "Shape Decomposition for Multi-channel Distance Fields" — the original thesis
- [Valve SDF paper](https://steamcdn-a.akamaihd.net/apps/valve/2007/SIGGRAPH2007_AlphaTestedMagnification.pdf) — the original single-channel SDF technique
