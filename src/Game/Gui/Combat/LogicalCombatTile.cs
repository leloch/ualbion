using System;
using System.Collections.Generic;
using System.Linq;
using UAlbion.Api.Eventing;
using UAlbion.Config;
using UAlbion.Core;
using UAlbion.Formats.Assets.Inv;
using UAlbion.Formats.Assets.Save;
using UAlbion.Formats.Assets.Sheets;
using UAlbion.Formats.Ids;
using UAlbion.Game.Combat;
using UAlbion.Game.Events;
using UAlbion.Game.Gui.Controls;
using UAlbion.Game.Input;
using UAlbion.Game.Scenes;
using UAlbion.Game.Text;

namespace UAlbion.Game.Gui.Combat;

public class LogicalCombatTile : UiElement
{
    class NopEvent : Event { }

    readonly int _tileIndex;
    readonly IReadOnlyBattle _battle;

    public LogicalCombatTile(int tileIndex, IReadOnlyBattle battle)
    {
        _tileIndex = tileIndex;
        _battle = battle ?? throw new ArgumentNullException(nameof(battle));

        AttachChild(new VisualCombatTile(tileIndex, battle))
            .OnClick(() =>
            {
            })
            .OnRightClick(OnRightClick)
            .OnHover(Hover)
            .OnBlur(Blur);
    }

    public override string ToString() => $"CombatTile:{_tileIndex}";

    void Blur()
    {
        // Raise(new SetCursorEvent(hand.Item.IsNone ? Base.CoreGfx.Cursor : Base.CoreGfx.CursorSmall));
        // Raise(new HoverTextEvent(null));
    }

    void Hover()
    {
        // _visual.Hoverable = true;
    }

    bool IsMagicItem(IReadOnlyItemSlot slot)
    {
        if (slot.Item.IsNone || slot.Item.Type != AssetType.Item)
            return false;

        var item = Assets.LoadItem(slot.Item);
        if (item == null)
            return false;

        return !item.Spell.IsNone;
    }

    void OnRightClick()
    {
        var contents = _battle.GetTile(_tileIndex);
        var sheet = contents?.Effective;

        var tf = Resolve<ITextFormatter>();
        var window = Resolve<IGameWindow>();
        var cursorManager = Resolve<ICursorManager>();

        var playerName = sheet?.GetName(ReadVar(V.User.Gameplay.Language));
        IText heading = playerName == null
            ? tf.Center().NoWrap().Fat().Format(Base.SystemText.Combat_Combat)
            : tf.Center().NoWrap().Fat().Format(playerName);

        IText S(TextId textId, bool disabled = false)
            => tf
                .Center()
                .NoWrap()
                .Ink(disabled ? Base.Ink.Yellow : Base.Ink.White)
                .Format(textId);

        // Drop (Yellow inactive when critical)
        // Examine
        // Use (e.g. torch)
        // Drink
        // Activate (compass, clock, monster eye)
        // Activate spell (if has spell, yellow if combat spell & not in combat etc)
        // Read (e.g. metal-magic knowledge, maps)

        var options = new List<ContextMenuOption>();

        if (sheet?.Type == CharacterType.Party)
        {
            var actor = contents.SheetId;

            options.Add(new ContextMenuOption(
                S(Base.SystemText.Combat_DoNothing),
                new QueueCombatActionEvent(actor, CombatAction.None, -1),
                ContextMenuGroup.Actions));

            options.Add(new ContextMenuOption(
                S(Base.SystemText.Combat_Attack, sheet.DisplayDamage <= 0),
                new QueueCombatActionEvent(actor, CombatAction.Melee, -1),
                ContextMenuGroup.Actions));

            options.Add(new ContextMenuOption(
                S(Base.SystemText.Combat_Move),
                new NopEvent(), // PLACEHOLDER: needs target-tile picker; queueing without a tile is a no-op
                ContextMenuGroup.Actions));

            if (sheet.Magic.KnownSpells.Count > 0)
            {
                options.Add(new ContextMenuOption(
                    S(Base.SystemText.Combat_UseMagic),
                    new NopEvent(), // PLACEHOLDER: needs spell picker dialog
                    ContextMenuGroup.Actions));
            }

            if (sheet.Inventory.EnumerateAll().Any(IsMagicItem))
            {
                options.Add(new ContextMenuOption(
                    S(Base.SystemText.Combat_UseMagicItem),
                    new NopEvent(), // PLACEHOLDER: needs item picker dialog
                    ContextMenuGroup.Actions));
            }

            // Flee is the back-row Retreat action (party occupies the bottom row in the
            // combat grid → row index CombatRows-1). The original engine's gate is at
            // fcn.0004f5d3: only members in row 0/CombatRows-1 can flee.
            int row = _tileIndex / SavedGame.CombatColumns;
            bool canFlee = row == SavedGame.CombatRows - 1
                        || row == 0;
            if (canFlee)
            {
                options.Add(new ContextMenuOption(
                    S(Base.SystemText.Combat_Flee),
                    new QueueCombatActionEvent(actor, CombatAction.Retreat, -1),
                    ContextMenuGroup.Actions));
            }
        }

        options.Add(new ContextMenuOption(
            S(Base.SystemText.Combat_AdvanceParty),
            new NopEvent(),
            ContextMenuGroup.Actions2));

        options.Add(new ContextMenuOption(
            S(Base.SystemText.Combat_Observe),
            new ObserveCombatEvent(),
            ContextMenuGroup.System));

        options.Add(new ContextMenuOption(
            S(Base.SystemText.MapPopup_MainMenu),
            new PushSceneEvent(SceneId.MainMenu),
            ContextMenuGroup.System));

        options.Add(new ContextMenuOption(
            S(Base.SystemText.Combat_EndCombat),
            new EndCombatEvent(CombatResult.Victory),
            ContextMenuGroup.System));

        var uiPosition = window.PixelToUi(cursorManager.Position);
        Raise(new ContextMenuEvent(uiPosition, heading, options));
    }
}
