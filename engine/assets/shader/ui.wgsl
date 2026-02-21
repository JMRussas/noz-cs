//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  UI shader - Rounded rectangle with per-corner radius support and texture sampling

struct Globals {
    projection: mat4x4<f32>,
    time: f32,
}

@group(0) @binding(0) var<uniform> globals: Globals;
@group(0) @binding(1) var ui_texture: texture_2d<f32>;
@group(0) @binding(2) var ui_sampler: sampler;

struct VertexInput {
    @location(0) position: vec2<f32>,
    @location(1) uv: vec2<f32>,
    @location(2) rect_size: vec2<f32>,
    @location(3) color: vec4<f32>,
    @location(4) border_width: f32,
    @location(5) border_color: vec4<f32>,
    @location(6) corner_radii: vec4<f32>,  // TL, TR, BL, BR
}

struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) uv: vec2<f32>,
    @location(1) color: vec4<f32>,
    @location(2) @interpolate(flat) rect_size: vec2<f32>,
    @location(3) @interpolate(flat) border_width: f32,
    @location(4) @interpolate(flat) border_color: vec4<f32>,
    @location(5) @interpolate(flat) corner_radii: vec4<f32>,
}

@vertex
fn vs_main(input: VertexInput) -> VertexOutput {
    var output: VertexOutput;
    output.position = globals.projection * vec4<f32>(input.position, 0.0, 1.0);
    output.uv = input.uv;
    output.color = input.color;
    output.rect_size = input.rect_size;
    output.border_width = input.border_width;
    output.border_color = input.border_color;
    output.corner_radii = input.corner_radii;
    return output;
}

// Branchless rounded rect SDF with per-corner radii
fn sdf_rounded_rect(p: vec2<f32>, size: vec2<f32>, radii: vec4<f32>) -> f32 {
    let half = size * 0.5;

    // Select radius based on quadrant (branchless using mix/step)
    // radii: x=TL, y=TR, z=BL, w=BR
    let is_right = step(half.x, p.x);
    let is_bottom = step(half.y, p.y);
    let r = mix(
        mix(radii.x, radii.y, is_right),  // top row
        mix(radii.z, radii.w, is_right),  // bottom row
        is_bottom
    );

    // Standard rounded rect SDF
    let q = abs(p - half) - half + r;
    return length(max(q, vec2<f32>(0.0))) + min(max(q.x, q.y), 0.0) - r;
}

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    // Sample texture
    let tex_color = textureSample(ui_texture, ui_sampler, input.uv);

    let p = input.uv * input.rect_size;
    let dist = sdf_rounded_rect(p, input.rect_size, input.corner_radii);
    let edge = fwidth(dist);

    // Alpha: fade out at boundary (branchless)
    let alpha = 1.0 - smoothstep(-edge, edge, dist);

    // Border blend (branchless) - sharp inner border edge
    let border_blend = step(0.0, dist + input.border_width);
    let has_border = step(0.001, input.border_width) * step(0.001, input.border_color.a);
    let base_color = input.color * tex_color;
    let final_color = mix(base_color, input.border_color, border_blend * has_border);

    // Early discard for fully transparent pixels
    let out_alpha = final_color.a * alpha;
    if (out_alpha < 0.001) {
        discard;
    }

    return vec4<f32>(final_color.rgb, out_alpha);
}
