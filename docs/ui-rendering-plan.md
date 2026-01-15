# UI Rendering System Plan

## Problem

Border radius isn't rendering because:
1. `DrawContainer` in UI.cs calls `Render.DrawQuad()` without setting shader or uniforms
2. The UI shader requires per-rect uniforms (`u_box_size`, `u_border_radius`, etc.)
3. Using uniforms breaks batching - every rounded rect needs different values
4. Current vertex format (`MeshVertex`) is hardcoded throughout the render system

## Solution Overview

1. **Add vertex format abstraction** to `IRenderDriver` for multiple vertex types
2. **Create dedicated UI rendering** in UI.cs with its own batching (separate from `Render.cs`)
3. **Bounds-based batch ordering** to minimize draw calls while maintaining painter's algorithm

---

## Part 1: Vertex Format Abstraction

### New Types

```csharp
public enum VertexAttribType { Float, Int, UByte }

public struct VertexAttribute
{
    public int Location;
    public int Components;      // 1-4
    public VertexAttribType Type;
    public int Offset;
    public bool Normalized;     // for UByte -> float conversion
}
```

### IRenderDriver Changes

```csharp
// New methods
nuint CreateVertexFormat(VertexAttribute[] attribs, int stride);
void DestroyVertexFormat(nuint handle);
void BindVertexFormat(nuint format);

// Modified - now takes raw bytes
void UpdateVertexBuffer(nuint buffer, int offsetBytes, ReadOnlySpan<byte> data);
```

### OpenGLRender Implementation

- `CreateVertexFormat` creates a VAO with the specified attribute layout
- `BindVertexFormat` binds the VAO
- Store VAO handle in a dictionary keyed by format handle
- Existing `MeshVertex` path continues to work (create format at init)

---

## Part 2: UI Vertex Formats

### UIVertex (32 bytes) - Containers/Shapes

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct UIVertex
{
    public Vector2 Position;    // 8 bytes  - location 0
    public Vector2 UV;          // 8 bytes  - location 1 (SDF coords, -1 to 1)
    public Color32 Color;       // 4 bytes  - location 2 (fill color)
    public float BorderRatio;   // 4 bytes  - location 3 (border_width / border_radius, <0 = no SDF)
    public Color32 BorderColor; // 4 bytes  - location 4 (RGB, A unused)
    public float Padding;       // 4 bytes  - alignment
}
```

### SpriteVertex (48 bytes) - Sprites with Animation

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct SpriteVertex
{
    public Vector2 Position;    // 8 bytes
    public Vector2 UV;          // 8 bytes
    public Color32 Color;       // 4 bytes
    public int Atlas;           // 4 bytes
    public int FrameCount;      // 4 bytes
    public float FrameWidth;    // 4 bytes
    public float FrameRate;     // 4 bytes
    public float AnimStartTime; // 4 bytes
    public float Padding0;      // 4 bytes
    public float Padding1;      // 4 bytes
}
```

### TextVertex

Text can likely reuse `UIVertex` or a simpler format - TBD based on text rendering needs.

---

## Part 3: UI Batching System

### Design

UI uses **bounds-based batch ordering** instead of Render.cs's sort-key system:

- Three batch types: UI (containers), Sprite, Text
- Track union bounds per batch type
- Flush when new element overlaps a higher-order batch
- Sequential painter's algorithm within batches

### Data Structures

```csharp
const int BATCH_UI = 0;
const int BATCH_SPRITE = 1;
const int BATCH_TEXT = 2;
const int BATCH_COUNT = 3;

struct BatchInfo
{
    public int Order;           // -1 = inactive, else position in render order
    public Rect Bounds;         // union of all elements in batch
    public int StartVertex;
    public int VertexCount;
    public int StartIndex;
    public int IndexCount;
}

// State
BatchInfo[] _batches = new BatchInfo[BATCH_COUNT];
int[] _batchOrder = new int[BATCH_COUNT];  // render order -> batch type
int _activeBatchCount;
```

### Algorithm

```csharp
void AddToBatch(int batchType, Rect bounds)
{
    ref var batch = ref _batches[batchType];

    // Check overlap with batches that render AFTER this one
    if (batch.Order >= 0)
    {
        for (int i = batch.Order + 1; i < _activeBatchCount; i++)
        {
            int otherType = _batchOrder[i];
            if (Overlaps(bounds, _batches[otherType].Bounds))
            {
                FlushAll();
                break;
            }
        }
    }

    // Activate batch if not active
    if (batch.Order < 0)
    {
        batch.Order = _activeBatchCount;
        _batchOrder[_activeBatchCount] = batchType;
        _activeBatchCount++;
        batch.Bounds = bounds;
        batch.StartVertex = _vertexCount;
        batch.StartIndex = _indexCount;
    }
    else
    {
        batch.Bounds = Union(batch.Bounds, bounds);
    }

    // Add vertices to appropriate buffer...
}

void FlushAll()
{
    for (int i = 0; i < _activeBatchCount; i++)
    {
        int type = _batchOrder[i];
        DrawBatch(type);
        _batches[type].Order = -1;
    }
    _activeBatchCount = 0;
}
```

### Batch Break Points (Hard Flush)

- Scene elements - switch to scene camera/Render.cs
- Scrollable/Clip start - stencil write
- Scrollable/Clip end - stencil restore
- End of UI frame

---

## Part 4: UI Shader

Port the original C++ squircle SDF shader to work with per-vertex data:

```glsl
#version 450

//@ VERTEX
layout(location = 0) in vec2 in_position;
layout(location = 1) in vec2 in_uv;
layout(location = 2) in vec4 in_color;
layout(location = 3) in float in_border_ratio;
layout(location = 4) in vec4 in_border_color;

uniform mat4 u_projection;

out vec2 v_uv;
out vec4 v_color;
flat out float v_border_ratio;
flat out vec3 v_border_color;

void main() {
    gl_Position = u_projection * vec4(in_position, 0.0, 1.0);
    v_uv = in_uv;
    v_color = in_color;
    v_border_ratio = in_border_ratio;
    v_border_color = in_border_color.rgb;
}

//@ END

//@ FRAGMENT
in vec2 v_uv;
in vec4 v_color;
flat in float v_border_ratio;
flat in vec3 v_border_color;

out vec4 f_color;

void main() {
    // Skip SDF for simple rectangles (border_ratio < 0)
    if (v_border_ratio < 0.0) {
        f_color = v_color;
        return;
    }

    // Squircle SDF (n=4 superellipse)
    float n = 4.0;
    float dist = pow(pow(abs(v_uv.x), n) + pow(abs(v_uv.y), n), 1.0 / n);
    float edge = fwidth(dist);

    // Border
    float border = (1.0 + edge) - v_border_ratio;
    vec4 color = v_color;
    color.rgb *= color.a;  // premultiply

    vec4 border_color = vec4(v_border_color, 1.0);
    border_color.rgb *= border_color.a;

    color = mix(color, border_color, smoothstep(border - edge, border, dist));

    // Edge falloff
    float alpha = 1.0 - smoothstep(1.0 - edge, 1.0, dist);
    if (alpha < 0.001) discard;

    f_color = color * alpha;
}

//@ END
```

---

## Part 5: Files to Modify

### New Files
- `noz/engine/src/render/VertexFormat.cs` - VertexAttribute, VertexAttribType
- `noz/engine/src/render/UIVertex.cs` - UIVertex struct
- `noz/engine/src/render/SpriteVertex.cs` - SpriteVertex struct
- `noz/engine/src/ui/UIRender.cs` - UI batching and rendering (or integrate into UI.cs)
- `noz/engine/assets/shader/ui.glsl` - Updated shader with per-vertex data

### Modified Files
- `noz/engine/src/platform/IRenderDriver.cs` - Add vertex format methods, byte-span UpdateVertexBuffer
- `noz/platform/sdl/OpenGLRender.cs` - Implement vertex format (VAO management)
- `noz/engine/src/ui/UI.cs` - Use new UIRender for drawing instead of Render.DrawQuad
- `noz/engine/src/render/Render.cs` - Migrate to use vertex format abstraction for MeshVertex

---

## Part 6: Implementation Order

1. **Vertex format abstraction** - IRenderDriver + OpenGLRender
2. **Migrate Render.cs** - Use new abstraction for existing MeshVertex
3. **UIVertex + UIRender** - New UI rendering path with batching
4. **UI shader** - Port squircle SDF
5. **Update UI.cs** - DrawContainer uses UIRender
6. **SpriteVertex** - Add sprite support to UI batching
7. **Text integration** - Ensure text batches correctly

---

## Verification

1. Build and run editor
2. Check containers render with correct border radius
3. Check border colors render correctly
4. Verify batching via draw call count (Render.Stats or GPU profiler)
5. Test grid of icons batches into single sprite draw
6. Test scrollable/clip still works with stencil
7. Test Scene elements inside UI render correctly
