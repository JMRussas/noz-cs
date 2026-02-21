//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  SDF texture shader - uses texture_2d (no texture array)
//  For editor rendering of SDF sprites
//  Distance in RGB channels, color from vertex color

// Bind group 0: Globals and texture
struct Globals {
    projection: mat4x4<f32>,
    time: f32,
}

@group(0) @binding(0) var<uniform> globals: Globals;
@group(0) @binding(1) var texture0: texture_2d<f32>;
@group(0) @binding(2) var sampler0: sampler;
@group(0) @binding(3) var bone_texture: texture_2d<f32>;  // Bone transforms (RGBA32F, unfilterable)

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

struct VertexOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) uv: vec2<f32>,
    @location(1) color: vec4<f32>,
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

    output.position = globals.projection * vec4<f32>(pos, 0.0, 1.0);
    output.uv = input.uv;
    output.color = input.color;
    return output;
}

fn median(r: f32, g: f32, b: f32) -> f32 {
    return max(min(r, g), min(max(r, g), b));
}

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    // MSDF distance stored in R, G, B channels
    let msd = textureSample(texture0, sampler0, input.uv).rgb;

    // Reconstruct distance via median
    let dist = median(msd.r, msd.g, msd.b);

    // Adaptive edge width based on screen-space derivatives
    let dx = dpdx(dist);
    let dy = dpdy(dist);
    let edgeWidth = 0.7 * length(vec2<f32>(dx, dy));

    let threshold = 0.5;
    let alpha = smoothstep(threshold - edgeWidth, threshold + edgeWidth, dist);

    // Color and opacity from vertex color
    return vec4<f32>(input.color.rgb, alpha * input.color.a);
}
