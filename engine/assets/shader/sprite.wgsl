//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

// Bind group 0: Globals and textures
struct Globals {
    projection: mat4x4<f32>,
    time: f32,
}

@group(0) @binding(0) var<uniform> globals: Globals;
@group(0) @binding(1) var texture_array: texture_2d_array<f32>;  // Slot 0: Main atlas texture
@group(0) @binding(2) var texture_sampler: sampler;
@group(0) @binding(3) var bone_texture: texture_2d<f32>;  // Slot 1: Bone transforms (RGBA32F, unfilterable)

// Bone texture layout:
// - Width: 128 texels (64 bones * 2 texels per bone)
// - Height: 1024 rows (one row per entity per frame)
// - Each bone uses 2 consecutive texels:
//   - Texel 0: M11, M12, M31, _ (column 0 and translation X)
//   - Texel 1: M21, M22, M32, _ (column 1 and translation Y)
// - Bone index encodes: row * 64 + localBoneIndex
// - Row 0, bone 0 contains identity transform

fn get_bone_transform(bone: i32) -> mat3x2<f32> {
    let row = bone / 64;
    let local_bone = bone % 64;
    let texel_x = local_bone * 2;

    let t0 = textureLoad(bone_texture, vec2<i32>(texel_x, row), 0);
    let t1 = textureLoad(bone_texture, vec2<i32>(texel_x + 1, row), 0);

    // Reconstruct 3x2 transform matrix (for 2D affine transform)
    // mat3x2 in WGSL is 3 columns, 2 rows: result = M * vec3(x, y, 1)
    return mat3x2<f32>(
        vec2<f32>(t0.x, t0.y),  // column 0: M11, M12
        vec2<f32>(t1.x, t1.y),  // column 1: M21, M22
        vec2<f32>(t0.z, t1.z)   // column 2: M31, M32 (translation)
    );
}

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

    // Apply bone transform if bone index is non-zero
    var pos = input.position;
    if (input.bone != 0) {
        let bone_transform = get_bone_transform(input.bone);
        pos = bone_transform * vec3<f32>(input.position, 1.0);
    }

    // Calculate UV with animation
    var uv = input.uv;
    if (input.frame_count > 1) {
        let animTime = globals.time - input.frame_time;
        let totalFrames = f32(input.frame_count);
        let currentFrame = floor(animTime * input.frame_rate % totalFrames);
        uv.x += currentFrame * input.frame_width;
    }

    output.position = globals.projection * vec4<f32>(pos, 0.0, 1.0);
    output.uv = uv;
    output.color = input.color;
    output.atlas = input.atlas;

    return output;
}

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    return textureSample(texture_array, texture_sampler, input.uv, input.atlas) * input.color;
}
