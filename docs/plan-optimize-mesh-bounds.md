# Plan: Optimize Sprite Mesh Bounds for Atlas Packing

## Problem

When sprites have multiple mesh slots (one per unique layer/bone combination), each slot uses the **full sprite bounds** instead of tight bounds around its actual content.

**Example:** A human sprite (931x135 pixels) with an eye path in its own layer:
- Current: Eye mesh occupies 931x135 in atlas
- Desired: Eye mesh occupies ~10x10 in atlas

This causes:
1. **Wasted atlas space** - small elements take up huge regions
2. **Overdraw** - rendering transparent pixels for each quad

## Solution Overview

Add per-slot bounds computation and storage so each mesh slot uses only the space its content requires.

## Files to Modify

| File | Changes |
|------|---------|
| `noz/editor/src/document/SpriteDocument.cs` | Add `GetMeshSlotBounds()`, update `AtlasSize`, modify sprite export |
| `noz/editor/src/document/AtlasDocument.cs` | Modify `ToUV()` and rasterization to use per-slot bounds |
| `noz/engine/src/graphics/Sprite.cs` | Add `Offset`/`Size` fields to `SpriteMesh`, bump version |
| `noz/engine/src/graphics/Graphics.Draw.cs` | Use per-mesh bounds for quad positioning |
| `noz/editor/src/shape/Shape.cs` | Add `GetRasterBoundsFor(layer, bone)` helper |

## Implementation Steps

### Step 1: Add Per-Slot Bounds Computation

**File:** `noz/editor/src/document/SpriteDocument.cs` (after line 105)

Add method after `GetMeshSlots()`:

```csharp
public Dictionary<(byte layer, StringId bone), RectInt> GetMeshSlotBounds()
{
    var result = new Dictionary<(byte layer, StringId bone), RectInt>();
    for (ushort fi = 0; fi < FrameCount; fi++)
    {
        var shape = Frames[fi].Shape;
        foreach (var slot in GetMeshSlots())
        {
            var slotBounds = shape.GetRasterBoundsFor(slot.layer, slot.bone);
            if (result.TryGetValue(slot, out var existing))
                result[slot] = RectInt.Union(existing, slotBounds);
            else
                result[slot] = slotBounds;
        }
    }
    return result;
}
```

### Step 2: Add Filtered Bounds Helper to Shape

**File:** `noz/editor/src/shape/Shape.cs`

Add method to compute bounds for paths matching specific layer/bone:

```csharp
public RectInt GetRasterBoundsFor(byte layer, StringId bone)
{
    // Similar to existing bounds computation but filtered by layer/bone
    // Return bounds of only paths where path.Layer == layer && path.Bone == bone
}
```

### Step 3: Extend SpriteMesh Struct

**File:** `noz/engine/src/graphics/Sprite.cs` (lines 9-14)

```csharp
public readonly struct SpriteMesh(
    Rect uv,
    short order,
    short boneIndex = -1,
    Vector2Int offset = default,   // NEW: Offset from sprite origin
    Vector2Int size = default)     // NEW: Tight bounds size
{
    public readonly Rect UV = uv;
    public readonly short SortOrder = order;
    public readonly short BoneIndex = boneIndex;
    public readonly Vector2Int Offset = offset;
    public readonly Vector2Int Size = size;
}
```

Bump `Version` from 5 to 6. Update `Load()` to read new fields with version check.

### Step 4: Update Atlas UV Computation

**File:** `noz/editor/src/document/AtlasDocument.cs` (lines 177-193)

Modify `ToUV()` to accept per-slot size instead of using full `RasterBounds.Size`:

```csharp
internal Rect ToUV(in AtlasSpriteRect rect, int sortGroupIndex, Vector2Int slotSize)
{
    var ts = (float)EditorApplication.Config.AtlasSize;
    var padding2 = Padding * 2;
    var frameStride = slotSize.X + padding2;  // Use slot size, not full bounds
    // ... rest of computation using slotSize
}
```

### Step 5: Update Atlas Packing Size

**File:** `noz/editor/src/document/SpriteDocument.cs` (lines 109-117)

Modify `AtlasSize` to sum actual slot sizes:

```csharp
public Vector2Int AtlasSize
{
    get
    {
        var padding2 = EditorApplication.Config.AtlasPadding * 2;
        var slotBounds = GetMeshSlotBounds();
        var totalWidth = 0;
        var maxHeight = 0;
        foreach (var (slot, bounds) in slotBounds)
        {
            totalWidth += (bounds.Size.X + padding2) * FrameCount;
            maxHeight = Math.Max(maxHeight, bounds.Size.Y + padding2);
        }
        return new(totalWidth, maxHeight);
    }
}
```

### Step 6: Update Rasterization

**File:** `noz/editor/src/document/AtlasDocument.cs` (UpdateInternal method)

Modify rasterization loop to use per-slot bounds and position each slot correctly.

### Step 7: Update Rendering

**File:** `noz/engine/src/graphics/Graphics.Draw.cs` (lines 109-141)

Use per-mesh offset and size instead of `sprite.Bounds`:

```csharp
foreach (ref readonly var mesh in sprite.Meshes.AsSpan())
{
    // Use per-mesh bounds from offset/size
    var meshBounds = new Rect(
        mesh.Offset.X * sprite.PixelsPerUnitInv,
        mesh.Offset.Y * sprite.PixelsPerUnitInv,
        mesh.Size.X * sprite.PixelsPerUnitInv,
        mesh.Size.Y * sprite.PixelsPerUnitInv
    );
    var p0 = new Vector2(meshBounds.Left, meshBounds.Top);
    // ... use meshBounds for quad corners
}
```

### Step 8: Update Sprite Export

**File:** `noz/editor/src/document/SpriteDocument.cs` (Import/UpdateSprite methods)

Write per-slot offset and size to binary format when exporting.

## Edge Cases

1. **Empty slots**: If a slot has no content in any frame, use minimum 1x1 bounds or skip
2. **Animation frames**: Union bounds across all frames for each slot
3. **Backwards compatibility**: Version check in `Sprite.Load()` - old assets default to offset=(0,0), size=full bounds

## Verification

1. **Visual comparison**: Render sprites before/after, ensure pixel-perfect match
2. **Atlas inspection**: Open atlas texture, verify tighter packing
3. **Rebuild atlases**: Trigger full atlas rebuild after changes
4. **Test animations**: Verify animated sprites with multiple slots play correctly
5. **Test skeletal binding**: Verify bone-bound sprites render correctly

## Expected Results

- Significant reduction in atlas texture usage for sprites with multiple mesh slots
- Reduced overdraw during rendering (smaller quads = fewer transparent pixels)
- No visual difference in rendered output
