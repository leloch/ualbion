using System.Collections.Generic;
using UAlbion.Config;
using UAlbion.Core;
using UAlbion.Formats;
using UAlbion.Formats.Assets.Inv;
using UAlbion.Formats.MapEvents;
using UAlbion.Game.Events;
using UAlbion.Game.Events.Inventory;
using UAlbion.Game.Gui.Controls;
using UAlbion.Game.Input;
using UAlbion.Game.State;
using UAlbion.Game.State.Player;
using UAlbion.Game.Text;

namespace UAlbion.Game.Gui.Inventory;

public class InventoryLockPane : UiElement
{
    readonly ILockedInventoryEvent _lockEvent;

    public InventoryLockPane(ILockedInventoryEvent lockEvent)
    {
        On<PickLockEvent>(_ => PickLock());
        _lockEvent = lockEvent;
        bool isChest = lockEvent is ChestEvent;
        var background = new UiSpriteElement(isChest ? Base.Picture.ClosedChest : Base.Picture.WoodenDoor);
        var backgroundStack = new FixedPositionStacker();
        backgroundStack.Add(background, 0, 0);
        AttachChild(backgroundStack);

        var lockButton = 
                new Button(
                        new Padding(
                            new UiSpriteElement(Base.CoreGfx.Lock),
                            4))
                    .OnHover(LockHovered)
                    .OnBlur(() =>
                    {
                        Raise(new HoverTextEvent(null));
                        if (Resolve<IInventoryManager>().ItemInHand.Item.IsNone)
                            Raise(new SetCursorEvent(Base.CoreGfx.Cursor));
                    })
                    .OnClick(LockClicked) // If holding key etc
                    .OnRightClick(LockRightClicked)
            ;
        AttachChild(new FixedPosition(new Rectangle(50, isChest ? 50 : 112, 32, 32), lockButton));
    }

    void LockHovered()
    {
        var tf = Resolve<ITextFormatter>();
        Raise(new HoverTextEvent(tf.Format(Base.SystemText.Lock_OpenTheLock)));
        if (Resolve<IInventoryManager>().ItemInHand.Item.IsNone)
            Raise(new SetCursorEvent(Base.CoreGfx.CursorSelected));
    }

    void LockClicked()
    {
        var hand = Resolve<IInventoryManager>().ItemInHand;
        if (hand.Item.IsNone)
            return;

        var tf = Resolve<ITextFormatter>();
        if (hand.Item == _lockEvent.Key)
        {
            Raise(new HoverTextEvent(tf.Format(Base.SystemText.Lock_LeaderOpenedTheLock)));
            // RE _RE_5C.md §2.2 (0x5a92a): a matching key is consumed ONLY if it carries the
            // "vanish when used up" flag (ITEMLIST +0x1B & 0x10 = ItemFlags.Unk4) — one-use quest
            // keys; ordinary reusable keys return to the hand as before.
            var keyItem = Assets.LoadItem(hand.Item);
            if (keyItem != null && (keyItem.Flags & ItemFlags.Unk4) != 0)
                Raise(new InventoryDestroyItemInHandEvent());
            else
                Raise(new InventoryReturnItemInHandEvent());
            Raise(new LockOpenedEvent());
        }
        else if (hand.Item == Base.Item.Lockpick)
        {
            if (_lockEvent.PickDifficulty == 100)
            {
                Raise(new DescriptionTextEvent(tf.Format(Base.SystemText.Lock_ThisLockCannotBePicked)));
                Raise(new InventoryDestroyItemInHandEvent());
            }
            else
            {
                Raise(new InventoryDestroyItemInHandEvent());
                Raise(new DescriptionTextEvent(tf.Format(Base.SystemText.Lock_LeaderPickedTheLockWithALockpick)));
                Raise(new LockOpenedEvent());
            }
        }
        else if (hand.Item.Type == AssetType.Item)
        {
            var item = Assets.LoadItem(hand.Item) 
                       ?? throw new AssetNotFoundException($"Could not load item {hand.Item}", hand.Item);

            Raise(new DescriptionTextEvent(tf.Format(
                item.TypeId == ItemType.Key 
                    ? Base.SystemText.Lock_ThisIsNotTheRightKey 
                    : Base.SystemText.Lock_YouCannotOpenTheLockWithThisItem)));
        }
    }

    bool CanPick(IPlayer player)
    {
        // RE 5C (door.c, button cb 0x5ab33): see CombatFormulas.Lockpick*. Difficulty >=
        // 100 unpickable; effective Lockpicking >= difficulty auto-succeeds; else a percent
        // roll of skill·(100−difficulty)/100. Untrapped locks allow unlimited free retries
        // (msg 538); a trapped lock rolls Dexterity to evade on failure (chest trap chain).
        int skill = player.Effective.Skills.LockPicking.Current;
        int difficulty = _lockEvent.PickDifficulty;
        if (UAlbion.Game.Combat.CombatFormulas.LockpickUnpickable(difficulty) || skill <= 0)
            return false;
        if (UAlbion.Game.Combat.CombatFormulas.LockpickAutoSuccess(skill, difficulty))
            return true;

        int chance = UAlbion.Game.Combat.CombatFormulas.LockpickChancePercent(skill, difficulty);
        return RaiseQuery(new QueryRandomChanceEvent((ushort)chance, QueryOperation.GreaterThan, 0));
    }

    void PickLock()
    {
        var tf = Resolve<ITextFormatter>();
        if (_lockEvent.PickDifficulty >= 100)
        {
            Raise(new DescriptionTextEvent(tf.Format(Base.SystemText.Lock_ThisLockCannotBePicked)));
            return;
        }

        var leader = Resolve<IParty>().Leader;
        if (CanPick(leader))
        {
            Raise(new DescriptionTextEvent(tf.Format(Base.SystemText.Lock_LeaderPickedTheLock)));
            Raise(new LockOpenedEvent());
        }
        else
        {
            Raise(new DescriptionTextEvent(tf.Format(Base.SystemText.Lock_LeaderCannotPickThisLock)));
            // A failed SKILL pick springs the trap on a trapped lock (the manager rolls Dexterity to
            // evade and, on failure, fires the chest/door's trap chain). Untrapped locks ignore this
            // and allow unlimited free retries. RE: _RE_CHEST_TRAP.md.
            Raise(new LockPickFailedEvent());
        }
    }

    void LockRightClicked()
    {
        // ContextMenu: Lock, Pick the lock
        var options = new List<ContextMenuOption>();
        var tf = Resolve<ITextFormatter>();
        var window = Resolve<IGameWindow>();
        var cursorManager = Resolve<ICursorManager>();

        options.Add(new ContextMenuOption(
            tf.Center().NoWrap().Format(Base.SystemText.Lock_PickTheLock),
            new PickLockEvent(),
            ContextMenuGroup.Actions));

        var heading = tf.Center().NoWrap().Fat().Format(Base.SystemText.Lock_Lock);
        var uiPosition = window.PixelToUi(cursorManager.Position);
        Raise(new ContextMenuEvent(uiPosition, heading, options));
    }
}
