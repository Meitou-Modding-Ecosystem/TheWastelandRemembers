using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Simulation.Dialogue;
using Meitou.Simulation.Locks;
using Xunit;

public class CrierPerformanceWorldTests
{
    [Fact]
    public void Out_of_range_locals_do_not_allocate_per_candidate()
    {
        var market = new Vector3(14000, 150, 14000);
        var owner = W.Rec("locals", FcsRecordType.Faction, "Locals");
        var town = W.Rec("market", FcsRecordType.Town, "Market");
        town.References["faction"] = [new("locals", 0, 0, 0)];
        var data = W.Data([owner, town], [new TownPlacement("market-placement", "market", market, null)]);
        var talk = new DialogueSystem(data);
        using var world = W.World();
        W.Spawn(world, "Leaf", W.Player, market);
        int locals = data.FactionIndex("locals");
        Assert.Equal(locals, data.SiteFactions[data.TownAt(market)]);
        for (int i = 0; i < 500; i++)
            W.Spawn(world, "Local", locals, market + new Vector3(900, 0, i));
        world.RunTicks(1);
        for (int i = 0; i < 8; i++) Criers.Look(world, talk, data);

        long before = GC.GetAllocatedBytesForCurrentThread();
        Criers.Look(world, talk, data);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 4096, $"An out-of-range crowd allocated {allocated} bytes in a crier pass.");
    }

    [Fact]
    public void Criers_keep_the_same_order_for_ties_and_choose_the_nearest_awake_speaker()
    {
        using var world = W.World();
        var market = new Vector3(14000, 150, 14000);
        var peer = market + new Vector3(40, 0, 0);
        var leaf = W.Spawn(world, "Leaf", W.Player, market);
        var ruka = W.Spawn(world, "Ruka", W.Player, peer);
        var sleeper = W.Spawn(world, "Sleeper", W.Bandits, market + new Vector3(120, 0, 0));
        var first = W.Spawn(world, "First", W.Bandits, market + new Vector3(160, 0, 0));
        W.Spawn(world, "Second", W.Bandits, market + new Vector3(160, 0, 0));
        W.Spawn(world, "Too close", W.Bandits, market + new Vector3(20, 0, 0));
        world.Characters.Cold(sleeper.Slot)!.InSomething = HeldState.Bed;
        world.RunTicks(1);

        var picked = Criers.FindCrier(world, W.Bandits, [(leaf, market), (ruka, peer)]);

        // The fallback client cannot identify sleepers, so it keeps the original closest candidate.
        Assert.Equal(Sleep.Is(world, sleeper) ? first : sleeper, picked!.Value.Crier);
        Assert.Equal(ruka, picked.Value.Listener);
    }
}
