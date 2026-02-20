# Multi-Channel SDF (MSDF) Sprites — Future Improvement

This document outlines how multi-channel signed distance fields could improve corner rendering quality in NoZ's SDF sprite system.

## The Problem

Single-channel SDF produces rounded artifacts at sharp corners — both concave (inward notches) and convex (blunted tips). This happens because the distance field is a scalar: at a vertex where two edges meet, the minimum distance to either edge forms a circular iso-contour around the vertex point, rather than the sharp angle formed by the two edges.

For example, a triangle tip rendered with single-channel SDF appears slightly rounded rather than perfectly sharp. A concave corner (like the inside of an L-shape) shows a small notch or pinch.

This is an inherent limitation of representing a 2D boundary with a single scalar field. No amount of resolution increase eliminates it — the artifact gets smaller but never disappears.

## How MSDF Works

Multi-channel SDF (pioneered by Viktor Chlumsky's [msdfgen](https://github.com/Chlumsky/msdfgen)) solves this by encoding distance information across three channels (R, G, B) instead of one.

### Edge Coloring

Each edge (line segment or curve) of the shape is assigned one of three "colors" (corresponding to R, G, B channels). The assignment follows rules:

1. At every sharp corner (angle below a threshold), the two edges meeting at that corner must have **different colors**
2. Colors alternate around the shape to minimize transitions
3. Smooth connections (obtuse angles) can share the same color

### Per-Channel Distance

Three separate distance fields are computed — one per channel. Each channel's distance field only considers edges assigned to that channel's color. At most locations, all three channels agree (same distance). But at sharp corners, the channels **disagree**: one channel sees the distance to edge A, another sees the distance to edge B.

### Reconstruction

The fragment shader reconstructs the shape by taking the **median** of the three channel values:

```wgsl
let r = textureSample(...).r;
let g = textureSample(...).g;
let b = textureSample(...).b;
let dist = median(r, g, b);
let alpha = smoothstep(0.5 - edgeWidth, 0.5 + edgeWidth, dist);
```

The median operation is the key insight. Along a straight edge, all three channels agree so the median equals any of them. At a sharp corner, two channels pull toward "inside" and one pulls toward "outside" (or vice versa), and the median correctly follows the sharp boundary.

The `median(a, b, c)` function is simply:

```wgsl
fn median(r: f32, g: f32, b: f32) -> f32 {
    return max(min(r, g), min(max(r, g), b));
}
```

## What Would Change in NoZ

### Rasterization (`Shape.Rasterize.cs`)

A new `RasterizeMSDF()` method would:

1. **Assign edge colors**: Walk each path's edges, assign R/G/B channel to each edge based on the angle at each vertex. At sharp corners (angle < ~157 degrees), force a color change.

2. **Compute three distance fields**: For each pixel, compute three signed distances — one considering only R-colored edges, one for G, one for B.

3. **Write to RGB**: Store the three distances in R, G, B channels of the atlas (A channel unused or set to max distance across all channels for compatibility).

### Shader (`sprite_msdf.wgsl`)

Replace single-channel sampling with median:

```wgsl
@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    let msd = textureSample(texture_array, texture_sampler, input.uv, input.atlas).rgb;

    let dx = dpdx(msd);
    let dy = dpdy(msd);
    let edgeWidth = 0.7 * length(vec2<f32>(
        length(dx),
        length(dy)
    ));

    let dist = median(msd.r, msd.g, msd.b);
    let alpha = smoothstep(0.5 - edgeWidth, 0.5 + edgeWidth, dist);

    return vec4<f32>(input.color.rgb, alpha * input.color.a);
}
```

The edge width calculation would need adjustment since derivatives are now per-channel. A reasonable approach is to use the derivatives of the median output, or the max derivative magnitude across channels.

### Binary Format

The sprite binary IsSDF byte could be extended to distinguish modes:
- `0` = normal (RGBA color)
- `1` = single-channel SDF (R only)
- `2` = multi-channel SDF (RGB)

### Atlas Impact

MSDF uses 3 channels (RGB) vs SDF's 1 channel (R only). Since the atlas is already RGBA8, this doesn't change atlas size — we just use 3 channels instead of 1. No additional atlas space per sprite beyond what single-channel SDF already requires.

## Edge Coloring Algorithm

The edge coloring step is the most complex part. A simple approach:

1. Walk the path's edges in order
2. Track the angle at each vertex (between incoming and outgoing edges)
3. If the angle is "sharp" (below threshold, e.g., 157 degrees):
   - Assign the next edge a different color than the previous edge
   - Cycle through R, G, B
4. If the angle is "smooth":
   - Keep the same color as the previous edge

For closed paths with an odd number of sharp corners, a third color is needed to avoid a conflict at the wrap-around point. With three channels, any planar shape can be properly colored (this follows from the four-color theorem, though three suffices for non-branching contours).

## Pseudo-Distance Enhancement

MSDF can be further improved with "pseudo-distance" — using perpendicular distance to edge line extensions instead of clamped segment distance at endpoints. This provides better behavior far from the shape (outside the narrow band) and slightly improves rendering at very low resolutions.

The key idea: when the closest point on a segment falls at an endpoint (parameter t=0 or t=1), extend using the perpendicular distance to the adjacent edge's line rather than the radial distance to the endpoint. This is what msdfgen calls "pseudo-SDF" and uses by default.

## Complexity vs Benefit

| Aspect | Single SDF | MSDF |
|--------|-----------|------|
| Corner quality | Rounded | Sharp |
| Atlas channels used | 1 (R) | 3 (RGB) |
| Atlas space | Same | Same |
| Rasterization complexity | Low | Medium (edge coloring) |
| Shader complexity | Low | Low (just add median) |
| Implementation effort | Done | Medium |

The main implementation cost is the edge coloring algorithm. The shader change is minimal (add median function). The per-pixel rasterization cost increases roughly 3x since three distance fields are computed instead of one, but this is an offline editor operation.

## References

- [msdfgen by Viktor Chlumsky](https://github.com/Chlumsky/msdfgen) — the reference implementation
- Chlumsky, V. (2015). "Shape Decomposition for Multi-channel Distance Fields" — the original thesis
- [Improved Alpha-Tested Magnification for Vector Textures and Special Effects](https://steamcdn-a.akamaihd.net/apps/valve/2007/SIGGRAPH2007_AlphaTestedMagnification.pdf) — Valve's original SDF paper (single-channel)
