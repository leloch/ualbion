using System.Collections.Generic;
using UAlbion.Api.Eventing;
using UAlbion.Config;
using UAlbion.Formats.Assets;
using UAlbion.Formats.Assets.Inv;
using UAlbion.Formats.Assets.Maps;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;
using UAlbion.Game.Events;
using UAlbion.Game.State;

namespace UAlbion.Game.Diag;

/// <summary>
/// Handlers for the harness-only diagnostic events in <see cref="DiagnosticEvents"/>. Kept in
/// one component so the game-logic classes (ScriptManager, MapManager, ...) stay free of test
/// scaffolding — these only ever run when a diagnostic event is fired by the HTTP harness or
/// the debug console.
/// </summary>
public class DiagnosticsManager : GameComponent
{
    public DiagnosticsManager()
    {
        On<DebugTrappedChestEvent>(FireTrappedChest);
        On<DebugFindCursedItemsEvent>(_ => FindCursedItems());
        On<DebugFindRangedMonstersEvent>(_ => FindRangedMonsters());
        On<DumpStringEvent>(DumpString);
        On<DumpChainEvent>(DumpChain);
    }

    /// <summary>
    /// Build a locked chest whose false branch points at a real trap node and trigger it.
    /// Chain: 0 = locked chest (true -> 2 success, false -> 1 trap), 1 = trap damage, 2 = no-op.
    /// </summary>
    void FireTrappedChest(DebugTrappedChestEvent e)
    {
        var leader = Resolve<IParty>().Leader;
        if (leader == null)
            return;

        var target = new TargetId(AssetType.PartyMember, leader.Id.Id);
        var trap = new DataChangeEvent(target, ChangeProperty.Health, NumericOperation.SubtractAmount, e.Damage);
        var success = new CommentEvent("trapped chest opened without springing the trap");
        // Difficulty 90 = hard but not unpickable, so the pick actually rolls and can fail.
        var chestEvent = new ChestEvent(new ChestId(AssetType.Chest, (ushort)Base.Chest.Unknown1_Empty), ItemId.None, 90, 255, 255);

        var trapNode = new EventNode(1, trap);
        var successNode = new EventNode(2, success);
        var chestNode = new BranchNode(0, chestEvent) { Next = successNode, NextIfFalse = trapNode };
        var nodes = new EventNode[] { chestNode, trapNode, successNode };

        var mapId = Resolve<IMapManager>().Current.MapId;
        var set = new ScriptEventSet(mapId, mapId.ToMapText(), nodes);
        Raise(new TriggerChainEvent(set, 0, new EventSource(mapId, TriggerType.Action)));
    }

    void FindCursedItems()
    {
        int count = 0;
        foreach (var id in AssetMapping.Global.EnumerateAssetsOfType(AssetType.Item))
        {
            var item = Assets.LoadItem(id);
            if (item == null || (item.Flags & ItemFlags.Cursed) == 0)
                continue;
            count++;
            Info($"[CursedItem] {id} type={item.TypeId}");
            TraceLog.Emit("cursed_item", ("id", id), ("type", item.TypeId));
        }

        Info($"[CursedItem] {count} cursed item(s) in ITEMLIST");
        TraceLog.Emit("cursed_item_count", ("count", count));
    }

    void FindRangedMonsters()
    {
        int count = 0;
        foreach (var id in AssetMapping.Global.EnumerateAssetsOfType(AssetType.MonsterSheet))
        {
            var inv = Assets.LoadSheet(id)?.Inventory; // implicit AssetId -> SheetId
            if (inv == null)
                continue;

            foreach (var slot in inv.EnumerateAll())
            {
                if (slot.Item.Type != AssetType.Item)
                    continue;

                var item = Assets.LoadItem(slot.Item);
                if (item == null || item.TypeId != ItemType.LongRangeWeapon)
                    continue;

                count++;
                Info($"[RangedMonster] {id} carries {slot.Item}");
                TraceLog.Emit("ranged_monster", ("sheet", id), ("weapon", slot.Item));
                break;
            }
        }

        Info($"[RangedMonster] {count} monster sheet(s) with long-range weapons");
        TraceLog.Emit("ranged_monster_count", ("count", count));
    }

    void DumpString(DumpStringEvent e)
    {
        var setId = new EventSetId(AssetType.EventSet, e.EventSetId).ToEventText();
        var text = Assets.LoadStringSafe(new StringId(setId, e.SubId));
        Info($"[DumpString] {setId}:{e.SubId} = {text}");
        TraceLog.Emit("string", ("set", setId), ("sub", e.SubId), ("raw", text ?? "<null>"));
    }

    void DumpChain(DumpChainEvent e)
    {
        if (TryResolve<IMapManager>()?.Current?.MapData is not BaseMapData mapData)
        {
            Warn("[DumpChain] no map loaded");
            return;
        }

        // A number below the chain count is a chain ordinal; anything larger is treated as a
        // raw event index (map zones carry chain ids that don't always index the Chains list).
        int entryIndex = e.Chain < mapData.Chains.Count ? mapData.Chains[e.Chain] : e.Chain;
        if (entryIndex >= mapData.Events.Count)
        {
            Warn($"[DumpChain] {e.Chain} out of range ({mapData.Chains.Count} chains, {mapData.Events.Count} events)");
            return;
        }

        var visited = new HashSet<ushort>();
        var queue = new Queue<IEventNode>();
        queue.Enqueue(mapData.Events[entryIndex]);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node == null || !visited.Add(node.Id))
                continue;

            var branch = node as IBranchNode;
            int falseId = branch != null ? branch.NextIfFalse?.Id ?? -1 : -2; // -2 = not a branch
            string falsePart = branch != null ? $" false=>{branch.NextIfFalse?.Id.ToString() ?? "!"}" : "";

            Info($"[Chain {e.Chain}] {node.Id} => {node.Next?.Id.ToString() ?? "!"}{falsePart}: {node.Event}");
            TraceLog.Emit("chain_node",
                ("chain", e.Chain), ("id", node.Id),
                ("next", node.Next?.Id ?? -1), ("false", falseId),
                ("event", node.Event?.ToString() ?? ""));

            if (node.Next != null) queue.Enqueue(node.Next);
            if (branch?.NextIfFalse != null) queue.Enqueue(branch.NextIfFalse);
        }
    }
}
