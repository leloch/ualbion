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

    // Combat morale 0..100 (sheet offset 0x0F): a monster flees when
    // (deadMonsterPct + ownLostLpPct)/2 >= Morale (fcn.00051506, RE batch 5A).
    byte Morale { get; }

    // AI behaviour/strategy id (MONCHAR sheet offset 0x0C → table 0x13e1f0, RE 5A):
    // selects the fight-predicate variant (1/3/4/5/6/9 = morale formula, 2 = flees once
    // any monster dies, 7 = outnumbered+LP threshold, 8 = caster stay-back).
    byte UnkownC { get; }

    // Battle-view render class (MONCHAR sheet offset 0x0D, RE 5A; 0 defaults to 1):
    // 1 = ground (no idle motion), 2 = ghostly (translucent + Y bob + X sway),
    // 3 = flying (Y bob), 4 = sway variant (Y bob + X sway).
    byte UnkownD { get; }

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