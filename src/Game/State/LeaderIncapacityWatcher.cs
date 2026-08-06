using UAlbion.Api.Eventing;
using UAlbion.Config;
using UAlbion.Formats.Assets.Sheets;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;
using UAlbion.Game.Events;

namespace UAlbion.Game.State;

/// <summary>
/// The original's leader-incapacity rescue (fcn.0003822d): if NO active party member has
/// conditions free of the incapacity mask 0x731 (Unconscious | Paralysed | Fleeing |
/// Panicking | Asleep | Insane), Tom is revived — LP raised to 1 (fcn.000377c5) and those
/// six conditions cleared on him. This is the anti-soft-lock rule that makes an
/// out-of-combat party wipe (e.g. poison draining the last conscious member to 0 LP)
/// survivable: there is no out-of-combat game-over in the original — Tom always gets back
/// up at 1 LP. In-combat incapacity is deliberately excluded: Battle owns those semantics
/// (PartyKilled / Retreat outcomes).
/// </summary>
public class LeaderIncapacityWatcher : GameComponent
{
    const PlayerConditions IncapacityMask = // 0x731, byte-exact vs the RE'd handler
        PlayerConditions.Unconscious | PlayerConditions.Paralysed | PlayerConditions.Fleeing |
        PlayerConditions.Panicking | PlayerConditions.Asleep | PlayerConditions.Insane;

    bool _checking;

    public LeaderIncapacityWatcher() => On<SheetChangedEvent>(_ => Check());

    void Check()
    {
        if (_checking) // the rescue itself raises sheet changes — don't recurse
            return;

        // Battle owns in-combat incapacity (all-down = PartyKilled). Only rescue in the world.
        var scene = TryResolve<ISceneManager>()?.ActiveSceneId;
        if (scene is not (Scenes.SceneId.World2D or Scenes.SceneId.World3D))
            return;

        var party = TryResolve<IParty>();
        var state = TryResolve<IGameState>();
        if (party == null || state == null)
            return;

        bool anyCapable = false;
        bool anyMember = false;
        foreach (var member in party.StatusBarOrder)
        {
            if (member == null) continue;
            var combat = state.GetSheet(member.Id.ToSheet())?.Combat;
            if (combat == null) continue;
            anyMember = true;
            if ((combat.Conditions & IncapacityMask) == 0)
            {
                anyCapable = true;
                break;
            }
        }

        if (!anyMember || anyCapable)
            return;

        _checking = true;
        try
        {
            var tom = new TargetId(AssetType.PartyMember, (ushort)Base.PartyMember.Tom);
            var tomSheet = state.GetSheet(((PartyMemberId)Base.PartyMember.Tom).ToSheet());
            Info("[LeaderIncapacity] whole party incapacitated — reviving Tom at 1 LP (fcn.0003822d)");

            if ((tomSheet?.Combat?.LifePoints?.Current ?? 0) < 1)
                Raise(new DataChangeEvent(tom, ChangeProperty.Health, NumericOperation.AddAmount, 1));

            foreach (var cond in new[]
                     {
                         PlayerCondition.Unconscious, PlayerCondition.Paralysed, PlayerCondition.Fleeing,
                         PlayerCondition.Panicking, PlayerCondition.Asleep, PlayerCondition.Insane
                     })
            {
                Raise(new ChangeStatusEvent(tom, cond, NumericOperation.SubtractAmount, 1));
            }
        }
        finally { _checking = false; }
    }
}
