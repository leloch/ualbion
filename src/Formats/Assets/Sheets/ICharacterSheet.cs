using UAlbion.Formats.Assets.Inv;
using UAlbion.Formats.Ids;

namespace UAlbion.Formats.Assets.Sheets;

public interface ICharacterSheet
{
    SheetId Id { get; }
    string GetName(string language);

    CharacterType Type { get; }
    Gender Gender { get; }
    PlayerRace Race { get; }
    PlayerClass PlayerClass { get; }
    ICharacterAttribute Age { get; }
    byte Level { get; }
    ushort ExperienceReward { get; } // XP pool contribution when killed in combat (sheet offset 0x20)

    // Visual
    SpriteId SpriteId { get; } // Overworld / 3D graphics
    SpriteId PortraitId { get; } // Conversation portrait
    SpriteId CombatGfx { get; } // Combat 3D graphics
    SpriteId TacticalGfx { get; } // Combat 2D graphics

    EventSetId EventSetId { get; }
    EventSetId WordSetId { get; } // Base set of conversation topics
    PlayerLanguages Languages { get; }

    // Grouped
    IMagicSkills Magic { get; }
    IInventory Inventory { get; }
    ICharacterAttributes Attributes { get; }
    ICharacterSkills Skills { get; }
    ICombatAttributes Combat { get; }
}