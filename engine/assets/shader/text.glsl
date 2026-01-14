//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  MSDF text shader - multi-channel signed distance field text rendering

#version 450

//@ VERTEX

layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec2 aUV;
layout(location = 2) in vec2 aNormal;
layout(location = 3) in vec4 aColor;
layout(location = 4) in float aOpacity;
layout(location = 5) in int aBone;
layout(location = 6) in int aAtlas;
layout(location = 7) in int aFrameCount;
layout(location = 8) in float aFrameWidth;
layout(location = 9) in float aFrameRate;
layout(location = 10) in float aAnimStartTime;

uniform mat4 uProjection;

out vec2 vUV;
out vec4 vColor;

void main() {
    gl_Position = uProjection * vec4(aPosition, 0.0, 1.0);
    vUV = aUV;
    vColor = aColor * aOpacity;
}

//@ END

//@ FRAGMENT

in vec2 vUV;
in vec4 vColor;

uniform sampler2D uFontTexture; // R8 SDF texture
uniform vec4 uOutlineColor;
uniform float uOutlineWidth;    // 0 = no outline

out vec4 FragColor;

float median(float r, float g, float b) {
    return max(min(r, g), min(max(r, g), b));
}

void main() {
    // Sample the SDF texture (single channel for basic SDF, or use median for MSDF)
    float dist = texture(uFontTexture, vUV).r;

    // Screen-space derivative for anti-aliasing
    float dx = dFdx(dist);
    float dy = dFdy(dist);
    float edgeWidth = 0.7 * length(vec2(dx, dy));

    // Distance threshold (0.5 = edge in normalized SDF)
    float threshold = 0.5;

    // Main text alpha
    float textAlpha = smoothstep(threshold - edgeWidth, threshold + edgeWidth, dist);

    // Outline
    vec4 color = vColor;
    if (uOutlineWidth > 0.0) {
        float outlineThreshold = threshold - uOutlineWidth;
        float outlineAlpha = smoothstep(outlineThreshold - edgeWidth, outlineThreshold + edgeWidth, dist);

        // Blend outline behind text
        color = mix(uOutlineColor, vColor, textAlpha);
        textAlpha = outlineAlpha;
    }

    FragColor = vec4(color.rgb, color.a * textAlpha);
}

//@ END
