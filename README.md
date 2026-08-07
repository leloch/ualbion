# UAlbion
A remake of the 1995 RPG Albion 

Prerequisites: 
* [.NET 9](https://docs.microsoft.com/en-us/dotnet/core/install/)
* Game data from an install of the original game ([full version](https://www.gog.com/game/albion) or [demo](https://archive.org/details/Albidemo))

## Screenshots
<details>
  <summary>Click to expand</summary>

  ![Example Screenshot 1](/.github/screenshots/1_FirstLevel.png?raw=true)
  ![Example Screenshot 2](/.github/screenshots/2_3DWorld.png?raw=true)
  ![Example Screenshot 3](/.github/screenshots/3_Outdoors.png?raw=true)
  ![Example Screenshot 4](/.github/screenshots/4_Inventory.png?raw=true)
  ![Example Screenshot 5](/.github/screenshots/5_MainMenu.png?raw=true)
</details>

## Current Status

Implemented (most mechanics reverse-engineered from MAIN.EXE for 1:1 behaviour — see
`_RE_COMBAT.md` / `_RE_NOTES.md` for the decoded formulas):
- Rendering of 2D and 3D environments (Veldrid: D3D11 / Vulkan / OpenGL)
- Player movement and collision in 2D and 3D (sub-tile margins, wall sliding)
- Environment interaction: examine / manipulate / take / talk, chests, doors, locks
- GUI system for menus, dialogs, inventory etc
- Inventory management, merchants, item use, charges, broken/cursed gear
- Conversations (topics, word/item queries, party joins/leaves)
- Combat: the original's strike pipeline (to-hit, instant-kill crits, equipment wear),
  byte-exact damage math, monster AI, animated battle view, post-combat loot
- Magic: all four playable spell schools with RE'd magnitudes/durations, out-of-combat
  casting, spell learning, active-spell shields
- NPC services: healers, taverns, trainers, spell teachers, repair/identify/recharge
- Resting/camping with the original's gating, rations and fatigue/exhaustion systems
- The 3D automap (facing-cone discovery, connection-mask wall glyphs, floor minis,
  goto-point markers) and the Map View / Teleporter spells
- Sound effects (RE'd combat SFX tables) and music; day/night cycle
- Loading/saving original-format saved games (byte-exact round trip)
- Video playback, NPC movement/schedules, map event scripting
- Exporting assets; mod layer system (e.g. an HD asset override mod)

Remaining gaps are mostly polish and modding-convenience items; the mechanics, combat, magic
and the story content pipeline are implemented and verified (all 166 story maps load and run
their event chains cleanly).

Planned improvements / changes from the original gameplay:
- Add hotkeys to streamline the interface, reduce the amount of right clicking required etc (largely done: quicksave/quickload, window scaling, conversation number keys + Escape)
- Add some pathfinding logic to make mouse-based movement easier (done: A* click-to-walk on 2D maps)
- Add a take-all button when looting chests / fallen foes (done)
- Graphical improvements in 3D environments (done, opt-in via the Extras menu: texture filtering, distance fog, minimap, gamma, interactable highlighting, bump mapping)
- Fix bugs in original game (with option to toggle when there is a gameplay impact)
- Modding support (asset override layers work; see `mods/`)
- A built-in editor for modifying and adding assets

## Getting started

### Game data

You need to have the original Albion game files. These days, Albion can be bought cheaply on [GOG](https://www.gog.com/game/albion).

If you have the GOG version of the game, extract the required game files as follows:

#### Linux:
1. Ensure `wine` and `dosbox` are installed
1. Download the [Albion installer for Windows from GOG](https://www.gog.com/game/albion)
1. Run the installer using wine (`wine setup_albion_1.38_\(28043\).exe`). Note that the installer may show some errors, but if the game can be launched in the end, it's okay.
1. Run `dosbox`
1. Run the following commands in dosbox to extract the data files. Replace `~/ualbion` with wherever you cloned the ualbion repository. Replace `~/.wine/drive_c/GOG Games/Albion/` with wherever you installed your GOG version of Albion. Note the double quotes (`"`), they are necessary if your path contains spaces.
    1. `mount C "~/ualbion"`
    1. `mount D "~/.wine/drive_c/GOG Games/Albion/"`
    1. `C:\src\Tools\GOG_EXTR.BAT`

#### Windows:
1. Download the [Albion installer from GOG](https://www.gog.com/game/albion)
1. Run installer
1. Open the Albion install directory in file explorer (e.g. `C:\GOG Games\Albion`)
1. Go into the `DOSBOX` directory and run `DOSBOX.exe`
1. Run the following commands in dosbox to extract the data files. Replace `C:\Git\ualbion` with wherever you cloned the ualbion repository. Replace `C:\GOG Games\Albion` with wherever you installed your GOG version of Albion. Note the double quotes (`"`), they are necessary if your path contains spaces.
    1. `mount C "C:\Git\ualbion"`
    1. `mount D "C:\GOG Games\Albion"`
    1. `C:\src\Tools\GOG_EXTR.BAT`

If you're not using the GOG version or want to select your game files manually, you can either copy the files into an "ALBION" subdirectory manually or configure `data/config.json` to set the paths for the files. The files can also be manually extracted from the GOG version by mounting the `game.gog` file using CDemu (it's just a raw binary dump of the CD contents) and then copy the ALBION directory into your UAlbion folder.

### Compile and run

To compile and run the project the easiest way is to use the run script (i.e. `run.bat` on Windows and `run.sh` on Linux).
- To start with default options, use `run` without parameters
- To show available options: `run -h` or `run --help`
- To run with Vulkan: `run -vk`
- To run with OpenGL: `run -gl`
- To run with Direct3D: `run -d3d`
- To extract all game resources into modern formats under the mods/Unpacked dir: `run -b Albion Unpacked`
- To repack the (possibly modified) assets in mods/Unpacked back into the original binary formats: `run -b Unpacked Repacked` (outputs to mods/Repacked)

For developers, any C# IDE should work (e.g. VS Code, Visual Studio, Rider etc). The available solutions are:
- `src/ualbion.nodeps.sln`: Loads ualbion with dependencies from NuGet (recommended for getting started)
- `src/ualbion.full.sln`: Loads ualbion with local dependencies (requires `get_dependencies` to have been run, recommended for advanced development, e.g. if debugging into the dependencies is required)
- `src/ualbion.ci.sln`: A solution for use by the continuous integration environment in github. Excludes tests that rely on the original assets being present.

## Attributions
Many thanks to Florian Ziesche and the other contributers to the [freealbion wiki](https://github.com/freealbion/freealbion/wiki) for their efforts in discovering and documenting the Albion file formats.

Thanks to IllidanS4 for the ILBM loading code in [AlbLib](https://github.com/IllidanS4/AlbLib) (MIT License) which my InterlacedBitmap implementation was based on.

Thanks also to the authors of and contributers to the dependencies of this project:
- [Veldrid](https://github.com/mellinoe/veldrid) graphics API abstraction by Eric Mellino et al
- [ImGui](https://github.com/ocornut/imgui/) immediate mode graphics library by Omar Cornut et al
- [OpenAL](https://www.openal.org/) audio abstraction API from Loki Entertainment / Creative Technology
- [OpenAL-CS](https://github.com/flibitijibibo/OpenAL-CS) C# wrapper for OpenAL (using the NuGet package of the [OpenRA](https://github.com/OpenRA/OpenAL-CS) fork)
- [AdlMidi](https://github.com/Wohlstand/libADLMIDI) OPL-3 synthesiser library by Vitaly Novichkov, Joel Yliluoma et al
- [Superpower](https://github.com/datalust/superpower) C# parsing library by Nicholas Blumhardt et al

