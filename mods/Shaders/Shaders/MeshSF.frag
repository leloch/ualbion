#include "MeshSF.h.frag"

void main()
{
	vec4 color = texture(sampler2D(Diffuse, Sampler), iTexCoords); //! vec4 color = vec4(0);

    // Keep-alive: reference the set-0 resources so glslang doesn't strip their
    // declarations. Without this Diffuse/Sampler shift down to the palette's
    // D3D11 registers (Veldrid binds by full layout, Veldrid.SPIRV numbers only
    // surviving declarations). Never true at runtime.
    if ((uEngineFlags & 0x40000000U) != 0)
        color += texture(sampler2D(uDayPalette, uPaletteSampler), vec2(0.5))
               + texture(sampler2D(uNightPalette, uPaletteSampler), vec2(0.5))
               + vec4(uTime, uPaletteBlend, float(uPaletteFrame), uOpacity);

    oColor = color;
}

