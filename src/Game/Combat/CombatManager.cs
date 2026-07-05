using UAlbion.Api.Eventing;
using UAlbion.Formats.Ids;
using UAlbion.Formats.MapEvents;
using UAlbion.Formats.ScriptEvents;
using UAlbion.Game.Events;
using UAlbion.Game.Gui;
using UAlbion.Game.Scenes;
using UAlbion.Game.State;

namespace UAlbion.Game.Combat;

public class CombatManager : GameComponent
{
    /*
    CombatManager
    |--Battle
      |--List<Mob> _mobs
      |--Mob[] _tiles
      |--Sprite (background)
      |-- Mobs contain a Monster, created by the Battle
    |--CombatDialog (owned by DialogManager, but CombatManager asks for it to be created)


     */
    public CombatManager()
    {
        On<EncounterEvent>(e => BeginCombat(e.GroupId, e.BackgroundId));
        OnAsync<EndCombatEvent>(OnCombatEnded);
        OnAsync<GameCompleteEvent>(_ => PlayEndgameAsync());
    }

    Battle _currentBattle;

    void BeginCombat(MonsterGroupId groupId, SpriteId backgroundId)
    {
        // Guard against empty/missing monster groups — e.g. a saved NPC slot typed MonsterGroup but
        // set to MonsterGroup.Empty (seen on Jirinaar save loads), or an encounter referencing a
        // group that doesn't load. Without this we'd push the combat scene, flash it, then instantly
        // "win" against nobody (Battle logs an error and ends). There is nothing to fight — bail.
        if (groupId.IsNone || Assets.LoadMonsterGroup(groupId) == null)
        {
            Warn($"Ignoring encounter with empty/missing monster group {groupId}");
            return;
        }

        if (backgroundId.IsNone)
            backgroundId = Resolve<IMapManager>().Current.MapData.CombatBackgroundId;

        // The original engine asserts fatally when no combat background loads (combat.c:419
        // in MAIN.EXE fcn.0004ac00) — every original fight supplies one, and the combat
        // PALETTE is looked up from the background index. Monster combat gfx are painted
        // for the combat palettes (default pal.24 DungeonCombat), so without this fallback
        // any map lacking a CombatBackgroundId renders the monsters in the map palette
        // (e.g. all-white in Jirinaar). Deliberate deviation (ledger §8): Dungeon chosen as
        // the generic fallback because it uses the monster gfx default palette; the original
        // has no fallback at all (it fatally asserts) — fail-soft is strictly better here.
        if (backgroundId.IsNone)
            backgroundId = (SpriteId)(CombatBackgroundId)Base.CombatBackground.Dungeon;

        Raise(new PushSceneEvent(SceneId.Combat));

        // Combat music: the original switches to the battle track for the duration of the
        // fight and restores the map song afterwards. Track selection: tech-environment
        // backgrounds use the tech battle theme, everything else the standard one.
        // PLACEHOLDER: exact per-background selection pending _RE_COMBATAUDIO.md.
        var mapSong = Resolve<IMapManager>().Current?.MapData?.SongId ?? SongId.None;
        Raise(new SongEvent((SongId)(backgroundId == (SpriteId)(CombatBackgroundId)Base.CombatBackground.Toronto
            ? Base.Song.TechCombatMusic
            : Base.Song.CombatMusic)));

        var info = Assets.GetAssetInfo(backgroundId);
        if (info != null)
            Raise(new LoadPaletteEvent(info.PaletteId));

        var scene = Resolve<ISceneManager>().ActiveScene;
        var battle = new Battle(groupId, backgroundId);
        scene.Add(battle);
        _currentBattle = battle;

        Raise(new DialogManager.CombatDialogEvent(battle));

        battle.Complete += () =>
        {
            scene.Remove(battle);
            Raise(new PopSceneEvent());
            if (!mapSong.IsNone)
                Raise(new SongEvent(mapSong)); // restore the map's music
        };
    }

    // Drive the post-combat sequence on EndCombatEvent. PartyKilled → play the GameOver
    // video then bounce back to the main menu (matches the original Albion's behaviour on
    // total-party-kill). Victory / Retreat fall through to whatever the Battle hooked up
    // via its Complete callback in BeginCombat.
    AlbionTask OnCombatEnded(EndCombatEvent e)
    {
        if (e.Result == CombatResult.PartyKilled)
            return PartyWipedAsync();
        // The final boss "asks for surrender" once the party is mostly downed (combat outcome 4,
        // _RE_ASK_SURRENDER.md) — that IS the win. Drive the canonical endgame terminal. The boss
        // map's continuation chain still runs (it pops back to the map), but the surrender outcome
        // is the engine-side signal that the game is won, so we sequence the ending here. (B3.)
        if (e.Result == CombatResult.Surrender)
            return PlayEndgameAsync();
        return AlbionTask.CompletedTask;
    }

    async AlbionTask PartyWipedAsync()
    {
        // Animation x/y/unk are unused for full-screen videos like GameOver — pass 0s.
        await RaiseA(new PlayAnimationEvent(Base.Video.GameOver, 0, 0, 0, 0, 0, 0));
        Raise(new PushSceneEvent(SceneId.MainMenu));
    }

    // The end-of-game sequence: the four Endgame FLICs (Seed detonation / outro / credits) in
    // order, then a terminal return to the main menu. Mirrors PartyWipedAsync's video→menu shape.
    // Reached on boss surrender or an explicit game_complete script opcode. (B3 terminal win-state.)
    async AlbionTask PlayEndgameAsync()
    {
        foreach (var video in new[] { Base.Video.Endgame1, Base.Video.Endgame2, Base.Video.Endgame3, Base.Video.Endgame4 })
            await RaiseA(new PlayAnimationEvent(video, 0, 0, 0, 0, 0, 0));
        Raise(new PushSceneEvent(SceneId.MainMenu));
    }
}
