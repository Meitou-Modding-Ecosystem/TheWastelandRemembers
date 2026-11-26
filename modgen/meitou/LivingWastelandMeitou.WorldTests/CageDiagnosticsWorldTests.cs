using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Simulation;
using Meitou.Simulation.Locks;
using Xunit;

[Collection("settings")]
public sealed class CageDiagnosticsWorldTests
{
    [Fact]
    public void Meitou_imprisoned_transitions_log_cage_entry_and_exit_once()
    {
        if (!LifecycleWatch.BuildWatches().TryGetValue("Held", out var adapter)) return;
        using var world = W.World((ITickSystem)Activator.CreateInstance(adapter)!);
        var id = W.Spawn(world, "Cage diagnostics witness", W.Bandits, Vector3.Zero);
        var captive = world.Characters.Cold(id.Slot)!;
        var originalLog = LwMod.Log;
        bool originalSetting = LwMod.Settings.LogChanges, originalDiagnostics = LwMod.Settings.PlaytestDiagnostics;
        var lines = new List<string>();
        try
        {
            LwMod.Log = lines.Add;
            LwMod.Settings.LogChanges = true;
            LwMod.Settings.PlaytestDiagnostics = false;
            world.RunTicks(2);
            captive.InSomething = HeldState.Imprisoned;
            captive.InWhat = "test-cell";
            world.RunTicks(20);
            captive.InSomething = HeldState.None;
            captive.InWhat = null;
            world.RunTicks(20);
            Assert.Single(lines, line => line.Contains("None to Imprisoned at test-cell"));
            Assert.Single(lines, line => line.Contains("Imprisoned to None at test-cell"));
        }
        finally
        {
            LwMod.Log = originalLog;
            LwMod.Settings.LogChanges = originalSetting;
            LwMod.Settings.PlaytestDiagnostics = originalDiagnostics;
        }
    }
}
