//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//
//  Unified sprite shader - handles sprites, animated sprites, skinned meshes, vertex-color meshes

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
uniform float uTime;

// Bone transforms - index 0 is always identity
// Each bone is a 3x2 matrix stored as 2 vec3s (column-major)
uniform vec3 uBones[128]; // 64 bones * 2 vec3s per bone

out vec2 vUV;
out vec4 vColor;
flat out int vAtlas;

void main() {
    // Get bone transform (2 vec3s per bone)
    int boneIdx = aBone * 2;
    vec3 boneCol0 = uBones[boneIdx];
    vec3 boneCol1 = uBones[boneIdx + 1];

    // Apply bone transform: pos' = M * pos (2D affine)
    vec2 skinnedPos = vec2(
        boneCol0.x * aPosition.x + boneCol1.x * aPosition.y + boneCol0.z,
        boneCol0.y * aPosition.x + boneCol1.y * aPosition.y + boneCol1.z
    );

    // Animation: offset UV.x based on current frame
    vec2 uv = aUV;
    if (aFrameCount > 1) {
        float animTime = uTime - aAnimStartTime;
        float totalFrames = float(aFrameCount);
        float currentFrame = floor(mod(animTime * aFrameRate, totalFrames));
        uv.x += currentFrame * aFrameWidth;
    }

    gl_Position = uProjection * vec4(skinnedPos, 0.0, 1.0);
    vUV = uv;
    vColor = aColor * aOpacity;
    vAtlas = aAtlas;
}

//@ END

//@ FRAGMENT

in vec2 vUV;
in vec4 vColor;
flat in int vAtlas;

uniform sampler2D uTexture;

out vec4 FragColor;

void main() {
    vec4 texColor = texture(uTexture, vUV);
    FragColor = texColor * vColor;
}

//@ END
