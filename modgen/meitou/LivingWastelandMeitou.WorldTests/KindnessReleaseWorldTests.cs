using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Simulation;
using Meitou.Simulation.Crime;
using Xunit;

[Collection("settings")]
public sealed class KindnessReleaseWorldTests
{
    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    public void Cage_release_needs_the_players_fresh_crime_and_the_same_live_prisoner(bool reportCrime, bool freed, bool later, bool credited)
    {
        var hook = typeof(World).Assembly.GetType("Meitou.Simulation.Crime.ICrimeObserver");
        var report = typeof(CrimeSystem).GetMethods().FirstOrDefault(m => m.Name == "SetPending" && m.GetParameters().Length >= 6);
        if (hook is null || report is null || !LastingActors.Available || !LifecycleWatch.BuildWatches().TryGetValue("Held", out var watch)) return;
        var data = W.Data([]);
        var crimes = new CrimeSystem(data.Relations);
        var deeds = new LwDeedsSystem([], data);
        using var world = W.World(crimes, deeds, (ITickSystem)Activator.CreateInstance(watch)!);
        var at = PrisonerDealsWorldTests.At;
        var helper = W.Spawn(world, "Leaf", W.Player, at);
        var prisoner = W.Spawn(world, "Prisoner", W.Bandits, at);
        var cage = PrisonerDealsWorldTests.Hold(world, prisoner);
        cage.Owner = W.Bandits;
        cage.Lock!.Locked = false;
        world.RunTicks(2);

        if (reportCrime)
        {
            var args = new List<object?> { world, helper.Slot, CrimeCode.Terrorism, W.Bandits, prisoner, 3f };
            args.AddRange(report.GetParameters().Skip(args.Count).Select(p => p.DefaultValue));
            Assert.True((bool)report.Invoke(crimes, args.ToArray())!);
        }
        if (later) world.RunTicks(2);
        typeof(CharacterCold).GetProperty("Freed")!.SetValue(world.Characters.Cold(prisoner.Slot), freed);
        Assert.True(world.Fixtures.Release(world, prisoner));
        world.RunTicks(2);

        Assert.Equal(credited ? 1 : 0, deeds.Deeds.Kindness.Freed);
        if (credited)
        {
            var deed = Assert.Single(deeds.Deeds.Kindness.People);
            Assert.Equal("Leaf", deed.Helper);
            Assert.Equal("Dust Bandits", deed.Captor);
            Assert.Equal(LastingActors.Of(world.Characters.Cold(prisoner.Slot)!), deed.Actor);
        }
    }
}
