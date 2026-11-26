using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Dialogue;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Items;
using Xunit;
using SimWorld = Meitou.Simulation.World;

[Collection("settings")]
public sealed class GuestCompatibilityWorldTests
{
    const string GuestTag = "bathhouse.customers.17";
    static readonly Vector3 At = new(1000, 150, 2000);
    static readonly PropertyInfo? SharedTag = typeof(Platoon).GetProperty("ModTag");

    static (Platoon Party, CharacterId Visitor) Visitors(SimWorld world, string template, string? mark = GuestTag)
    {
        var party = new Platoon { Id = world.Platoons.NextId(), Origin = 0, TemplateId = template,
            Faction = W.Bandits, Key = 1, State = PlatoonState.Loaded, Position = new Vector2(At.X, At.Z) };
        var squad = new Squad { Id = world.Squads.NextId(), Faction = W.Bandits, TemplateId = template, PlatoonId = party.Id };
        party.SquadId = squad.Id;
        var visitor = W.Spawn(world, "Bathhouse patron", W.Bandits, At, squad: squad.Id);
        squad.Members.Add(visitor);
        world.Squads.Add(squad);
        if (mark is not null) SharedTag!.SetValue(party, mark);
        world.Platoons.Add(party);
        return (party, visitor);
    }

    [Fact]
    public void A_hostile_bathhouse_patron_does_not_trigger_a_lookout()
    {
        if (SharedTag is null) return;
        var data = W.Data([]);
        data.Relations.Add(W.Bandits, W.Player, -100);
        var talk = new DialogueSystem(data);
        using var world = W.World(talk);
        W.Spawn(world, "Leaf", W.Player, At + Vector3.UnitX * 500);
        var (party, _) = Visitors(world, "customers");
        world.RunTicks(2);

        var previous = LwMod.Log;
        bool logChanges = LwMod.Settings.LogChanges;
        var lines = new List<string>();
        try
        {
            LwMod.Log = lines.Add;
            LwMod.Settings.LogChanges = true;
            new Lookout().Look(world, talk, data);
            Assert.DoesNotContain(lines, line => line.StartsWith("lookout: ", StringComparison.Ordinal));
            SharedTag.SetValue(party, "");
            new Lookout().Look(world, talk, data);
            Assert.Single(lines, line => line.StartsWith("lookout: ", StringComparison.Ordinal));
        }
        finally { LwMod.Log = previous; LwMod.Settings.LogChanges = logChanges; }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_news_menu_excludes_bathhouse_patrons_until_they_leave(bool atLoad)
    {
        if (SharedTag is null || PlatoonFates.BuildWatch() is not { } watch) return;
        var data = W.Data([W.Rec("town-stack", FcsRecordType.Town, "Stack")], [new TownPlacement("i-stack", "town-stack", At, null)]);
        var news = new NewsSystem(data);
        using var world = W.World(news, (ITickSystem)Activator.CreateInstance(watch)!);
        var reg = new FakeRegistration();
        NightlyHooks.RegisterUi(reg, _ => { });
        var wasEnabled = LwMod.Settings.AskForNews;
        var previousSession = NightlyHooks.CurrentAsks;
        try
        {
            LwMod.Settings.AskForNews = true;
            if (!atLoad) world.RunTicks(2);
            var (party, visitor) = Visitors(world, "customers");
            world.RunTicks(61);
            var target = new FakeMenuContext(new FakeSnapshot { Id = visitor, Position = At });
            Assert.Empty(reg.Verbs!(target));
            SharedTag.SetValue(party, "");
            world.RunTicks(61);
            Assert.Single(reg.Verbs(target));
        }
        finally { LwMod.Settings.AskForNews = wasEnabled; NightlyHooks.CurrentAsks = previousSession; }
    }

    [Fact]
    public void An_already_queued_ask_does_not_make_a_new_patron_answer()
    {
        if (SharedTag is null) return;
        var data = W.Data([W.Rec("town-stack", FcsRecordType.Town, "Stack")], [new TownPlacement("i-stack", "town-stack", At, null)]);
        var news = new NewsSystem(data);
        using var world = W.World(news);
        var (party, visitor) = Visitors(world, "customers", mark: null);
        var reg = new FakeRegistration();
        NightlyHooks.RegisterUi(reg, _ => { });
        var wasEnabled = LwMod.Settings.AskForNews;
        var previousSession = NightlyHooks.CurrentAsks;
        try
        {
            LwMod.Settings.AskForNews = true;
            world.RunTicks(2);
            var target = new FakeMenuContext(new FakeSnapshot { Id = visitor, Position = At });
            Assert.Single(reg.Verbs!(target)).Choose();
            SharedTag.SetValue(party, GuestTag);
            world.RunTicks(61);
            var barks = (System.Collections.IDictionary)typeof(SimWorld).GetField("_barks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
            Assert.False(barks.Contains(visitor));
        }
        finally { LwMod.Settings.AskForNews = wasEnabled; NightlyHooks.CurrentAsks = previousSession; }
    }

    [Fact]
    public void A_bathhouse_party_with_the_right_template_and_position_is_not_a_legacy_bounty_crew()
    {
        if (SharedTag is null) return;
        const string template = "crew";
        using var world = W.World();
        var (party, _) = Visitors(world, template);
        var hunters = new HuntersSystem(template, null, null);
        var match = typeof(HuntersSystem).GetMethod("MatchCrews", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Crew[] request = [new("Stack", 130, At.X, At.Z)];
        Assert.Null(Assert.Single((Platoon?[])match.Invoke(hunters, [world, request])!));
        SharedTag.SetValue(party, "");
        Assert.Same(party, Assert.Single((Platoon?[])match.Invoke(hunters, [world, request])!));
    }

    [Fact]
    public void A_mod_recruit_entering_the_player_roster_gets_a_record_and_inherits_a_keepsake()
    {
        const string template = "keepsake-t";
        var previous = Trophies.Keepsake;
        bool wasEnabled = LwMod.Settings.Trophies;
        try
        {
            Trophies.Keepsake = template;
            LwMod.Settings.Trophies = true;
            var data = W.Data([W.Rec(template, FcsRecordType.Item, "Keepsake of the Fallen")]);
            var deeds = new LwDeedsSystem([], data);
            using var world = W.World(new LwSystem(null, null, data), deeds);
            var squad = new Squad { Id = world.Squads.NextId(), Faction = W.Player, Name = "Travellers", TemplateId = "travellers" };
            world.Squads.Add(squad);
            var old = W.Spawn(world, "Leaf", W.Player, At, squad: squad.Id);
            squad.Leader = old;
            squad.Members.Add(old);
            world.RunTicks(31);

            // The relevant Kenshi Races result: the new character is a regular member of an existing player squad.
            var bag = new Inventory();
            var recruit = W.Spawn(world, "Ruka", W.Player, At + Vector3.UnitX, bag, squad.Id);
            squad.Members.Add(recruit);
            world.RunTicks(31);
            var sheet = LwDeeds.For(world)!;
            Assert.Equal("Ruka", sheet.Record.PeekKey(LwDeeds.Identity(world.Characters.Cold(recruit.Slot)!), "untracked"));
            typeof(MedicalState).GetProperty(nameof(MedicalState.Dead))!.SetValue(world.Characters.Cold(old.Slot)!.Medical, true);
            world.RunTicks(61);  // The heir may show a remembrance bar during the first look.
            Assert.Single(bag.Items, item => item.Record == template);
        }
        finally { Trophies.Keepsake = previous; LwMod.Settings.Trophies = wasEnabled; }
    }
}
