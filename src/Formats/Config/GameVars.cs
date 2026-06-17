using UAlbion.Api.Settings;

namespace UAlbion.Formats.Config;

public class GameVars
{
    GameVars() { }
    public static VarLibrary Library { get; } = new();
    public static GameVars Instance { get; } = new();

    public IntVar Version { get; } = new(Library, "ConfigVersion", 0);
    public AudioVars Audio { get; } = new();
    public class AudioVars
    {
        public FloatVar PollIntervalSeconds { get; } = new(Library, "Game.Audio.PollIntervalSeconds", 0.1f);
    }

    public CombatVars Combat { get; } = new();
    public class CombatVars
    {
        public IntVar StatRandomisationPercentage { get; }= new(Library, "Game.Combat.StatRandomizationPercentage", 10);
    }

    public InventoryVars Inventory { get; } = new();
    public class InventoryVars
    {
        public IntVar CarryWeightPerStrength { get; } = new(Library, "Game.Inventory.CarryWeightPerStrength", 1000);
        public IntVar GramsPerGold           { get; } = new(Library, "Game.Inventory.GramsPerGold", 20);
        public IntVar GramsPerRation         { get; } = new(Library, "Game.Inventory.GramsPerRation", 250);
    }

    public NpcMovementVars NpcMovement { get; } = new();
    public class NpcMovementVars
    {
        public IntVar TicksPerFrame { get; } = new(Library, "Game.NpcMovement.TicksPerFrame", 12); // Number of game ticks it takes to advance to the next animation frame
        public IntVar TicksPerTile  { get; } = new(Library, "Game.NpcMovement.TicksPerTile", 24); // Number of game ticks it takes to move across a map tile
    }

    public PartyMovementVars PartyMovement { get; } = new();
    public class PartyMovementVars
    {
        public IntVar MaxTrailDistanceLarge { get; } = new(Library, "Game.PartyMovement.MaxTrailDistanceLarge", 18);
        public IntVar MaxTrailDistanceSmall { get; } = new(Library, "Game.PartyMovement.MaxTrailDistanceSmall", 12);
        public IntVar MinTrailDistanceLarge { get; } = new(Library, "Game.PartyMovement.MinTrailDistanceLarge", 12);
        public IntVar MinTrailDistanceSmall { get; } = new(Library, "Game.PartyMovement.MinTrailDistanceSmall", 6);
        public IntVar TicksPerFrame         { get; } = new(Library, "Game.PartyMovement.TicksPerFrame", 9); // Number of game ticks it takes to advance to the next animation frame
        public IntVar TicksPerTile          { get; } = new(Library, "Game.PartyMovement.TicksPerTile", 12); // Number of game ticks it takes to move across a map tile
    }

    public TimeVars Time { get; } = new();
    public class TimeVars
    {
        public IntVar FastTicksPerAssetCacheCycle { get; } = new(Library, "Game.Time.FastTicksPerAssetCacheCycle", 3600);
        public IntVar FastTicksPerMapTileFrame    { get; } = new(Library, "Game.Time.FastTicksPerMapTileFrame", 10);
        public FloatVar FastTicksPerSecond        { get; } = new(Library, "Game.Time.FastTicksPerSecond", 60.0f);
        public IntVar FastTicksPerSlowTick        { get; } = new(Library, "Game.Time.FastTicksPerSlowTick", 8); // Used for palette cycling & 'every-step' events
        public FloatVar GameSecondsPerSecond      { get; } = new(Library, "Game.Time.GameSecondsPerSecond", 180.0f);
        public FloatVar IdleTicksPerSecond        { get; } = new(Library, "Game.Time.IdleTicksPerSecond", 8.0f);
    }

    public GraphicsVars Graphics { get; } = new();
    public class GraphicsVars
    {
        // Optional 3D rendering enhancements. ALL default to the vanilla look so the base
        // experience is unchanged; players opt in. (#34 etc.)
        public BoolVar SmoothDungeonTextures { get; } = new(Library, "Game.Graphics.SmoothDungeonTextures", false); // #34: trilinear vs point
        public BoolVar HighlightInteractableTiles { get; } = new(Library, "Game.Graphics.HighlightInteractableTiles", false); // #65: brighten 3D tiles with action zones
        public BoolVar Minimap { get; } = new(Library, "Game.Graphics.Minimap", false); // #43: small always-on automap in the corner of 3D levels
        public BoolVar DungeonFog { get; } = new(Library, "Game.Graphics.DungeonFog", false); // #39: opt-in distance fog/lighting in 3D levels
        public BoolVar BumpMapping { get; } = new(Library, "Game.Graphics.BumpMapping", false); // #33: opt-in synthetic relief on 3D textures
    }

    public UiVars Ui { get; } = new();
    public class UiVars
    {
        public FloatVar ButtonDoubleClickIntervalSeconds { get; } = new(Library, "Game.UI.ButtonDoubleClickIntervalSeconds", 0.35f);
        public FloatVar MouseLookSensitivity             { get; } = new(Library, "Game.UI.MouseLookSensitivity", 2.0f);
        // #36: enforce the original's right-click reach limits (touch 2 / talk 3 / examine 4 tiles,
        // Euclidean). Disable to allow interacting with any visible tile (the old behaviour + the
        // unintentional extended-reach "steal").
        public BoolVar ContextMenuReachLimit            { get; } = new(Library, "Game.UI.ContextMenuReachLimit", true);
        // Modern main menu (live 3D backdrop) vs the classic 1996 box. Default = modern; set true for vanilla.
        public BoolVar ClassicMainMenu                  { get; } = new(Library, "Game.UI.ClassicMainMenu", false);

        public TransitionsVars Transitions { get; } = new();
        public class TransitionsVars
        {
            public FloatVar DefaultTransitionTimeSeconds      { get; } = new(Library, "Game.UI.Transitions.DefaultTransitionTimeSeconds", 0.35f);
            public FloatVar DiscardItemGravity                { get; } = new(Library, "Game.UI.Transitions.DiscardItemGravity", 9.8f);
            public FloatVar DiscardItemMaxInitialX            { get; } = new(Library, "Game.UI.Transitions.DiscardItemMaxInitialX", 2.0f);
            public FloatVar DiscardItemMaxInitialY            { get; } = new(Library, "Game.UI.Transitions.DiscardItemMaxInitialY", 2.2f);
            public FloatVar InventoryChangLerpSeconds         { get; } = new(Library, "Game.UI.Transitions.InventoryChangLerpSeconds", 0.25f);
            public FloatVar ItemMovementTransitionTimeSeconds { get; } = new(Library, "Game.UI.Transitions.ItemMovementTransitionTimeSeconds", 0.2f);
            public IntVar   MaxDiscardTransitions             { get; } = new(Library, "Game.UI.Transitions.MaxDiscardTransitions", 24);
        }
    }

    public VisualVars Visual { get; } = new();
    public class VisualVars // added Vars suffix to avoid clashing with UAlbion.Api.Visual namespace etc
    {
        public Camera2DVars Camera2D { get; } = new();
        public class Camera2DVars
        {
            public FloatVar LerpRate    { get; } = new(Library, "Game.Visual.Camera2D.LerpRate", 3.0f);
            public FloatVar TileOffsetX { get; } = new(Library, "Game.Visual.Camera2D.TileOffsetX", 0.0f);
            public FloatVar TileOffsetY { get; } = new(Library, "Game.Visual.Camera2D.TileOffsetY", 0.0f);
        }
    }
}