//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  UI composite shader - renders fullscreen quad with Y flip for final output

#version 450

#ifdef VERTEX_PROGRAM

layout(location = 0) in vec2 in_position;
layout(location = 1) in vec2 in_uv;

layout(location = 0) out vec2 v_uv;

void main()
{
    gl_Position = vec4(in_position, 0.0, 1.0);
    v_uv = vec2(in_uv.x, 1.0 - in_uv.y);
}

#endif

#ifdef FRAGMENT_PROGRAM

layout(location = 0) in vec2 v_uv;

layout(binding = 0) uniform sampler2D sampler_texture;

layout(location = 0) out vec4 f_color;

void main()
{
    f_color = texture(sampler_texture, v_uv);
}

#endif
