//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  UI shader - SDF squircle with per-vertex border data

#version 450

//@ VERTEX

layout(location = 0) in vec2 in_position;
layout(location = 1) in vec2 in_uv;
layout(location = 2) in vec2 in_normal;
layout(location = 3) in vec4 in_color;
layout(location = 4) in float in_border_ratio;
layout(location = 5) in vec4 in_border_color;

uniform mat4 u_projection;

out vec2 v_uv;
out vec4 v_color;
flat out float v_border_ratio;
flat out vec4 v_border_color;

void main() {
    vec4 screen_pos = u_projection * vec4(in_position, 0.0, 1.0);
    float scale_x = u_projection[0][0];
    float scale_y = u_projection[1][1];
    screen_pos.x += in_normal.x * scale_x;
    screen_pos.y += in_normal.y * scale_y;
    
    v_border_ratio = in_border_ratio;
    v_border_color = in_border_color;

    gl_Position = vec4(screen_pos.xy, 0.0, 1.0);
    v_uv = in_uv;
    v_color = in_color;
}

//@ END

//@ FRAGMENT

in vec2 v_uv;
in vec4 v_color;
flat in float v_border_ratio;
flat in vec4 v_border_color;

out vec4 f_color;

void main() {
    // Skip SDF for simple rectangles (border_ratio < 0 signals no rounding)
    if (v_border_ratio < 0.0) {
        f_color = v_color;
        return;
    }

    // Squircle SDF (n=4 superellipse): |x|^n + |y|^n = 1
    float n = 4.0;
    float dist = pow(pow(abs(v_uv.x), n) + pow(abs(v_uv.y), n), 1.0 / n);
    float edge = fwidth(dist);

    // Premultiply colors
    vec4 color = v_color;
    vec4 border_color = v_border_color;

    float border = (1.0 + edge) - v_border_ratio;
    color = mix(color, border_color, smoothstep(border - edge, border, dist));
    color.a = 1.0 - smoothstep(1.0 - edge, 1.0, dist);

    if (color.a < 0.001) discard;

    f_color = color;
}

//@ END
