#version 450
#extension GL_EXT_nonuniform_qualifier : enable

layout(location = 0) in vec2 fragTexCoord;
layout(location = 1) in vec4 fragColor;
layout(location = 2) in flat uint fragTextureIndex;

layout(location = 0) out vec4 outColor;

layout(set = 0, binding = 0) uniform sampler2D textures[256];

void main() {
    vec4 tex = texture(textures[nonuniformEXT(fragTextureIndex)], fragTexCoord);
    outColor = tex * fragColor;
}
