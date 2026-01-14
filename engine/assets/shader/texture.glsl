//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  Simple texture shader - uses sampler2D, no texture array
//  For editor document rendering (textures, sprite editor)
//

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

uniform sampler2D uTexture;

out vec4 FragColor;

void main() {
    vec4 texColor = texture(uTexture, vUV);
    FragColor = texColor * vColor;
}

//@ END
