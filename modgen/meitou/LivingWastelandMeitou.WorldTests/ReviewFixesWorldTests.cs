using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Population;
using Xunit;
using SimWorld = Meitou.Simulation.World;

// Fixes from a review of the code (2026-11-18): each test fails on the code before the fix.
public class ReviewFixesWorldTests
{
    [Fact]
    public void A_rival_pass_with_nobody_in_view_waits_instead_of_stopping_rivals()
    {
        // the first ticks after a load have no player characters in view; Announce's distance check threw on the empty list and the catch
        // stopped rivals for the session
        var rivals = new RivalsSystem(W.Data([]));
        rivals.Read(W.SavedRival("Hollin One-Arm", "lost an arm to your squad", 0, 50000, 0, told: false));
        var world = W.World(rivals);
        world.RunTicks(301);                                              // a pass with no squad
        Assert.Contains("not told yet", rivals.Summary());
        W.Spawn(world, "Leaf", W.Player, new Vector3(0, 150, 0));
        world.RunTicks(301);
        Assert.DoesNotContain("not told yet", rivals.Summary());          // still running: told once the squad is there
    }

    [Fact]
    public void A_rivals_saved_platoon_number_does_not_steer_another_squad_after_a_load()
    {
        var rivals = new RivalsSystem(W.Data([]));
        var world2 = W.World(rivals);
        var other = new Platoon { Id = world2.Platoons.NextId(), Origin = default, Key = default, TemplateId = "someone-else", Faction = W.Bandits, Position = new Vector2(90000, 0) };
        world2.Platoons.Add(other);                                       // after the load, the saved number belongs to an unrelated squad
        rivals.Read(W.SavedRival("Hollin One-Arm", "lost an arm to your squad", 1, 50000, 0, platoonId: other.Id));   // hunting, off screen
        W.Spawn(world2, "Leaf", W.Player, new Vector3(0, 150, 0));
        world2.RunTicks(301);
        Assert.Null(other.Point);                                         // not steered at the squad
    }

    [Fact]
    public void A_hunting_rival_off_screen_after_a_load_is_found_by_its_tag_and_steered()
    {
        // seen in play (build bb): once the saved platoon number was dropped (build ba), a rival off screen after a load was never steered
        // and sat 5 km away for days. Meitou 0.20.2+ saves a platoon's ModTag: the rival's tag finds it again. Older Meitou: nothing to test.
        var modTag = typeof(Platoon).GetProperty("ModTag");
        if (modTag is null) return;
        var rivals = new RivalsSystem(W.Data([]));
        var world = W.World(rivals);
        var own = new Platoon { Id = world.Platoons.NextId(), Origin = default, Key = default, TemplateId = "rival-squad", Faction = W.Bandits, Position = new Vector2(50000, 0) };
        modTag.SetValue(own, "lw:rival:Hollin One-Arm");                  // as tagged when the rival was last seen, and saved by the engine
        world.Platoons.Add(own);
        var other = new Platoon { Id = world.Platoons.NextId(), Origin = default, Key = default, TemplateId = "someone-else", Faction = W.Bandits, Position = new Vector2(49000, 0) };
        world.Platoons.Add(other);
        rivals.Read(W.SavedRival("Hollin One-Arm", "lost an arm to your squad", 1, 50000, 0));   // hunting, off screen
        W.Spawn(world, "Leaf", W.Player, new Vector3(0, 150, 0));
        world.RunTicks(301);
        Assert.NotNull(own.Point);                                        // steered at the squad
        Assert.Null(other.Point);                                         // and nobody else
    }
}

