using System.Collections;
using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Simulation;
using Meitou.Simulation.Factions;
using Xunit;

[Collection("settings")]
public sealed class WitnessMemoryWorldTests
{
    [Theory]
    [InlineData("Witnessed", false, false, true)]
    [InlineData("Witnessed", true, false, true)]
    [InlineData("Pending", false, false, false)]
    [InlineData("Witnessed", false, true, false)]
    public void A_witness_needs_proven_sight_and_a_real_return_to_the_same_town(string notice, bool reload, bool otherMod, bool speaks)
    {
        var hook = typeof(World).Assembly.GetType("Meitou.Simulation.Crime.ICrimeObserver");
        if (hook is null || !LastingActors.Available) return;
        var setting = typeof(LwSettings).GetProperty("WitnessesRememberFaces");
        Assert.NotNull(setting);
        var settings = LwMod.Settings;
        var old = (Enabled: (bool)setting.GetValue(settings)!, settings.Talk);
        try
        {
            setting.SetValue(settings, true);
            settings.Talk = 100;
            var home = new Vector3(12000, 150, 12000);
            var town = W.Rec("scratch-town", FcsRecordType.Town, "Scratch");
            town.References["faction"] = [new FcsReference("60-t", 0, 0, 0)];
            var data = W.Data([town], [new TownPlacement("scratch-site", town.StringId, home, null)]);
            var state = new LwSystem(null, null, data);
            // Reflect over the observer ABI so the same fixture also builds on 0.17.3.
            var watch = LifecycleWatch.BuildWatches()[nameof(LifecycleWatch.Held)];
            Assert.True(hook.IsAssignableFrom(watch));
            var system = (ITickSystem)Activator.CreateInstance(watch)!;
            using var world = W.World(state, system);
            int squadId = -1;
            if (otherMod)
            {
                var squad = new Squad { Id = world.Squads.NextId(), Name = "Residents", TemplateId = "test", Faction = W.Player };
                world.Squads.Add(squad);
                var tags = Assert.IsAssignableFrom<IDictionary<string, string>>(typeof(Squad).GetProperty("ModTags")!.GetValue(squad));
                tags["fiefdom"] = "residents";
                squadId = squad.Id;
            }
            var suspect = W.Spawn(world, "Leaf", W.Player, home, squad: squadId);
            var guard = W.Spawn(world, "Guard", W.Bandits, home + Vector3.UnitX * 70);
            world.RunTicks(2);

            var dispatch = hook.GetMethod("CrimeNoted")!;
            var payload = hook.GetMethod("CrimeNoted")!.GetParameters()[1].ParameterType.GetElementType()!;
            var make = Assert.Single(payload.GetConstructors(), constructor => constructor.GetParameters().Length == 7);
            var args = make.GetParameters();
            var message = make.Invoke([world.Tick, Enum.Parse(args[1].ParameterType, notice), suspect,
                Enum.ToObject(args[3].ParameterType, 0), W.Bandits, 0, guard]);
            dispatch.Invoke(system, [world, message]);
            world.RunTicks(150);

            var barks = (IDictionary)typeof(World).GetField("_barks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
            Assert.False(barks.Contains(guard));
            world.Characters.Place(suspect.Slot, home + Vector3.UnitX * 40000, 0);
            world.RunTicks(150);
            if (reload)
            {
                using var section = new MemoryStream();
                state.Write(section);
                Assert.Contains("WIT1", System.Text.Encoding.UTF8.GetString(section.ToArray()));
                section.Position = 0;
                state.Read(section);
            }
            world.Characters.Place(suspect.Slot, home, 0);
            world.RunTicks(150);
            Assert.Equal(speaks, barks.Contains(guard));
            barks.Clear();
            world.RunTicks(300);
            Assert.False(barks.Contains(guard));
        }
        finally
        {
            setting.SetValue(settings, old.Enabled);
            settings.Talk = old.Talk;
        }
    }
}
