#version 450

// Vertex3D (binding 0) - 32 bytes
layout(location = 0) in vec3 inPosition;   // 12 bytes
layout(location = 1) in vec3 inNormal;     // 12 bytes
layout(location = 2) in vec2 inTexCoord;   // 8 bytes

// InstanceData (binding 1) - 16 bytes
layout(location = 3) in uint inTransformIndex;
layout(location = 4) in uint inTextureIndex;
layout(location = 5) in uint inPackedColor;
layout(location = 6) in uint inPackedUVOffset;

layout(location = 0) out vec2 fragTexCoord;
layout(location = 1) out vec4 fragColor;
layout(location = 2) out flat uint fragTextureIndex;

layout(push_constant) uniform PushConstants {
    mat4 viewProjection;
} pc;

layout(std430, set = 0, binding = 1) readonly buffer Transforms {
    mat4 transforms[];
};

void main() {
    mat4 model = transforms[inTransformIndex];
    vec4 worldPos = model * vec4(inPosition, 1.0);
    gl_Position = pc.viewProjection * worldPos;

    vec2 uvOffset = unpackHalf2x16(inPackedUVOffset);
    fragTexCoord = inTexCoord + uvOffset;

    // Color from instance data only (no per-vertex color)
    fragColor = unpackUnorm4x8(inPackedColor);
    fragTextureIndex = inTextureIndex;
}
