//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  Simple texture shader - uses sampler2D, no texture array
//  For editor document rendering (textures, sprite editor)
//

#version 450

#ifdef VERTEX_PROGRAM

layout(location = 0) in vec2 in_position;
layout(location = 1) in vec2 in_uv;
layout(location = 3) in vec4 in_color;

layout(std140, binding = 0) uniform Globals {
    mat4 u_projection;
    float u_time;
};

layout(location = 0) out vec2 v_uv;
layout(location = 1) out vec4 v_color;

void main()
{
    gl_Position = u_projection * vec4(in_position, 0.0, 1.0);
    v_uv = in_uv;
    v_color = in_color;
}

#endif

#ifdef FRAGMENT_PROGRAM

layout(location = 0) in vec2 v_uv;
layout(location = 1) in vec4 v_color;

layout(binding = 0) uniform sampler2D sampler_texture;

layout(location = 0) out vec4 f_color;

void main()
{
    f_color = texture(sampler_texture, v_uv) * v_color;
}

#endif
