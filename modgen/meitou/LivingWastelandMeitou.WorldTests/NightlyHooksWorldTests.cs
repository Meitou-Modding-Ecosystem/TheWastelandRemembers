using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Simulation;
using Xunit;
using SimWorld = Meitou.Simulation.World;

// The hooks of v0.20.2-nightly.20261117 (NightlyHooks.cs). The UI ones take stand-in registrations with the engine's shapes (a function
// from a context to markers or menu rows), so the reflection wiring is checked on every Meitou version; the relation observer runs only
// where the engine has it.
public sealed record FakeMarker(Vector2 At, string Group, Vector4 Colour, string Label = "", string Tooltip = "", int FromLevel = 0, float Scale = 1);
public sealed record FakeMapContext(int Level);
public sealed record FakeVerb(string Caption, Action Choose);
public sealed class FakeSnapshot { public CharacterId Id { get; set; } public bool IsPlayer { get; set; } public bool FightsPlayer { get; set; } public Vector3 Position { get; set; } }
public sealed record FakeMenuContext(FakeSnapshot? Target);

public sealed class FakeRegistration
{
    public Func<FakeMapContext, IEnumerable<FakeMarker>>? Markers;
    public Func<FakeMenuContext, IEnumerable<FakeVerb>>? Verbs;
    public void AddMapMarkers(Func<FakeMapContext, IEnumerable<FakeMarker>> markers) => Markers = markers;
    public void AddCharacterMenuVerbs(Func<FakeMenuContext, IEnumerable<FakeVerb>> verbs) => Verbs = verbs;
}

[Collection("settings")]
public class NightlyHooksWorldTests
{
    [Fact]
    public void Map_marks_reach_the_engines_marker_type_only_with_the_setting_on()
    {
        var reg = new FakeRegistration();
        NightlyHooks.RegisterUi(reg, _ => { });
        NightlyHooks.Marks = [new MapMark(MarkKind.Rival, 10, 20, "Hollin One-Arm", "Rival\thunting your squad", 0)];
        try
        {
            LwMod.Settings.MapMarks = false;
            Assert.Empty(reg.Markers!(new FakeMapContext(0)));
            LwMod.Settings.MapMarks = true;
            var m = Assert.Single(reg.Markers!(new FakeMapContext(0)));
            Assert.Equal(new Vector2(10, 20), m.At);
            Assert.Equal("Character", m.Group);
            Assert.Equal("Hollin One-Arm", m.Label);
        }
        finally { LwMod.Settings.MapMarks = false; NightlyHooks.Marks = []; }
    }

    [Fact]
    public void Townsfolk_offer_ask_for_news_and_choosing_it_queues_the_ask()
    {
        var reg = new FakeRegistration();
        NightlyHooks.RegisterUi(reg, _ => { });
        var session = new NightlyHooks.AskSession([(1000, 2000)]);
        NightlyHooks.CurrentAsks = session;
        var local = new FakeSnapshot { Id = new CharacterId(5, 1), Position = new Vector3(1200, 150, 2000) };
        try
        {
            LwMod.Settings.AskForNews = true;
            var verb = Assert.Single(reg.Verbs!(new FakeMenuContext(local)));
            Assert.Equal("Ask for news", verb.Caption);
            verb.Choose();
            Assert.True(session.Asks.TryDequeue(out var asked) && asked == local.Id);

            Assert.Empty(reg.Verbs!(new FakeMenuContext(new FakeSnapshot { IsPlayer = true, Position = local.Position })));
            Assert.Empty(reg.Verbs!(new FakeMenuContext(new FakeSnapshot { FightsPlayer = true, Position = local.Position })));
            Assert.Empty(reg.Verbs!(new FakeMenuContext(new FakeSnapshot { Position = new Vector3(90000, 150, 2000) })));   // the wilds
            Assert.Empty(reg.Verbs!(new FakeMenuContext(null)));
            LwMod.Settings.AskForNews = false;
            Assert.Empty(reg.Verbs!(new FakeMenuContext(local)));
        }
        finally { LwMod.Settings.AskForNews = false; NightlyHooks.CurrentAsks = null; }
    }

    [Fact]
    public void An_old_menu_cannot_ask_somebody_in_the_next_world()
    {
        var stack = new Vector3(10000, 150, 10000);
        var data = W.Data([W.Rec("town-stack", FcsRecordType.Town, "Stack")], [new TownPlacement("i-stack", "town-stack", stack, null)]);
        try
        {
            LwMod.Settings.AskForNews = true;
            var reg = new FakeRegistration();
            NightlyHooks.RegisterUi(reg, _ => { });
            var first = W.World(new NewsSystem(data));
            var old = W.Spawn(first, "Old speaker", W.Bandits, stack);
            first.RunTicks(2);
            var oldRow = Assert.Single(reg.Verbs!(new FakeMenuContext(new FakeSnapshot { Id = old, Position = stack })));

            var next = W.World(new NewsSystem(data));
            var replacement = W.Spawn(next, "New speaker", W.Bandits, stack);
            Assert.Equal(old, replacement);
            next.RunTicks(2);
            oldRow.Choose();  // The host can still hold a callback after the new world's initial tick.
            var barks = (System.Collections.IDictionary)typeof(SimWorld).GetField("_barks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(next)!;
            next.RunTicks(60);
            Assert.False(barks.Contains(replacement));
            Assert.Single(reg.Verbs(new FakeMenuContext(new FakeSnapshot { Id = replacement, Position = stack }))).Choose();
            next.RunTicks(60);
            Assert.True(barks.Contains(replacement));
        }
        finally { LwMod.Settings.AskForNews = false; }
    }

    [Fact]
    public void Someone_asked_says_the_news_and_the_squad_has_heard_it()
    {
        var stack = new Vector3(10000, 150, 10000);
        var data = W.Data([W.Rec("town-stack", FcsRecordType.Town, "Stack")], [new TownPlacement("i-stack", "town-stack", stack, null)]);
        var news = new NewsSystem(data);
        var world = W.World(news);
        W.Spawn(world, "Leaf", W.Player, new Vector3(300000, 150, 300000));
        world.RunTicks(2);
        Assert.True(news.Report("siege-began", "Stack", "Shek Kingdom", "", 70, "test"));
        var local = W.Spawn(world, "Stack local", W.Bandits, stack + new Vector3(2000, 0, 0));
        world.RunTicks(1);
        Assert.Empty(news.AtlasRows());

        news.AnswerAsk(world, local);
        var barks = (System.Collections.IDictionary)typeof(SimWorld).GetField("_barks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
        Assert.Contains("laid siege to Stack", barks[local]!.ToString());
        Assert.Single(news.AtlasRows());                                  // heard now, without going there
    }

    [Fact]
    public void A_faction_turning_on_the_squad_goes_into_History_with_the_reason()
    {
        var observer = typeof(SimWorld).Assembly.GetType("Meitou.Simulation.Factions.IRelationChangeObserver");
        if (observer is null) return;                                    // Meitou before v0.20.2-nightly.20261117: nothing to watch
        var type = RelationWatch.Build(observer);
        Assert.NotNull(type);
        var data = W.Data([]);
        var deeds = new LwDeedsSystem([], data);
        var world = W.World(deeds, (ITickSystem)Activator.CreateInstance(type!)!);
        var leaf = W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
        world.RunTicks(31);
        var add = typeof(Meitou.Simulation.Factions.PlayerRelations).GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => m.Name == "Add");
        Assert.True(add is not null, "PlayerRelations.Add not found");
        try
        {
            LwMod.Settings.RelationNews = true;
            var args = add!.GetParameters().Select(p => p.Name switch
            {
                "world" => (object?)world, _ when p.ParameterType.IsInstanceOfType(data.Relations) => data.Relations,
                _ when p.Position == 2 => W.Bandits, _ when p.Position == 3 => world.Player.Faction,
                _ when p.Position == 4 => Convert.ChangeType(-100, p.ParameterType),
                _ => p.HasDefaultValue ? p.DefaultValue : null,
            }).ToArray();
            add.Invoke(null, args);
            world.RunTicks(1);
            var history = LwDeeds.For(world)!.Record.History.Select(h => h.Text).ToList();
            Assert.Contains(history, l => l.Contains("turned against the squad", StringComparison.Ordinal));
        }
        finally { LwMod.Settings.RelationNews = false; }
    }
}
