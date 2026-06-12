using UAlbion.Formats.Assets.Save;
using UAlbion.Formats.Assets.Sheets;
using UAlbion.Formats.Ids;

namespace UAlbion.Game.State;

public interface ICombatParticipant
{
    int CombatPosition { get; }
    int X => CombatPosition % SavedGame.CombatColumns;
    int Y => CombatPosition / SavedGame.CombatColumns;
    SheetId SheetId { get; }
    // Combat visuals come from Effective.TacticalGfx / Effective.CombatGfx —
    // the sheet derives them for both party members and monsters.
    IEffectiveCharacterSheet Effective { get; }
}