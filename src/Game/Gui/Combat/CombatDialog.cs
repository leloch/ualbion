using System;
using System.Collections.Generic;
using UAlbion.Api.Eventing;
using UAlbion.Formats.Assets.Save;
using UAlbion.Game.Combat;
using UAlbion.Game.Gui.Controls;

namespace UAlbion.Game.Gui.Combat;

/// <summary>
/// The top-level UI for a battle
/// </summary>
public class CombatDialog : Dialog
{
    public record ShowCombatDialogEvent(bool Show) : EventRecord, IVerboseEvent;
    readonly IReadOnlyBattle _battle;
    readonly Button _startRoundButton;

    public CombatDialog(int depth, IReadOnlyBattle battle) : base(DialogPositioning.Center, depth)
    {
        On<EndCombatEvent>(_ => Remove());
        On<ShowCombatDialogEvent>(e => SetVisible(e.Show));

        _battle = battle ?? throw new ArgumentNullException(nameof(battle));
        var stack = new List<IUiElement>();

        for (int row = 0; row < SavedGame.CombatRows; row++)
            stack.Add(BuildRow(row));

        stack.Add(new Spacing(0, 2));
        _startRoundButton =
            new Button(Base.SystemText.Combat_StartRound)
            {
                DoubleFrame = true
            }.OnClick(StartRound);

        stack.Add(new NonGreedy(_startRoundButton));
        stack.Add(new Spacing(0, 2));

        AttachChild(new DialogFrame(new VerticalStacker(stack) { ProgressiveOverlap = true })
        {
            Background = DialogFrameBackgroundStyle.MainMenuPattern
        });
    }

    void SetVisible(bool show)
    {
        foreach (var child in Children)
            child.IsActive = show;
    }

    void StartRound()
    {
        // The original hides the planning grid during round playback — the player watches
        // the battle view (monster animations, hit splashes) and the status-bar messages,
        // then the grid returns for the next round's orders. (Direct calls, not Raise —
        // Raise skips the sender's own handlers.)
        SetVisible(false);
        RaiseA(new BeginCombatRoundEvent()).OnCompleted(() => SetVisible(true));
    }

    HorizontalStacker BuildRow(int row)
    {
        var stack = new List<IUiElement>();

        for (int col = 0; col < SavedGame.CombatColumns; col++)
            stack.Add(new LogicalCombatTile(col + row * SavedGame.CombatColumns, _battle));

        return new HorizontalStacker(stack);
    }
}