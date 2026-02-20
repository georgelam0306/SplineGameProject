#version 450

// Vertex2D (binding 0)
layout(location = 0) in vec2 inPosition;
layout(location = 1) in vec2 inTexCoord;
layout(location = 2) in vec4 inColor;

// InstanceData (binding 1)
layout(location = 3) in uint inTransformIndex;
layout(location = 4) in uint inTextureIndex;
layout(location = 5) in uint inPackedColor;
layout(location = 6) in uint inPackedUVOffset;

layout(location = 0) out vec4 fragColor;

layout(push_constant) uniform PushConstants {
    mat4 viewProjection;
} pc;

layout(std430, set = 0, binding = 1) readonly buffer Transforms {
    mat4 transforms[];
};

void main() {
    mat4 model = transforms[inTransformIndex];
    gl_Position = pc.viewProjection * model * vec4(inPosition, 0.0, 1.0);

    // Combine vertex color with instance tint
    fragColor = inColor * unpackUnorm4x8(inPackedColor);
}
