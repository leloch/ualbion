using System.Collections.Generic;
using UAlbion.Api.Eventing;
using UAlbion.Formats.Assets.Sheets;
using UAlbion.Formats.Ids;
using UAlbion.Game.Combat;
using UAlbion.Game.Events;
using UAlbion.Core;
using UAlbion.TestCommon;
using Xunit;

namespace UAlbion.Game.Tests;

public class Mob3DTests
{
    static Mob3D Attach(MonsterData data, int ticksPerFrame = 1)
    {
        var mob = new Mob3D(new SheetId(0), data) { TicksPerFrame = ticksPerFrame };
        var exchange = new EventExchange(new LogExchange());
        exchange.Attach(mob);
        return mob;
    }


    static MonsterData MakeData()
    {
        var data = new MonsterData
        {
            Animations = new Dictionary<CombatAnimationId, int[]>
            {
                [CombatAnimationId.Move]    = [0, 1, 2, 3],
                [CombatAnimationId.Melee]   = [0, 1, 2],
                [CombatAnimationId.Initial] = [0],
                [CombatAnimationId.Die]     = [0, 1, 2, 3, 4],
                [CombatAnimationId.Hit]     = [0, 1],
            },
        };
        return data;
    }

    static void Tick(Mob3D mob, int ticks)
    {
        var evt = new FastClockEvent(1);
        for (int i = 0; i < ticks; i++)
            ((IComponent)mob).Receive(evt, null);
    }

    [Fact]
    public void Initial_Animation_Is_Initial_With_Frame_Zero()
    {
        var mob = Attach(MakeData());
        Assert.Equal(CombatAnimationId.Initial, mob.CurrentAnimation);
        Assert.Equal(0, mob.Frame);
    }

    [Fact]
    public void Play_Switches_Animation_And_Resets_Frame()
    {
        var mob = Attach(MakeData());
        mob.Play(CombatAnimationId.Move);
        Assert.Equal(CombatAnimationId.Move, mob.CurrentAnimation);
        Assert.Equal(0, mob.Frame);
    }

    [Fact]
    public void Looping_Animation_Wraps_To_Zero()
    {
        var mob = Attach(MakeData());
        mob.Play(CombatAnimationId.Move);

        Tick(mob, 1); Assert.Equal(1, mob.Frame);
        Tick(mob, 1); Assert.Equal(2, mob.Frame);
        Tick(mob, 1); Assert.Equal(3, mob.Frame);
        Tick(mob, 1); Assert.Equal(0, mob.Frame);     // wrapped
    }

    [Fact]
    public void Die_Animation_Stops_At_Final_Frame()
    {
        var mob = Attach(MakeData());
        mob.Play(CombatAnimationId.Die);
        for (int i = 0; i < 20; i++)
            Tick(mob, 1);
        Assert.Equal(4, mob.Frame);     // stops at last frame, doesn't wrap
    }

    [Fact]
    public void Hit_Animation_Stops_At_Final_Frame()
    {
        var mob = Attach(MakeData());
        mob.Play(CombatAnimationId.Hit);
        for (int i = 0; i < 10; i++)
            Tick(mob, 1);
        Assert.Equal(1, mob.Frame);
    }

    [Fact]
    public void Ticks_Per_Frame_Throttles_Advance()
    {
        var mob = Attach(MakeData(), ticksPerFrame: 3);
        mob.Play(CombatAnimationId.Move);
        Tick(mob, 2); Assert.Equal(0, mob.Frame);
        Tick(mob, 1); Assert.Equal(1, mob.Frame);
    }
}
