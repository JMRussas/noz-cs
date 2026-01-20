//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

#version 450

#ifdef VERTEX_PROGRAM

layout(location = 0) in vec2 in_position;
layout(location = 1) in vec2 in_uv;
layout(location = 2) in vec2 in_normal;
layout(location = 3) in vec4 in_color;
layout(location = 4) in int in_bone;
layout(location = 5) in int in_atlas;
layout(location = 6) in int in_frame_count;
layout(location = 7) in float in_frame_width;
layout(location = 8) in float in_frame_rate;
layout(location = 9) in float in_frame_time;

layout(std140, binding = 0) uniform Globals {
    mat4 u_projection;
    float u_time;
};

layout(binding = 1) uniform sampler2D u_bones;

layout(location = 0) out vec2 v_uv;
layout(location = 1) out vec4 v_color;
layout(location = 2) flat out int v_atlas;

void main()
{
    // in_bone is flat index, each row holds 64 bones (128 texels)
    int row = in_bone / 64;
    int localBone = in_bone % 64;
    int texelX = localBone * 2;
    vec4 row0 = texelFetch(u_bones, ivec2(texelX, row), 0);
    vec4 row1 = texelFetch(u_bones, ivec2(texelX + 1, row), 0);

    vec2 skinnedPos = vec2(
        row0.x * in_position.x + row0.y * in_position.y + row0.z,
        row1.x * in_position.x + row1.y * in_position.y + row1.z
    );

    vec2 uv = in_uv;
    if (in_frame_count > 1) {
        float animTime = u_time - in_frame_time;
        float totalFrames = float(in_frame_count);
        float currentFrame = floor(mod(animTime * in_frame_rate, totalFrames));
        uv.x += currentFrame * in_frame_width;
    }

    gl_Position = u_projection * vec4(skinnedPos, 0.0, 1.0);
    v_uv = uv;
    v_color = in_color;
    v_atlas = in_atlas;
}

#endif

#ifdef FRAGMENT_PROGRAM

layout(location = 0) in vec2 v_uv;
layout(location = 1) in vec4 v_color;
layout(location = 2) flat in int v_atlas;

layout(binding = 0) uniform sampler2DArray sampler_texture;

layout(location = 0) out vec4 f_color;

void main()
{
    f_color = texture(sampler_texture, vec3(v_uv, v_atlas)) * v_color;
}

#endif
