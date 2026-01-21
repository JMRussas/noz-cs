//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

// Bind group 0: Globals and textures
struct Globals {
    projection: mat4x4<f32>,
    time: f32,
}

@group(0) @binding(0) var<uniform> globals: Globals;
@group(0) @binding(1) var bone_texture: texture_2d<f32>;
@group(0) @binding(2) var bone_sampler: sampler;
@group(0) @binding(3) var texture_array: texture_2d_array<f32>;
@group(0) @binding(4) var texture_sampler: sampler;

// Vertex input
struct VertexInput {
    @location(0) position: vec2<f32>,
    @location(1) uv: vec2<f32>,
    @location(2) normal: vec2<f32>,
    @location(3) color: vec4<f32>,
    @location(4) bone: i32,
    @location(5) atlas: i32,
    @location(6) frame_count: i32,
    @location(7) frame_width: f32,
    @location(8) frame_rate: f32,
    @location(9) frame_time: f32,
}

// Vertex output / Fragment input
struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) uv: vec2<f32>,
    @location(1) color: vec4<f32>,
    @location(2) @interpolate(flat) atlas: i32,
}

@vertex
fn vs_main(input: VertexInput) -> VertexOutput {
    var output: VertexOutput;

    // in_bone is flat index, each row holds 64 bones (128 texels)
    let row = input.bone / 64;
    let localBone = input.bone % 64;
    let texelX = localBone * 2;
    let row0 = textureLoad(bone_texture, vec2<i32>(texelX, row), 0);
    let row1 = textureLoad(bone_texture, vec2<i32>(texelX + 1, row), 0);

    // Apply bone transformation
    let skinnedPos = vec2<f32>(
        row0.x * input.position.x + row0.y * input.position.y + row0.z,
        row1.x * input.position.x + row1.y * input.position.y + row1.z
    );

    // Calculate UV with animation
    var uv = input.uv;
    if (input.frame_count > 1) {
        let animTime = globals.time - input.frame_time;
        let totalFrames = f32(input.frame_count);
        let currentFrame = floor(animTime * input.frame_rate % totalFrames);
        uv.x += currentFrame * input.frame_width;
    }

    output.position = globals.projection * vec4<f32>(skinnedPos, 0.0, 1.0);
    output.uv = uv;
    output.color = input.color;
    output.atlas = input.atlas;

    return output;
}

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    return textureSample(texture_array, texture_sampler, input.uv, input.atlas) * input.color;
}
