using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Xunit;
using SimWorld = Meitou.Simulation.World;

// Proving Grounds' arena (ArenaLink.cs): a blow between two characters both in a bout is sport. The arena's API is stood in for by
// ArenaLink.Bind; the stand-ins answer only on this test's own thread, so tests running alongside on other threads see no arena.
[Collection("settings")]
public class ArenaWorldTests
{
    [ThreadStatic] static HashSet<CharacterId>? inBout;
    [ThreadStatic] static HashSet<string>? fighters;

    static readonly Vector3 Scratch = new(10000, 150, 10000);

    static void Arena(IEnumerable<CharacterId> bout, params string[] townFighters)
    {
        inBout = [.. bout];
        fighters = new(townFighters, StringComparer.OrdinalIgnoreCase);
        ArenaLink.Bind(id => inBout?.Contains(id) == true, n => fighters?.Contains(n.Trim()) == true);
    }

    static void NoArena()
    {
        inBout = null;
        fighters = null;
        ArenaLink.Reset();
    }

    /// <summary>Scratch, a squad member and one of the town's own people standing next to each other.</summary>
    static (SimWorld World, LwDeedsSystem Deeds, CharacterId Leaf, CharacterId Local) Town()
    {
        var town = W.Rec("town-scratch", FcsRecordType.Town, "Scratch");
        town.References["faction"] = [new FcsReference("60-t", 0, 0, 0)];
        var data = W.Data([town], [new TownPlacement("i-scratch", "town-scratch", Scratch, null)]);
        var deeds = new LwDeedsSystem([], data);
        var world = W.World(deeds);
        var leaf = W.Spawn(world, "Leaf", W.Player, Scratch + new Vector3(100, 0, 0));
        var local = W.Spawn(world, "Hagar", W.Bandits, Scratch + new Vector3(120, 0, 0));
        world.RunTicks(31);
        return (world, deeds, leaf, local);
    }

    /// <summary>Leaf knocks the local out (the engine's own setter, by reflection: the test has no fight to run).</summary>
    static void KnockOut(SimWorld world, LwDeedsSystem deeds, CharacterId leaf, CharacterId local)
    {
        var med = world.Characters.Cold(local.Slot)!.Medical!;
        typeof(MedicalState).GetProperty(nameof(MedicalState.Unconscious))!.SetValue(med, true);
        deeds.HitResolved(world, W.Hit(world, leaf, local));
    }

    static double Heat(SimWorld world) => LwDeeds.For(world)!.Heat.Here("Scratch");
    static int Knockouts(SimWorld world) => LwDeeds.For(world)!.Record.Members.Values.Sum(m => m.Knockouts);

    [Fact]
    public void A_knockout_in_a_bout_is_sport_and_heats_no_town()
    {
        try
        {
            var (world, deeds, leaf, local) = Town();                       // control: no arena, the town minds
            NoArena();
            KnockOut(world, deeds, leaf, local);
            Assert.True(Heat(world) > 0);
            Assert.Equal(1, Knockouts(world));

            var (world2, deeds2, leaf2, local2) = Town();
            Arena([leaf2, local2]);
            KnockOut(world2, deeds2, leaf2, local2);
            Assert.Equal(0, Heat(world2));                                  // sport
            Assert.Equal(1, Knockouts(world2));                             // but the record keeps it
        }
        finally { NoArena(); }
    }

    [Fact]
    public void Only_one_side_in_a_bout_is_no_sport()
    {
        try
        {
            var (world, deeds, leaf, local) = Town();
            Arena([leaf]);                                                  // a fighter steps out of the ring and hits a bystander
            KnockOut(world, deeds, leaf, local);
            Assert.True(Heat(world) > 0);
        }
        finally { NoArena(); }
    }

    [Fact]
    public void An_arena_fighter_is_never_made_a_rival()
    {
        static string After(string name)
        {
            var rivals = new RivalsSystem(W.Data([]));
            var world = W.World(rivals);
            var leaf = W.Spawn(world, "Leaf", W.Player, new Vector3(0, 150, 0));
            var foe = W.Spawn(world, name, W.Bandits, new Vector3(50, 150, 0));
            world.RunTicks(1);
            rivals.Hit(world, W.Hit(world, leaf, foe), world.Characters.Cold(foe.Slot)!, world.Characters.Cold(leaf.Slot), "Leaf", leader: true);
            rivals.FightEnded(world, won: true);
            return world.Characters.Cold(foe.Slot)!.Name;
        }
        try
        {
            NoArena();
            Assert.NotEqual("Zokaa the Scarred", After("Zokaa the Scarred"));    // control: a beaten leader becomes a rival (renamed)
            Arena([], "Zokaa the Scarred");                                      // the same name as one of Scratch's fighters
            Assert.Equal("Zokaa the Scarred", After("Zokaa the Scarred"));
        }
        finally { NoArena(); }
    }

    [Fact]
    public void Without_Proving_Grounds_nothing_is_sport()
    {
        NoArena();
        var (world, _, leaf, local) = Town();
        Assert.False(ArenaLink.Sport(leaf, local));
        Assert.False(ArenaLink.IsTownFighter("Skarn"));
        Assert.False(ArenaLink.InBout(leaf));
    }
}
