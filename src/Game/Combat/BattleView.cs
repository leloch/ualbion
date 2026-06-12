using System.Collections.Generic;
using System.Numerics;
using UAlbion.Api.Eventing;
using UAlbion.Api.Visual;
using UAlbion.Core.Events;
using UAlbion.Core.Visual;
using UAlbion.Formats.Assets.Save;
using UAlbion.Formats.Assets.Sheets;
using UAlbion.Game.State;

namespace UAlbion.Game.Combat;

/// <summary>
/// The animated battle view: draws each monster's big combat graphic over the backdrop
/// using the original's CONFIRMED mini-3D projection (RE'd from MAIN.EXE — see
/// _RE_COMBAT.md "Battle view rendering"): camera height 83, focal length 148,
/// tile→world x = 64·col − 160, z = 128 − 64·row (row 3 ⇒ z = −21.33), screen
/// = (180 + 148x/(z+148), 96 + 148·83/(z+148)), sprite scale 148/(z+148) on top of the
/// monster's Width/HeightPercentage, bottom-centre anchored. Physical gfx frames are
/// animation-list frame ×2 (odd frames are shadow masks). Idle is the static Move[0]
/// frame; the acting combatant plays Melee, damaged ones play Hit, at the original's
/// 6.67 fps animation rate. Party members are never drawn (the original has no party
/// battle sprites). PLACEHOLDERs: no ground shadows / hover bob / death-freeze yet.
/// </summary>
public class BattleView : GameComponent
{
    sealed class Mob
    {
        public Sprite Sprite;
        public ICombatParticipant Participant;
        public CombatAnimationId Animation = CombatAnimationId.Move;
        public int AnimationStep;
        public bool OneShot;
    }

    const float UiW = 360f, UiH = 240f;
    const int FramesPerAnimStep = 9; // 6.67 fps at 60 Hz — the original's anim rate (CONFIRMED)

    readonly IReadOnlyBattle _battle;
    readonly Dictionary<int, Mob> _mobs = [];
    int _frameCounter;

    public BattleView(IReadOnlyBattle battle)
    {
        _battle = battle ?? throw new System.ArgumentNullException(nameof(battle));
        On<PostEngineUpdateEvent>(_ => Update());
        On<CombatTurnHighlightEvent>(e => SetAnimation(e.TileIndex, CombatAnimationId.Melee));
        On<CombatHitEvent>(e =>
        {
            if (!e.Heal)
                SetAnimation(e.TileIndex, CombatAnimationId.Hit);
        });
    }

    void SetAnimation(int tileIndex, CombatAnimationId animation)
    {
        if (_mobs.TryGetValue(tileIndex, out var mob))
        {
            mob.Animation = animation;
            mob.AnimationStep = 0;
            mob.OneShot = true; // revert to idle once the sequence completes
        }
    }

    /// <summary>
    /// Tile (col,row) → (screen UI x, baseline y, scale): the original's mini-3D
    /// projection (CONFIRMED). World x = 64·col − 160, ground z = 128 − 64·row except
    /// row 3 which sits at z = −21.33; camera height 83, focal length 148;
    /// screenX = 180 + 148·x/(z+148), baselineY = 96 + 148·83/(z+148),
    /// scale = 148/(z+148). Resulting row scales: 0.536 / 0.698 / 1.0 / 1.168.
    /// </summary>
    static (float X, float Y, float Scale) TileToScreen(int col, int row)
    {
        const float Focal = 148f, CameraHeight = 83f;
        float x = 64f * col - 160f;
        float z = row == 3 ? -21.33f : 128f - 64f * row;
        float invDepth = Focal / (z + Focal);
        return (180f + x * invDepth, 96f + CameraHeight * invDepth, invDepth);
    }

    void Update()
    {
        bool stepAnims = ++_frameCounter % FramesPerAnimStep == 0;

        for (int tile = 0; tile < SavedGame.CombatRows * SavedGame.CombatColumns; tile++)
        {
            int row = tile / SavedGame.CombatColumns;
            if (row > SavedGame.CombatRowsForMobs)
                continue; // party rows aren't drawn; monsters may advance into row 3 (z=-21.33)

            var occupant = _battle.GetTile(tile);
            var monster = occupant?.Effective?.Monster;

            // Monsters only — the original never draws party battle sprites (no back-view
            // gfx exist), and party sheets can carry junk Monster/CombatGfx data.
            if (occupant == null || monster == null || occupant.Effective.CombatGfx.IsNone
                || occupant.SheetId.Type != UAlbion.Config.AssetType.MonsterSheet)
            {
                if (_mobs.Remove(tile, out var stale))
                    stale.Sprite.Remove();
                continue;
            }

            if (!_mobs.TryGetValue(tile, out var mob) || !ReferenceEquals(mob.Participant, occupant))
            {
                if (mob != null)
                    mob.Sprite.Remove();

                mob = new Mob
                {
                    Participant = occupant,
                    Sprite = AttachChild(new Sprite(
                        occupant.Effective.CombatGfx,
                        DrawLayer.Info, // above the backdrop (Interface is the UI/dialog layer)
                        SpriteKeyFlags.NoTransform | SpriteKeyFlags.NoDepthTest,
                        SpriteFlags.LeftAligned)),
                };
                _mobs[tile] = mob;
                Info($"[BattleView] tile {tile} ({tile % SavedGame.CombatColumns},{row}): {occupant.SheetId} gfx {occupant.Effective.CombatGfx}");
            }

            if (stepAnims && mob.OneShot)
                mob.AnimationStep++;

            // Idle is the STATIC Move[0] frame (the original doesn't cycle idles);
            // one-shot animations (Melee/Hit) play through then revert to idle.
            // Physical gfx frame = animation-list frame ×2 (odd frames are shadow masks).
            int listFrame = 0;
            if (monster.Animations != null
                && monster.Animations.TryGetValue(mob.Animation, out var frames)
                && frames is { Length: > 0 })
            {
                if (mob.OneShot && mob.AnimationStep >= frames.Length)
                {
                    mob.Animation = CombatAnimationId.Move;
                    mob.AnimationStep = 0;
                    mob.OneShot = false;
                    monster.Animations.TryGetValue(mob.Animation, out frames);
                }

                if (frames is { Length: > 0 })
                    listFrame = frames[mob.AnimationStep % frames.Length];
            }

            int physicalFrame = listFrame * 2;
            var animTex = Assets.LoadTexture(occupant.Effective.CombatGfx);
            if (animTex?.Regions is { Count: > 0 } && physicalFrame >= animTex.Regions.Count)
                physicalFrame = 0;
            mob.Sprite.Frame = physicalFrame;

            // Position: UI pixels → NDC, bottom-centre anchored (the original's anchor).
            var (x, y, scale) = TileToScreen(tile % SavedGame.CombatColumns, row);
            float wPct = monster.WidthPercentage <= 0 ? 100 : monster.WidthPercentage;
            float hPct = monster.HeightPercentage <= 0 ? 100 : monster.HeightPercentage;

            float frameW = animTex?.Regions is { Count: > 0 } ? animTex.Regions[physicalFrame].Width : 32;
            float frameH = animTex?.Regions is { Count: > 0 } ? animTex.Regions[physicalFrame].Height : 32;

            float w = frameW * wPct / 100f * scale;
            float h = frameH * hPct / 100f * scale;

            // Bottom-centre at (x, y): top-left = (x - w/2, y - h).
            mob.Sprite.Position = new Vector3(-1 + 2 * (x - w / 2) / UiW, 1 - 2 * (y - h) / UiH, 0);
            mob.Sprite.Size = new Vector2(2 * w / UiW, -2 * h / UiH);
        }
    }

    protected override void Unsubscribed()
    {
        foreach (var mob in _mobs.Values)
            mob.Sprite.Remove();
        _mobs.Clear();
    }
}
