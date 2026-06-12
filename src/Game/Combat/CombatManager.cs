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
    }

    Battle _currentBattle;

    void BeginCombat(MonsterGroupId groupId, SpriteId backgroundId)
    {
        if (backgroundId.IsNone)
            backgroundId = Resolve<IMapManager>().Current.MapData.CombatBackgroundId;

        // The original engine asserts fatally when no combat background loads (combat.c:419
        // in MAIN.EXE fcn.0004ac00) — every original fight supplies one, and the combat
        // PALETTE is looked up from the background index. Monster combat gfx are painted
        // for the combat palettes (default pal.24 DungeonCombat), so without this fallback
        // any map lacking a CombatBackgroundId renders the monsters in the map palette
        // (e.g. all-white in Jirinaar). PLACEHOLDER: Dungeon chosen as the generic fallback
        // because it uses the monster gfx default palette; the original has no fallback at all.
        if (backgroundId.IsNone)
            backgroundId = (SpriteId)(CombatBackgroundId)Base.CombatBackground.Dungeon;

        Raise(new PushSceneEvent(SceneId.Combat));

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
        return AlbionTask.CompletedTask;
    }

    async AlbionTask PartyWipedAsync()
    {
        // Animation x/y/unk are unused for full-screen videos like GameOver — pass 0s.
        await RaiseA(new PlayAnimationEvent(Base.Video.GameOver, 0, 0, 0, 0, 0, 0));
        Raise(new PushSceneEvent(SceneId.MainMenu));
    }
}
