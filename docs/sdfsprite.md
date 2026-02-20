# SDF Sprite Rendering

Signed Distance Field (SDF) sprites store distance-to-edge per pixel instead of raw color. This enables resolution-independent crisp edges at any scale — the same atlas pixel data renders sharp whether the sprite is drawn at 50% or 400% zoom.

## How It Works

### Editor: Rasterization

When a sprite has SDF enabled, the editor rasterizes each shape into a distance field instead of colored pixels.

For each pixel in the atlas region, `Shape.RasterizeSDF()` computes the signed distance from the pixel center to the nearest shape edge in world (shape) space. The result is normalized to `[0, 1]` and stored in the **R channel only** of the atlas texture:

- `0.5` = exactly on the edge
- `> 0.5` = inside the shape
- `< 0.5` = outside the shape

The normalization uses a configurable `range` (default 1.5 pixels):

```
sdf = clamp(signedPixelDist / (range * 2) + 0.5, 0, 1)
```

A smaller range gives sharper edges but less room for antialiasing at extreme scales. The current value of 1.5 provides a good balance.

### Per-Path Union

When a mesh slot contains multiple paths (shapes with the same fill color), each path's signed distance is computed independently. Results are combined using CSG-style operations:

- **Non-subtract paths**: Union via `max(dist_A, dist_B, ...)` — inside either shape counts as inside
- **Subtract paths**: Carved out via `min(base, -subtract_dist)` — subtracts interior

This prevents nearby shapes from interfering with each other's distance fields. Without per-path evaluation, a pixel deep inside shape A would get a small distance value just because shape B's edge is nearby, causing rendering artifacts.

### Color-Aware Mesh Slots

Since SDF encodes only distance (not color), each fill color needs its own mesh slot. When `IsSDF` is true, `GetMeshSlots()` breaks on fill color boundaries in addition to layer and bone boundaries. Each mesh slot gets its own atlas region with its own SDF.

The fill color (resolved from the palette at import time) is baked into the sprite binary as a per-mesh `FillColor` field. At draw time, this color is set as vertex color before rendering each mesh quad.

### Atlas Padding

SDF sprites rasterize into the full outer rect (including padding area) so the distance field extends naturally beyond the shape boundary. Normal sprites use `BleedColors` and `ExtrudeEdges` for padding — these are skipped for SDF since they would corrupt the distance values.

## Binary Format

Sprite binary version 8 adds:

| Field | Size | Description |
|-------|------|-------------|
| IsSDF | 1 byte | `0` = normal, `1` = SDF |
| FillColor | 4 bytes per mesh (SDF only) | RGBA fill color |

The IsSDF byte is written after the frame rate field in the sprite header. FillColor bytes are appended after each mesh's size fields, only when IsSDF is true.

`TextureDocument.ImportAsSprite()` always writes `IsSDF = 0` since texture-sourced sprites are never SDF.

## Shaders

Two SDF-specific WGSL shaders:

### Runtime: `sprite_sdf.wgsl`

Uses `texture_2d_array` (same atlas texture array as normal sprites). The fragment shader:

1. Samples the R channel as the distance value
2. Computes adaptive edge width from screen-space derivatives (`dpdx`/`dpdy`)
3. Applies `smoothstep` around threshold 0.5 to produce an alpha mask
4. Colors from vertex color (per-mesh `FillColor`)

```
dist = sample R channel
edgeWidth = 0.7 * length(vec2(dpdx(dist), dpdy(dist)))
alpha = smoothstep(0.5 - edgeWidth, 0.5 + edgeWidth, dist)
output = vec4(vertexColor.rgb, alpha * vertexColor.a)
```

The `dpdx`/`dpdy` derivatives automatically adapt the antialiasing width based on how much the distance field changes per screen pixel. This is what makes SDF scale-independent — at high zoom the derivatives are small (sharp edges), at low zoom they're larger (wider antialiasing).

### Editor: `texture_sdf.wgsl`

Same fragment logic but uses `texture_2d` instead of `texture_2d_array` for the editor's single-atlas rendering path.

## Runtime Draw Path

In `Graphics.Draw.cs`, all sprite draw overloads check `sprite.IsSDF`:

1. **Shader selection**: Uses `_spriteSdfShader` instead of `_spriteShader`
2. **Texture filter**: SDF requires `Linear` filtering (smooth interpolation of distance values). Normal sprites use `Point`.
3. **Per-mesh color**: Sets `Graphics.Color` to `mesh.FillColor` before drawing each mesh quad

## Editor UI

The SDF toggle is a toolbar button in `SpriteEditor.cs`. Toggling it marks the raster as dirty, triggering re-rasterization of the atlas with SDF encoding.

## Data Flow

```
Vector paths (editor)
  |
  v
RasterizeSDF() -- per-path signed distance, union/subtract
  |
  v
Atlas R channel [0-255], per mesh slot
  |
  v
Sprite binary (.sprite file) -- IsSDF flag + FillColor per mesh
  |
  v
Sprite.Load() -- reads IsSDF, per-mesh FillColor
  |
  v
Graphics.Draw(Sprite) -- selects SDF shader, sets vertex color
  |
  v
sprite_sdf.wgsl -- smoothstep on R channel, vertex color
  |
  v
Screen: antialiased edges at any scale
```

## Limitations

- **Single-channel SDF loses sharp corners**: At concave vertices, the distance field produces a rounded notch because the minimum distance to any edge endpoint is shorter than the perpendicular distance to either adjacent edge. This is inherent to single-channel SDF and also visible in SDF font rendering. See `msdfsprite.md` for how multi-channel SDF addresses this.

- **One color per mesh slot**: SDF can only render one fill color per region. Multi-color sprites are split into separate mesh slots, each with its own SDF atlas region.

- **Atlas space**: Each color region gets its own atlas rectangle. A sprite with 5 colors uses 5x the atlas space compared to a single normal rasterization.
