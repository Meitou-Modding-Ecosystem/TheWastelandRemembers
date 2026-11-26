using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Simulation;
using Xunit;

[Collection("settings")]
public sealed class NewsTaskBarWorldTests
{
    [Fact]
    public void A_listening_request_leaves_the_inbound_queue_but_still_answers_on_schedule()
    {
        var bar = typeof(CharacterCold).GetProperty("ModTaskBar");
        if (bar is null) return;
        var oldSession = NightlyHooks.CurrentAsks;
        bool enabled = LwMod.Settings.AskForNews;
        try
        {
            LwMod.Settings.AskForNews = true;
            using var world = W.World(new NewsSystem(null));
            var local = W.Spawn(world, "Local", W.Bandits, Vector3.Zero);
            world.RunTicks(2);
            var inbox = NightlyHooks.CurrentAsks!.Asks;
            inbox.Enqueue(local);
            world.RunTicks(1);
            Assert.Empty(inbox);
            var barks = (System.Collections.IDictionary)typeof(World)
                .GetField("_barks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
            Assert.False(barks.Contains(local));
            world.RunTicks(38);
            Assert.True(barks.Contains(local));
        }
        finally
        {
            LwMod.Settings.AskForNews = enabled;
            NightlyHooks.CurrentAsks = oldSession;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_queued_news_question_shows_a_bar_until_the_original_answer_tick(bool differentBar)
    {
        var bar = typeof(CharacterCold).GetProperty("ModTaskBar");
        if (bar is null) return;
        var oldSession = NightlyHooks.CurrentAsks;
        bool enabled = LwMod.Settings.AskForNews;
        try
        {
            LwMod.Settings.AskForNews = true;
            using var world = W.World(new NewsSystem(null));
            var local = W.Spawn(world, "Local", W.Bandits, Vector3.Zero);
            var cold = world.Characters.Cold(local.Slot)!;
            world.RunTicks(2);
            NightlyHooks.CurrentAsks!.Asks.Enqueue(local);
            world.RunTicks(1);
            var barks = (System.Collections.IDictionary)typeof(World)
                .GetField("_barks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
            Assert.Contains("Listening...", bar.GetValue(cold)?.ToString() ?? "");
            Assert.False(barks.Contains(local));

            object other = Activator.CreateInstance(Nullable.GetUnderlyingType(bar.PropertyType)!, ["Another mod", .4f])!;
            if (differentBar) bar.SetValue(cold, other);
            world.RunTicks(38);
            Assert.True(barks.Contains(local));
            Assert.Equal(differentBar ? other : null, bar.GetValue(cold));
        }
        finally
        {
            LwMod.Settings.AskForNews = enabled;
            NightlyHooks.CurrentAsks = oldSession;
        }
    }
}
