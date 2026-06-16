#include "ExtrudedTileMapSF.h.frag"
#include "CommonResources.glsl"

vec4 getFloor(vec3 coords)
{
	vec4 day = texture(sampler2DArray(DayFloors, TextureSampler), coords); //! vec4 day;
	vec4 night = texture(sampler2DArray(NightFloors, TextureSampler), coords); //! vec4 night;
	return mix(day, night, uPaletteBlend);
}

vec4 getWall(vec3 coords)
{
	vec4 day = texture(sampler2DArray(DayWalls, TextureSampler), coords); //! vec4 day;
	vec4 night = texture(sampler2DArray(NightWalls, TextureSampler), coords); //! vec4 night;
	return mix(day, night, uPaletteBlend);
}

#ifdef USE_PALETTE
vec4 Pal(float color)
{
	float palHeight = textureSize(sampler2D(uDayPalette, uPaletteSampler), 0).y; //! float palHeight = 1;
	vec2 uv = PaletteUv(color, uPaletteFrame, palHeight);

	vec4 day = texture(sampler2D(uDayPalette, uPaletteSampler), uv); //! vec4 day = vec4(0);
	vec4 night = texture(sampler2D(uNightPalette, uPaletteSampler), uv); //! vec4 night = vec4(0);
	return mix(day, night, uPaletteBlend);
}
#endif

void main()
{
	float floorLayer   = float(iTextures & 0x000000ff);
	float ceilingLayer = float((iTextures & 0x0000ff00) >> 8);
	float wallLayer    = float((iTextures & 0x00ff0000) >> 16);
	float overlayLayer = float((iTextures & 0xff000000) >> 24);

	vec4 color;
	switch (iFlags & TF_TEXTURE_TYPE_MASK)
	{
		case TF_TEXTURE_TYPE_FLOOR:
			color = getFloor(vec3(iTexCoords, floorLayer)); //! {}
			break;
		case TF_TEXTURE_TYPE_CEILING:
			color = getFloor(vec3(iTexCoords, ceilingLayer)); //! {}
			break;
		case TF_TEXTURE_TYPE_WALL:
			color = getWall(vec3(iTexCoords, wallLayer)); //! {}
			break;
	}

#ifdef USE_PALETTE
	color = Pal(color[0]); //! {}
#endif

	// Keep-alive: reference the set-0 palette resources so glslang doesn't strip
	// their declarations from the SPIR-V module. Veldrid.SPIRV assigns D3D11
	// registers from the surviving declarations only, while Veldrid binds by the
	// full resource-set layout — when these were stripped every later texture
	// shifted down a slot and the dungeon sampled the palette textures instead of
	// the wall/floor atlases (the "striped walls" bug). The flag bit is never set
	// at runtime so this contributes nothing to the image.
	if ((uEngineFlags & 0x40000000U) != 0)
		color += texture(sampler2D(uDayPalette, uPaletteSampler), vec2(0.5))
		       + texture(sampler2D(uNightPalette, uPaletteSampler), vec2(0.5));

	// Dungeon ambient lighting: LABDATA Lighting is percent-scale (the original multiplies
	// it with a constant and divides by 100 — freealbion wiki). 0 means "no lighting data"
	// (render at full brightness); otherwise clamp so dungeons never go fully black. The
	// Light spell raises uAmbient at runtime.
	float ambient = uAmbient == 0u ? 1.0f : clamp(float(uAmbient) / 100.0f, 0.15f, 1.0f);
	color = vec4(color.rgb * ambient, color.a);

	// #39 (opt-in, default off): distance fog. uFog = (startDist, endDist, enable, _). Distant
	// geometry fades toward the map's background/fog colour, giving real depth cueing instead of
	// the flat full-bright look. Skipped for fully-transparent texels so it never tints holes.
	if (uFog.z > 0.5f && color.a > 0.0f)
	{
		float fog = clamp((iViewDepth - uFog.x) / max(uFog.y - uFog.x, 0.0001f), 0.0f, 1.0f);
		// Fade distant geometry toward the map's fog/background colour. When the map has no fog colour
		// (uFogColor == 0) this darkens with distance, i.e. a torch-light falloff - the faithful reading
		// of "real lighting" for a dungeon crawler. uFog.w scales the maximum strength (0..1).
		vec3 fogColor = vec3(
			float(uFogColor & 0xFFu) / 255.0f,
			float((uFogColor >> 8) & 0xFFu) / 255.0f,
			float((uFogColor >> 16) & 0xFFu) / 255.0f);
		color = vec4(mix(color.rgb, fogColor, fog * uFog.w), color.a);
	}

	float depth = (color.w == 0.0f) ?  1.0f : gl_FragCoord.z;

	if ((iFlags & TF_HIGHLIGHT)  != 0) color = color * 1.2; // Highlight
	if ((iFlags & TF_RED_TINT)   != 0) color = vec4(color.x * 1.5f + 0.3f, color.yzw);         // Red tint
	if ((iFlags & TF_GREEN_TINT) != 0) color = vec4(color.x, color.y * 1.5f + 0.3f, color.zw); // Green tint
	if ((iFlags & TF_BLUE_TINT)  != 0) color = vec4(color.xy, color.z * 1.5f + 0.3f, color.w); // Blue tint
	if ((iFlags & TF_TRANSPARENT) != 0) color = vec4(color.xyz, color.w * 0.5f); // Transparent
	if ((iFlags & TF_NO_TEXTURE) != 0) {
		if ((iFlags & TF_TEXTURE_TYPE_MASK) == TF_TEXTURE_TYPE_FLOOR)
			color = vec4(floorLayer / 255.0f, floorLayer / 255.0f, floorLayer / 255.0f, 1.0f);
		else if ((iFlags & TF_TEXTURE_TYPE_MASK) == TF_TEXTURE_TYPE_CEILING)
			color = vec4(ceilingLayer / 255.0f, ceilingLayer / 255.0f, ceilingLayer / 255.0f, 1.0f);
		else // TF_TEXTURE_TYPE_WALL
			color = vec4(wallLayer / 255.0f, wallLayer / 255.0f, wallLayer / 255.0f, 1.0f);
	}

	if ((iFlags & TF_TEXTURE_TYPE_MASK) == TF_TEXTURE_TYPE_FLOOR)
		depth = 1.0f;
 
	if ((uEngineFlags & EF_RENDER_DEPTH) != 0)
		color = DepthToColor(depth);

	if ((uEngineFlags & EF_SHOW_BOUNDING_BOXES) != 0)
	{
		color =
			mix(color,
				vec4((2*color.xyz + vec3(1.0f)) * 0.333f, 1.0f),
				max(smoothstep(0.47, 0.5, abs(iTexCoords.x-0.5f)),
					smoothstep(0.47, 0.5, abs(iTexCoords.y-0.5f))));
	}

	oColor = color;
	gl_FragDepth = ((uEngineFlags & EF_FLIP_DEPTH_RANGE) != 0) ? 1.0f - depth : depth;
}

