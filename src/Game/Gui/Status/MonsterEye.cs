using System.Numerics;
using UAlbion.Config;
using UAlbion.Formats.Assets.Save;
using UAlbion.Game.Events;
using UAlbion.Game.Gui.Controls;
using UAlbion.Game.State;

namespace UAlbion.Game.Gui.Status;

public class MonsterEye : Dialog
{
    // RE 5D: the eye is BINARY — open iff any ChaseParty monster NPC currently DETECTS
    // the party (the same predicate as the chase logic: 10-tile Euclidean on 2D
    // dungeon/wilderness maps, line-of-sight on 3D — approximated here by the 10-tile
    // radius; the global is the original's 0x15cc5a, which also gates Rest).
    const float DetectRadiusSquared = 10 * 10;
    static readonly (int, int) Position = (5, 40);
    static readonly (int, int) Size = (32, 27);
    readonly UiSpriteElement _sprite;

    public MonsterEye() : base(DialogPositioning.TopLeft)
    {
        On<FastClockEvent>(_ => Update());
        _sprite = new UiSpriteElement(AssetId.None);
        AttachChild(new FixedPositionStacker().Add(_sprite, Position.Item1, Position.Item2, Size.Item1, Size.Item2));
    }

    void Update()
    {
        bool active = ((Resolve<IGameState>()?.ActiveItems ?? 0) & ActiveItems.MonsterEye) != 0;
        foreach (var child in Children)
            child.IsActive = active;

        if (!active) 
            return;

        var state = Resolve<IGameState>();
        var pos = state.Party.Leader.GetPosition();
        bool detected = false;
        for (int i = 0; i < state.Npcs.Count; i++)
        {
            var npc = state.Npcs[i];
            if (npc == null || npc.Id.Type != AssetType.MonsterGroup)
                continue;
            if (npc.MovementType != UAlbion.Formats.Assets.Maps.NpcMovement.ChaseParty)
                continue;
            if (state.IsNpcDisabled(UAlbion.Formats.Ids.MapId.None, (byte)i))
                continue;

            var dist = (new Vector2(pos.X, pos.Y) - new Vector2(npc.X, npc.Y)).LengthSquared();
            if (dist <= DetectRadiusSquared)
            {
                detected = true;
                break;
            }
        }

        _sprite.Id = detected ? Base.CoreGfx.MonsterEyeOn : Base.CoreGfx.MonsterEyeOff;
    }
}