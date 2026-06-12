using System.Collections.Generic;
using UAlbion.Api.Eventing;
using UAlbion.Formats.Ids;
using UAlbion.Game.Gui.Controls;
using UAlbion.Game.State;
using UAlbion.Game.Text;

namespace UAlbion.Game.Combat;

/// <summary>Opens a context menu listing the actor's known spells (UseMagic).</summary>
public record ShowCombatSpellMenuEvent(SheetId Actor) : EventRecord;

/// <summary>Opens a context menu listing the actor's charged magic items (UseMagicItem).</summary>
public record ShowCombatItemMenuEvent(SheetId Actor) : EventRecord;

/// <summary>Arms the target-tile picker: the next combat tile the player left-clicks
/// completes the pending action as a QueueCombatActionEvent.</summary>
public record BeginCombatTargetPickEvent(SheetId Actor, CombatAction Action, SpellId Spell = default, ItemId Item = default) : EventRecord;

/// <summary>Raised by LogicalCombatTile when a combat grid tile is left-clicked.</summary>
public record CombatTilePickedEvent(int TileIndex) : EventRecord;

/// <summary>
/// Drives the two-step combat action selections: "Move" / "Use magic" / "Use magic item"
/// from the per-tile context menu first pick a spell or item (via a second context menu),
/// then a target tile (via a left-click on the combat grid), and finally queue the
/// completed choice as a <see cref="QueueCombatActionEvent"/> for the next round.
/// Attached by <see cref="Battle"/> so its lifetime matches the combat.
/// </summary>
public class CombatActionPicker : GameComponent
{
    (SheetId Actor, CombatAction Action, SpellId Spell, ItemId Item)? _pending;

    public CombatActionPicker()
    {
        On<BeginCombatTargetPickEvent>(e =>
        {
            _pending = (e.Actor, e.Action, e.Spell, e.Item);
            Info($"[Combat] Pick a target tile for {e.Actor} {e.Action}" +
                 (e.Spell.IsNone ? "" : $" spell {e.Spell}") +
                 (e.Item.IsNone ? "" : $" item {e.Item}"));
        });

        On<CombatTilePickedEvent>(e =>
        {
            if (_pending == null)
                return;

            var (actor, action, spell, item) = _pending.Value;
            _pending = null;
            Raise(new QueueCombatActionEvent(actor, action, e.TileIndex, spell, item));
            Info($"[Combat] {actor} queued {action} on tile {e.TileIndex}");
        });

        On<ShowCombatSpellMenuEvent>(e => ShowSpellMenu(e.Actor));
        On<ShowCombatItemMenuEvent>(e => ShowItemMenu(e.Actor));
    }

    void ShowSpellMenu(SheetId actor)
    {
        var sheet = Resolve<IGameState>().GetSheet(actor);
        if (sheet == null)
            return;

        var tf = Resolve<ITextFormatter>();
        var options = new List<ContextMenuOption>();
        foreach (var spellId in sheet.Magic.KnownSpells)
        {
            var spell = Assets.LoadSpell(spellId);
            if (spell == null)
                continue;

            // Only combat-capable spells; SP gating: grey out unaffordable later (first
            // pass lists all known combat spells).
            if ((spell.Environments & UAlbion.Formats.Assets.SpellEnvironments.Combat) == 0)
                continue;

            var name = spell.Name.IsNone
                ? tf.Center().NoWrap().Format(spellId.ToString())
                : tf.Center().NoWrap().Format((TextId)spell.Name);
            options.Add(new ContextMenuOption(
                name,
                new BeginCombatTargetPickEvent(actor, CombatAction.CastSpell, spellId),
                ContextMenuGroup.Actions));
        }

        if (options.Count == 0)
            return;

        ShowMenu(tf.Center().NoWrap().Fat().Format(Base.SystemText.Combat_UseMagic), options);
    }

    void ShowItemMenu(SheetId actor)
    {
        var sheet = Resolve<IGameState>().GetSheet(actor);
        if (sheet == null)
            return;

        var tf = Resolve<ITextFormatter>();
        var options = new List<ContextMenuOption>();
        foreach (var slot in sheet.Inventory.EnumerateAll())
        {
            if (slot.Item.IsNone || slot.Item.Type != UAlbion.Config.AssetType.Item)
                continue;

            var item = Assets.LoadItem(slot.Item);
            if (item == null || item.Spell.IsNone || item.Charges == 0)
                continue;

            var name = tf.Center().NoWrap().Format(item.Name);
            options.Add(new ContextMenuOption(
                name,
                new BeginCombatTargetPickEvent(actor, CombatAction.UseItem, item.Spell, slot.Item),
                ContextMenuGroup.Actions));
        }

        if (options.Count == 0)
            return;

        ShowMenu(tf.Center().NoWrap().Fat().Format(Base.SystemText.Combat_UseMagicItem), options);
    }

    void ShowMenu(IText heading, List<ContextMenuOption> options)
    {
        var window = Resolve<UAlbion.Core.IGameWindow>();
        var cursorManager = Resolve<UAlbion.Game.Input.ICursorManager>();
        var uiPosition = window.PixelToUi(cursorManager.Position);
        Raise(new ContextMenuEvent(uiPosition, heading, options));
    }
}
