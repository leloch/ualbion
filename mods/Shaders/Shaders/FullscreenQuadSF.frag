#include "FullscreenQuadSF.h.frag"

void main()
{
    vec4 c = texture(sampler2D(uTexture, uSampler), iNormCoords); //! vec4 c = vec4(1);

    // #71: optional gamma correction at the final composite. uGamma == 1 leaves the image untouched
    // (the default, so the vanilla look is unchanged); other values brighten/darken the midtones.
    if (uGamma != 1.0f)
        c = vec4(pow(max(c.rgb, vec3(0.0f)), vec3(1.0f / uGamma)), c.a);

    oColor = c;
}
