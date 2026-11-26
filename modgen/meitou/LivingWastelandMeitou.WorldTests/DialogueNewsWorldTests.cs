using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Game.Ui;
using Meitou.Simulation;
using Xunit;

[Collection("settings")]
public sealed class DialogueNewsWorldTests
{
    public sealed record Reply(string Text, Action Press);
    public sealed record Conversation(bool PlayerStarted);
    public sealed record Speaker(CharacterId Id, bool IsPlayer, bool FightsPlayer, int Faction, Vector3 Position);
    public sealed record Context(Conversation Conversation, Speaker? Npc)
    {
        public int Leaves { get; private set; }
        public void Leave() => Leaves++;
    }
    public sealed class Registration
    {
        public Func<Context, IEnumerable<Reply>>? Replies;
        public void AddDialogueReplies(Func<Context, IEnumerable<Reply>> source) => Replies = source;
    }

    static readonly Vector3 Stack = new(10000, 150, 10000);

    [Fact]
    public void The_released_game_reply_hook_accepts_the_news_source_on_clients_that_have_it()
    {
        if (typeof(UiRegistration).GetMethod("AddDialogueReplies") is null) return;
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var hostType = typeof(UiRegistration).Assembly.GetType("Meitou.Game.Ui.UiHost", throwOnError: true)!;
        var host = Activator.CreateInstance(hostType, nonPublic: true)!;
        var make = Assert.Single(typeof(UiRegistration).GetConstructors(any));
        using var registration = (UiRegistration)make.Invoke([host, "LivingWastelandMeitou",
            .. make.GetParameters().Skip(2).Select(p => p.HasDefaultValue ? p.DefaultValue : null)]);
        var log = new List<string>();
        NightlyHooks.RegisterUi(registration, log.Add);
        Assert.Contains("conversation news: added the town news reply", log);
    }

    [Fact]
    public void A_player_talking_to_a_townsperson_can_ask_the_same_news_question()
    {
        var data = W.Data([W.Rec("town-stack", FcsRecordType.Town, "Stack"), W.Rec("fish", FcsRecordType.Faction, "Fishmen")],
            [new TownPlacement("i-stack", "town-stack", Stack, null)]);
        using var world = W.World(new NewsSystem(data));
        var local = W.Spawn(world, "Local", W.Bandits, Stack);
        var reg = new Registration();
        var original = LwMod.Settings.AskForNews;
        var oldSession = NightlyHooks.CurrentAsks;
        try
        {
            LwMod.Settings.AskForNews = true;
            NightlyHooks.RegisterUi(reg, _ => { });
            Assert.NotNull(reg.Replies);
            world.RunTicks(2);

            var person = new Speaker(local, false, false, W.Bandits, Stack);
            var active = new Context(new(true), person);
            var row = Assert.Single(reg.Replies(active));
            Assert.Equal("Heard any news?", row.Text);
            Assert.Empty(reg.Replies(new Context(new(false), person)));
            Assert.Empty(reg.Replies(new Context(new(true), person with { FightsPlayer = true })));
            Assert.Empty(reg.Replies(new Context(new(true), person with { Faction = data.FactionIndex("fish") })));
            Assert.Empty(reg.Replies(new Context(new(true), person with { Position = new Vector3(90000, 150, 2000) })));
            row.Press();
            Assert.Equal(1, active.Leaves);
            world.RunTicks(50);
            var barks = (System.Collections.IDictionary)typeof(World).GetField("_barks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
            Assert.True(barks.Contains(local));

            using var second = W.World(new NewsSystem(data));
            Assert.Equal(local, W.Spawn(second, "Another local", W.Bandits, Stack));
            second.RunTicks(2);
            row.Press();
            Assert.Equal(1, active.Leaves);
            second.RunTicks(50);
            var secondBarks = (System.Collections.IDictionary)typeof(World).GetField("_barks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(second)!;
            Assert.False(secondBarks.Contains(local));
        }
        finally { LwMod.Settings.AskForNews = original; NightlyHooks.CurrentAsks = oldSession; }
    }
}
