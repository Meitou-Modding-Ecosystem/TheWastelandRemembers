using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Simulation;
using Xunit;

[Collection("settings")]
public sealed class KindnessAtlasWorldTests
{
    [Fact]
    public void The_atlas_lists_a_remembered_neighbour_and_only_their_own_death_closes_it()
    {
        if (!KindnessWatch.Available || typeof(World).Assembly.GetType("Meitou.Simulation.Bodies.CharacterDeath") is not { } payload) return;
        var data = W.Data([]);
        var tracker = new LwDeedsSystem([], data);
        using var world = W.World(tracker, new AtlasSystem(new AtlasTable(), null, data));
        var actor = W.Spawn(world, "Kite", W.Bandits, Vector3.Zero);
        var sameName = W.Spawn(world, "Kite", W.Bandits, Vector3.UnitX);
        world.RunTicks(2);
        ulong stable = LastingActors.Grant(world, actor);
        LastingActors.Grant(world, sameName);
        var book = tracker.Deeds.Kindness;
        book.Add(new KindnessDeed(stable, KindnessKind.Saved, "Kite", "r-Kite", "Dust Bandits", "Stack", "Leaf", 100,
            default, true, false));
        book.SetHome(stable, new KindnessHome("Squin", 0, 0, 3500, 132));
        world.GameHours = 145;
        world.RunTicks(61);
        Assert.Contains("People who owe you", RecordHost.AtlasList);
        var page = RecordHost.AtlasPages[FormattableString.Invariant($"kindness:{stable}")];
        Assert.Contains("Squin", page);
        Assert.Contains("Leaf", page);

        void Death(CharacterId whom)
        {
            object Parse(string property, string value) => Enum.Parse(payload.GetProperty(property)!.PropertyType, value);
            var death = payload.GetConstructors().Single(constructor => constructor.GetParameters().Length == 9)
                .Invoke([world.Tick, whom, "r-Kite", W.Bandits, -1, Parse("Cause", "VitalPart"), Parse("Site", "Bolt"), CharacterId.None, -1]);
            DeathNotices.Died(world, death);
        }
        Death(sameName);
        Assert.Single(book.People);
        Death(actor);
        Death(actor);
        Assert.Empty(book.People);
        Assert.Single(tracker.Deeds.Record.History, item => item.Text.Contains("whom Leaf tended"));
        world.RunTicks(61);
        Assert.DoesNotContain("People who owe you", RecordHost.AtlasList);
    }
}
