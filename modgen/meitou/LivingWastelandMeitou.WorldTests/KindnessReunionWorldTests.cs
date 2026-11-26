using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Xunit;
using World = Meitou.Simulation.World;

[Collection("settings")]
public sealed class KindnessReunionWorldTests
{
    [Fact]
    public void Two_ready_neighbours_speak_once_each_and_wait_for_the_world_cooldown_after_reload()
    {
        if (!LastingActors.Available || !AidUseWatch.Available) return;
        var speech = typeof(LwMod).Assembly.GetType("LivingWasteland.MeitouMod.KindnessVoices");
        Assert.NotNull(speech);
        var current = speech.GetProperty("Current", BindingFlags.Static | BindingFlags.NonPublic)!;
        var old = current.GetValue(null);
        try
        {
            var read = speech.GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)!;
            read.Invoke(null, [AppContext.BaseDirectory, (Action<string>)(_ => { })]);
            var tracker = new LwDeedsSystem([], W.Data([]));
            using var world = W.World(tracker);
            W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
            var left = W.Spawn(world, "Ruka", W.Bandits, Vector3.UnitX);
            var right = W.Spawn(world, "Kite", W.Bandits, Vector3.UnitZ);
            world.RunTicks(2);

            var book = tracker.Deeds.Kindness;
            foreach (var character in new[] { left, right })
            {
                var cold = world.Characters.Cold(character.Slot)!;
                ulong actor = LastingActors.Grant(world, character);
                book.Add(new KindnessDeed(actor, KindnessKind.Saved, cold.Name, cold.RecordId, "Dust Bandits", "Squin",
                    "Leaf", 100, default, true, false));
                book.SetHome(actor, new KindnessHome("the wilds", 0, 0, 3500, 128, true, true));
            }

            world.GameHours = 100 + 35 * 24;
            world.RunTicks(151);
            var barks = (System.Collections.IDictionary)typeof(World).GetField("_barks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
            Assert.Contains("35 days ago", barks[left]!.ToString());
            Assert.False(barks.Contains(right));
            var announced = typeof(KindnessBook).GetMethod("HasReunited", BindingFlags.Instance | BindingFlags.NonPublic)!;
            bool Has(KindnessBook from, ulong id) => (bool)announced.Invoke(from, [id])!;
            ulong first = LastingActors.Of(world.Characters.Cold(left.Slot)!);
            ulong second = LastingActors.Of(world.Characters.Cold(right.Slot)!);
            Assert.True(Has(book, first));
            Assert.False(Has(book, second));

            using var save = new MemoryStream();
            tracker.Write(save);
            save.Position = 0;
            var restored = new LwDeedsSystem([], tracker.Deeds.Data);
            restored.Read(save);
            Assert.True(Has(restored.Deeds.Kindness, first));
            var next = typeof(KindnessBook).GetProperty("NextReunion", BindingFlags.Instance | BindingFlags.NonPublic)!;
            double deadline = (double)next.GetValue(restored.Deeds.Kindness)!;
            Assert.True(deadline > world.GameHours + 24);
            world.GameHours = deadline + 1;
            world.RunTicks(151);
            Assert.True(Has(book, second));
            Assert.True(barks.Contains(right));
        }
        finally { current.SetValue(null, old); }
    }
}
