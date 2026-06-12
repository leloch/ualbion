using System;
using System.Collections.Generic;
using System.Numerics;
using UAlbion.Api.Eventing;
using UAlbion.Api.Visual;
using UAlbion.Core.Events;
using UAlbion.Core.Visual;
using UAlbion.Formats.Assets.Save;
using UAlbion.Formats.Assets.Sheets;
using UAlbion.Formats.Ids;
using UAlbion.Game.State;

namespace UAlbion.Game.Combat;

/// <summary>
/// The animated battle view: draws each monster's big combat graphic over the backdrop
/// using the original's CONFIRMED mini-3D projection (RE'd from MAIN.EXE — see
/// _RE_COMBAT.md "Battle view rendering"): camera height 83, focal length 148,
/// tile→world x = 64·col − 160, z = 128 − 64·row (row 3 ⇒ z = −21.33), screen
/// = (180 + 148x/(z+148), 96 + 148·83/(z+148)), sprite scale 148/(z+148) on top of the
/// monster's Width/HeightPercentage, bottom-centre anchored. Physical gfx frames are
/// animation-list frame ×2 (odd frames are shadow masks, drawn as the ground shadow).
/// Idle is the static Move[0] frame; the acting combatant plays Melee, damaged ones play
/// Hit (with the file-0x28 #46/#47 damage splash at 200%+4·damage), killed ones play Die
/// and freeze on the last frame as a corpse — all at the original's 6.67 fps animation
/// rate (effects at 2 engine frames per gfx frame). Party members are never drawn (the
/// original has no party battle sprites). Render classes 2/3/4 (MONCHAR +0x0D) get the
/// RE'd sine hover bob (±4-6 world units, 3.0-4.95 s, random phase; 2/4 add an X sway).
/// PLACEHOLDERs: soul-rise for demonic corpses / smooth walk paths / class-2
/// translucency; shadows are 50 %-opacity silhouettes instead of the darkening LUT blit.
/// </summary>
public class BattleView : GameComponent
{
    sealed class Mob
    {
        public Sprite Sprite;
        public Sprite Shadow;
        public ICombatParticipant Participant;
        public CombatAnimationId Animation = CombatAnimationId.Move;
        public int AnimationStep;
        public bool OneShot;
        public bool Dying; // Die anim pending/playing; on tile clear the mob becomes a corpse
        public int Tile;

        // Hover bob (RE 5A, oscillator cb 0x54eeb): render classes 2 (ghostly) / 3
        // (flying) / 4 (sway) attach true-sine oscillators at 20 Hz — amplitude uniform
        // 4.00..5.99 world units (tile = 64), period 60..99 ticks (3.0..4.95 s), random
        // phase; classes 2/4 add an equal X sway. Ground monsters (class 1) hold still.
        public int RenderClass = 1;
        public float BobAmplitude;
        public int BobPeriod;
        public int BobPhase;
        public float SwayAmplitude;
        public int SwayPeriod;
        public int SwayPhase;

        public float BobOffset =>
            RenderClass >= 2 && BobPeriod > 0
                ? BobAmplitude * MathF.Sin(2 * MathF.PI * BobPhase / BobPeriod)
                : 0;

        public float SwayOffset =>
            RenderClass is 2 or 4 && SwayPeriod > 0
                ? SwayAmplitude * MathF.Sin(2 * MathF.PI * SwayPhase / SwayPeriod)
                : 0;
    }

    sealed class Effect
    {
        public Sprite Sprite;
        public Vector2 Centre; // UI pixels
        public float Scale;    // total scale (projection × 200%+4·damage)
        public int Frame;
        public int FrameCount;
        public int Counter;
    }

    const float UiW = 360f, UiH = 240f;
    const int FramesPerAnimStep = 9;  // 6.67 fps at 60 Hz — the original's anim rate (CONFIRMED)
    const int FramesPerEffectStep = 2; // hit-splash etc run at 2 engine frames per gfx frame (CONFIRMED)

    // Draw-order: the world's render passes end at 0x202 and the UI layout assigns
    // orders from Interface+1 (0x302) upward, so the combat presentation slots into the
    // unused 0x203..0x300 band: backdrop, then shadows (the original's "pass 1"), then
    // monster rows back-to-front (painter's algorithm, like the original's
    // worldZ-descending qsort), then hit effects on top. These sprites are NoDepthTest,
    // so the DrawLayer IS the draw order — rows must get distinct layers or overlapping
    // monsters with different textures would sort arbitrarily.
    public const DrawLayer BackdropLayer = (DrawLayer)0x2F0;
    const DrawLayer ShadowLayer = (DrawLayer)0x2F1;
    const DrawLayer EffectLayer = (DrawLayer)0x2FE;
    static DrawLayer RowLayer(int row) => (DrawLayer)(0x2F2 + row); // rows 0..3 => 0x2F2..0x2F5

    readonly IReadOnlyBattle _battle;
    readonly Dictionary<int, Mob> _mobs = [];
    readonly List<Mob> _corpses = [];
    readonly List<Effect> _effects = [];
    int _frameCounter;

    public BattleView(IReadOnlyBattle battle)
    {
        _battle = battle ?? throw new System.ArgumentNullException(nameof(battle));
        On<PostEngineUpdateEvent>(_ => Update());
        On<CombatTurnHighlightEvent>(e => SetAnimation(e.TileIndex, CombatAnimationId.Melee));
        On<CombatHitEvent>(OnHit);
    }

    void OnHit(CombatHitEvent e)
    {
        if (e.Heal)
            return;

        if (e.Killed)
        {
            // Die-and-freeze: vtable_2 sub 9 plays Die then leaves the sprite on the LAST
            // Die frame as a corpse (non-demonic ground monsters — the common case).
            if (_mobs.TryGetValue(e.TileIndex, out var mob))
            {
                mob.Animation = CombatAnimationId.Die;
                mob.AnimationStep = 0;
                mob.OneShot = false;
                mob.Dying = true;
            }
        }
        else
            SetAnimation(e.TileIndex, CombatAnimationId.Hit);

        if (e.Amount > 0)
            SpawnSplash(e);
    }

    void SetAnimation(int tileIndex, CombatAnimationId animation)
    {
        if (_mobs.TryGetValue(tileIndex, out var mob) && !mob.Dying)
        {
            mob.Animation = animation;
            mob.AnimationStep = 0;
            mob.OneShot = true; // revert to idle once the sequence completes
        }
    }

    /// <summary>
    /// Hit feedback effect — RE'd from vtable_2 sub 8: monsters get the impact splash
    /// (file 0x28 #46) over the victim's chest point (feet + 3/4 of the scaled height);
    /// party members get the cast-burst (#47) at their virtual aim point
    /// (24 + 62.4·col, 108 — the party "stands just behind the camera" at z=-110,
    /// projection scale 3.89). Both at scale (200 + 4·damage)% on top of the projection.
    /// </summary>
    void SpawnSplash(CombatHitEvent e)
    {
        int col = e.TileIndex % SavedGame.CombatColumns;
        int row = e.TileIndex / SavedGame.CombatColumns;
        float damageScale = (200f + 4f * e.Amount) / 100f;

        SpriteId gfxId;
        Vector2 centre;
        float scale;
        if (row >= SavedGame.CombatRowsForMobs) // party rows: virtual aim point
        {
            gfxId = Base.CombatGfx.DamageScratch;
            centre = new Vector2(24f + 62.4f * col, 108f);
            scale = 3.89f * damageScale;
        }
        else
        {
            if (!_mobs.TryGetValue(e.TileIndex, out var mob))
                return;

            var (x, y, rowScale) = TileToScreen(col, row);
            float bodyH = GetBodyHeight(mob, rowScale);
            gfxId = Base.CombatGfx.DamageSplat;
            centre = new Vector2(x, y - 0.75f * bodyH);
            scale = rowScale * damageScale;
        }

        var tex = Assets.LoadTexture(gfxId);
        if (tex?.Regions is not { Count: > 0 })
            return;

        var sprite = AttachChild(new Sprite(
            gfxId,
            EffectLayer,
            SpriteKeyFlags.NoTransform | SpriteKeyFlags.NoDepthTest,
            SpriteFlags.LeftAligned));

        var effect = new Effect
        {
            Sprite = sprite,
            Centre = centre,
            Scale = scale,
            FrameCount = tex.Regions.Count,
        };
        LayoutEffect(effect, tex);
        _effects.Add(effect);
    }

    float GetBodyHeight(Mob mob, float rowScale)
    {
        var monster = mob.Participant?.Effective?.Monster;
        float hPct = monster == null || monster.HeightPercentage <= 0 ? 100 : monster.HeightPercentage;
        var tex = Assets.LoadTexture(mob.Participant?.Effective?.CombatGfx ?? SpriteId.None);
        float frameH = tex?.Regions is { Count: > 0 } ? tex.Regions[mob.Sprite.Frame % tex.Regions.Count].Height : 32;
        return frameH * hPct / 100f * rowScale;
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
        bool stepBob = _frameCounter % 3 == 0; // 20 Hz logic ticks at 60 fps (oscillator cb 0x54eeb)
        if (stepBob)
        {
            foreach (var m in _mobs.Values)
            {
                if (m.RenderClass < 2) continue;
                if (m.BobPeriod > 0) m.BobPhase = (m.BobPhase + 1) % m.BobPeriod;
                if (m.SwayPeriod > 0) m.SwayPhase = (m.SwayPhase + 1) % m.SwayPeriod;
            }
        }

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
                {
                    // A dying monster's tile is cleared by Battle the moment it's killed;
                    // keep the sprite as a corpse playing/frozen-on its Die animation if
                    // it has one, otherwise remove it like any other vacated tile.
                    if (stale.Dying && HasFrames(stale, CombatAnimationId.Die))
                    {
                        stale.Shadow?.Remove();
                        stale.Shadow = null;
                        _corpses.Add(stale);
                    }
                    else
                    {
                        stale.Sprite.Remove();
                        stale.Shadow?.Remove();
                    }
                }
                continue;
            }

            if (!_mobs.TryGetValue(tile, out var mob) || !ReferenceEquals(mob.Participant, occupant))
            {
                if (mob != null)
                {
                    mob.Sprite.Remove();
                    mob.Shadow?.Remove();
                }

                // Render class (MONCHAR +0x0D): class 2 (ghostly) draws translucent
                // (the original's render kind 8) — resolved before sprite creation.
                int renderClass = Math.Max((int)occupant.Effective.UnkownD, 1);
                var bodyFlags = SpriteFlags.LeftAligned;
                if (renderClass == 2)
                    bodyFlags = bodyFlags.SetOpacity(0.6f);

                mob = new Mob
                {
                    Participant = occupant,
                    Tile = tile,
                    Sprite = AttachChild(new Sprite(
                        occupant.Effective.CombatGfx,
                        RowLayer(row), // above the backdrop+shadows, below the UI; back rows draw first
                        SpriteKeyFlags.NoTransform | SpriteKeyFlags.NoDepthTest,
                        bodyFlags)),
                    // The ground shadow: the odd physical frame is the mask, drawn in the
                    // original's pass 1 (under every body sprite) centred on the feet
                    // (anchor 50,50). PLACEHOLDER: drawn as a half-opacity BLACK silhouette
                    // (DropShadow renders every opaque mask pixel black — the mask's own
                    // palette colour is a light grey); the original darkens the backdrop
                    // through a LUT instead.
                    Shadow = AttachChild(new Sprite(
                        occupant.Effective.CombatGfx,
                        ShadowLayer,
                        SpriteKeyFlags.NoTransform | SpriteKeyFlags.NoDepthTest,
                        (SpriteFlags.LeftAligned | SpriteFlags.DropShadow).SetOpacity(0.5f))),
                };
                // Hover-bob oscillators (RE 5A): classes 2/3/4 bob, 2/4 sway.
                mob.RenderClass = renderClass;
                if (mob.RenderClass >= 2)
                {
                    var rng = TryResolve<IRandom>();
                    int Roll(int n) => rng?.Generate(n) ?? 0;
                    mob.BobPeriod = Roll(40) + 60;             // 60..99 logic ticks (20 Hz)
                    mob.BobPhase = Roll(mob.BobPeriod);
                    mob.BobAmplitude = (Roll(200) + 400) / 100f; // 4.00..5.99 world units
                    mob.SwayPeriod = Roll(40) + 60;
                    mob.SwayPhase = Roll(mob.SwayPeriod);
                    mob.SwayAmplitude = (Roll(200) + 400) / 100f;
                }
                _mobs[tile] = mob;
                Info($"[BattleView] tile {tile} ({tile % SavedGame.CombatColumns},{row}): {occupant.SheetId} gfx {occupant.Effective.CombatGfx} class {mob.RenderClass}");
            }

            if (stepAnims && mob.OneShot)
                mob.AnimationStep++;

            // Idle is the STATIC Move[0] frame (the original doesn't cycle idles);
            // one-shot animations (Melee/Hit) play through then revert to idle.
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

            LayoutMob(mob, listFrame);
        }

        UpdateCorpses(stepAnims);
        UpdateEffects();
    }

    static bool HasFrames(Mob mob, CombatAnimationId animation)
    {
        var anims = mob.Participant?.Effective?.Monster?.Animations;
        return anims != null && anims.TryGetValue(animation, out var f) && f is { Length: > 0 };
    }

    /// <summary>
    /// Corpses play their Die animation through once and freeze on the LAST frame
    /// (vtable_2 sub 9 for non-demonic ground monsters), staying for the rest of the
    /// battle like the original's corpse sprites.
    /// </summary>
    void UpdateCorpses(bool stepAnims)
    {
        foreach (var corpse in _corpses)
        {
            var anims = corpse.Participant?.Effective?.Monster?.Animations;
            if (anims == null || !anims.TryGetValue(CombatAnimationId.Die, out var frames) || frames is not { Length: > 0 })
                continue;

            if (stepAnims && corpse.AnimationStep < frames.Length - 1)
                corpse.AnimationStep++;

            LayoutMob(corpse, frames[corpse.AnimationStep]);
        }
    }

    void UpdateEffects()
    {
        for (int i = _effects.Count - 1; i >= 0; i--)
        {
            var effect = _effects[i];
            if (++effect.Counter % FramesPerEffectStep != 0)
                continue;

            effect.Frame++;
            if (effect.Frame >= effect.FrameCount)
            {
                effect.Sprite.Remove();
                _effects.RemoveAt(i);
                continue;
            }

            LayoutEffect(effect, null);
        }
    }

    void LayoutEffect(Effect effect, ITexture tex)
    {
        tex ??= Assets.LoadTexture(effect.Sprite.Id);
        if (tex?.Regions is not { Count: > 0 })
            return;

        effect.Sprite.Frame = effect.Frame;
        var region = tex.Regions[effect.Frame % tex.Regions.Count];
        float w = region.Width * effect.Scale;
        float h = region.Height * effect.Scale;

        // Centred on the aim point (anchor 50,50 in the original).
        effect.Sprite.Position = new Vector3(
            -1 + 2 * (effect.Centre.X - w / 2) / UiW,
            1 - 2 * (effect.Centre.Y - h / 2) / UiH,
            0);
        effect.Sprite.Size = new Vector2(2 * w / UiW, -2 * h / UiH);
    }

    /// <summary>
    /// Project the mob's tile and update its body sprite (bottom-centre anchored on the
    /// tile point) and ground shadow (odd physical frame, centred on the feet).
    /// Physical gfx frame = animation-list frame ×2 (odd frames are shadow masks).
    /// </summary>
    void LayoutMob(Mob mob, int listFrame)
    {
        var monster = mob.Participant?.Effective?.Monster;
        if (monster == null)
            return;

        int physicalFrame = listFrame * 2;
        var animTex = Assets.LoadTexture(mob.Participant.Effective.CombatGfx);
        if (animTex?.Regions is { Count: > 0 } && physicalFrame >= animTex.Regions.Count)
            physicalFrame = 0;
        mob.Sprite.Frame = physicalFrame;

        // Position: UI pixels → NDC, bottom-centre anchored (the original's anchor).
        var (x, y, scale) = TileToScreen(mob.Tile % SavedGame.CombatColumns, mob.Tile / SavedGame.CombatColumns);
        float wPct = monster.WidthPercentage <= 0 ? 100 : monster.WidthPercentage;
        float hPct = monster.HeightPercentage <= 0 ? 100 : monster.HeightPercentage;

        float frameW = animTex?.Regions is { Count: > 0 } ? animTex.Regions[physicalFrame].Width : 32;
        float frameH = animTex?.Regions is { Count: > 0 } ? animTex.Regions[physicalFrame].Height : 32;

        float w = frameW * wPct / 100f * scale;
        float h = frameH * hPct / 100f * scale;

        // Vertical offset: the original sets slot.y = -Unk152 world units (ShowCombatant,
        // sheet+0x4B8) — flying monsters have NEGATIVE values, lifting the body above the
        // ground; the shadow slot stays at y = 0. Classes 2/3/4 add the sine bob (and
        // 2/4 the X sway) — world units project through the same scale factor.
        float bodyY = y + (monster.Unk152 - mob.BobOffset) * scale;
        float bodyX = x + mob.SwayOffset * scale;

        // Bottom-centre at (bodyX, bodyY): top-left = (bodyX - w/2, bodyY - h).
        mob.Sprite.Position = new Vector3(-1 + 2 * (bodyX - w / 2) / UiW, 1 - 2 * (bodyY - h) / UiH, 0);
        mob.Sprite.Size = new Vector2(2 * w / UiW, -2 * h / UiH);

        if (mob.Shadow != null)
        {
            // The odd physical frame is a small pre-squashed ground-shadow blob (e.g.
            // Skrinn frame 1 is 39x23 vs the 57x97 body) — drawn at its OWN frame size
            // scaled like the body, centred on the feet point (anchor 50,50).
            int shadowFrame = physicalFrame + 1;
            if (animTex?.Regions is { Count: > 0 } && shadowFrame < animTex.Regions.Count)
            {
                float sw = animTex.Regions[shadowFrame].Width * wPct / 100f * scale;
                float sh = animTex.Regions[shadowFrame].Height * hPct / 100f * scale;
                mob.Shadow.Frame = shadowFrame;
                mob.Shadow.Position = new Vector3(-1 + 2 * (x - sw / 2) / UiW, 1 - 2 * (y - sh / 2) / UiH, 0);
                mob.Shadow.Size = new Vector2(2 * sw / UiW, -2 * sh / UiH);
            }
        }
    }

    protected override void Unsubscribed()
    {
        foreach (var mob in _mobs.Values)
        {
            mob.Sprite.Remove();
            mob.Shadow?.Remove();
        }

        foreach (var corpse in _corpses)
            corpse.Sprite.Remove();

        foreach (var effect in _effects)
            effect.Sprite.Remove();

        _mobs.Clear();
        _corpses.Clear();
        _effects.Clear();
    }
}
