using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Simulation;
using Xunit;

// The engine's character lifecycle notices (Meitou after 0.22.2-nightly.20261124.3, VlcekM/MeitouClient#1370), through the emitted
// watchers as the engine calls them: a squadmate knocked out and a joiner are logged, and a joiner is first seen by the record when they
// join. Clients without the notices build no watcher and nothing changes.
[Collection("settings")]
public sealed class LifecycleWorldTests
{
    static object Payload(string type, params object[] args) =>
        typeof(World).Assembly.GetType(type)!.GetConstructors().Single(c => c.GetParameters().Length == args.Length).Invoke(args);

    static void Tell(Type watch, string iface, string method, World world, object payload) =>
        watch.GetInterface(iface)!.GetMethod(method)!.Invoke(Activator.CreateInstance(watch), [world, payload]);

    [Fact]
    public void The_squads_knockouts_and_joiners_are_told_and_a_joiner_is_first_seen_on_joining()
    {
        var watches = LifecycleWatch.BuildWatches();
        bool has = typeof(World).Assembly.GetType("Meitou.Simulation.Factions.IRecruitObserver") is not null;
        Assert.Equal(has ? 5 : 0, watches.Count);
        if (!has) return;

        var world = W.World();
        var deeds = new LwDeeds([], W.Data([]));
        LwDeeds.Register(world, deeds);
        var leaf = W.Spawn(world, "Leaf", W.Player, new Vector3(0, 150, 0));
        var lines = new List<string>();
        var (log, diag) = (LwMod.Log, LwMod.Settings.PlaytestDiagnostics);
        try
        {
            LwMod.Log = lines.Add;
            LwMod.Settings.PlaytestDiagnostics = true;
            world.RunTicks(1);
            Tell(watches["Knockout"], "IKnockoutObserver", "KnockoutChanged", world,
                Payload("Meitou.Simulation.Bodies.KnockoutChange", world.Tick, leaf, true, 30f, false));
            Tell(watches["Recruit"], "IRecruitObserver", "RecruitChanged", world,
                Payload("Meitou.Simulation.Factions.RecruitChange", world.Tick, leaf, W.Bandits, true));
        }
        finally { (LwMod.Log, LwMod.Settings.PlaytestDiagnostics) = (log, diag); }

        Assert.Contains("lifecycle: Leaf knocked out for 30 s", lines);
        Assert.Contains("lifecycle: Leaf joined the squad", lines);
        deeds.Record.Died("Leaf", world.GameHours + 24 * 5, "Squin", null, null, null);
        Assert.Equal(5, deeds.Record.FallenMembers[0].DaysServed, 1);
    }
}
