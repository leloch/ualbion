using System;
using SerdesNet;
using UAlbion.Api.Eventing;
using UAlbion.Config;

namespace UAlbion.Formats.MapEvents;

public abstract class QueryEvent : MapEvent, IBranchingEvent
{
    public override MapEventType EventType => MapEventType.Query;
    public abstract QueryType QueryType { get; }
    public static QueryEvent Serdes(QueryEvent e, AssetMapping mapping, ISerdes s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (s.IsWriting() && e == null) throw new ArgumentNullException(nameof(e));

        var queryType = s.EnumU8(nameof(QueryType), e?.QueryType ?? 0);
        return queryType switch
        {
            QueryType.Switch => QuerySwitchEvent.Serdes((QuerySwitchEvent)e, mapping, s),
            QueryType.ChainActive => QueryChainActiveEvent.Serdes((QueryChainActiveEvent)e, mapping, s),
            QueryType.DoorUnlocked => QueryDoorUnlockedEvent.Serdes((QueryDoorUnlockedEvent)e, mapping, s),
            QueryType.ChestUnlocked => QueryChestUnlockedEvent.Serdes((QueryChestUnlockedEvent)e, mapping, s),
            QueryType.NpcActiveOnMap => QueryNpcActiveOnMapEvent.Serdes((QueryNpcActiveOnMapEvent)e, mapping, s),
            QueryType.HasPartyMember => QueryHasPartyMemberEvent.Serdes((QueryHasPartyMemberEvent)e, mapping, s),
            QueryType.HasItem => QueryHasItemEvent.Serdes((QueryHasItemEvent)e, mapping, s),
            QueryType.UsedItem => QueryUsedItemEvent.Serdes((QueryUsedItemEvent)e, mapping, s),
            QueryType.PreviousActionResult => QueryPreviousActionResultEvent.Serdes((QueryPreviousActionResultEvent)e, s),
            QueryType.ScriptDebugMode => QueryScriptDebugModeEvent.Serdes((QueryScriptDebugModeEvent)e, s),
            QueryType.UnkC => QueryUnkCEvent.Serdes((QueryUnkCEvent)e, s),
            QueryType.NpcActive => QueryNpcActiveEvent.Serdes((QueryNpcActiveEvent)e, mapping, s),
            QueryType.Gold => QueryGoldEvent.Serdes((QueryGoldEvent)e, s),
            QueryType.Rations => QueryRationsEvent.Serdes((QueryRationsEvent)e, s),
            QueryType.RandomChance => QueryRandomChanceEvent.Serdes((QueryRandomChanceEvent)e, s),
            QueryType.Hour => QueryHourEvent.Serdes((QueryHourEvent)e, s),
            QueryType.ChosenVerb => QueryVerbEvent.Serdes((QueryVerbEvent)e, s),
            QueryType.Conscious => QueryConsciousEvent.Serdes((QueryConsciousEvent)e, mapping, s),
            QueryType.Gender => QueryGenderEvent.Serdes((QueryGenderEvent)e, s),
            QueryType.Class => QueryClassEvent.Serdes((QueryClassEvent)e, s),
            QueryType.Race => QueryRaceEvent.Serdes((QueryRaceEvent)e, s),
            QueryType.Day => QueryDayEvent.Serdes((QueryDayEvent)e, s),
            QueryType.IsCurrentMap2D => QueryIsCurrentMap2DEvent.Serdes((QueryIsCurrentMap2DEvent)e, s),
            QueryType.Leader => QueryLeaderEvent.Serdes((QueryLeaderEvent)e, mapping, s),
            QueryType.Ticker => QueryTickerEvent.Serdes((QueryTickerEvent)e, mapping, s),
            QueryType.Map => QueryMapEvent.Serdes((QueryMapEvent)e, mapping, s),
            QueryType.Unk1E => QueryUnk1EEvent.Serdes((QueryUnk1EEvent)e, s),
            QueryType.PromptPlayer => PromptPlayerEvent.Serdes((PromptPlayerEvent)e, s),
            QueryType.Unk19 => QueryUnk19Event.Serdes((QueryUnk19Event)e, s),
            QueryType.TriggerType => QueryTriggerTypeEvent.Serdes((QueryTriggerTypeEvent)e, s),
            QueryType.Unk21 => QueryUnk21Event.Serdes((QueryUnk21Event)e, s),
            QueryType.EventUsed => QueryEventUsedEvent.Serdes((QueryEventUsedEvent)e, s),
            QueryType.DemoVersion => QueryDemoVersionEvent.Serdes((QueryDemoVersionEvent)e, s),
            QueryType.NpcXCoord => QueryNpcXEvent.Serdes((QueryNpcXEvent)e, s),
            QueryType.NpcYCoord => QueryNpcYEvent.Serdes((QueryNpcYEvent)e, s),
            QueryType.PromptPlayerNumeric => PromptPlayerNumericEvent.Serdes((PromptPlayerNumericEvent)e, s),
            // Undecoded query subtypes — round-trip via the standard 6-byte body so a real map
            // using one loads instead of throwing FormatException at deserialize (Phase-0 crash
            // guard, see QueryUndecodedEvents.cs). Semantics still pending RE.
            QueryType.Unk8 => QueryUnk8Event.Serdes((QueryUnk8Event)e, s),
            QueryType.UnkB => QueryUnkBEvent.Serdes((QueryUnkBEvent)e, s),
            QueryType.UnkD => QueryUnkDEvent.Serdes((QueryUnkDEvent)e, s),
            QueryType.Unk13 => QueryUnk13Event.Serdes((QueryUnk13Event)e, s),
            QueryType.Unk24 => QueryUnk24Event.Serdes((QueryUnk24Event)e, s),
            QueryType.Unk25 => QueryUnk25Event.Serdes((QueryUnk25Event)e, s),
            QueryType.Unk26 => QueryUnk26Event.Serdes((QueryUnk26Event)e, s),
            QueryType.Unk27 => QueryUnk27Event.Serdes((QueryUnk27Event)e, s),
            _ => throw new FormatException($"Unexpected query type \"queryType\"")
        };
    }
}