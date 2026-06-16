#include "SkyBoxSV.h.vert"

void main()
{
	gl_Position = vec4(iPosition, 0, 1);
	// #54: uYawScale = 1 / horizontalFov (supplied by the CPU from the live camera FOV + aspect),
	// so the panorama scrolls in lockstep with the world geometry instead of at an arbitrary rate.
	float pitchFudge = 0.72f;
	oTexPosition =
		iTexCoords
		* vec2(1.0f, -uVisibleProportion)
		+ vec2(-uYaw * uYawScale, 1.38f - uPitch * pitchFudge)
		;

	oNormCoords = iTexCoords;
	oWorldPosition = vec3(iPosition, 0);
}

