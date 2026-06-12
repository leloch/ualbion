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
        // RE 5C (door.c, button cb 0x5ab33): effective Lockpicking >= difficulty →
        // automatic success; else PercentRoll(skill·(100−difficulty)/100, 100) — i.e.
        // rand()%100 <= chance. Difficulty >= 100 is rejected before this. Untrapped
        // locks allow unlimited free retries (msg 538); a trapped lock rolls the
        // leader's Dexterity to evade on failure (handled by the chest's trap chain).
        int skill = player.Effective.Skills.LockPicking.Current;
        if (skill <= 0)
            return false;
        if (skill >= _lockEvent.PickDifficulty)
            return true;

        int chance = skill * (100 - _lockEvent.PickDifficulty) / 100;
        return RaiseQuery(new QueryRandomChanceEvent((ushort)(chance + 1), QueryOperation.GreaterThan, 0));
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
