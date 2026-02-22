# SDF Sprite Stroke + Remove Raster Mode

## Context

Two changes combined:

1. **Add stroke to SDF path**: The sprite editor's SDF mode has no stroke rendering. Stroke needs to work in both the editor mesh preview (tessellated geometry) and the runtime SDF output (shader-based threshold shift). Stroke is generated from the final booleaned shape — a circle with a subtract hole shows stroke on both outer and inner hole edges.

2. **Remove raster sprite mode**: All sprite documents become SDF vector. Raster images will use plain textures (external editor). This removes the `IsSDF` toggle, the `IsAntiAliased` option, the raster rendering path, and `Shape.Rasterize.cs`.

---

## How SDF Stroke Works

The MSDF atlas encodes distance from the shape edge. The current shader thresholds at `0.5` — anything with `dist >= 0.5` is "inside" (fill), anything below is "outside" (transparent). The distance data extends naturally beyond the fill edge into the atlas padding.

Stroke exploits this: by **lowering the threshold**, pixels just outside the fill edge become "inside." This is the stroke band. No extra texture data needed — the existing MSDF already has the distance information.

Each stroked slot emits **two SpriteMesh entries** sharing the same MSDF atlas region:

1. **Stroke mesh** (drawn first, behind): `Color = strokeColor`, `Normal.X = threshold offset`
2. **Fill mesh** (drawn second, on top): `Color = fillColor`, `Normal.X = 0`

The shader change is minimal:
```wgsl
// Before:
let threshold = 0.5;
// After:
let threshold = 0.5 - input.normal.x;
```

`Normal` (`vec2<f32>`, `@location(2)`) is already in the vertex format but never read by any shader. We repurpose `Normal.X` to carry the threshold offset. Zero-cost, no vertex format changes.

---

## Part 1: Remove Raster Sprite Mode

### 1a. Delete `Shape.Rasterize.cs`

**Delete:** `noz/editor/src/shape/Shape.Rasterize.cs` (724 lines)

Contains `Rasterize()`, `RasterizePath()`, `RasterizeStroke()`, `RasterizeScanline()`, `RasterizeSDF()` — all raster-only.

### 1b. Remove `IsAntiAliased`

**File:** `noz/editor/src/document/Document.cs` (line 37)
- Remove `public bool IsAntiAliased { get; set; }`

### 1c. SpriteDocument simplifications

**File:** `noz/editor/src/document/SpriteDocument.cs`

| What | Where | Change |
|------|-------|--------|
| `IsSDF` property | line 99 | Remove — always SDF |
| `antialias` parse | line 406-408 | Remove (silently ignore in Load) |
| `antialias` write | line 609 | Remove from Save |
| `sdf` parse/write | lines 411-412, 610 | Remove |
| `NewFile()` | lines 364-367 | Remove `antialias true` |
| `GetMeshSlots()` | line 143-144 | Remove `IsSDF &&` — always split on fill color |
| `GetMeshSlotBounds()` | lines 179, 195 | Remove `IsSDF ?` — always pass `slot.FillColor` |
| `DrawSprite()` | lines 704, 713, 746, 770 | Always use SDF shader, always set per-mesh color |
| `Rasterize()` | lines 914-935 | Remove raster else branch (BleedColors, ExtrudeEdges) |
| `UpdateSprite()` | lines 1033, 1055, 1060 | Always extract fill color, Linear filter, isSDF=true |
| `Import()` | lines 1088, 1092, 1129 | Always Linear, always SDF=1, always write fill color |
| `Clone()` | line 795 | Remove `IsSDF = src.IsSDF` |

### 1d. SpriteEditor simplifications

**File:** `noz/editor/src/document/SpriteEditor.cs`

- **Remove** AntiAlias button (lines 246-258)
- **Remove** SDF toggle button (lines 260-270)
- **Remove** `_image` PixelData (lines 43-44) and `_rasterTexture` Texture (lines 46, 55-58)
- **Remove** their Dispose calls (lines 157-158)
- **`UpdateRaster()`** (lines 670-696): Remove if/else — always call `UpdateMeshSDF(shape)`
- **`DrawRaster()`** (lines 1625-1636): Remove raster else branch. Just call `DrawMeshSDF()`.

### 1e. Runtime compatibility

**Keep** `IsSDF` flag and conditional loading in `Sprite.cs` and `Graphics.Draw.cs` — runtime must still handle legacy non-SDF sprites. The editor just always produces SDF now.

### 1f. PixelData cleanup

**File:** `noz/editor/src/utils/PixelData.cs`

Remove `BleedColors()` if no other callers (only used in raster atlas path).

---

## Part 2: Add SDF Stroke Support

### 2a. Shader: Threshold offset via `Normal.X`

**Files:** `noz/engine/assets/shader/sprite_sdf.wgsl`, `noz/engine/assets/shader/texture_sdf.wgsl`

- Add `normal: vec2<f32>` to `VertexOutput`
- Pass `output.normal = input.normal` in `vs_main`
- Change `let threshold = 0.5;` → `let threshold = 0.5 - input.normal.x;`

### 2b. SpriteMesh: Add `StrokeThreshold`

**File:** `noz/engine/src/graphics/Sprite.cs`

Add `float StrokeThreshold = 0f` field to `SpriteMesh`. Written into `MeshVertex.Normal.X` at draw time.

### 2c. Draw pipeline: Set `Normal.X`

**File:** `noz/engine/src/graphics/Graphics.Draw.cs`

In all `Draw(Sprite ...)` overloads, set `Normal = new Vector2(mesh.StrokeThreshold, 0)` on each vertex.

### 2d. MeshSlot: Track stroke

**File:** `noz/editor/src/document/SpriteDocument.cs`

Add to `MeshSlot`:
```csharp
public Color32 StrokeColor;
public byte StrokeWidth;
public bool HasStroke => StrokeColor.A > 0 && StrokeWidth > 0;
```

Copy stroke info from first non-subtract path when building slots.

### 2e. Dual mesh emission

**File:** `noz/editor/src/document/SpriteDocument.cs` — `UpdateSprite()`

For each slot with stroke, emit two `SpriteMesh` entries (same UV/bounds):

1. Stroke mesh: `FillColor = strokeColor`, `StrokeThreshold = computed`
2. Fill mesh: `FillColor = fillColor`, `StrokeThreshold = 0`

Threshold: `strokePixels / (msdfRange * 2.0)`
where `strokePixels = StrokeWidth * Shape.StrokeScale * dpi`

### 2f. MSDF range expansion

**File:** `noz/editor/src/document/SpriteDocument.cs` — `ISpriteSource.Rasterize()`

Expand range for stroked slots:
```csharp
var msdfRange = 1.5f;
if (slot.HasStroke)
    msdfRange = MathF.Max(msdfRange, slot.StrokeWidth * Shape.StrokeScale * dpi + 0.5f);
```

### 2g. Binary format

**Files:** `SpriteDocument.Import()`, `Sprite.Load()`

Bump `Sprite.Version` 8 → 9. Write/read `float strokeThreshold` per SDF mesh after fill RGBA.

### 2h. Editor mesh preview: Stroke band

**File:** `noz/editor/src/document/SpriteEditor.Mesh.cs`

For stroked slots, after tessellating fill:

1. Extract `PathsD` from booleaned shape (make `ShapeClipper.ShapeToPaths` internal)
2. `Clipper.InflatePaths(paths, halfStrokeWidth, JoinType.Round, EndType.Polygon)`
3. `Clipper.BooleanOp(ClipType.Difference, expanded, original)` → stroke band
4. Tessellate with LibTessDotNet, draw with stroke color behind fill

Stroke comes from `BuildShape()` output (post-boolean), so holes get stroked on inner edges.

**File:** `noz/editor/src/MSDF/Msdf.ShapeClipper.cs` — Make `ShapeToPaths` internal.

### 2i. DrawSprite stroke support

**File:** `noz/editor/src/document/SpriteDocument.cs`

`DrawSprite()` uses `Graphics.Draw(Rect, Rect)` which doesn't expose Normal. Add a `Graphics.StrokeThreshold` state property that `AddQuad` writes into `Normal.X`. Set before drawing stroke mesh, reset to 0 after.

---

## Files Summary

| File | Change |
|------|--------|
| `noz/editor/src/shape/Shape.Rasterize.cs` | **DELETE** |
| `noz/editor/src/document/Document.cs` | Remove `IsAntiAliased` |
| `noz/editor/src/document/SpriteDocument.cs` | Remove raster/IsSDF; add stroke to MeshSlot, dual mesh, range expansion |
| `noz/editor/src/document/SpriteEditor.cs` | Remove raster rendering, AA/SDF buttons, _image/_rasterTexture |
| `noz/editor/src/document/SpriteEditor.Mesh.cs` | Add stroke band tessellation |
| `noz/editor/src/MSDF/Msdf.ShapeClipper.cs` | Make `ShapeToPaths` internal |
| `noz/editor/src/utils/PixelData.cs` | Remove `BleedColors()` |
| `noz/engine/assets/shader/sprite_sdf.wgsl` | Pass normal to frag, threshold offset |
| `noz/engine/assets/shader/texture_sdf.wgsl` | Same |
| `noz/engine/src/graphics/Sprite.cs` | Add `StrokeThreshold`, version 8→9 |
| `noz/engine/src/graphics/Graphics.Draw.cs` | Set `Normal.X` from `StrokeThreshold` |

## Verification

1. Open any sprite — renders in SDF mode, no toggle needed
2. No AA or SDF buttons in toolbar
3. Add stroke to a path — editor preview shows stroke band around fill
4. Circle + subtract hole with stroke — stroke on both edges
5. Close/reopen sprite — MSDF atlas includes stroke
6. Runtime rendering shows stroke with correct color/width
7. Non-stroked sprites render identically
8. Build succeeds with no references to deleted raster code
