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

    // Combat flags / creature-class bitmask (sheet offset 0x0E). RE'd from MAIN.EXE:
    // bit 7 (0x80) = crit-immunity (checked in the strike callbacks fcn.0004eac1/0004f057);
    // the low bits feed the monster-class mask of the spell success gate fcn.000601a6
    // (demons = mask 0x44 — the all-demons value 20 is the only sheet value with bit 2 set).
    byte UnknownE { get; }

    // Visual
    SpriteId SpriteId { get; } // Overworld / 3D graphics
    SpriteId PortraitId { get; } // Conversation portrait
    SpriteId CombatGfx { get; } // Combat 3D graphics
    SpriteId TacticalGfx { get; } // Combat 2D graphics

    EventSetId EventSetId { get; }
    EventSetId WordSetId { get; } // Base set of conversation topics
    PlayerLanguages Languages { get; }

    MonsterData Monster { get; } // Monster-only data (combat gfx animations, scaling); null for party/NPC sheets

    // Grouped
    IMagicSkills Magic { get; }
    IInventory Inventory { get; }
    ICharacterAttributes Attributes { get; }
    ICharacterSkills Skills { get; }
    ICombatAttributes Combat { get; }
}