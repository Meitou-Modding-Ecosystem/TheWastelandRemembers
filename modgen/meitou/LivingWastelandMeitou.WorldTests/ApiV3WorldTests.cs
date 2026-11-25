using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Simulation;
using Meitou.Simulation.Combat;
using Xunit;

// WastelandApi version 3: another mod's named characters left alone (MeitouMods #25), a crew asked for by another mod (#26).
public class LeaveAloneWorldTests
{
    static Meitou.Simulation.World? RivalsSystemWorld;   // the world of the fight being set up (the callback's)

    static (Meitou.Simulation.World World, RivalsSystem Rivals, CharacterId Leaf, CharacterId Foe) Fight(string foeName, Action<RivalsSystem>? before = null)
    {
        var rivals = new RivalsSystem(W.Data([]));
        var world = W.World(rivals);
        RivalsSystemWorld = world;
        var leaf = W.Spawn(world, "Leaf", W.Player, new Vector3(0, 150, 0));
        var foe = W.Spawn(world, foeName, W.Bandits, new Vector3(50, 150, 0));
        world.RunTicks(1);
        before?.Invoke(rivals);
        rivals.Hit(world, W.Hit(world, leaf, foe), world.Characters.Cold(foe.Slot)!, world.Characters.Cold(leaf.Slot), "Leaf", leader: true);
        rivals.FightEnded(world, won: true);
        return (world, rivals, leaf, foe);
    }

    [Fact]
    public void A_survivor_another_mod_named_is_never_made_a_rival()
    {
        var (world, _, _, foe) = Fight("Zokaa the Scarred");                  // control: an ordinary beaten leader becomes a rival
        Assert.NotEqual("Zokaa the Scarred", world.Characters.Cold(foe.Slot)!.Name);

        var (world2, _, _, foe2) = Fight("Zokaa the Scarred", r => r.LeftAlone.Add("Zokaa the Scarred", "Dust Bandits"));
        Assert.Equal("Zokaa the Scarred", world2.Characters.Cold(foe2.Slot)!.Name);
    }

    // Version 6: left alone by Meitou's lasting id (0.23+), which survives a rename: the same survivor, renamed by its own mod before the
    // fight, is still left alone. Older Meitou has no lasting ids: nothing to check.
    [Fact]
    public void A_survivor_left_alone_by_lasting_id_is_never_made_a_rival_even_renamed()
    {
        if (!LastingActors.Available) return;
        var (world, _, _, foe) = Fight("Zokaa the Scarred");
        Assert.NotEqual("Zokaa the Scarred", world.Characters.Cold(foe.Slot)!.Name);   // control

        ulong id = 0;
        var (world2, _, _, foe2) = Fight("Zokaa the Scarred", r =>
        {
            var w2 = RivalsSystemWorld!;
            var z = W.Find(w2, "Zokaa the Scarred");
            id = LastingActors.Grant(w2, z);
            w2.Characters.Cold(z.Slot)!.Name = "Zokaa Ironhand";                      // its own mod renamed it since
            Assert.True(r.LeftAlone.AddId(id));
        });
        Assert.NotEqual(0UL, id);
        Assert.Equal("Zokaa Ironhand", world2.Characters.Cold(foe2.Slot)!.Name);
    }

    [Fact]
    public void The_same_name_in_another_faction_is_not_covered()
    {
        var (world, _, _, foe) = Fight("Zokaa the Scarred", r => r.LeftAlone.Add("Zokaa the Scarred", "Shek Kingdom"));
        Assert.NotEqual("Zokaa the Scarred", world.Characters.Cold(foe.Slot)!.Name);
    }

    [Fact]
    public void The_marks_survive_a_save_and_an_old_save_still_loads()
    {
        var rivals = new RivalsSystem(null);
        rivals.LeftAlone.Add("Zokaa the Scarred", "Shek Kingdom");
        var saved = new MemoryStream();
        rivals.Write(saved);
        saved.Position = 0;
        var loaded = new RivalsSystem(null);
        loaded.Read(saved);
        Assert.True(loaded.LeftAlone.Covers("Zokaa the Scarred", "Shek Kingdom"));

        var old = new RivalsSystem(null);                                    // a save from before build ap: rivals, no marks
        old.Read(W.SavedRival("Hollin One-Arm", "lost an arm to your squad", 0, 500, 0));
        Assert.Empty(old.LeftAlone.All);
        Assert.NotEmpty(W.Page(old));                                       // the rival itself still read
    }
}

public class SendHuntersWorldTests
{
    [Fact]
    public void A_request_is_queued_once_per_town_and_kept_in_the_save()
    {
        var hunters = new HuntersSystem("crew", null, null);
        Assert.True(hunters.Request("Squin", "unpaid loan"));
        Assert.False(hunters.Request("Squin", "another loan"));              // one crew from a town at a time
        Assert.True(hunters.Request("Stack", "broken contract"));

        var saved = new MemoryStream();
        hunters.Write(saved);
        saved.Position = 0;
        var loaded = new HuntersSystem("crew", null, null);
        loaded.Read(saved);
        Assert.False(loaded.Request("Squin", "again"));                     // still asked for after the load
        Assert.True(loaded.Request("Shark", "fresh"));
    }
}
