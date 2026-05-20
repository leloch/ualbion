using UAlbion.Api.Eventing;
using UAlbion.Formats.Assets.Sheets;
using UAlbion.Formats.Ids;
using UAlbion.Game.Events;

namespace UAlbion.Game.Combat;

/// <summary>
/// Physical 3D / sprite representation of a single combatant. Tracks which animation
/// frame to display (idle / melee-strike / hit / die / etc.) based on the per-monster
/// <see cref="MonsterData.Animations"/> table, and advances the frame counter on
/// <see cref="FastClockEvent"/>.
/// </summary>
/// <remarks>
/// PLACEHOLDER: visual integration with the combat scene is not yet wired — the
/// component compiles and tracks state but no renderer subscribes to it. The original
/// engine's frame ticker in `fcn.00075e71` advances at the same rate as the FastClock
/// (one tick per game-frame). Once a CombatScene is added that reads <see cref="Frame"/>
/// + <see cref="CurrentAnimation"/>, the existing 8-row animation table on MonsterData
/// can drive the sprite directly.
/// </remarks>
public sealed class Mob3D : Component
{
    readonly MonsterData _monsterData;
    int _ticksThisFrame;

    public SheetId SheetId { get; }
    public CombatAnimationId CurrentAnimation { get; private set; } = CombatAnimationId.Initial;
    public int Frame { get; private set; }
    public int TicksPerFrame { get; set; } = 6;     // PLACEHOLDER: original probably scales per animation

    public Mob3D(SheetId sheetId, MonsterData monsterData)
    {
        SheetId = sheetId;
        _monsterData = monsterData;
        On<FastClockEvent>(_ => Advance());
    }

    public void Play(CombatAnimationId anim)
    {
        if (CurrentAnimation == anim) return;
        CurrentAnimation = anim;
        Frame = 0;
        _ticksThisFrame = 0;
    }

    void Advance()
    {
        if (_monsterData?.Animations == null) return;
        if (!_monsterData.Animations.TryGetValue(CurrentAnimation, out var frames)) return;
        if (frames == null || frames.Length == 0) return;

        _ticksThisFrame++;
        if (_ticksThisFrame < TicksPerFrame) return;
        _ticksThisFrame = 0;

        Frame++;
        if (Frame >= frames.Length)
        {
            // Non-looping animations (Die, Hit) end on the last frame; looping ones
            // (Move, Initial) wrap. PLACEHOLDER: original engine's per-animation loop
            // flag isn't decoded; assume Die/Hit are one-shot and everything else loops.
            if (CurrentAnimation == CombatAnimationId.Die || CurrentAnimation == CombatAnimationId.Hit)
                Frame = frames.Length - 1;
            else
                Frame = 0;
        }
    }
}