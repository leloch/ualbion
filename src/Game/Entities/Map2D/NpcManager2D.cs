using System;
using System.Numerics;
using UAlbion.Api;
using UAlbion.Api.Eventing;
using UAlbion.Config;
using UAlbion.Formats.Assets;
using UAlbion.Formats.Assets.Maps;
using UAlbion.Formats.Assets.Save;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;
using UAlbion.Formats.ScriptEvents;
using UAlbion.Game.State;

namespace UAlbion.Game.Entities.Map2D;

class NpcManager2D : Component
{
    readonly LogicalMap2D _logicalMap;
    readonly Container _sceneObjects;
    readonly Npc2D[] _npcs;

    public NpcManager2D(LogicalMap2D logicalMap2D, Container sceneObjects)
    {
        _logicalMap = logicalMap2D ?? throw new ArgumentNullException(nameof(logicalMap2D));
        _sceneObjects = sceneObjects;
        _npcs = new Npc2D[_logicalMap.Npcs.Count];

        After<ModifyNpcOffEvent>(e => UpdateNpcStatus(e.NpcNum));
        After<NpcOffEvent>(e => UpdateNpcStatus(e.NpcNum));
        After<NpcOnEvent>(e => UpdateNpcStatus(e.NpcNum));

        On<NpcJumpEvent>(DispatchNpcEvent);
        On<NpcLockEvent>(DispatchNpcEvent);
        On<NpcMoveEvent>(DispatchNpcEvent);
        On<NpcTurnEvent>(DispatchNpcEvent);
        On<NpcUnlockEvent>(DispatchNpcEvent);
        // NPC morph events apply live via the NPC component AND are recorded in the
        // map-change collection so they replay when the map is re-entered (previously
        // they were lost on map change — scripts that morph NPCs only worked until
        // the player left the map).
        On<ChangeNpcMovementEvent>(e =>
        {
            DispatchNpcEvent(e);
            RecordNpcChange(e.NpcNum, IconChangeType.NpcMovement, (ushort)e.Mode, e.Scope);
        });
        On<ChangeNpcSpriteEvent>(e =>
        {
            DispatchNpcEvent(e);
            int disk = e.SpriteOrGroup.ToDisk(AssetMapping.Global);
            if (disk is >= 0 and <= ushort.MaxValue)
                RecordNpcChange(e.NpcNum, IconChangeType.NpcSprite, (ushort)disk, e.Scope);
        });
    }

    void RecordNpcChange(byte npcNum, IconChangeType type, ushort value, EventScope scope)
    {
        bool temp = scope is EventScope.AbsTemp or EventScope.RelTemp;
        _logicalMap.Modify(npcNum, 0, type, temp, ChangeIconLayers.None, value);
    }

    void ApplyNpcChange(NpcState state, IconChangeType type, ushort value)
    {
        switch (type)
        {
            case IconChangeType.NpcMovement:
                state.MovementType = (NpcMovement)value;
                break;
            case IconChangeType.NpcSprite:
                state.SpriteOrGroup = _logicalMap.UseSmallSprites
                    ? SpriteId.FromDisk(AssetType.NpcSmallGfx, value, AssetMapping.Global)
                    : SpriteId.FromDisk(AssetType.NpcLargeGfx, value, AssetMapping.Global);
                break;
        }
    }

    void UpdateNpcStatus(byte npcNum)
    {
        if (npcNum >= _npcs.Length)
            return;

        var game = Resolve<IGameState>();
        bool active = !game.IsNpcDisabled(MapId.None, npcNum);
        _npcs[npcNum].IsActive = active;
        game.Npcs[npcNum].WasActive = (ushort)(active ? 1 : 0);
    }

    protected override void Subscribed()
    {
        var game = Resolve<IGameState>();
        bool initialise = game.MapIdForNpcs != _logicalMap.Id;

        for (var index = 0; index < _logicalMap.Npcs.Count; index++)
        {
            var npc = _logicalMap.Npcs[index];
            var state = game.Npcs[index];
            if (state == null)
            {
                state = new NpcState();
                game.Npcs[index] = state;
            }

            if (initialise)
                InitialiseState(npc, state, !game.IsNpcDisabled(_logicalMap.Id, (byte)index), _logicalMap.Events, _logicalMap.TileSize);
        }

        // Re-apply persisted NPC sprite/movement changes (change_npc_* map events) on
        // top of the freshly initialised states, before the NPC entities are built.
        if (initialise)
        {
            foreach (var (npcNum, type, value) in _logicalMap.NpcChanges)
            {
                if (npcNum < game.Npcs.Count && game.Npcs[npcNum] != null)
                    ApplyNpcChange(game.Npcs[npcNum], type, value);
            }
        }

        for (var index = 0; index < _logicalMap.Npcs.Count; index++)
        {
            var npc = _logicalMap.Npcs[index];
            if (npc.IsUnused)
                continue;

            bool isDisabled = game.IsNpcDisabled(_logicalMap.Id, (byte)index);
            _npcs[index] = new Npc2D(
                _sceneObjects,
                game.Npcs[index],
                npc,
                (byte)index,
                !_logicalMap.UseSmallSprites,
                new Vector3(_logicalMap.TileSize, 1),
                GetSitMode)
            {
                IsActive = !isDisabled
            };

            AttachChild(_npcs[index]);
        }

        game.MapIdForNpcs = _logicalMap.Id;
    }

    /// <summary>
    /// Sit/sleep pose for a tile: the overlay's sit flags win, then the underlay's —
    /// NPCs whose waypoint parks them on a chair/bed tile use the matching pose.
    /// </summary>
    SitMode GetSitMode(int x, int y)
    {
        var overlay = _logicalMap.GetOverlay(x, y);
        if (overlay != null && overlay.SitMode != SitMode.None)
            return overlay.SitMode;
        return _logicalMap.GetUnderlay(x, y)?.SitMode ?? SitMode.None;
    }

    internal static void InitialiseState(MapNpc npc, NpcState state, bool active, IEventSet mapEvents, Vector2 tileSize)
    {
        state.Id = npc.Id;
        state.SpriteOrGroup = npc.SpriteOrGroup;
        state.Type = npc.Type;
        state.NoClip = (npc.Flags & MapNpcFlags.NoClip) != 0;
        state.Sound = npc.Sound;
        state.ActiveSfx0 = 0xffff;
        state.ActiveSfx1 = 0xffff;
        state.ActiveSfx2 = 0xffff;
        state.ActiveSfx3 = 0xffff;
        state.Triggers = npc.Triggers;
        state.EventSet = mapEvents;
        state.EventIndex = npc.EventIndex;

        state.MovementType = npc.Movement;
        state.WasActive = (ushort)(active ? 1 : 0);
        state.Flags =
            // TODO: Other flags
            (npc.Flags & MapNpcFlags.SimpleMsg) != 0 ? NpcFlags.SimpleMsg : 0
            ;

        state.Unk1A = 0;
        state.Unk1B = 0;
        state.Unk1D = 0;
        state.WaypointDataOffset = 0;
        state.Unk23 = 0;
        state.Angle = 0;
        state.WaypointIndex = 0;
        state.Unk29 = 0;
        state.X = npc.Waypoints[0].X;
        state.Y = npc.Waypoints[0].Y;
        state.X2 = 0;
        state.Y2 = 0;
        state.PixelX = state.X * tileSize.X;
        state.PixelY = state.Y * tileSize.Y;
        state.PixelDeltaX = 0;
        state.PixelDeltaY = 0;
        state.Unk42 = 0;
        state.OldX = 0;
        state.OldY = 0;
        state.MoveToX = 0;
        state.MoveToY = 0;
        state.Unk4C = 0;
        state.Unk4E = 0;
        state.Unk50 = 0;
        state.Unk51 = 0;
        state.Unk52 = 0;
        state.Unk53 = 0;
        state.Unk54 = 0;
        state.Unk56 = 0;
        state.Unk58 = 0;
        state.GfxWidth = 0;
        state.GfxHeight = 0;
        state.Unk5EGfxRelated = 0;
        state.GfxAlloc = 0;
        state.Unk64 = 0;
        state.Unk65 = 0;
        state.Unk66 = 0;
        state.NpcMoveState.Reset();
    }

    protected override void Unsubscribed() => RemoveAllChildren();

    void DispatchNpcEvent(INpcEvent npcEvent)
    {
        if (npcEvent.NpcNum > _npcs.Length)
            return;

        var npc = _npcs[npcEvent.NpcNum];
        if (npc == null)
        {
            ApiUtil.Assert($"Tried to send event {npcEvent} to NPC {npcEvent.NpcNum}, but no such NPC exists!");
            return;
        }

        npc.Receive(npcEvent, this);
    }
}