//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  MSDF text shader - multi-channel signed distance field text rendering

#version 450

#ifdef VERTEX_PROGRAM

layout(location = 0) in vec2 in_position;
layout(location = 1) in vec2 in_uv;
layout(location = 2) in vec4 in_color;

layout(std140, binding = 0) uniform Globals {
    mat4 u_projection;
    float u_time;
};

layout(location = 0) out vec2 v_uv;
layout(location = 1) out vec4 v_color;

void main() {
    gl_Position = u_projection * vec4(in_position, 0.0, 1.0);
    v_uv = in_uv;
    v_color = in_color;
}

#endif

#ifdef FRAGMENT_PROGRAM

layout(location = 0) in vec2 v_uv;
layout(location = 1) in vec4 v_color;

layout(binding = 0) uniform sampler2D sampler_font;

layout(std140, binding = 1) uniform TextParams {
    vec4 u_outline_color;
    float u_outline_width;
};

layout(location = 0) out vec4 f_color;

float median(float r, float g, float b) {
    return max(min(r, g), min(max(r, g), b));
}

void main() {
    float dist = texture(sampler_font, v_uv).r;

    float dx = dFdx(dist);
    float dy = dFdy(dist);
    float edgeWidth = 0.7 * length(vec2(dx, dy));

    float threshold = 0.49;

    float textAlpha = smoothstep(threshold - edgeWidth, threshold + edgeWidth, dist);

    vec4 color = v_color;
    if (u_outline_width > 0.0) {
        float outlineThreshold = threshold - u_outline_width;
        float outlineAlpha = smoothstep(outlineThreshold - edgeWidth, outlineThreshold + edgeWidth, dist);

        color = mix(u_outline_color, v_color, textAlpha);
        textAlpha = outlineAlpha;
    }

    f_color = vec4(color.rgb, color.a * textAlpha);
}

#endif
