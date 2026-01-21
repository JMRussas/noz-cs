//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  Composite shader - renders fullscreen quad with Y flip for final output

// Bind group 0: Texture
@group(0) @binding(0) var scene_texture: texture_2d<f32>;
@group(0) @binding(1) var scene_sampler: sampler;

// Vertex input
struct VertexInput {
    @location(0) position: vec2<f32>,
    @location(1) uv: vec2<f32>,
}

// Vertex output / Fragment input
struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) uv: vec2<f32>,
}

@vertex
fn vs_main(input: VertexInput) -> VertexOutput {
    var output: VertexOutput;
    output.position = vec4<f32>(input.position, 0.0, 1.0);
    output.uv = vec2<f32>(input.uv.x, 1.0 - input.uv.y);
    return output;
}

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    return textureSample(scene_texture, scene_sampler, input.uv);
}
