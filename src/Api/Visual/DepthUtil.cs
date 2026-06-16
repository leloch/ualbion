using System;

namespace UAlbion.Api.Visual;

public static class DepthUtil
{
    public const int LayerCount = 4096;
    public const float MaxLayer = LayerCount - 1;
    public static float GetAbsDepth(float yCoordinateInTiles) => 1.0f - (float)Math.Ceiling(yCoordinateInTiles) / MaxLayer;

    // #51: sprite (party/NPC) depth. Tiles snap to integer rows via Ceiling, but a moving sprite sits
    // at a fractional Y. Using Ceiling for the sprite too makes it jump a whole row's depth the instant
    // it leaves a tile, so mid-step (especially on diagonals) it TIES the depth of the row it hasn't
    // reached yet -> ambiguous draw order against that row's overlays = the layering glitch. Sorting the
    // sprite by its continuous Y keeps it cleanly behind the next row until its feet actually cross into
    // it. (+0.5 biases the tie at an integer row toward drawing in front of that tile's own overlay.)
    public static float GetSpriteDepth(float yCoordinateInTiles) => 1.0f - (yCoordinateInTiles + 0.5f) / MaxLayer;

    public static float GetRelDepth(int tiles) => -tiles / MaxLayer;
    public static int DepthToLayer(float depth) => (int)((1.0f - depth) * MaxLayer);
}