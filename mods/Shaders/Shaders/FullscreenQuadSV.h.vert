// !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
// !!! This file was auto-generated using VeldridGen. It should not be edited by hand. !!!
// !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
//!#version 450 // Comments with //! are just for the VS GLSL plugin
//!#extension GL_KHR_vulkan_glsl: enable

layout(set = 0, binding = 2) uniform _Uniform {
    vec4 uRect;
    float uGamma;
    float _pad0;
    float _pad1;
    float _pad2;
};

// UAlbion.Core.Veldrid.Vertex2DTextured
layout(location = 0) in vec2 iPosition;
layout(location = 1) in vec2 iTexCoords;

// UAlbion.Core.Veldrid.FullscreenQuadIntermediate
layout(location = 0) out vec2 oNormCoords;

