using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Simulation.Dialogue;
using Xunit;

[Collection("settings")]
public sealed class LookoutWorldTests
{
    [Fact]
    public void A_fresh_lookout_notices_its_first_hostile_and_then_waits()
    {
        var data = W.Data([]);
        data.Relations.Add(W.Bandits, W.Player, -100);
        var talk = new DialogueSystem(data);
        var world = W.World(talk);
        W.Spawn(world, "Leaf", W.Player, new Vector3(0, 150, 0));
        W.Spawn(world, "Bandit", W.Bandits, new Vector3(500, 150, 0));
        world.RunTicks(1);

        var previous = LwMod.Log;
        bool logChanges = LwMod.Settings.LogChanges;
        var lines = new List<string>();
        try
        {
            LwMod.Log = lines.Add;
            LwMod.Settings.LogChanges = true;
            var lookout = new Lookout();
            lookout.Look(world, talk, data);
            Assert.Single(lines, line => line.StartsWith("lookout: ", StringComparison.Ordinal));
            lookout.Look(world, talk, data);
            Assert.Single(lines, line => line.StartsWith("lookout: ", StringComparison.Ordinal));
        }
        finally { LwMod.Log = previous; LwMod.Settings.LogChanges = logChanges; }
    }
}
