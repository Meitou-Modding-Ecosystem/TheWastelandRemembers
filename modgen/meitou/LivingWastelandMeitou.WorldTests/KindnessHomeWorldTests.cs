using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Xunit;

[Collection("settings")]
public sealed class KindnessHomeWorldTests
{
    [Fact]
    public void A_remembered_actor_gets_a_home_only_after_settling_and_the_squad_has_to_approach_it()
    {
        if (!LastingActors.Available || !AidUseWatch.Available) return;
        var at = new Vector3(12000, 150, 12000);
        var stack = W.Rec("town-stack", FcsRecordType.Town, "Stack");
        var data = W.Data([stack], [new TownPlacement("i-stack", stack.StringId, at, null)]);
        var tracker = new LwDeedsSystem([], data);
        using var world = W.World(tracker);
        var leaf = W.Spawn(world, "Leaf", W.Player, at);
        var patient = W.Spawn(world, "Ruka", W.Bandits, at + Vector3.UnitX);
        W.Spawn(world, "Ruka", W.Bandits, at + Vector3.One * 4000);
        world.RunTicks(2);
        ulong id = LastingActors.Grant(world, patient);
        var book = tracker.Deeds.Kindness;
        book.Add(new KindnessDeed(id, KindnessKind.Saved, "Ruka", "r-Ruka", "Dust Bandits", "the wilds", "Leaf",
            100, default, true, false));

        var onBook = typeof(KindnessBook).GetMethod("Home", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(onBook);
        object? Home(KindnessBook from) => onBook.Invoke(from, [id]);
        bool Approached(object home) => (bool)home.GetType().GetProperty("Approached")!.GetValue(home)!;
        void StepAt(double hours)
        {
            world.GameHours = hours;
            world.RunTicks(151);
        }

        StepAt(120);
        Assert.Null(Home(book));
        StepAt(125);  // first settled-area sample
        Assert.Null(Home(book));
        StepAt(132);
        var home = Home(book);
        Assert.NotNull(home);
        Assert.Equal("Stack", home.GetType().GetProperty("Place")!.GetValue(home));
        Assert.False(Approached(home));

        world.Characters.Place(leaf.Slot, at + Vector3.UnitX * 8000, 0);
        StepAt(133);
        Assert.False(Approached(Home(book)!));
        world.Characters.Place(leaf.Slot, at, 0);
        StepAt(134);
        Assert.True(Approached(Home(book)!));

        using var saved = new MemoryStream();
        tracker.Write(saved);
        saved.Position = 0;
        var loaded = new LwDeedsSystem([], data);
        loaded.Read(saved);
        var restored = Home(loaded.Deeds.Kindness);
        Assert.NotNull(restored);
        Assert.Equal(Home(book), restored);
    }
}
