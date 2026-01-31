//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  SDF text shader with per-vertex outline support

// Bind group 0: Globals and font texture
struct Globals {
    projection: mat4x4<f32>,
    time: f32,
}

@group(0) @binding(0) var<uniform> globals: Globals;
@group(0) @binding(1) var font_texture: texture_2d<f32>;
@group(0) @binding(2) var font_sampler: sampler;

// Vertex input
struct VertexInput {
    @location(0) position: vec2<f32>,
    @location(1) uv: vec2<f32>,
    @location(2) color: vec4<f32>,
    @location(3) outline_color: vec4<f32>,
    @location(4) outline_width: f32,
    @location(5) outline_softness: f32,
}

// Vertex output / Fragment input
struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) uv: vec2<f32>,
    @location(1) color: vec4<f32>,
    @location(2) outline_color: vec4<f32>,
    @location(3) outline_width: f32,
    @location(4) outline_softness: f32,
}

@vertex
fn vs_main(input: VertexInput) -> VertexOutput {
    var output: VertexOutput;
    output.position = globals.projection * vec4<f32>(input.position, 0.0, 1.0);
    output.uv = input.uv;
    output.color = input.color;
    output.outline_color = input.outline_color;
    output.outline_width = input.outline_width;
    output.outline_softness = input.outline_softness;
    return output;
}

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    let dist = textureSample(font_texture, font_sampler, input.uv).r;

    let dx = dpdx(dist);
    let dy = dpdy(dist);
    let edgeWidth = 0.7 * length(vec2<f32>(dx, dy));

    let threshold = 0.49;
    let textAlpha = smoothstep(threshold - edgeWidth, threshold + edgeWidth, dist);

    var color = input.color;
    var alpha = textAlpha;

    if (input.outline_width > 0.0) {
        let softEdge = edgeWidth + input.outline_softness;
        let outlineThreshold = threshold - input.outline_width;
        let outlineAlpha = smoothstep(outlineThreshold - softEdge, outlineThreshold + softEdge, dist);

        color = mix(input.outline_color, input.color, textAlpha);
        alpha = outlineAlpha;
    }

    return vec4<f32>(color.rgb, color.a * alpha);
}
