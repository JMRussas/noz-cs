//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  UI shader - SDF rounded rectangles with borders

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

// UI-specific uniforms
uniform vec2 uBoxSize;      // Size of the box in pixels
uniform float uBorderRadius; // Corner radius
uniform float uBorderWidth;  // Border thickness
uniform vec4 uBorderColor;   // Border color

out vec2 vUV;
out vec4 vColor;
out vec2 vLocalPos; // Position within box (0-1)

void main() {
    gl_Position = uProjection * vec4(aPosition, 0.0, 1.0);
    vUV = aUV;
    vColor = aColor * aOpacity;
    vLocalPos = aUV; // UV maps to box position
}

//@ END

//@ FRAGMENT

in vec2 vUV;
in vec4 vColor;
in vec2 vLocalPos;

uniform vec2 uBoxSize;
uniform float uBorderRadius;
uniform float uBorderWidth;
uniform vec4 uBorderColor;

out vec4 FragColor;

// SDF for rounded rectangle
float sdRoundedBox(vec2 p, vec2 b, float r) {
    vec2 q = abs(p) - b + r;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

void main() {
    // Convert UV (0-1) to centered coordinates (-halfSize to +halfSize)
    vec2 halfSize = uBoxSize * 0.5;
    vec2 p = (vLocalPos - 0.5) * uBoxSize;

    // Clamp border radius to half the smaller dimension
    float maxRadius = min(halfSize.x, halfSize.y);
    float radius = min(uBorderRadius, maxRadius);

    // Distance to rounded rectangle edge
    float dist = sdRoundedBox(p, halfSize, radius);

    // Anti-aliasing width (in pixels, ~1px smoothing)
    float aa = 1.0;

    // Fill: inside the shape
    float fillAlpha = 1.0 - smoothstep(-aa, 0.0, dist);

    // Border: between inner and outer edge
    float innerDist = dist + uBorderWidth;
    float borderAlpha = smoothstep(-aa, 0.0, innerDist) * (1.0 - smoothstep(-aa, 0.0, dist));

    // Combine fill and border
    vec4 fillColor = vColor;
    vec4 finalColor = mix(fillColor, uBorderColor, borderAlpha);
    finalColor.a *= fillAlpha;

    // If no fill color (transparent), only show border
    if (vColor.a < 0.001) {
        finalColor = uBorderColor;
        finalColor.a *= borderAlpha;
    }

    FragColor = finalColor;
}

//@ END
