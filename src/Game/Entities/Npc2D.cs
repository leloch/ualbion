using System;
using System.Collections.Generic;
using System.Numerics;
using UAlbion.Api.Eventing;
using UAlbion.Api.Visual;
using UAlbion.Config;
using UAlbion.Core;
using UAlbion.Core.Visual;
using UAlbion.Formats.Assets.Maps;
using UAlbion.Formats.Assets.Save;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;
using UAlbion.Formats.ScriptEvents;
using UAlbion.Game.Entities.Map2D;
using UAlbion.Game.Events;
using UAlbion.Game.Gui.Controls;
using UAlbion.Game.Scenes;
using UAlbion.Game.State;
using UAlbion.Game.Text;

namespace UAlbion.Game.Entities;

public class Npc2D : Component
{
    static readonly Vector2 LargeTileOffset = new(1, 1);
    readonly Func<(int, int), (int, int)> _getDesiredDirectionDelegate = x => x;
    readonly Action<int, int> _onTileEnteredDelegate;
    readonly NpcState _state;
    readonly MapNpc _mapData;
    readonly MapSprite _sprite;
    readonly byte _npcNumber;
    readonly bool _isLarge;
    bool _isLocked;

    MovementSettings _moveSettings;
    // int _frameCount;
    int _targetX;
    int _targetY;

    public override string ToString() => $"Npc {_npcNumber} @ ({_state.X},{_state.Y}) {_state.Id} {_sprite.Id}";

    public Npc2D(Container sceneObjects, NpcState state, MapNpc definition, byte npcNumber, bool isLarge, Vector3 tileSize)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _mapData = definition ?? throw new ArgumentNullException(nameof(definition));
        _npcNumber = npcNumber;
        _isLarge = isLarge;

        _sprite = new MapSprite(
            _state.SpriteOrGroup,
            tileSize,
            DrawLayer.Character,
            0,
            SpriteFlags.BottomAligned)
        {
            SelectionCallback = () => this
        };

        sceneObjects.Add(_sprite);

        _onTileEnteredDelegate = (x, y) => Raise(new NpcEnteredTileEvent(_npcNumber, x, y));

        On<FastClockEvent>(_ => Update());
        On<Combat.EndCombatEvent>(e =>
        {
            if (!_inContactCombat)
                return;
            _inContactCombat = false;
            if (e.Result == Combat.CombatResult.Victory)
                Raise(new NpcOffEvent(_npcNumber));
        });
        OnDirectCall<ShowMapMenuEvent>(OnRightClick);
        OnDirectCall<NpcJumpEvent>(OnJump);
        OnDirectCall<NpcMoveEvent>(OnMove);
        OnDirectCall<NpcTurnEvent>(OnTurn);
        OnDirectCall<ChangeNpcMovementEvent>(OnChangeMovement);
        OnDirectCall<ChangeNpcSpriteEvent>(OnChangeIcon);
        OnDirectCall<NpcLockEvent>(_ => _isLocked = true);
        OnDirectCall<NpcUnlockEvent>(_ => _isLocked = false);
    }

    void OnChangeMovement(ChangeNpcMovementEvent e)
    {
        _state.MovementType = e.Mode;
    }

    void OnChangeIcon(ChangeNpcSpriteEvent e)
    {
        _state.SpriteOrGroup = e.SpriteOrGroup;
        _sprite.Id = e.SpriteOrGroup;
    }

    void Update()
    {
        // TODO: Fix this hacky solution to the issue where
        // a component gets removed from the exchange, but it
        // was already in the dispatch list for an event in progress.
        if (Exchange == null) 
            return;

        if (!_isLocked)
        {
            switch (_state.MovementType)
            {
                case NpcMovement.Waypoints:
                case NpcMovement.Waypoints2:
                    MovementFollowWaypoints();
                    break;
                case NpcMovement.RandomWander:
                    MovementRandom();
                    break;
                case NpcMovement.ChaseParty:
                    MovementChaseParty();
                    break;
                default:
                    MovementStationary();
                    break;
            }
        }

        if (Movement2D.Update(_state,
                _moveSettings,
                Resolve<ICollisionManager>(),
                (_targetX - _state.X, _targetY - _state.Y),
                _getDesiredDirectionDelegate,
                _onTileEnteredDelegate))
        {
            SyncSprite();
        }
    }

    void SyncSprite()
    {
        var pos = new Vector2(
            _state.PixelX / _moveSettings.TileWidth,
            _state.PixelY / _moveSettings.TileHeight);

        if (_isLarge)
            pos += LargeTileOffset;

        _sprite.TilePosition = new Vector3(pos.X, pos.Y, _moveSettings.GetDepth(pos.Y));
        _sprite.Frame = _moveSettings.GetSpriteFrame(_state, GetSitModeDelegate);
    }

    static readonly Func<int, int, SitMode> GetSitModeDelegate = GetSitMode;
    static SitMode GetSitMode(int x, int y) => SitMode.None; // TODO
    void OnTurn(NpcTurnEvent e)
    {
        _state.NpcMoveState.Direction = e.Direction;
        SyncSprite();
    }

    void OnMove(NpcMoveEvent e) => SetTarget(_state.X + e.X, _state.Y + e.Y);

    void OnJump(NpcJumpEvent e)
    {
        _state.X = (ushort)(e.X ?? _state.X);
        _state.Y = (ushort)(e.Y ?? _state.Y);
        _state.PixelX = _state.X * _moveSettings.TileWidth;
        _state.PixelY = _state.Y * _moveSettings.TileHeight;
        SetTarget(_state.X, _state.Y);
        SyncSprite();
    }

    protected override void Subscribed()
    {
        _moveSettings ??= new MovementSettings(_isLarge ? LargeSpriteAnimations.Frames : SmallSpriteAnimations.Frames)
        {
            TicksPerFrame = ReadVar(V.Game.NpcMovement.TicksPerFrame),
            TicksPerTile = ReadVar(V.Game.NpcMovement.TicksPerTile)
        };

        SyncSprite();
    }

    bool CanTalk => 
        _state.Id.Type is AssetType.NpcSheet or AssetType.MapTextIndex 
        || _state.EventIndex != EventNode.UnusedEventId;

    void OnRightClick(ShowMapMenuEvent e)
    {
        if (!CanTalk)
            return;

        var window = Resolve<IGameWindow>();
        var camera = Resolve<ICameraProvider>().Camera;
        var tf = Resolve<ITextFormatter>();

        var normPosition = camera.ProjectWorldToNorm(_sprite.Position);
        var uiPosition = window.NormToUi(normPosition.X, normPosition.Y);

        // TODO: NPC type check.
        IText S(TextId textId) => tf.NoWrap().Center().Format(textId);
        var heading = S(Base.SystemText.MapPopup_Person);

        var options = new List<ContextMenuOption>();
        if (_state.Type == NpcType.Npc)
        {
            var talkEvent = BuildInteractionEvent();
            if (talkEvent != null)
            {
                options.Add(new ContextMenuOption(
                    S(Base.SystemText.MapPopup_TalkTo),
                    talkEvent,
                    ContextMenuGroup.Actions));
            }
        }

        options.Add(new ContextMenuOption(
            S(Base.SystemText.MapPopup_MainMenu),
            new PushSceneEvent(SceneId.MainMenu),
            ContextMenuGroup.System
        ));

        Raise(new ContextMenuEvent(uiPosition, heading, options));
        e.Propagating = false;
    }

    IEvent BuildInteractionEvent()
    {
        if (_state.EventIndex != EventNode.UnusedEventId)
            return new TriggerChainEvent(
                _state.EventSet,
                _state.EventIndex,
                new EventSource(_state.Id, TriggerType.TalkTo));

        if (_state.Id.Type == AssetType.NpcSheet)
            return new StartDialogueEvent(_state.Id);

        if (_state.Id.Type == AssetType.MapTextIndex)
            return new TextEvent((ushort)_state.Id.Id, TextLocation.NoPortrait, SheetId.None);

        return null;
    }

    void SetTarget(int x, int y)
    {
        if (_targetX == x && _targetY == y)
            return;

        GameTrace.Log.SetNpcMoveTarget(_npcNumber, x, y);
        _targetX = x;
        _targetY = y;
    }

    void MovementStationary() => SetTarget(_state.X, _state.Y);

    void MovementFollowWaypoints()
    {
        var game = Resolve<IGameState>();
        var waypointIndex = game.MTicksToday;
        if (waypointIndex >= _mapData.Waypoints.Length)
            waypointIndex = 0;

        var waypoint = _mapData.Waypoints[waypointIndex];
        SetTarget(waypoint.X, waypoint.Y);

        // if too far, teleport
        int dx = _targetX - _state.X;
        int dy = _targetY - _state.Y;
        int d2 = dx * dx + dy * dy;
        if (d2 > 4)
        {
            GameTrace.Log.TeleportNpc(_npcNumber, _targetX, _targetY);
            _state.X = (ushort)_targetX;
            _state.Y = (ushort)_targetY;
        }
    }

    bool _inContactCombat;

    void MovementChaseParty()
    {
        // Only retarget on tile arrival — same rationale as MovementRandom: re-aiming on
        // every FastClock tick stalls the sprite step before it completes.
        if (_state.X != _targetX || _state.Y != _targetY)
            return;

        var party = Resolve<IParty>();
        var pos = party.Leader.GetPosition();

        // Contact: a chasing monster group that catches the party (same or adjacent tile)
        // starts combat — the original's touch trigger. Victory removes the group from
        // the map via npc_off; any other outcome resumes the chase.
        int cdx = System.Math.Abs((int)pos.X - _state.X);
        int cdy = System.Math.Abs((int)pos.Y - _state.Y);
        if (!_inContactCombat && System.Math.Max(cdx, cdy) <= 1 && _state.Id.Type == AssetType.MonsterGroup)
        {
            _inContactCombat = true;
            Raise(new EncounterEvent((MonsterGroupId)_state.Id, CombatBackgroundId.None));
            return;
        }

        // PLACEHOLDER give-up distance — 16 tiles Manhattan radius. The original engine
        // likely had a per-NPC pursue range (MapNpc field), but until that's RE'd this
        // prevents off-map NPCs from following the party across an entire 100×100 map.
        const int GiveUpRadius = 16;
        int dx = System.Math.Abs((int)pos.X - _state.X);
        int dy = System.Math.Abs((int)pos.Y - _state.Y);
        if (dx + dy > GiveUpRadius)
        {
            // Out of range — stop where we are rather than oscillating toward the party.
            SetTarget(_state.X, _state.Y);
            return;
        }

        SetTarget((int)pos.X, (int)pos.Y);
    }

    void MovementRandom()
    {
        // Only pick a new direction once the NPC actually finishes traversing to its
        // current target tile. Without this gate the NPC re-rolls on every FastClock tick
        // and oscillates in place — the new random direction overwrites the in-flight one
        // before the sprite can complete the step.
        if (_state.X != _targetX || _state.Y != _targetY)
            return;

        var (x,y) = Resolve<IRandom>().Generate(4) switch
        {
            0 => (-1, 0),
            1 => (0, 1),
            2 => (1, 0),
            _ => (0, -1),
        };

        SetTarget(x + _state.X, y + _state.Y);
    }

}
