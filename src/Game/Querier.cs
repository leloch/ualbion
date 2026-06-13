using System.Linq;
using UAlbion.Api.Eventing;
using UAlbion.Config;
using UAlbion.Formats;
using UAlbion.Formats.Assets;
using UAlbion.Formats.Assets.Maps;
using UAlbion.Formats.Assets.Sheets;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;
using UAlbion.Game.Events;
using UAlbion.Game.State;

namespace UAlbion.Game;

public class Querier : Component // : ServiceComponent<IQuerier>, IQuerier
{
#pragma warning disable CA1506 // '.ctor' is coupled with '66' different types from '15' different namespaces. Rewrite or refactor the code to decrease its class coupling below '41'.
    public Querier()
    {
        OnQuery<          QueryVerbEvent, bool>(q => ((EventContext)Context).Source.Trigger == q.TriggerType);
        OnQuery<          QueryGoldEvent, bool>(q => FormatUtil.Compare(q.Operation, Resolve<IGameState>().Party.TotalGold, q.Argument));
        OnQuery<       QueryRationsEvent, bool>(q => FormatUtil.Compare(q.Operation, Resolve<IGameState>().Party.TotalRations, q.Argument));
        OnQuery<       QueryHasItemEvent, bool>(q => FormatUtil.Compare(q.Operation, Resolve<IGameState>().Party.GetItemCount(q.ItemId), q.Immediate));
        OnQuery<QueryHasPartyMemberEvent, bool>(q => Resolve<IGameState>().Party.StatusBarOrder.Any(x => x.Id == q.PartyMemberId));
        OnQuery<          QueryHourEvent, bool>(q => FormatUtil.Compare(q.Operation, Resolve<IGameState>().Time.Hour, q.Argument));
        OnQuery<        QueryLeaderEvent, bool>(q => Resolve<IGameState>().Party.Leader.Id == q.PartyMemberId);
        OnQuery<           QueryMapEvent, bool>(q => FormatUtil.Compare(q.Operation, Resolve<IGameState>().MapId.Id, q.MapId.Id));
        OnQuery<QueryPreviousActionResultEvent, bool> (_ => ((EventContext)Context).LastEventResult);
        OnQuery<   QueryRandomChanceEvent, bool>(q => Resolve<IRandom>().Generate(100) < q.Argument);
        OnQuery<         QuerySwitchEvent, bool>(q => FormatUtil.Compare(q.Operation, Resolve<IGameState>().GetSwitch(q.SwitchId) ? 1 : 0, q.Immediate));
        OnQuery<         QueryTickerEvent, bool>(q => FormatUtil.Compare(q.Operation, Resolve<IGameState>().GetTicker(q.TickerId), q.Immediate));
        OnQuery<    QueryTriggerTypeEvent, bool>(q => ((EventContext)Context).Source.Trigger == (TriggerType)q.Argument);
        OnQuery<       QueryUsedItemEvent, bool>(q =>
        {
            var ctx = (EventContext)Context;
            var used = ctx.UsedItemOverride ?? ctx.Source.AssetId; // change_used_item override, else the UseItem source
            return used == (AssetId)q.ItemId;
        });
        // change_used_item (RE _RE_OPCODES_FLAGS.md, handler 0x3bed3): the original CONSUMES one
        // of the tool the player just applied and GIVES one of the event's ItemId — an inventory
        // transformation (consume tool → receive product), guarded so the give only happens if the
        // consume succeeded. (We also keep the query-override so a later query used_item==X matches.)
        On<UAlbion.Formats.MapEvents.ChangeUsedItemEvent>(e =>
        {
            if (Context is not EventContext c) return;
            var used = c.UsedItemOverride ?? c.Source.AssetId; // the tool being applied
            c.UsedItemOverride = (AssetId)e.ItemId;
            if (used.Type != AssetType.Item || e.ItemId.IsNone)
                return;
            var party = Resolve<IGameState>().Party;
            if (party.GetItemCount((ItemId)used) <= 0) // consume must succeed
                return;
            Raise(new ModifyItemCountEvent(NumericOperation.SubtractAmount, 1, (ItemId)used));
            Raise(new ModifyItemCountEvent(NumericOperation.AddAmount, 1, e.ItemId));
        });
        // SCRIPT-02: "active" = NOT disabled (was un-negated), and key on the NPC index q.NpcNum
        // (was q.Immediate, the comparison byte, so it always tested NPC 0). Mirrors line ~109.
        OnQuery<      QueryNpcActiveEvent, bool>(q => !Resolve<IGameState>().IsNpcDisabled(MapId.None, (byte)q.NpcNum));
        OnQuery<QueryScriptDebugModeEvent, bool>(_ => false);
        OnQuery<  QueryIsCurrentMap2DEvent, bool>(_ =>
        {
            var t = TryResolve<IMapManager>()?.Current?.MapData?.MapType;
            return t is UAlbion.Formats.Assets.Maps.MapType.TwoD or UAlbion.Formats.Assets.Maps.MapType.TwoDOutdoors;
        });
        OnQuery<   QueryDoorUnlockedEvent, bool>(q => FormatUtil.Compare(q.Operation, Resolve<IGameState>().IsDoorOpen(q.DoorId) ? 1 : 0, q.Immediate));
        OnQuery<  QueryChestUnlockedEvent, bool>(q => FormatUtil.Compare(q.Operation, Resolve<IGameState>().IsChestOpen(q.ChestId) ? 1 : 0, q.Immediate));
        // Leader character-attribute / day-count branches (previously threw at map load).
        OnQuery<         QueryGenderEvent, bool>(q => FormatUtil.Compare(q.Operation, (int)Resolve<IGameState>().Party.Leader.Effective.Gender, q.Immediate));
        OnQuery<          QueryClassEvent, bool>(q => FormatUtil.Compare(q.Operation, (int)Resolve<IGameState>().Party.Leader.Effective.PlayerClass, q.Immediate));
        OnQuery<           QueryRaceEvent, bool>(q => FormatUtil.Compare(q.Operation, (int)Resolve<IGameState>().Party.Leader.Effective.Race, q.Immediate));
        OnQuery<            QueryDayEvent, bool>(q =>
        {
            int days = (int)(Resolve<IGameState>().Time - UAlbion.Formats.Assets.Save.SavedGame.Epoch).TotalDays;
            int modulo = q.Immediate == 0 ? 1 : q.Immediate;
            return FormatUtil.Compare(q.Operation, days % modulo, q.Argument);
        });

        OnQuery<QueryConsciousEvent, bool>(q =>
        {
            var state = Resolve<IGameState>();
            var member = state.GetSheet(q.PartyMemberId.ToSheet());
            if (member == null)
                return false;
            return (member.Combat.Conditions & PlayerConditions.UnconsciousMask) == 0;
        });

        OnQuery<QueryEventUsedEvent, bool>(_ =>
        {
            var context = (EventContext)Context;
            if (context.EventSet.Id.Type != AssetType.EventSet)
                return false;

            var game = Resolve<IGameState>();
            return game.IsEventUsed(context.EventSet.Id, context.LastAction);
        });

        // UAlbion always plays the full game data — never the demo. (The demo build of
        // the original gated some content behind this query.)
        OnQuery<QueryDemoVersionEvent, bool>(_ => false);

        OnQueryAsync<PromptPlayerEvent, bool>(async q =>
        {
            var context = (EventContext)Context;
            if (context.Source == null)
                return false;

            var innerEvent = new YesNoPromptEvent(new StringId(context.EventSet.StringSetId, q.Argument));
            return await RaiseQueryA(innerEvent);
        });

        OnQueryAsync<PromptPlayerNumericEvent, bool>(async q =>
        {
            var context = (EventContext)Context;
            if (context?.Source == null)
                return false;

            var innerEvent = new NumericPromptEvent(Base.SystemText.MsgBox_EnterNumber, 0, 9999);
            var result = await RaiseQueryA(innerEvent);
            return result == q.Argument;
        });

        OnQuery<QueryChainActiveEvent, bool>(q => !Resolve<IGameState>().IsChainDisabled(q.MapId, q.ChainNum));
        OnQuery<QueryNpcActiveOnMapEvent, bool>(q => !Resolve<IGameState>().IsNpcDisabled(q.MapId, q.NpcNum));
        OnQuery<QueryNpcXEvent, bool>(q => FormatUtil.Compare(q.Operation, Resolve<IGameState>().Npcs[q.Immediate].X, q.Argument));
        OnQuery<QueryNpcYEvent, bool>(q => FormatUtil.Compare(q.Operation, Resolve<IGameState>().Npcs[q.Immediate].Y, q.Argument));

        // The four formerly-unknown opcodes, RE batch 5C (dispatcher table 0x3ca53):

        // 0x0C = FACING: imm 0xFF → the party's facing quadrant (camera yaw, 0..3);
        // else NPC #imm's facing. Compared with the usual comparator.
        OnQuery<QueryUnkCEvent, bool>(q =>
        {
            var state = Resolve<IGameState>();
            int facing;
            if (q.Immediate == 0xFF)
            {
                facing = PartyFacingQuadrant();
            }
            else
            {
                var npc = q.Immediate < state.Npcs.Count ? state.Npcs[q.Immediate] : null;
                if (npc == null)
                    return false;
                facing = npc.Angle / 64 & 3; // 256-step angle → quadrant (INFERRED mapping)
            }
            return FormatUtil.Compare(q.Operation, facing, q.Argument);
        });

        // 0x19 = LEADER SPEAKS LANGUAGE: leaderSheet[+8] & (1 << arg); op/imm ignored.
        OnQuery<QueryUnk19Event, bool>(q =>
        {
            var langs = Resolve<IGameState>().Leader?.Languages ?? 0;
            return ((int)langs & (1 << q.Argument)) != 0;
        });

        // 0x1E = TIME-OF-DAY SCHEDULE TICK: Compare(tickOfDay % 1152, op, arg) — the
        // same 48-per-hour M-tick the NPC waypoint tables use.
        OnQuery<QueryUnk1EEvent, bool>(q =>
            FormatUtil.Compare(q.Operation, Resolve<IGameState>().MTicksToday, q.Argument));

        // 0x21 = "IS IT LIGHT ENOUGH": true unless the map uses dungeon lighting (map
        // flags & 3 == 1) and the effective light (max(Light-spell pct, party light-item
        // total), cap 100) is below 25. Operands ignored by the original.
        OnQuery<QueryUnk21Event, bool>(_ => Magic.DungeonLighting.IsLightEnough(
            Resolve<IGameState>(),
            Resolve<IParty>(),
            TryResolve<IMapManager>()?.Current?.MapData,
            Resolve<IAssetManager>()));
    }

    /// <summary>Camera yaw → facing quadrant 0..3 (the original's 0x153b38 word).</summary>
    int PartyFacingQuadrant()
    {
        var camera = TryResolve<UAlbion.Core.Visual.ICameraProvider>()?.Camera;
        if (camera == null)
            return 0;
        var look = camera.LookDirection;
        double angle = System.Math.Atan2(look.X, -look.Z);
        int quadrant = (int)System.Math.Round(angle / (System.Math.PI / 2), System.MidpointRounding.AwayFromZero);
        return (quadrant % 4 + 4) % 4;
    }
#pragma warning restore CA1506 // '.ctor' is coupled with '66' different types from '15' different namespaces. Rewrite or refactor the code to decrease its class coupling below '41'.

/*
        bool Query(QueryEvent query, Action<bool> continuation)
        {
            var context = Resolve<IEventManager>().Context;
            return InnerQuery(context, query, false, continuation);
        }

        public bool? QueryDebug(QueryEvent query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            var context = Resolve<IEventManager>().Context;
            bool result = true;
            InnerQuery(context, query, true, x => result = x);
            return result;
        }
*/
}
