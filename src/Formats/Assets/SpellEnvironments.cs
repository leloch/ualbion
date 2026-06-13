using System;

namespace UAlbion.Formats.Assets;

[Flags]
public enum SpellEnvironments : byte
{
    // RE'd (RE 5B fcn.00060554): the castable-environment bit is selected by the environment
    // index = 5 in combat, else (mapFlags & 0xC) >> 2 == (int)RestMode. The first four were
    // previously mis-named Indoors/Outdoors/Dungeon/Inventory.
    City       = 1 << 0, // RestMode.Wait
    Dungeon    = 1 << 1, // RestMode.RestEightHours
    Wilderness = 1 << 2, // RestMode.RestUntilDawn
    Interior   = 1 << 3, // RestMode.NoResting

    Camp       = 1 << 4, // never produced by fcn.00060554 (dead)
    Combat     = 1 << 5,
    Spaceship  = 1 << 6, // only in dead school-5/6 records
    Unk7       = 1 << 7,
}