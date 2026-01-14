//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  UI composite shader - renders fullscreen quad with Y flip for final output

#version 450

//@ VERTEX

layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec2 aUV;

out vec2 vUV;

void main() {
    gl_Position = vec4(aPosition, 0.0, 1.0);
    // Flip Y: map UV.y from [0,1] to [1,0]
    vUV = vec2(aUV.x, 1.0 - aUV.y);
}

//@ END

//@ FRAGMENT

in vec2 vUV;

uniform sampler2D uTexture;

out vec4 FragColor;

void main() {
    FragColor = texture(uTexture, vUV);
}

//@ END
