using System.Linq;
using UAlbion.Api.Eventing;
using UAlbion.Core.Events;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;
using UAlbion.Game.Events;
using UAlbion.Game.Events.Inventory;
using UAlbion.Game.Scenes;
using UAlbion.Game.State;
using UAlbion.Game.Text;

namespace UAlbion.Game.Gui.Inventory;

public class InventoryScreenManager : Component
{
    AlbionTaskCore<bool> _source;
    bool _lockIsTrapped; // the current chest/door has a false-branch (trap) chain — RE docs/re/RE_CHEST_TRAP.md
    IEvent _modeEvent = new InventoryOpenEvent(PartyMemberId.None); // Should never be null.
    InventoryPage _page;
    PartyMemberId _activeCharacter;
    InventoryScreen _screen;

    public InventoryScreenManager()
    {
        OnQueryAsync<ChestEvent, bool>(OpenChest);
        OnQueryAsync<DoorEvent, bool>(OpenDoor);
        OnAsync<MerchantEvent>(TalkToMerchant);
        On<InventoryOpenEvent>(e => SetDisplayedPartyMember(e.PartyMemberId));
        On<InventorySetPageEvent>(e => _page = e.Page);
        On<InventoryOpenPositionEvent>(e =>
        {
            var party = Resolve<IParty>();
            if (party?.StatusBarOrder.Count > e.Position)
                SetDisplayedPartyMember(party.StatusBarOrder[e.Position].Id);
        });
        On<InventoryCloseEvent>(_ => InventoryClosed(false, false));
        On<LockOpenedEvent>(_ => LockOpened());
        On<LockPickFailedEvent>(_ => OnLockPickFailed());
        On<TakeAllEvent>(_ =>
        {
            if (_modeEvent is ChestEvent chest)
                Raise(new InventoryTakeAllEvent(chest.ChestId));
        });
    }

    AlbionTask TalkToMerchant(MerchantEvent e)
    {
        _source?.SetResult(false);
        _source = new AlbionTaskCore<bool>("InventoryScreenManager.TalkToMerchant");
        SetMode(e);
        return _source.Task.AsUntyped;
    }

    AlbionTask<bool> OpenDoor(DoorEvent e)
    {
        _source?.SetResult(false);
        _source = new AlbionTaskCore<bool>("InventoryScreenManager.OpenDoor");
        _lockIsTrapped = CurrentLockIsTrapped();
        Raise(new PushSceneEvent(SceneId.Inventory));
        SetMode(e);
        return _source.Task;
    }

    AlbionTask<bool> OpenChest(ChestEvent e)
    {
        _source?.SetResult(false);
        _source = new AlbionTaskCore<bool>("InventoryScreenManager.OpenChest");
        _lockIsTrapped = CurrentLockIsTrapped();
        Raise(new PushSceneEvent(SceneId.Inventory));
        SetMode(e);
        return _source.Task;
    }

    // A chest/door is "trapped" iff its event node has a false branch — the per-chest trap chain
    // (RE'd docs/re/RE_CHEST_TRAP.md: there is no trap byte; the NextIfFalse link IS the arm signal). The
    // chain interpreter parks this node on the context while the open query is awaited, so it's
    // readable here. Returning false from the query (triggeredTrap) routes the chain down that
    // false branch, exactly as the original fires the trap.
    static bool CurrentLockIsTrapped() =>
        (Context as EventContext)?.Node is IBranchNode branch && branch.NextIfFalse != null;

    // A failed SKILL pick (LockPickFailedEvent): trapped locks roll the leader's Dexterity to evade
    // (PercentRoll(Dex,100), fcn at door.c 0x5ab33); a failed evade springs the trap = close with
    // triggeredTrap so the event chain takes the false (trap) branch. Untrapped locks no-op here →
    // unlimited free retries. The key/Lockpick-item path never reaches this (it bypasses the trap).
    void OnLockPickFailed()
    {
        if (!_lockIsTrapped)
            return;

        var leader = Resolve<IParty>().Leader;
        int dex = leader?.Effective?.Attributes?.Dexterity?.Current ?? 0;
        int roll = Resolve<UAlbion.Game.IRandom>().Generate(100);
        if (UAlbion.Game.Combat.DamageCalculator.PercentRoll(dex, roll))
            return; // evaded — free retry

        InventoryClosed(triggeredTrap: true, unlocked: false); // springs the trap (false branch)
    }

    void SetMode(IEvent e)
    {
        _modeEvent = e;
        SetDisplayedPartyMember(null);

        if (e is ILockedInventoryEvent locked && locked.OpenedText != 255)
            Raise(new TextEvent(locked.OpenedText, TextLocation.NoPortrait, SheetId.None));
    }

    void SetDisplayedPartyMember(PartyMemberId? member)
    {
        if (Resolve<ISceneManager>().ActiveSceneId != SceneId.Inventory)
        {
            Raise(new PushSceneEvent(SceneId.Inventory));
            Raise(new SetClearColourEvent(0, 0, 0, 1));
        }

        var party = Resolve<IParty>();
        member ??= party.Leader.Id;
        if (party.WalkOrder.All(x => x.Id != member.Value))
            member = party.Leader.Id;

        _activeCharacter = member.Value;

        Raise(new SetContextEvent(ContextType.Inventory, member.Value));
        Rebuild();
    }

    void Rebuild()
    {
        _screen?.Remove();
        var scene = Resolve<ISceneManager>().GetScene(SceneId.Inventory);
        _screen = new InventoryScreen(_modeEvent, _activeCharacter, () => _page);
        scene.Add(_screen);
    }

    void LockOpened()
    {
        // RE'd (chest dispatcher 0x58c59): the UnlockedText byte (+5) is shown on the unlock path
        // after a successful unlock — distinct from OpenedText (+4), shown when the lock UI first
        // opened (SetMode). This convergence point covers every unlock method (key/lockpick/skill).
        if (_modeEvent is ILockedInventoryEvent locked && locked.UnlockedText != 255)
            Raise(new TextEvent(locked.UnlockedText, TextLocation.NoPortrait, SheetId.None));
        InventoryClosed(false, true);
    }

    void InventoryClosed(bool triggeredTrap, bool unlocked)
    {
        _modeEvent = new InventoryOpenEvent(PartyMemberId.None);
        _screen?.Remove();
        _screen = null;
        Raise(new PopSceneEvent());

        var source = _source;
        _source = null;
        ((EventContext)Context).LastEventResult = unlocked;
        // triggeredTrap=false → query returns true → chain takes the Next branch (normal). A sprung
        // trap returns false → the chain takes NextIfFalse (the trap chain). RE docs/re/RE_CHEST_TRAP.md.
        source?.SetResult(!triggeredTrap);
    }
}
