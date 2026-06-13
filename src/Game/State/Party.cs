using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using UAlbion.Api.Eventing;
using UAlbion.Formats.Assets.Inv;
using UAlbion.Formats.Assets.Save;
using UAlbion.Formats.Assets.Sheets;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;
using UAlbion.Game.Events;
using UAlbion.Game.Gui.Inventory;
using UAlbion.Game.Text;

namespace UAlbion.Game.State;

public class Party : ServiceComponent<IParty>, IParty
{
    readonly IDictionary<SheetId, CharacterSheet> _characterSheets;
    readonly List<Player.PartyMember> _statusBarOrder = [];
    readonly List<Player.PartyMember> _walkOrder = [];
    readonly IReadOnlyList<Player.PartyMember> _readOnlyStatusBarOrder;
    readonly IReadOnlyList<Player.PartyMember> _readOnlyWalkOrder;
    readonly byte[] _combatPositions;

    public Party(IDictionary<SheetId, CharacterSheet> characterSheets, Func<InventoryId, Inventory> getInventory, byte[] combatPositions)
    {
        _characterSheets = characterSheets ?? throw new ArgumentNullException(nameof(characterSheets));
        _combatPositions = combatPositions ?? throw new ArgumentNullException(nameof(combatPositions));
        _readOnlyStatusBarOrder = _statusBarOrder.AsReadOnly();
        _readOnlyWalkOrder = _walkOrder.AsReadOnly();

        On<AddPartyMemberEvent>(e => SetLastResult(AddMember(e.PartyMemberId)));
        On<RemovePartyMemberEvent>(OnRemovePartyMember);
        On<SetPartyLeaderEvent>(e => SetLeader(e.PartyMemberId));
        On<SetPlayerCombatSlotEvent>(OnSetCombatSlot);

        AttachChild(new PartyInventory(getInventory));
    }

    void OnSetCombatSlot(SetPlayerCombatSlotEvent e)
    {
        for (int i = 0; i < _statusBarOrder.Count; i++)
        {
            if (_statusBarOrder[i].Id != e.Id)
                continue;

            _combatPositions[i] = (byte)e.Slot;
            return;
        }
    }

    [SuppressMessage("Design", "CA1043:Use Integral Or String Argument For Indexers", Justification = "<Pending>")]
    public IPlayer this[PartyMemberId id]
    {
        get
        {
            foreach (var x in _statusBarOrder) // Don't use LINQ, we want to avoid allocations
                if (x.Id == id)
                    return x;
            return null;
        }
    }

    public IPlayer Leader => _walkOrder[0];
    public IReadOnlyList<IPlayer> StatusBarOrder => _readOnlyStatusBarOrder;
    public IReadOnlyList<IPlayer> WalkOrder => _readOnlyWalkOrder;
    public int TotalGold => _statusBarOrder.Sum(x => x.Effective.Inventory.Gold.Amount);
    public int TotalRations => _statusBarOrder.Sum(x => x.Effective.Inventory.Rations.Amount);
    public int GetItemCount(ItemId item) =>
        _statusBarOrder
            .SelectMany(x => x.Effective.Inventory.EnumerateAll())
            .Where(x => x.Item == item)
            .Sum(x => x.Amount);

    // The current party leader (shown with a white outline on
    // health bar and slightly raised in the status bar)
    void SetLeader(PartyMemberId value)
    {
        int index = _walkOrder.FindIndex(x => x.Id == value);
        if (index == -1)
            return;

        var player = _walkOrder[index];
        _walkOrder.RemoveAt(index);
        _walkOrder.Insert(0, player);
        Raise(new SetContextEvent(ContextType.Leader, value));
    }

    public bool AddMember(PartyMemberId id)
    {
        if (_statusBarOrder.Any(x => x.Id == id))
            return false;

        var player = new Player.PartyMember(id, _characterSheets[id.ToSheet()]);
        if (_statusBarOrder.Count >= SavedGame.MaxPartySize) 
            return false;

        int index = _statusBarOrder.Count;
        _statusBarOrder.Add(player);

        // Allocate combat position
        if (_combatPositions[index] == 0)
        {
            for (int combatSlot = SavedGame.CombatColumns * SavedGame.CombatRowsForParty - 1; combatSlot >= 0; combatSlot--)
            {
                bool occupied = false;
                for (int i = 0; i < index && !occupied; i++)
                    if (_combatPositions[i] == combatSlot)
                        occupied = true;

                if (occupied)
                    continue;

                _combatPositions[index] = (byte)combatSlot;
                break;
            }
        }

        _walkOrder.Add(player);
        AttachChild(player);
        Raise(new PartyChangedEvent());
        return true;
    }

    // RE 5D §6 (RemovePartyMember fcn.0003822d): leaving the party re-enables the member's
    // home-map NPC by clearing its RemovedNpcs bit, so the NPC reappears at its scripted
    // post. The recruit chain hid it via modify_npc_off; the leave event carries the same
    // flat home index (mapId*96 + slot) in Unk6 (0 = no home NPC, nothing to restore).
    void OnRemovePartyMember(RemovePartyMemberEvent e)
    {
        bool removed = RemoveMember(e.PartyMemberId);
        if (removed && HomeNpcIndex.TryDecode(e.Unk6, out var map, out var slot))
            Raise(new ModifyNpcOffEvent(SwitchOperation.Clear, slot, map));
        SetLastResult(removed);
    }

    bool RemoveMember(PartyMemberId id)
    {
        int index = _statusBarOrder.FindIndex(x => x.Id == id);
        if (index < 0)
            return false;

        var player = _statusBarOrder[index];
        _walkOrder.Remove(player);
        _statusBarOrder.RemoveAt(index);

        // STATE-02: _combatPositions is keyed in lockstep with _statusBarOrder, so compact it too
        // — otherwise members after the removed one keep the wrong battle-grid slot (and the
        // corruption persists in the save via SavedGame.CombatPositions).
        for (int i = index; i < _combatPositions.Length - 1; i++)
            _combatPositions[i] = _combatPositions[i + 1];
        _combatPositions[^1] = 0;

        player.Remove();
        Raise(new PartyChangedEvent());
        return true;
    }

    static void SetLastResult(bool result) => ((EventContext)Context).LastEventResult = result;

    public void Clear()
    {
        foreach(var id in _statusBarOrder.Select(x => x.Id).ToList())
            RemoveMember(id);
    }
}
