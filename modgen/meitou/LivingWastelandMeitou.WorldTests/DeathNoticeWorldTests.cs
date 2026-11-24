using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Simulation;
using Xunit;

// The engine's death notice (Meitou 0.22.2+) read through DeathNotices' reflection, with the engine's own payload: a squadmate shot by a
// bandit out of the hit feed's sight is remembered as shot dead, by that bandit. Older clients have no notice: nothing is registered.
[Collection("settings")]
public sealed class DeathNoticeWorldTests
{
    [Fact]
    public void A_squadmate_shot_out_of_sight_is_remembered_with_how_and_by_whom()
    {
        var payload = typeof(World).Assembly.GetType("Meitou.Simulation.Bodies.CharacterDeath");
        Assert.Equal(payload is not null, DeathNotices.Available);
        if (payload is null) return;

        var data = W.Data([]);
        var world = W.World();
        var deeds = new LwDeeds([], data);
        LwDeeds.Register(world, deeds);
        var ruka = W.Spawn(world, "Ruka", W.Player, new Vector3(0, 150, 0));
        var bandit = W.Spawn(world, "Bandit", W.Bandits, new Vector3(300, 150, 0));
        Enum Parse(string type, string name) => (Enum)Enum.Parse(payload.GetProperty(type)!.PropertyType, name);
        var death = payload.GetConstructors().Single(c => c.GetParameters().Length == 9)
            .Invoke([world.Tick, ruka, "r-Ruka", W.Player, -1, Parse("Cause", "VitalPart"), Parse("Site", "Bolt"), bandit, W.Bandits]);

        var watch = DeathNotices.BuildWatch()!;                         // through the emitted watcher, as the engine calls it
        var system = Activator.CreateInstance(watch)!;
        watch.GetInterface("ICharacterDeathObserver")!.GetMethod("Died")!.Invoke(system, [world, death]);
        deeds.Record.Died("Ruka", 48, "Stack", null, null, null);   // the squad record sees the body later, with no blow of its own

        var f = Assert.Single(deeds.Record.FallenMembers);
        Assert.Equal(("Shot dead", "Bandit", "Dust Bandits"), (f.Cause, f.Killer, f.KillerFaction));
    }
}
