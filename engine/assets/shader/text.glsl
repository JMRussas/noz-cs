//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  MSDF text shader - multi-channel signed distance field text rendering

#version 450

//@ VERTEX

layout(location = 0) in vec2 in_position;
layout(location = 1) in vec2 in_uv;
layout(location = 3) in vec4 in_color;

uniform mat4 u_projection;

out vec2 v_uv;
out vec4 v_color;

void main() {
    gl_Position = u_projection * vec4(in_position, 0.0, 1.0);
    v_uv = in_uv;
    v_color = in_color;
}

//@ END

//@ FRAGMENT

in vec2 v_uv;
in vec4 v_color;

uniform sampler2D sampler_font; // R8 SDF texture
uniform vec4 u_outline_color;
uniform float u_outline_width;    // 0 = no outline

out vec4 f_color;

float median(float r, float g, float b) {
    return max(min(r, g), min(max(r, g), b));
}

void main() {
    // Sample the SDF texture (single channel for basic SDF, or use median for MSDF)
    float dist = texture(sampler_font, v_uv).r;

    // Screen-space derivative for anti-aliasing
    float dx = dFdx(dist);
    float dy = dFdy(dist);
    float edgeWidth = 0.7 * length(vec2(dx, dy));

    // Distance threshold (0.5 = edge in normalized SDF)
    float threshold = 0.5;

    // Main text alpha
    float textAlpha = smoothstep(threshold - edgeWidth, threshold + edgeWidth, dist);

    // Outline
    vec4 color = v_color;
    if (u_outline_width > 0.0) {
        float outlineThreshold = threshold - u_outline_width;
        float outlineAlpha = smoothstep(outlineThreshold - edgeWidth, outlineThreshold + edgeWidth, dist);

        // Blend outline behind text
        color = mix(u_outline_color, v_color, textAlpha);
        textAlpha = outlineAlpha;
    }

    f_color = vec4(color.rgb, color.a * textAlpha);
}

//@ END
