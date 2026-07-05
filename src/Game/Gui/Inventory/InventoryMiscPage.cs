using System.Collections.Generic;
using System.Linq;
using UAlbion.Formats.Assets.Sheets;
using UAlbion.Formats.Ids;
using UAlbion.Game.Events;
using UAlbion.Game.Gui.Controls;
using UAlbion.Game.Gui.Text;
using UAlbion.Game.State;
using UAlbion.Game.Text;

namespace UAlbion.Game.Gui.Inventory;

/// <summary>
/// Character sheet page III: active conditions, known languages and temporary spell effects
/// (the active-spell table entries — physical defense / magic resistance boosts and their
/// remaining hours), plus the combat-positions dialog button. Content is dynamic so curing
/// a condition or an hourly spell decay updates the page live.
/// </summary>
public class InventoryMiscPage : UiElement
{
    static readonly (PlayerConditions Flag, TextId Text)[] ConditionText =
    [
        (PlayerConditions.Unconscious, (TextId)Base.SystemText.Condition_Unconscious),
        (PlayerConditions.Poisoned,    (TextId)Base.SystemText.Condition_Poisoned),
        (PlayerConditions.Ill,         (TextId)Base.SystemText.Condition_Ill),
        (PlayerConditions.Exhausted,   (TextId)Base.SystemText.Condition_Exhausted),
        (PlayerConditions.Paralysed,   (TextId)Base.SystemText.Condition_Paralyzed),
        (PlayerConditions.Fleeing,     (TextId)Base.SystemText.Condition_Fleeing),
        (PlayerConditions.Intoxicated, (TextId)Base.SystemText.Condition_Intoxicated),
        (PlayerConditions.Blind,       (TextId)Base.SystemText.Condition_Blind),
        (PlayerConditions.Panicking,   (TextId)Base.SystemText.Condition_Panicking),
        (PlayerConditions.Asleep,      (TextId)Base.SystemText.Condition_Asleep),
        (PlayerConditions.Insane,      (TextId)Base.SystemText.Condition_Insane),
        (PlayerConditions.Irritated,   (TextId)Base.SystemText.Condition_Irritated),
    ];

    static readonly (PlayerLanguages Flag, TextId Text)[] LanguageText =
    [
        (PlayerLanguages.Terran, (TextId)Base.SystemText.Lang_Terran),
        (PlayerLanguages.Iskai,  (TextId)Base.SystemText.Lang_Iskai),
        (PlayerLanguages.Celtic, (TextId)Base.SystemText.Lang_Celtic),
    ];

    readonly PartyMemberId _activeCharacter;

    ICharacterSheet Sheet => Resolve<IParty>()[_activeCharacter]?.Apparent;

    IEnumerable<TextBlock> BuildConditions()
    {
        var tf = Resolve<ITextFormatter>();
        var conditions = Sheet?.Combat?.Conditions ?? PlayerConditions.None;
        foreach (var (flag, text) in ConditionText)
        {
            if ((conditions & flag) == 0)
                continue;
            var block = tf.Format(text).GetBlocks().First();
            block.ArrangementFlags = TextArrangementFlags.ForceNewLine;
            yield return block;
        }
    }

    IEnumerable<TextBlock> BuildLanguages()
    {
        var tf = Resolve<ITextFormatter>();
        var languages = Sheet?.Languages ?? PlayerLanguages.None;
        foreach (var (flag, text) in LanguageText)
        {
            if ((languages & flag) == 0)
                continue;
            var block = tf.Format(text).GetBlocks().First();
            block.ArrangementFlags = TextArrangementFlags.ForceNewLine;
            yield return block;
        }
    }

    IEnumerable<TextBlock> BuildTemporarySpells()
    {
        // The active-spell table (RE 5B): per-member type 1 = physical defense boost,
        // type 2 = magic resistance boost, decaying hourly; plus the party-wide Light
        // entry. Shown as "<effect> <pct>% (<hours>h)".
        var state = TryResolve<IGameState>();
        var tf = Resolve<ITextFormatter>();
        if (state == null)
            yield break;

        for (int type = 1; type <= 2; type++)
        {
            int pct = state.GetActiveSpellPct(_activeCharacter, type);
            int hours = state.GetActiveSpellHours(_activeCharacter, type);
            if (pct <= 0 || hours <= 0)
                continue;

            var name = tf.Format(type == 1
                ? Base.SystemText.Examine1_Protection
                : Base.SystemText.Attrib_MagicResistance).GetBlocks().First();
            name.Text += $" +{pct}% ({hours}h)";
            name.ArrangementFlags = TextArrangementFlags.ForceNewLine;
            yield return name;
        }
    }

    public InventoryMiscPage(PartyMemberId activeCharacter)
    {
        _activeCharacter = activeCharacter;

        var stack = new VerticalStacker(
            new Spacing(0, 1),
            new Header(Base.SystemText.Inv3_Conditions, 4) { Underline = true },
            new UiText(new DynamicText(BuildConditions)),
            new Spacing(0, 4),
            new Header(Base.SystemText.Inv3_Languages, 3) { Underline = true },
            new UiText(new DynamicText(BuildLanguages)),
            new Spacing(0, 4),
            new Header(Base.SystemText.Inv3_TemporarySpells, 3) { Underline = true },
            new UiText(new DynamicText(BuildTemporarySpells)),
            new Spacing(0, 6),
            new Button(Base.SystemText.Inv3_CombatPositions)
            {
                DoubleFrame = true
            }.OnClick(() => Raise(new ShowCombatPositionsDialogEvent()))
        );
        AttachChild(stack);
    }
}
