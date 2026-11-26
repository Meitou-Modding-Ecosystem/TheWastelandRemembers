using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Ai;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Save;
using Meitou.Data.World;
using Meitou.Game.Ui;
using Meitou.Simulation;
using Meitou.Simulation.Ai;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Construction;
using Meitou.Simulation.Crime;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Population;
using Meitou.Simulation.Player;
using Meitou.Simulation.Saving;
using Rivalry;
using Xunit;
using SimWorld = Meitou.Simulation.World;

[Collection("settings")]
public sealed class OutpostWorldTests : IDisposable
{
    const string ModId = "LivingWastelandMeitou";
    const string Camp = "Rivergate";
    const string Defence = "Your squad defended Rivergate against raiders from the Dust Bandits.";
    static readonly Vector3 At = new(10000, 150, 10000);
    readonly bool old = LwMod.Settings.RememberOutposts, news = LwMod.Settings.OtherModsNews, couriers = LwMod.Settings.OutpostCouriers;

    public void Dispose()
    {
        LwMod.Settings.RememberOutposts = old;
        LwMod.Settings.OtherModsNews = news;
        LwMod.Settings.OutpostCouriers = couriers;
    }

    static PopulationData Data(IEnumerable<FcsRecord>? extra = null, IEnumerable<TownPlacement>? towns = null)
    {
        var shack = W.Rec("shack", FcsRecordType.Building, "Small Shack");
        shack.References["interior"] = [new FcsReference("interior-mesh", 0, 0, 0)];
        var bed = W.Rec("bed", FcsRecordType.Building, "Bed");
        bed.Bools["is interior furniture"] = true;
        var turret = W.Rec("turret", FcsRecordType.Building, "Turret");
        turret.Bools["is node"] = true;
        turret.Bools["is exterior furniture"] = true;
        var assault = W.Rec("dust-assault", FcsRecordType.FactionCampaign, "Bandit Raid");
        assault.Bools["is hostile"] = true;
        return W.Data([shack, bed, turret, assault, W.Rec("traders", FcsRecordType.Faction, "Traders Guild"), .. extra ?? []], towns);
    }

    static BuildSite Build(SimWorld world, PlayerTown town, int id, string record, bool complete = true, string? parent = null, int? owner = null)
    {
        var at = town.Position + new Vector3(id, 0, 0);
        var zone = WorldLayout.ZoneOf(at.X, at.Z);
        var site = new BuildSite { Id = $"{id}-zone{zone.X}.{zone.Y}-INGAME", Record = record, Owner = owner ?? W.Player,
            Position = at, Materials = [], Parent = parent, Town = town.Number, Complete = complete };
        world.Buildings.Add(site);
        return site;
    }

    static void FinishShack(SimWorld world, PlayerTown town)
    {
        var shack = Build(world, town, 1, "shack");
        Build(world, town, 2, "bed", parent: shack.Id);
    }

    static IEnumerable<string> Notes(SimWorld world) => LwDeeds.For(world)!.Record.History.Select(h => h.Text);

    static void Reload<T>(T before, T after) where T : Meitou.Modding.IModSaveData
    {
        using var save = new MemoryStream();
        before.Write(save);
        save.Position = 0;
        after.Read(save);
    }

    [Fact]
    public void A_defensive_wall_needs_a_delivery_and_completion_and_the_milestone_survives_reload()
    {
        if (!OutpostMemories.Available || WallConstructionWatch.Build() is not { } adapter) return;
        LwMod.Settings.RememberOutposts = true;
        var wall = W.Rec("wall", FcsRecordType.Building, "Defensive Wall");
        wall.Bools["links together"] = true;
        var data = Data([wall]);
        var hook = (ITickSystem)Activator.CreateInstance(adapter)!;
        var observe = adapter.GetMethod("ItemUsedUp")!;
        var payload = observe.GetParameters()[1].ParameterType.GetElementType()!;
        var kind = Enum.Parse(payload.GetProperty("Kind")!.PropertyType, "BuildingMaterial");
        void Deliver(SimWorld world, BuildSite site) =>
            observe.Invoke(hook, [world, Activator.CreateInstance(payload, world.Tick, kind, "stone", CharacterId.None, site.Id, 1f, true)]);

        var before = new OutpostMemories(data, null, ModId);
        using var world = W.World(new LwDeedsSystem([], data), before, hook);
        var town = world.Buildings.AddTown(At, W.Player, Camp);
        FinishShack(world, town);
        Build(world, town, 8, "wall"); // Already there before an observed delivery.
        var cancelled = Build(world, town, 9, "wall", complete: false);
        var building = Build(world, town, 10, "wall", complete: false);
        world.RunTicks(150);
        Deliver(world, cancelled);
        cancelled.Destroyed = true;
        Deliver(world, building);
        world.RunTicks(150);
        const string note = "The squad finished a defensive wall at Rivergate.";
        Assert.DoesNotContain(note, Notes(world));

        var resumed = new OutpostMemories(data, null, ModId);
        Reload(before, resumed);
        using var next = W.World(new LwDeedsSystem([], data), resumed, hook);
        var rebuilt = next.Buildings.AddTown(At, W.Player, Camp);
        FinishShack(next, rebuilt);
        Build(next, rebuilt, 10, "wall");
        next.RunTicks(300);
        Assert.Single(Notes(next), line => line == note);
        Assert.Contains(Assert.Single(resumed.AtlasRows()).Page, line => line.StartsWith("First defensive wall recorded", StringComparison.Ordinal));
        Assert.Contains("First defensive wall recorded", StatsUi.StoryText());

        var final = new OutpostMemories(data, null, ModId);
        Reload(resumed, final);
        using var third = W.World(new LwDeedsSystem([], data), final, hook);
        var again = third.Buildings.AddTown(At, W.Player, Camp);
        FinishShack(third, again);
        var later = Build(third, again, 11, "wall");
        third.RunTicks(150);
        Deliver(third, later);
        third.RunTicks(150);
        Assert.DoesNotContain(note, Notes(third));
    }

    [Fact]
    public void Founding_requires_a_completed_player_built_shelter_furnished_or_not()
    {
        // A completed shelter is enough (play test of bf: an empty storm house never counted until a bed went in); an unfinished one,
        // furniture outdoors, or the setting off are still not.
        Assert.False(new LwSettings().RememberOutposts);
        var data = Data();
        var outposts = new OutpostMemories(data, null, ModId);
        var world = W.World(new LwDeedsSystem([], data), outposts, new AtlasSystem(new AtlasTable(), null, data));
        var town = world.Buildings.AddTown(At, W.Player, Camp);
        var shack = Build(world, town, 1, "shack", complete: false);
        Build(world, town, 2, "bed"); // Outdoors.
        Build(world, town, 3, "turret", parent: shack.Id);
        LwMod.Settings.RememberOutposts = true;
        world.RunTicks(150);
        Assert.Empty(outposts.AtlasRows());

        shack.Complete = true;
        LwMod.Settings.RememberOutposts = false;
        world.RunTicks(150);
        Assert.Empty(outposts.AtlasRows());
        LwMod.Settings.RememberOutposts = true;
        world.RunTicks(250);
        if (!OutpostMemories.Available)
        {
            Assert.Empty(outposts.AtlasRows());
            return;
        }
        Assert.Single(outposts.AtlasRows());
        Assert.Single(Notes(world), line => line == "The squad founded Rivergate.");
        Assert.Equal(Camp, Assert.Single(LwDeeds.For(world)!.Record.History, item => item.Text == "The squad founded Rivergate.").Place);
        Assert.Contains("-- Outposts --", RecordHost.AtlasList);
        string key = "o:" + Assert.Single(outposts.AtlasRows()).Key;
        Assert.Contains("Founded on day", RecordHost.AtlasPages[key]);
    }

    [Fact]
    public void A_save_preserves_the_town_tag_and_its_foundation_through_rename_and_rebuild()
    {
        if (!OutpostMemories.Available) return;
        LwMod.Settings.RememberOutposts = true;
        var data = Data();
        var deeds = new LwDeedsSystem([], data);
        var outposts = new OutpostMemories(data, null, ModId);
        var world = W.World(deeds, outposts);
        var town = world.Buildings.AddTown(At, W.Player, Camp);
        FinishShack(world, town);
        // Existing world before the feature has a save chunk.
        using (var legacy = new MemoryStream()) outposts.Read(legacy);
        world.RunTicks(150);
        var original = Assert.Single(outposts.AtlasRows());
        Assert.Single(Notes(world), line => line == "The squad first recorded Rivergate.");

        var quick = new SaveFile();
        quick.Data.Records.Add(W.Rec("camera", FcsRecordType.Camera, "camera"));
        var game = new SaveGame(quick);
        BuildingSave.Write(game, world, "204-gamedata.base");

        var nextDeeds = new LwDeedsSystem([], data);
        var nextOutposts = new OutpostMemories(data, null, ModId);
        Reload(deeds, nextDeeds);
        Reload(outposts, nextOutposts);
        var next = W.World(nextDeeds, nextOutposts);
        next.GameHours = world.GameHours;
        next.Buildings.AddTown(At * 4, W.Player, "Other Place"); // Shift the runtime town number.
        BuildingSave.Read(game, next, data.Db, "204-gamedata.base", W.Player);
        var restored = Assert.Single(next.Buildings.Towns, t => t.Name == Camp);
        Assert.NotEqual(town.Number, restored.Number);
        restored.Name = "North Gate";
        next.RunTicks(150);
        var row = Assert.Single(nextOutposts.AtlasRows());
        Assert.Equal(original.Key, row.Key);
        Assert.Equal("North Gate", row.Name);
        Assert.Single(Notes(next), line => line.Contains("first recorded", StringComparison.Ordinal));
        Assert.DoesNotContain(Notes(next), line => line.Contains("founded", StringComparison.Ordinal));

        var oldShack = Assert.Single(next.Buildings.All, s => s.Town == restored.Number && s.Record == "shack");
        oldShack.Destroyed = true;
        next.RunTicks(150);
        Assert.Contains("There is no finished shelter here now.", Assert.Single(nextOutposts.AtlasRows()).Page);
        var replacement = Build(next, restored, 3, "shack");
        Build(next, restored, 4, "bed", parent: replacement.Id);
        next.RunTicks(150);
        Assert.Equal(original.Key, Assert.Single(nextOutposts.AtlasRows()).Key);
        Assert.Single(Notes(next), line => line.Contains("first recorded", StringComparison.Ordinal));
    }

    sealed class RaidScene
    {
        public readonly SimWorld World;
        public readonly LwDeedsSystem Deeds;
        public readonly OutpostMemories Outposts;
        public readonly NewsSystem News;
        public readonly PlayerTown Town;
        public readonly int Dust, Traders;
        public readonly CharacterId Leaf, Enemy;
        public readonly CampaignInstance Raid;
        readonly WarManager war;

        public RaidScene(PopulationData data, RaidScene? saved = null, bool start = true)
        {
            Dust = data.FactionIndex("60-t");
            Traders = data.FactionIndex("traders");
            var pop = new PopulationSystem(data);
            Deeds = new LwDeedsSystem([], data);
            News = new NewsSystem(data);
            Outposts = new OutpostMemories(data, pop, ModId);
            World = W.World(Deeds, News, Outposts);
            if (saved is not null)
            {
                Reload(saved.Deeds, Deeds);
                Reload(saved.Outposts, Outposts);
                Reload(saved.News, News);
                World.GameHours = saved.World.GameHours;
            }
            Town = World.Buildings.AddTown(At, W.Player, Camp);
            FinishShack(World, Town);
            Leaf = W.Spawn(World, "Leaf", W.Player, At);
            war = new WarManager { Faction = Dust };
            typeof(PopulationSystem).GetField("_wars", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(pop, new[] { war });
            (Raid, Enemy) = AddRaid(1);
            if (start) World.RunTicks(150);
        }

        public (CampaignInstance Campaign, CharacterId Raider) AddRaid(int id)
        {
            var raid = new CampaignInstance { Faction = Dust, Enemy = W.Player, Id = id, CampaignId = "dust-assault",
                Target = CampaignTown.OfPlayer(Town.Number), Phase = 1, Reached = true };
            war.Actives.Add(raid);
            var squad = new Squad { Id = World.Squads.NextId(), TemplateId = "squad", Faction = Dust };
            World.Squads.Add(squad);
            var platoon = new Platoon { Id = World.Platoons.NextId(), Origin = 0, TemplateId = "squad", Key = (ulong)id, State = PlatoonState.Loaded,
                SquadId = squad.Id, Faction = Dust, Campaign = raid, Position = new Vector2(At.X, At.Z) };
            World.Platoons.Add(platoon);
            squad.PlatoonId = platoon.Id;
            var raider = W.Spawn(World, "Raider", Dust, At + Vector3.UnitX * 10, squad: squad.Id);
            return (raid, raider);
        }

        public void Hit(CharacterId attacker, CharacterId victim) => Outposts.HitResolved(World, W.Hit(World, attacker, victim));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_fiefdom_resident_cannot_start_or_add_a_casualty_to_a_squad_defence(bool squadAlsoFought)
    {
        if (!OutpostMemories.Available || typeof(Squad).GetProperty("ModTags") is not { } tags) return;
        LwMod.Settings.RememberOutposts = true;
        var scene = new RaidScene(Data());
        using var world = scene.World;
        var locals = new Squad { Id = world.Squads.NextId(), Name = "Residents", TemplateId = "locals", Faction = W.Player };
        world.Squads.Add(locals);
        Assert.IsAssignableFrom<IDictionary<string, string>>(tags.GetValue(locals))["fiefdom"] = "town";
        var resident = W.Spawn(world, "Local Guard", W.Player, At, squad: locals.Id);

        if (squadAlsoFought) scene.Hit(scene.Leaf, scene.Enemy);
        typeof(MedicalState).GetProperty(nameof(MedicalState.Dead))!.SetValue(world.Characters.Cold(resident.Slot)!.Medical, true);
        scene.Hit(scene.Enemy, resident);
        scene.Raid.Phase = 2;
        world.RunTicks(300);

        Assert.Equal(squadAlsoFought ? 1 : 0, Notes(world).Count(line => line.StartsWith(Defence, StringComparison.Ordinal)));
        Assert.DoesNotContain(Notes(world), line => line.Contains("Local Guard", StringComparison.Ordinal));
    }

    [Fact]
    public void The_outpost_record_keeps_simultaneous_further_raids_and_their_fallen_across_a_save()
    {
        if (!OutpostMemories.Available) return;
        var enabled = typeof(LwSettings).GetProperty("OutpostDefenceRecord");
        Assert.NotNull(enabled);
        var before = enabled.GetValue(LwMod.Settings);
        try
        {
            LwMod.Settings.RememberOutposts = true;
            enabled.SetValue(LwMod.Settings, true);
            var data = Data();
            var scene = new RaidScene(data);
            using var world = scene.World;
            scene.Hit(scene.Leaf, scene.Enemy);
            scene.Raid.Phase = 2;
            world.RunTicks(150);
            scene.Hit(scene.Leaf, scene.AddRaid(2).Raider);
            var (_, lastRaider) = scene.AddRaid(3);
            var kite = W.Spawn(world, "Kite", W.Player, At);
            typeof(MedicalState).GetProperty(nameof(MedicalState.Dead))!.SetValue(world.Characters.Cold(kite.Slot)!.Medical, true);
            scene.Hit(lastRaider, kite);
            world.RunTicks(150);
            Assert.Contains("Raids fought: 1.", Assert.Single(scene.Outposts.AtlasRows()).Page);

            var next = new RaidScene(data, scene, start: false);
            using var loaded = next.World;
            var (second, raider) = next.AddRaid(2);
            var (third, _) = next.AddRaid(3);
            loaded.RunTicks(150);
            next.Hit(next.Leaf, raider);
            loaded.RunTicks(150);
            Assert.Contains("Raids fought: 1.", Assert.Single(next.Outposts.AtlasRows()).Page);
            second.Phase = third.Phase = 2;
            loaded.RunTicks(450);

            var page = Assert.Single(next.Outposts.AtlasRows()).Page;
            Assert.Contains("Raids fought: 3.", page);
            Assert.Single(page, line => line.Contains("Fallen in the fighting: Kite.", StringComparison.Ordinal));
            Assert.Equal(2, Notes(loaded).Count(line => line.StartsWith("The squad fought raiders", StringComparison.Ordinal)));
            Assert.Contains("Rivergate: 3 raids fought.", RecordHost.Panel);
            Assert.DoesNotContain("raids repelled", RecordHost.Panel, StringComparison.OrdinalIgnoreCase);
        }
        finally { enabled.SetValue(LwMod.Settings, before); }
    }

    [Fact]
    public void A_real_defence_is_recorded_once_after_the_raid_ends_and_a_visitor_can_spread_it()
    {
        if (!OutpostMemories.Available) return;
        LwMod.Settings.RememberOutposts = true;
        LwMod.Settings.OtherModsNews = false;
        var data = Data();
        var scene = new RaidScene(data);
        var fighter = W.Spawn(scene.World, "Kite", W.Player, At);
        typeof(MedicalState).GetProperty(nameof(MedicalState.Dead))!.SetValue(scene.World.Characters.Cold(fighter.Slot)!.Medical, true);
        scene.Hit(scene.Enemy, fighter);
        scene.World.RunTicks(150);
        Assert.DoesNotContain(Notes(scene.World), line => line.StartsWith(Defence, StringComparison.Ordinal));

        var loaded = new RaidScene(data, scene);
        Assert.DoesNotContain(Notes(loaded.World), line => line.StartsWith(Defence, StringComparison.Ordinal));
        loaded.Raid.Phase = 2;
        loaded.World.RunTicks(300);
        var entry = Assert.Single(Notes(loaded.World), line => line.StartsWith(Defence, StringComparison.Ordinal));
        Assert.Contains("Fallen in the fighting: Kite.", entry);
        Assert.Contains(Assert.Single(loaded.Outposts.AtlasRows()).Page, line => line.StartsWith("First defence", StringComparison.Ordinal));
        Assert.Empty(loaded.News.AtlasRows());

        W.Spawn(loaded.World, "Passing trader", loaded.Traders, At + new Vector3(1400, 0, 0));
        loaded.World.RunTicks(300);
        Assert.Empty(loaded.News.AtlasRows());
        W.Spawn(loaded.World, "Visitor", loaded.Traders, At + new Vector3(100, 0, 0));
        loaded.World.RunTicks(300);
        Assert.Contains("There's been fighting at Rivergate.", Assert.Single(loaded.News.AtlasRows()).Row);
        loaded.World.RunTicks(300);
        Assert.Single(loaded.News.AtlasRows());
        Assert.Single(Notes(loaded.World), line => line.StartsWith(Defence, StringComparison.Ordinal));
    }

    [Fact]
    public void An_outpost_resident_cannot_hear_a_report_on_behalf_of_an_absent_squad()
    {
        if (!OutpostMemories.Available || typeof(Squad).GetProperty("ModTags") is not { } tags) return;
        LwMod.Settings.RememberOutposts = true;
        var scene = new RaidScene(Data());
        using var world = scene.World;
        scene.Hit(scene.Leaf, scene.Enemy);
        scene.Raid.Phase = 2;
        world.RunTicks(150);

        world.Characters.Place(scene.Leaf.Slot, At + Vector3.UnitX * 7000, 0);
        var residents = new Squad { Id = world.Squads.NextId(), Name = "Residents", TemplateId = "locals", Faction = W.Player };
        world.Squads.Add(residents);
        Assert.IsAssignableFrom<IDictionary<string, string>>(tags.GetValue(residents))["fiefdom"] = "town";
        W.Spawn(world, "Resident", W.Player, At, squad: residents.Id);
        var trader = W.Spawn(world, "Visitor", scene.Traders, At + Vector3.UnitX * 100);
        world.RunTicks(151);

        Assert.False(OutpostMemories.CanHearCourier(world, scene.Town, trader));
        Assert.Empty(scene.News.AtlasRows());

        world.Characters.Place(scene.Leaf.Slot, At, 0);
        world.RunTicks(151);
        Assert.True(OutpostMemories.CanHearCourier(world, scene.Town, trader));
        Assert.Contains("There's been fighting at Rivergate.", Assert.Single(scene.News.AtlasRows()).Row);
    }

    [Fact]
    public void A_road_fight_and_an_uncontested_raid_do_not_award_an_outpost_defence()
    {
        if (!OutpostMemories.Available) return;
        LwMod.Settings.RememberOutposts = true;
        var scene = new RaidScene(Data());
        var distant = W.Spawn(scene.World, "Traveller", W.Player, At + Vector3.UnitX * 7500);
        scene.World.RunTicks(1);
        scene.Hit(scene.Enemy, distant);
        scene.Raid.Phase = 2;
        scene.World.RunTicks(300);
        Assert.DoesNotContain(Notes(scene.World), line => line.StartsWith(Defence, StringComparison.Ordinal));
        Assert.DoesNotContain(Assert.Single(scene.Outposts.AtlasRows()).Page, line => line.StartsWith("First defence", StringComparison.Ordinal));
    }

    [Fact]
    public void A_hit_on_a_captured_raider_does_not_count_as_a_defence()
    {
        if (!OutpostMemories.Available) return;
        LwMod.Settings.RememberOutposts = true;
        var scene = new RaidScene(Data());
        scene.World.Characters.Cold(scene.Enemy.Slot)!.InSomething = HeldState.Imprisoned;
        scene.Hit(scene.Leaf, scene.Enemy);
        scene.Raid.Phase = 2;
        scene.World.RunTicks(300);
        Assert.DoesNotContain(Notes(scene.World), line => line.StartsWith(Defence, StringComparison.Ordinal));
    }

    [Fact]
    public void The_text_export_includes_outposts_and_clears_the_previous_world()
    {
        if (!OutpostMemories.Available) return;
        LwMod.Settings.RememberOutposts = true;
        var data = Data();
        using var world = W.World(new LwDeedsSystem([], data), new OutpostMemories(data, null, ModId));
        FinishShack(world, world.Buildings.AddTown(At, W.Player, Camp));
        world.RunTicks(150);
        string chapter = "OUTPOSTS" + Environment.NewLine + Camp;
        Assert.Contains(chapter, StatsUi.StoryText());
        Assert.Matches(@"First recorded on day [0-9]+\.", StatsUi.StoryText());

        LwMod.Settings.RememberOutposts = false;
        Assert.DoesNotContain(chapter, StatsUi.StoryText());
        LwMod.Settings.RememberOutposts = true;
        using var next = W.World(new LwDeedsSystem([], data), new OutpostMemories(data, null, ModId));
        WorldReady.Ready(next, true);
        Assert.DoesNotContain(chapter, StatsUi.StoryText());
        next.RunTicks(150);
        Assert.DoesNotContain(chapter, StatsUi.StoryText());
    }

    [Fact]
    public void The_native_town_panel_displays_only_the_current_outposts_history()
    {
        Type registrationType = typeof(UiRegistration);
        if (registrationType.GetMethod("AddTownPanelLines") is null)
        {
            Assert.False(OutpostTownPanel.Register(null!, _ => { }));
            return;
        }

        const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var hostType = registrationType.Assembly.GetType("Meitou.Game.Ui.UiHost", throwOnError: true)!;
        var host = Activator.CreateInstance(hostType, nonPublic: true)!;
        var make = registrationType.GetConstructors(instance).Single();      // (host, id), and from the 24 Nov nightly an optional mod folder
        using var registration = (UiRegistration)make.Invoke([host, ModId, .. make.GetParameters().Skip(2).Select(p => p.HasDefaultValue ? p.DefaultValue : null)]);
        var logs = new List<string>();
        Assert.True(OutpostTownPanel.Register(registration, logs.Add), string.Join(", ", logs));
        var lines = hostType.GetMethod("TownLines", instance)!;
        var contextType = lines.GetParameters()[0].ParameterType;
        var snapshotType = contextType.GetProperty("Town")!.PropertyType;
        var snapshotConstructor = snapshotType.GetConstructors().Single();
        var power = Activator.CreateInstance(snapshotConstructor.GetParameters()[6].ParameterType);

        LwMod.Settings.RememberOutposts = true;
        var scene = new RaidScene(Data());
        var other = scene.World.Buildings.AddTown(At * 3, W.Player, Camp);

        object[] Rows(PlayerTown town, bool owned = true, int? owner = null)
        {
            var snapshot = snapshotConstructor.Invoke([town.Number, town.Name, town.Position, town.Radius, owner ?? town.Owner, town.Public, power]);
            var context = contextType.GetConstructors().Single().Invoke(
                [snapshot, owned, null, (Func<long>)(() => 0), (Action<SimCommand>)(_ => throw new InvalidOperationException())]);
            return ((System.Collections.IEnumerable)lines.Invoke(host, [context])!).Cast<object>().ToArray();
        }

        void HasStatus(string value)
        {
            var row = Assert.Single(Rows(scene.Town));
            Assert.Equal("Outpost history", row.GetType().GetProperty("Label")!.GetValue(row));
            Assert.Equal(value, row.GetType().GetProperty("Value")!.GetValue(row));
            Assert.Null(row.GetType().GetProperty("Press")!.GetValue(row));
        }

        HasStatus("Recorded");
        Assert.Empty(Rows(other));
        Assert.Empty(Rows(scene.Town, owned: false));
        Assert.Empty(Rows(scene.Town, owner: scene.Dust));
        scene.Hit(scene.Leaf, scene.Enemy);
        scene.Raid.Phase = 2;
        scene.World.RunTicks(150);
        HasStatus("First defence recorded");

        LwMod.Settings.RememberOutposts = false;
        Assert.Empty(Rows(scene.Town));
        LwMod.Settings.RememberOutposts = true;
        using var next = W.World();
        WorldReady.Ready(next, true);
        Assert.Empty(Rows(scene.Town));
    }

    [Fact]
    public void A_first_trader_visit_requires_an_actual_public_stop_and_survives_a_reload()
    {
        if (!OutpostMemories.Available) return;
        LwMod.Settings.RememberOutposts = true;
        const string visit = "A trader from the Traders Guild visited Rivergate.";
        var data = Data();
        var scene = new RaidScene(data);
        scene.Town.Public = false;
        var board = new Blackboard([]);
        typeof(Blackboard).GetProperty(nameof(Blackboard.Current))!.SetValue(board,
            new AiPackage { Id = "trade", Name = "Trade", Signal = 16, Roles = [[], [], [], [], []] });
        board.InTown = CampaignTown.OfPlayer(scene.Town.Number);
        board.Destination = board.InTown;
        board.TraderState = 1;
        var squad = new Squad { Id = scene.World.Squads.NextId(), TemplateId = "traders", Faction = scene.Traders, Ai = board };
        scene.World.Squads.Add(squad);
        W.Spawn(scene.World, "Trader", scene.Traders, At + Vector3.UnitX * 100, squad: squad.Id);
        scene.World.RunTicks(150);
        Assert.DoesNotContain(visit, Notes(scene.World));

        scene.Town.Public = true;
        board.TraderState = 0; // A party on its way through has not stopped.
        scene.World.RunTicks(150);
        Assert.DoesNotContain(visit, Notes(scene.World));
        board.TraderState = 1;
        board.Destination = CampaignTown.None;
        scene.World.RunTicks(150);
        Assert.DoesNotContain(visit, Notes(scene.World));
        board.Destination = board.InTown;
        scene.World.RunTicks(300);
        Assert.Single(Notes(scene.World), text => text == visit);
        Assert.Contains("First trader from the Traders Guild", StatsUi.StoryText());

        var loaded = new RaidScene(data, scene);
        Assert.Contains(Assert.Single(loaded.Outposts.AtlasRows()).Page, line => line.StartsWith("First trader", StringComparison.Ordinal));
        Assert.Single(Notes(loaded.World), text => text == visit);
    }

    [Fact]
    public void An_outpost_save_from_before_trader_visits_keeps_its_first_defence()
    {
        if (!OutpostMemories.Available) return;
        LwMod.Settings.RememberOutposts = true;
        using var stream = new MemoryStream();
        using var save = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        save.Write(1); // The preview's original sidecar layout.
        save.Write(true);
        save.Write(1);
        save.Write("outpost:shack");
        save.Write(Camp);
        save.Write(50d);
        save.Write(false);
        save.Write(true);
        save.Write(true);
        save.Write("60-t");
        save.Write("Dust Bandits");
        save.Write("dust-assault");
        save.Write(1);
        save.Write(80d);
        save.Write(false);
        save.Write(0);
        save.Flush();
        stream.Position = 0;
        var outposts = new OutpostMemories(Data(), null, ModId);
        outposts.Read(stream);
        var page = Assert.Single(outposts.AtlasRows()).Page;
        Assert.Contains("Founded on day 3.", page);
        Assert.Contains("First defence, day 4.", page);
        Assert.DoesNotContain(page, line => line.StartsWith("First trader", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false, true)]
    public void A_courier_keeps_its_report_and_outpost_when_the_town_number_changes(bool close, bool hostileGuild, bool sharedTagChanged = false)
    {
        var scopedTags = typeof(Platoon).GetProperty("ModTags");
        if (!Couriers.Available || sharedTagChanged && scopedTags is null) return;
        LwMod.Settings.RememberOutposts = LwMod.Settings.OtherModsNews = LwMod.Settings.OutpostCouriers = true;
        var sourceAt = new Vector3(150000, 150, 10000);
        var origin = W.Rec("source", FcsRecordType.Town, "Squin");
        origin.References["faction"] = [new FcsReference("60-t", 0, 0, 0)];
        var race = W.Rec("courier-race", FcsRecordType.Race, "People");
        var actor = W.Rec("courier-person", FcsRecordType.Character, "Trader");
        actor.References["race"] = [new FcsReference(race.StringId, 100, 0, 0)];
        var package = W.Rec("courier-ai", FcsRecordType.AiPackage, "Wander and trade");
        package.Ints["signal func"] = 16;
        var template = W.Rec("courier-template", FcsRecordType.SquadTemplate, "Trade party");
        template.References["faction"] = [new FcsReference("traders", 0, 0, 0)];
        template.References["leader"] = [new FcsReference(actor.StringId, 1, 0, 100)];
        template.References["AI packages"] = [new FcsReference(package.StringId, 0, 0, 0)];
        var nomads = W.Rec("nomads", FcsRecordType.Faction, "Nomads");
        var fallback = W.Rec("nomad-template", FcsRecordType.SquadTemplate, "Nomad trade party");
        fallback.References["faction"] = [new FcsReference(nomads.StringId, 0, 0, 0)];
        fallback.References["leader"] = template.References["leader"];
        fallback.References["AI packages"] = template.References["AI packages"];
        var data = Data([origin, race, actor, package, template, nomads, fallback], [new TownPlacement("source-placement", origin.StringId, sourceAt, null)]);
        if (hostileGuild) data.Relations.Add(data.FactionIndex("traders"), W.Player, -100);

        var pop = new PopulationSystem(data);
        var deeds = new LwDeedsSystem([], data);
        var news = new NewsSystem(data);
        var outposts = new OutpostMemories(data, pop, ModId);
        var original = new Couriers(data, pop);
        using var world = W.World(deeds, news, outposts, original);
        var town = world.Buildings.AddTown(At, W.Player, Camp);
        town.Public = true;
        FinishShack(world, town);
        W.Spawn(world, "Leaf", W.Player, At);
        world.RunTicks(110);
        const string text = "A caravan was lost near Squin.";
        Assert.True(news.Report("caravan-lost", "squin", "Traders Guild", text, 70, "companion.test"));
        world.RunTicks(130);
        var tag = typeof(Platoon).GetProperty("ModTag")!;
        var goal = typeof(Platoon).GetProperty("ModGoal")!;
        var party = Assert.Single(world.Platoons.All, p => ((string?)tag.GetValue(p))?.StartsWith("lw:courier:", StringComparison.Ordinal) == true);
        Assert.Equal(hostileGuild ? fallback.StringId : template.StringId, party.TemplateId);
        Assert.Equal(new Vector2(At.X, At.Z), goal.GetValue(party));
        Assert.Empty(news.AtlasRows());

        // Round-trip the sidecars and the engine's town records. Remake the tagged stand-in with a fresh runtime id.
        var newPop = new PopulationSystem(data);
        var newDeeds = new LwDeedsSystem([], data);
        var newNews = new NewsSystem(data);
        var newOutposts = new OutpostMemories(data, newPop, ModId);
        var newCourier = new Couriers(data, newPop);
        Reload(deeds, newDeeds); Reload(news, newNews); Reload(outposts, newOutposts); Reload(original, newCourier);
        using var next = W.World(newDeeds, newNews, newOutposts, newCourier);
        next.GameHours = world.GameHours;
        var wrong = next.Buildings.AddTown(At * 3, W.Player, Camp);
        wrong.Public = true;
        var quick = new SaveFile();
        quick.Data.Records.Add(W.Rec("camera", FcsRecordType.Camera, "camera"));
        var saved = new SaveGame(quick);
        BuildingSave.Write(saved, world, "204-gamedata.base");
        BuildingSave.Read(saved, next, data.Db, "204-gamedata.base", W.Player);
        var restored = Assert.Single(next.Buildings.Towns, t => t.Number != wrong.Number && t.Position == At);
        Assert.NotEqual(town.Number, restored.Number);
        restored.Public = !close;
        W.Spawn(next, "Leaf", W.Player, At);
        _ = next.Platoons.NextId();
        var resumed = new Platoon { Id = next.Platoons.NextId(), TemplateId = party.TemplateId, Faction = party.Faction,
            Origin = party.Origin, Key = party.Key, Position = party.Position };
        var ownTag = (string)tag.GetValue(party)!;
        tag.SetValue(resumed, sharedTagChanged ? "companion:route" : ownTag);
        if (sharedTagChanged)
            ((IDictionary<string, string>)scopedTags!.GetValue(resumed)!)["LivingWastelandMeitou"] = ownTag;
        next.Platoons.Add(resumed);
        next.RunTicks(60);
        if (close)
        {
            Assert.Equal("", tag.GetValue(resumed));
            Assert.Null(goal.GetValue(resumed));
            Assert.Empty(newNews.AtlasRows());
            return;
        }

        Assert.Equal(new Vector2(At.X, At.Z), goal.GetValue(resumed));
        var board = new Blackboard([]);
        typeof(Blackboard).GetProperty(nameof(Blackboard.Current))!.SetValue(board,
            new AiPackage { Id = "trade", Name = "Trade", Signal = 16, Roles = [[], [], [], [], []] });
        board.InTown = board.Destination = CampaignTown.OfPlayer(wrong.Number);
        board.TraderState = 1;
        var squad = new Squad { Id = next.Squads.NextId(), TemplateId = party.TemplateId, Faction = party.Faction, Ai = board };
        next.Squads.Add(squad);
        var trader = W.Spawn(next, "Trader", party.Faction, At + Vector3.UnitX * 50, squad: squad.Id);
        squad.Members.Add(trader);
        resumed.SquadId = squad.Id;
        resumed.State = PlatoonState.Loaded;
        next.RunTicks(150);
        Assert.Empty(newNews.AtlasRows());
        board.InTown = board.Destination = CampaignTown.OfPlayer(restored.Number);
        next.RunTicks(180);
        Assert.Single(Notes(next), line => line == text);
        Assert.Equal(text, Assert.Single(newNews.AtlasRows()).Row);
        Assert.Equal(sharedTagChanged ? "companion:route" : "", tag.GetValue(resumed));
        if (sharedTagChanged)
            Assert.False(((IDictionary<string, string>)scopedTags!.GetValue(resumed)!).ContainsKey("LivingWastelandMeitou"));
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(false, false, 1)]
    [InlineData(false, false, 2)]
    [InlineData(false, false, 3)]
    [InlineData(false, false, 4)]
    public void A_western_hive_envoy_keeps_its_destination_and_honours_prisoner_deals(bool closed, bool takeover, int deal)
    {
        if (!Couriers.Available || deal != 0 && !PrisonerHandover.Available) return;
        bool oldCases = LwMod.Settings.PrisonerCases, oldEnvoys = LwMod.Settings.HiveEnvoys, oldDeals = LwMod.Settings.PrisonerDeals;
        LwMod.Settings.RememberOutposts = LwMod.Settings.PrisonerCases = LwMod.Settings.HiveEnvoys = true;
        LwMod.Settings.PrisonerDeals = deal != 0;
        var ui = new RecruitmentRegistration();
        PrisonerMenu.Register(ui);
        try
        {
            var hive = W.Rec("hive", FcsRecordType.Faction, "Western Hive");
            var source = W.Rec("hive-village", FcsRecordType.Town, "Hive Village");
            source.References["faction"] = [new FcsReference(hive.StringId, 0, 0, 0)];
            var replacement = W.Rec("hive-lost", FcsRecordType.Town, "Hive Village");
            replacement.References["faction"] = [new FcsReference("60-t", 0, 0, 0)];
            var race = W.Rec("hive-race", FcsRecordType.Race, "Hiver");
            var actor = W.Rec("hive-trader", FcsRecordType.Character, "Hive trader");
            actor.References["race"] = [new FcsReference(race.StringId, 100, 0, 0)];
            var package = W.Rec("hive-ai", FcsRecordType.AiPackage, "Wander and trade");
            package.Ints["signal func"] = 16;
            var template = W.Rec("hive-squad", FcsRecordType.SquadTemplate, "Trade caravan");
            template.References["faction"] = [new FcsReference(hive.StringId, 0, 0, 0)];
            template.References["leader"] = [new FcsReference(actor.StringId, 1, 0, 100)];
            template.References["AI packages"] = [new FcsReference(package.StringId, 0, 0, 0)];
            var empire = W.Rec("empire", FcsRecordType.Faction, "United Cities");
            empire.Bools["offers bounties"] = true;
            var city = W.Rec("empire-city", FcsRecordType.Town, "Heft");
            city.References["faction"] = [new FcsReference(empire.StringId, 0, 0, 0)];
            var data = Data([hive, source, replacement, race, actor, package, template, empire, city],
                [new TownPlacement("hive-origin", source.StringId, new Vector3(50000, 150, 50000), null),
                 new TownPlacement("z-empire", city.StringId, new Vector3(100000, 150, 100000), null)]);
            int empireFaction = data.FactionIndex(empire.StringId);
            int hiveFaction = data.FactionIndex(hive.StringId);

            PlayerTown Outpost(SimWorld world)
            {
                var town = world.Buildings.AddTown(At, W.Player, Camp);
                town.Public = true;
                FinishShack(world, town);
                var queen = W.Spawn(world, "The Queen", hiveFaction, At);
                world.Uniques.Bind(PrisonerCases.WesternQueen, queen);
                world.Uniques.Captured(PrisonerCases.WesternQueen, true);
                PrisonerDealsWorldTests.Hold(world, queen, "queen-cage", town.Number, At);
                if (deal != 0)
                {
                    var queenCold = world.Characters.Cold(queen.Slot)!;
                    queenCold.Bounties = new BountyLedger();
                    queenCold.Bounties.Get(empireFaction, "United Cities").Amount = 4000;
                }
                W.Spawn(world, "Leaf", W.Player, At);
                return town;
            }

            var population = new PopulationSystem(data);
            var outposts = new OutpostMemories(data, population, ModId);
            var cases = new PrisonerCases(data, []);
            var envoys = new HiveEnvoys(data, population);
            using var first = W.World(new LwDeedsSystem([], data), outposts, cases, envoys);
            Outpost(first);
            first.RunTicks(300);
            var party = Assert.Single(first.Platoons.All, p => p is not null);
            var goal = typeof(Platoon).GetProperty("ModGoal")!;
            Assert.StartsWith("lw:hive-envoy:", Pursuit.ReadTag(party));
            Assert.Equal(template.StringId, party.TemplateId);
            Assert.Equal(new Vector2(At.X, At.Z), goal.GetValue(party));

            var restoredOutposts = new OutpostMemories(data, population, ModId);
            var restoredCases = new PrisonerCases(data, []);
            var restoredEnvoys = new HiveEnvoys(data, population);
            Reload(outposts, restoredOutposts);
            Reload(cases, restoredCases);
            Reload(envoys, restoredEnvoys);
            using var next = W.World(new LwDeedsSystem([], data), restoredOutposts, restoredCases, restoredEnvoys);
            next.GameHours = first.GameHours;
            _ = next.Buildings.AddTown(At * 3, W.Player, Camp);
            var town = Outpost(next);
            town.Public = !closed;
            var other = new Platoon { Id = next.Platoons.NextId(), Origin = party.Origin, Key = 5, TemplateId = party.TemplateId,
                Position = party.Position, Faction = party.Faction };
            next.Platoons.Add(other);
            Pursuit.Mark(other, "other-mod:caravan");
            var resumed = new Platoon { Id = next.Platoons.NextId(), Origin = party.Origin, Key = party.Key, TemplateId = party.TemplateId,
                Position = party.Position, Faction = party.Faction };
            next.Platoons.Add(resumed);
            Pursuit.Mark(resumed, Pursuit.ReadTag(party)!);
            if (takeover)
                typeof(PopulationData).GetMethod("SetReplacement")!.Invoke(data, [0, Meitou.Data.Gameplay.TownData.From(data.Db.Find(replacement.StringId)!)]);
            next.RunTicks(300);

            const string note = "A Western Hive envoy came to ask about the Queen.";
            Assert.DoesNotContain(note, Notes(next));
            Assert.Equal("other-mod:caravan", Pursuit.ReadTag(other));
            if (closed || takeover)
            {
                Assert.Equal("", Pursuit.ReadTag(resumed));
                Assert.Null(goal.GetValue(resumed));
                return;
            }
            FakeVerb Action(SimWorld sim, CharacterId target, string caption, Action<SimCommand> queue) =>
                ui.Verbs!(new RecruitmentMenu(new(target, sim.Characters.Cold(target.Slot)!.Name),
                    [W.Find(sim, "Leaf")], sim.Tick, queue)).Single(v => v.Caption == caption);

            var queen = W.Find(next, "The Queen");
            var legal = deal == 0 ? null : Action(next, queen, "Reserve delivery to United Cities", command => restoredCases.Inputs(next, [command]));
            if (deal == 2)
            {
                long purse = next.Player.Money;
                legal!.Choose();
                Assert.Equal(PrisonerCases.Route.Lawful, restoredCases.RouteOf(PrisonerCases.WesternQueen));
                Assert.Equal("", Pursuit.ReadTag(resumed));
                Assert.Equal(purse, next.Player.Money);
                Assert.True(next.Characters.Cold(queen.Slot)!.IsImprisoned);
                return;
            }

            Assert.Equal(new Vector2(At.X, At.Z), goal.GetValue(resumed));

            (Squad Squad, CharacterId Trader) Arrive(SimWorld sim, Platoon standIn, PlayerTown dest, long money)
            {
                var board = new Blackboard([]);
                typeof(Blackboard).GetProperty(nameof(Blackboard.Current))!.SetValue(board,
                    new AiPackage { Id = "trade", Name = "Trade", Signal = 16, Roles = [[], [], [], [], []] });
                board.InTown = board.Destination = CampaignTown.OfPlayer(dest.Number);
                board.TraderState = 1;
                var squad = new Squad { Id = sim.Squads.NextId(), TemplateId = standIn.TemplateId, Faction = standIn.Faction, Ai = board, Money = money };
                sim.Squads.Add(squad);
                var trader = W.Spawn(sim, "Trader", hiveFaction, At + Vector3.UnitX * 30, squad: squad.Id);
                squad.Members.Add(trader);
                standIn.State = PlatoonState.Loaded;
                standIn.SquadId = squad.Id;
                return (squad, trader);
            }

            var meeting = Arrive(next, resumed, town, party.Money);
            next.RunTicks(300);
            Assert.Single(Notes(next), h => h == note);
            if (deal is 1 or 3 or 4)
            {
                Assert.Equal(HiveEnvoys.RansomCats, meeting.Squad.Money);
                long purse = next.Player.Money;
                Action(next, meeting.Trader, "Accept 12,000-cat ransom", command => restoredEnvoys.Inputs(next, [command])).Choose();
                legal!.Choose();  // an older menu cannot displace accepted terms
                Assert.Equal(PrisonerCases.Route.Ransom, restoredCases.RouteOf(PrisonerCases.WesternQueen));
                Assert.Equal(purse, next.Player.Money);
                Assert.True(next.Characters.Cold(queen.Slot)!.IsImprisoned);

                SimCommand? stale = null;
                Action(next, meeting.Trader, "Hand over the Queen", command => stale = command).Choose();

                var finalOutposts = new OutpostMemories(data, population, ModId);
                var finalCases = new PrisonerCases(data, []);
                var finalEnvoys = new HiveEnvoys(data, population);
                if (deal == 1)
                {
                    PrisonerDealsWorldTests.ReplayPreviewSave(restoredOutposts, finalOutposts, "ransom-outpost");
                    PrisonerDealsWorldTests.ReplayPreviewSave(restoredCases, finalCases, "ransom-cases");
                    PrisonerDealsWorldTests.ReplayPreviewSave(restoredEnvoys, finalEnvoys, "ransom-envoy");
                }
                else
                {
                    Reload(restoredOutposts, finalOutposts);
                    Reload(restoredCases, finalCases);
                    Reload(restoredEnvoys, finalEnvoys);
                }
                using var final = W.World(new LwDeedsSystem([], data), finalOutposts, finalCases, finalEnvoys);
                final.GameHours = next.GameHours;
                final.Player.Money = purse;
                var finalTown = Outpost(final);
                var finalQueen = W.Find(final, "The Queen");
                var finalParty = new Platoon { Id = final.Platoons.NextId(), Origin = resumed.Origin, Key = resumed.Key,
                    TemplateId = resumed.TemplateId, Position = resumed.Position, Faction = resumed.Faction };
                Pursuit.Mark(finalParty, Pursuit.ReadTag(resumed)!);
                final.Platoons.Add(finalParty);
                var finalMeeting = Arrive(final, finalParty, finalTown, meeting.Squad.Money);
                if (deal == 3)
                {
                    // The restored envoy's first tick occurs before the custody ledger's first sweep.
                    LwMod.Settings.HiveEnvoys = false;
                    final.RunTicks(300);
                    Assert.Equal("", Pursuit.ReadTag(finalParty));
                    Assert.Equal(PrisonerCases.Route.Open, finalCases.RouteOf(PrisonerCases.WesternQueen));
                    Assert.Equal(purse, final.Player.Money);
                    Assert.True(final.Characters.Cold(finalQueen.Slot)!.IsImprisoned);
                    Assert.Equal("Reserve delivery to United Cities",
                        Action(final, finalQueen, "Reserve delivery to United Cities", _ => { }).Caption);
                    return;
                }
                final.RunTicks(300);
                finalEnvoys.Inputs(final, [stale!]);
                Assert.True(final.Characters.Cold(finalQueen.Slot)!.IsImprisoned);

                var handover = Action(final, finalMeeting.Trader, "Hand over the Queen", command => finalEnvoys.Inputs(final, [command]));
                if (deal == 4)
                {
                    Assert.True(finalCases.TryHeld(final, PrisonerCases.WesternQueen, out var held));
                    Assert.True(PrisonerHandover.Free(final, held));
                    var leaf = W.Find(final, "Leaf");
                    final.Characters.Cold(leaf.Slot)!.Carrying = finalQueen;
                    held.Captive.CarriedBy = leaf;
                    var officer = W.Spawn(final, "Bounty officer", empireFaction, At);
                    var desk = new ArrestSystem(new CrimeSystem(data.Relations), Meitou.Data.Gameplay.GameConstants.Default);
                    Assert.Equal(4000, desk.PayBounty(final, officer, leaf));
                    final.Characters.Cold(leaf.Slot)!.Carrying = CharacterId.None;
                    held.Captive.CarriedBy = CharacterId.None;
                    final.Uniques.Captured(PrisonerCases.WesternQueen, true);
                    var secondCell = PrisonerDealsWorldTests.Hold(final, finalQueen, "returned-queen-cage", finalTown.Number, At);
                    Assert.True(finalCases.TryHeld(final, PrisonerCases.WesternQueen, out _));
                    // Consume the queued envoy action before the custody system has read the native ledger.
                    Assert.Equal(PrisonerCases.Route.Ransom, finalCases.RouteOf(PrisonerCases.WesternQueen));
                    handover.Choose();
                    Assert.Equal(0, desk.PayBounty(final, officer, leaf));
                    final.RunTicks(300);
                    handover.Choose();
                    Assert.Equal(PrisonerCases.Route.LawfulPaid, finalCases.RouteOf(PrisonerCases.WesternQueen));
                    Assert.Equal("", Pursuit.ReadTag(finalParty));
                    Assert.Equal(purse + 4000, final.Player.Money);
                    Assert.Equal(HiveEnvoys.RansomCats, finalMeeting.Squad.Money);
                    Assert.Equal(finalQueen, secondCell.Occupant);
                    Assert.DoesNotContain(Notes(final), line => line.Contains("paid a ransom"));
                    return;
                }
                var captured = Recap.RecapApi.Captured = [];
                var recapEnabled = RecapLink.Enabled;
                RecapLink.Enabled = () => true;
                try
                {
                    finalMeeting.Squad.Money--;
                    handover.Choose();  // the reserve must still be present when the choice executes
                    Assert.Empty(captured);
                    Assert.Equal(purse, final.Player.Money);
                    Assert.True(final.Characters.Cold(finalQueen.Slot)!.IsImprisoned);
                    finalMeeting.Squad.Money++;
                    handover.Choose();
                    handover.Choose();
                    Assert.Equal(((Vector3?)At, true), Assert.Single(captured));
                    Assert.Equal(purse + HiveEnvoys.RansomCats, final.Player.Money);
                    Assert.Equal(0, finalMeeting.Squad.Money);
                    Assert.False(final.Characters.Cold(finalQueen.Slot)!.IsImprisoned);
                    Assert.True(final.Fixtures.Find("queen-cage")!.Occupant.IsNone);
                    Assert.Equal(UniqueState.Alive, final.Uniques.StateOf(PrisonerCases.WesternQueen));
                    Assert.Equal("", Pursuit.ReadTag(finalParty));
                    final.RunTicks(300);
                    Assert.Single(Notes(final), line => line.Contains("paid a ransom"));
                    Assert.Single(captured);  // The next custody sweep still writes History, with no second capture request.
                }
                finally
                {
                    Recap.RecapApi.Captured = null;
                    RecapLink.Enabled = recapEnabled;
                }
                using var receipt = PrisonerDealsWorldTests.Save(finalCases);
                var receiptBack = new PrisonerCases(data, []);
                receiptBack.Read(receipt);
                Assert.False(receiptBack.CanRansom(PrisonerCases.WesternQueen, 1));
                final.Uniques.Captured(PrisonerCases.WesternQueen, true);
                PrisonerDealsWorldTests.Hold(final, finalQueen, "new-queen-cage", finalTown.Number, At);
                final.RunTicks(300);
                Assert.True(finalCases.TryHeld(final, PrisonerCases.WesternQueen, out var recaptured));
                Assert.NotEqual(1, recaptured.Episode);
                Assert.False(finalCases.CanRansom(PrisonerCases.WesternQueen, recaptured.Episode));
                using var recapturedSave = PrisonerDealsWorldTests.Save(finalCases);
                receiptBack.Read(recapturedSave);
                Assert.Equal(recaptured.Episode, receiptBack.QueenEpisode);
                Assert.False(receiptBack.CanRansom(PrisonerCases.WesternQueen, recaptured.Episode));
                handover.Choose();
                Assert.Equal(purse + HiveEnvoys.RansomCats, final.Player.Money);
            }
            else
            {
                Assert.Equal("", Pursuit.ReadTag(resumed));
            }
            next.GameHours += 96;
            next.RunTicks(300);
            if (deal == 0) Assert.DoesNotContain(next.Platoons.All, p => p is not null && Pursuit.ReadTag(p)?.StartsWith("lw:hive-envoy:") == true);
        }
        finally
        {
            LwMod.Settings.PrisonerCases = oldCases;
            LwMod.Settings.HiveEnvoys = oldEnvoys;
            LwMod.Settings.PrisonerDeals = oldDeals;
        }
    }

    [Theory]
    [InlineData(false, false, "release")]
    [InlineData(true, false, "leader")]
    [InlineData(false, true, "release")]
    [InlineData(false, true, "disabled")]
    [InlineData(false, false, "lawful")]
    [InlineData(false, false, "wrong template")]
    [InlineData(false, false, "reused same template")]
    public void A_dust_bandit_recovery_requires_the_shared_slot_and_the_exact_unlocked_cell(bool ruler, bool legacy, string outcome)
    {
        if (!Couriers.Available || !PrisonerHandover.Available) return;
        bool oldCases = LwMod.Settings.PrisonerCases, oldRecovery = LwMod.Settings.PrisonerRecovery;
        bool oldDeals = LwMod.Settings.PrisonerDeals;
        LwMod.Settings.PrisonerDeals = outcome == "lawful";
        LwMod.Settings.RememberOutposts = LwMod.Settings.PrisonerCases = LwMod.Settings.PrisonerRecovery = true;
        try
        {
            string record = ruler ? PrisonerCases.Esata : "outlaw";
            var origin = W.Rec("origin", FcsRecordType.Town, "Squin");
            var captive = W.Rec(record, FcsRecordType.Character, ruler ? "Esata" : "Quillon");
            var race = W.Rec("recovery-race", FcsRecordType.Race, "Greenlander");
            var second = W.Rec("second", FcsRecordType.Character, "Benna");
            second.References["race"] = [new FcsReference(race.StringId, 100, 0, 0)];
            var fighter = W.Rec("rescuer", FcsRecordType.Character, "Dust Bandit");
            fighter.References["race"] = second.References["race"];
            var gang = W.Rec("gang", FcsRecordType.SquadTemplate, "Benna's crew");
            gang.References["faction"] = [new FcsReference("60-t", 0, 0, 0)];
            gang.References["leader"] = [new FcsReference(second.StringId, 1, 0, 100)];
            var rescue = W.Rec("rescue", FcsRecordType.SquadTemplate, "Rescue party");
            rescue.References["faction"] = gang.References["faction"];
            rescue.References["leader"] = [new FcsReference(fighter.StringId, 1, 0, 100)];
            var empire = W.Rec("empire", FcsRecordType.Faction, "United Cities");
            empire.Bools["offers bounties"] = true;
            var city = W.Rec("heft", FcsRecordType.Town, "Heft");
            city.References["faction"] = [new FcsReference(empire.StringId, 0, 0, 0)];
            var data = Data([origin, captive, race, second, fighter, gang, rescue, empire, city],
                [new TownPlacement("origin-placement", origin.StringId, At * 3, null),
                 new TownPlacement("z-empire", city.StringId, At * 4, null)]);
            int empireFaction = data.FactionIndex(empire.StringId);
            var ui = new RecruitmentRegistration();
            PrisonerMenu.Register(ui);
            data.Relations.Set(W.Bandits, W.Player, -80);
            CampCode[] camps = [new(0, captive.Name, captive.StringId, gang.StringId, [], rescue.StringId,
                new SuccessorCode(second.Name, second.StringId, gang.StringId))];

            (PlayerTown Town, CharacterId Leaf, CharacterId Actor, Fixture Cage) Scene(SimWorld sim)
            {
                var post = sim.Buildings.AddTown(At, W.Player, Camp);
                FinishShack(sim, post);
                var leaf = W.Spawn(sim, "Leaf", W.Player, At);
                var actor = W.Spawn(sim, captive.Name, W.Bandits, At);
                sim.Uniques.Bind(record, actor);
                sim.Uniques.Captured(record, true);
                var jail = PrisonerDealsWorldTests.Hold(sim, actor, "cell", post.Number, At);
                if (outcome == "lawful")
                {
                    var cold = sim.Characters.Cold(actor.Slot)!;
                    cold.Bounties = new BountyLedger();
                    cold.Bounties.Get(empireFaction, "United Cities").Amount = 4000;
                }
                var authority = W.Spawn(sim, second.Name, W.Bandits, At + Vector3.UnitX * 12000);
                sim.Uniques.Bind(second.StringId, authority);
                return (post, leaf, actor, jail);
            }

            var population = new PopulationSystem(data);
            var outposts = new OutpostMemories(data, population, ModId);
            var cases = new PrisonerCases(data, camps);
            var recovery = new PrisonerRecovery(data, population, camps);
            var core = new FakeRivalry();
            core.Claims.Add(("elsewhere", "already out"));
            using var first = W.World(core, new LwDeedsSystem([], data), outposts, cases, recovery);
            Scene(first);
            first.RunTicks(300);
            first.GameHours += 25;
            first.RunTicks(300);
            Assert.DoesNotContain(first.Platoons.All, p => p is not null);
            core.Claims.Clear();
            first.RunTicks(300);
            if (ruler)
            {
                Assert.DoesNotContain(first.Platoons.All, p => p is not null);
                Assert.Empty(core.Claims);
                Assert.Equal(UniqueState.Imprisoned, first.Uniques.StateOf(record));
                Assert.Single(Notes(first), text => text == "The squad took Esata alive.");
                return;
            }
            var sent = Assert.Single(first.Platoons.All, p => p is not null);
            Assert.StartsWith("lw:custody-recovery:", Pursuit.ReadTag(sent));
            Assert.Single(core.Claims, c => c.Key.StartsWith("custody-recovery:", StringComparison.Ordinal));

            var nextCore = new FakeRivalry();
            var nextOutposts = new OutpostMemories(data, population, ModId);
            var nextCases = new PrisonerCases(data, camps);
            var nextRecovery = new PrisonerRecovery(data, population, camps);
            if (outcome == "release" && !legacy)
            {
                PrisonerDealsWorldTests.ReplayPreviewSave(outposts, nextOutposts, "recovery-outpost");
                PrisonerDealsWorldTests.ReplayPreviewSave(cases, nextCases, "recovery-cases");
            }
            else
            {
                Reload(outposts, nextOutposts);
                Reload(cases, nextCases);
            }
            if (legacy)
            {
                // Format 4 is the extraction preview, before the lookup gained a cage record and a witness report.
                Assert.True(cases.TryHeld(first, record, out var held));
                string outpost = Assert.Single(outposts.FoundedTowns(first)).Key;
                using var oldSave = new MemoryStream();
                using (var writer = new BinaryWriter(oldSave, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    writer.Write(4);
                    writer.Write(1);
                    writer.Write(record);
                    writer.Write(held.Episode);
                    writer.Write(true);
                    writer.Write(held.Episode);
                    writer.Write(record);
                    writer.Write(second.StringId);
                    writer.Write(sent.TemplateId);
                    writer.Write(outpost);
                    writer.Write(first.GameHours + 72);
                    writer.Write((byte)PrisonerRecovery.Approach.OpenCell);
                    writer.Write(false);
                    RecoveryGate.Crossing.Write(writer, []);
                    writer.Write(sent.Size);
                    writer.Write(false);
                }
                oldSave.Position = 0;
                nextRecovery.Read(oldSave);
            }
            else if (outcome == "release")
                PrisonerDealsWorldTests.ReplayPreviewSave(recovery, nextRecovery, "recovery-party");
            else Reload(recovery, nextRecovery);
            using var next = W.World(nextCore, new LwDeedsSystem([], data), nextOutposts, nextCases, nextRecovery);
            next.GameHours = first.GameHours;
            _ = next.Buildings.AddTown(At * 2, W.Player, Camp);
            var target = Scene(next);
            var falseParty = new Platoon { Id = next.Platoons.NextId(), Key = 99, Origin = sent.Origin, TemplateId = sent.TemplateId,
                Faction = sent.Faction, Position = Couriers.Flat(At) };
            next.Platoons.Add(falseParty);
            Pursuit.Mark(falseParty, "foreign:gang");
            var party = new Platoon { Id = next.Platoons.NextId(), Key = sent.Key, Origin = sent.Origin, TemplateId = sent.TemplateId,
                Faction = sent.Faction, Position = sent.Position };
            next.Platoons.Add(party);
            Pursuit.Mark(party, Pursuit.ReadTag(sent)!);
            if (outcome == "disabled")
            {
                LwMod.Settings.PrisonerRecovery = false;
                next.RunTicks(300);
                Assert.True(Pursuit.IsUntagged(party));
                Assert.Null(typeof(Platoon).GetProperty("ModGoal")!.GetValue(party));
                Assert.Equal("foreign:gang", Pursuit.ReadTag(falseParty));
                Assert.Empty(nextCore.Claims);
                Assert.True(next.Characters.Cold(target.Actor.Slot)!.IsImprisoned);
                return;
            }
            next.RunTicks(300);
            Assert.Single(nextCore.Claims, c => c.Key.StartsWith("custody-recovery:", StringComparison.Ordinal));

            var board = new Blackboard([]) { Destination = CampaignTown.OfPlayer(target.Town.Number), TraderState = 1 };
            bool reused = outcome is "wrong template" or "reused same template";
            var squad = new Squad { Id = next.Squads.NextId(), TemplateId = outcome == "wrong template" ? gang.StringId : party.TemplateId,
                PlatoonId = reused ? falseParty.Id : party.Id, Faction = W.Bandits, Ai = board };
            next.Squads.Add(squad);
            var raider = W.Spawn(next, "Rescuer", W.Bandits, At + Vector3.UnitX * 100, squad: squad.Id);
            squad.Members.Add(raider);
            party.State = PlatoonState.Loaded;
            party.SquadId = squad.Id;
            if (outcome == "lawful" || reused)
            {
                if (outcome == "lawful")
                {
                    var cold = next.Characters.Cold(target.Actor.Slot)!;
                    var legal = Assert.Single(ui.Verbs!(new RecruitmentMenu(new(target.Actor, cold.Name), [target.Leaf],
                        next.Tick, input => nextCases.Inputs(next, [input]))));
                    legal.Choose();
                    legal.Choose();
                    Assert.Equal(PrisonerCases.Route.Lawful, nextCases.RouteOf(record));
                    Assert.Single(Notes(next), line => line.Contains("agreed to take"));
                }
                long purse = next.Player.Money;
                next.Characters.Place(raider.Slot, At, 0);
                next.Characters.Place(target.Leaf.Slot, At + Vector3.UnitX * 1500, 0);
                target.Cage.Lock!.Locked = false;
                next.RunTicks(300);
                Assert.True(next.Characters.Cold(target.Actor.Slot)!.IsImprisoned);
                Assert.Equal(target.Actor, target.Cage.Occupant);
                Assert.Equal(purse, next.Player.Money);
                Assert.DoesNotContain(Notes(next), text => text.Contains("gang freed them"));
                Assert.True(Pursuit.IsUntagged(party));
                Assert.Equal("foreign:gang", Pursuit.ReadTag(falseParty));
                Assert.Empty(nextCore.Claims);
                if (reused)
                {
                    Assert.Equal(CampaignTown.OfPlayer(target.Town.Number), board.Destination);
                    Assert.Equal(1, board.TraderState);
                }
                else
                {
                    Assert.True(nextCases.TryHeld(next, record, out var held));
                    Assert.True(PrisonerHandover.Free(next, held));
                    var keeper = next.Characters.Cold(target.Leaf.Slot)!;
                    keeper.Carrying = target.Actor;
                    held.Captive.CarriedBy = target.Leaf;
                    var officer = W.Spawn(next, "Bounty officer", empireFaction, At);
                    var desk = new ArrestSystem(new CrimeSystem(data.Relations), Meitou.Data.Gameplay.GameConstants.Default);
                    Assert.Equal(4000, desk.PayBounty(next, officer, target.Leaf));
                    Assert.Equal(0, desk.PayBounty(next, officer, target.Leaf));
                    keeper.Carrying = CharacterId.None;
                    held.Captive.CarriedBy = CharacterId.None;
                    next.RunTicks(450);
                    Assert.Equal(purse + 4000, next.Player.Money);
                    Assert.Single(Notes(next), text => text.Contains("is no longer in the squad's custody"));
                    Assert.DoesNotContain(Notes(next), text => text.Contains("gang freed them"));
                }
                return;
            }
            next.RunTicks(150);
            Assert.True(next.Characters.Cold(target.Actor.Slot)!.IsImprisoned);

            next.Characters.Place(raider.Slot, At, 0);
            target.Cage.Lock!.Locked = false;
            next.RunTicks(150);
            Assert.True(next.Characters.Cold(target.Actor.Slot)!.IsImprisoned);  // still defended

            next.Characters.Place(target.Leaf.Slot, At + Vector3.UnitX * 1500, 0);
            target.Cage.Lock.Locked = true;
            next.RunTicks(150);
            Assert.True(next.Characters.Cold(target.Actor.Slot)!.IsImprisoned);  // locked despite no defenders

            target.Cage.Lock.Locked = false;
            long balance = next.Player.Money;
            next.RunTicks(300);
            Assert.False(next.Characters.Cold(target.Actor.Slot)!.IsImprisoned);
            Assert.Equal(UniqueState.Alive, next.Uniques.StateOf(record));
            Assert.Equal(balance, next.Player.Money);
            Assert.Empty(nextCore.Claims);
            Assert.Equal("foreign:gang", Pursuit.ReadTag(falseParty));
            Assert.Equal("", Pursuit.ReadTag(party));
            Assert.Single(Notes(next), text => text.Contains("gang freed them"));
        }
        finally
        {
            LwMod.Settings.PrisonerCases = oldCases;
            LwMod.Settings.PrisonerRecovery = oldRecovery;
            LwMod.Settings.PrisonerDeals = oldDeals;
        }
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void The_scouts_pick_the_gate_or_search_an_old_cage_and_follow_a_witness_report(bool caught, bool moved)
    {
        if (!Couriers.Available || !PrisonerHandover.Available || !CovertRecovery.Available) return;
        bool oldCases = LwMod.Settings.PrisonerCases, oldRecovery = LwMod.Settings.PrisonerRecovery;
        bool oldQuiet = LwMod.Settings.CovertRecovery;
        LwMod.Settings.RememberOutposts = LwMod.Settings.PrisonerCases = LwMod.Settings.PrisonerRecovery = LwMod.Settings.CovertRecovery = true;
        try
        {
            var faction = W.Rec("sand", FcsRecordType.Faction, "Sand ninjas");
            var origin = W.Rec("sand-camp", FcsRecordType.Town, "Hidden Camp");
            var captive = W.Rec("sayo", FcsRecordType.Character, "Sayo of the Grey Ribbon");
            var race = W.Rec("sand-race", FcsRecordType.Race, "Greenlander");
            var second = W.Rec("kiri", FcsRecordType.Character, "Kiri of the Torn Ribbon");
            second.References["race"] = [new FcsReference(race.StringId, 100, 0, 0)];
            var fighter = W.Rec("scout", FcsRecordType.Character, "Grey Ribbon Infiltrator");
            fighter.References["race"] = second.References["race"];
            var home = W.Rec("home", FcsRecordType.SquadTemplate, "Kiri's cell");
            home.References["faction"] = [new FcsReference(faction.StringId, 0, 0, 0)];
            home.References["leader"] = [new FcsReference(second.StringId, 1, 0, 100)];
            var rescue = W.Rec("quiet", FcsRecordType.SquadTemplate, "Grey Ribbon Recovery Cell");
            rescue.References["faction"] = home.References["faction"];
            rescue.References["leader"] = [new FcsReference(fighter.StringId, 1, 0, 100)];
            var data = Data([faction, origin, captive, race, second, fighter, home, rescue],
                [new TownPlacement("sand-placement", origin.StringId, At * 3, null)]);
            int ninjas = data.FactionIndex(faction.StringId);
            data.Relations.Set(ninjas, W.Player, -80);
            CampCode[] camps = [new(0, captive.Name, captive.StringId, home.StringId, [], Successor:
                new SuccessorCode(second.Name, second.StringId, home.StringId), Infiltrators: rescue.StringId)];
            var population = new PopulationSystem(data);
            var core = new FakeRivalry();
            var locks = new LockSystem(data.Bodies.Constants, data.BodyOptions);
            using var world = W.World(locks, core, new LwDeedsSystem([], data), new OutpostMemories(data, population, ModId),
                new PrisonerCases(data, camps), new PrisonerRecovery(data, population, camps), new RecoveryClues());
            var post = world.Buildings.AddTown(At, W.Player, Camp);
            FinishShack(world, post);
            var guard = W.Spawn(world, "Guard", W.Player, At + Vector3.UnitZ * 160);
            var prisoner = W.Spawn(world, captive.Name, ninjas, At + Vector3.UnitX * 100);
            world.Uniques.Bind(captive.StringId, prisoner);
            world.Uniques.Captured(captive.StringId, true);
            var cage = PrisonerDealsWorldTests.Hold(world, prisoner, "quiet-cell", post.Number, At + Vector3.UnitX * 100);
            var gate = RecoveryGateWorldTests.Gate(world, "entry", post.Number, At);
            var decoy = RecoveryGateWorldTests.Gate(world, "wrong-entry", post.Number, At + Vector3.UnitZ * 100);
            var inner = RecoveryGateWorldTests.Gate(world, "inner-door", post.Number, At + Vector3.UnitX * 50, isGate: false);
            var issuer = W.Spawn(world, second.Name, ninjas, At + Vector3.UnitX * 12000);
            world.Uniques.Bind(second.StringId, issuer);
            world.RunTicks(300);
            world.GameHours += 25;
            world.RunTicks(300);

            var party = Assert.Single(world.Platoons.All, p => p is not null);
            Assert.InRange(party.Size, 1, 2);
            Assert.DoesNotContain(world.TakeMessages(), m => m.Contains("has sent people"));
            var squad = new Squad { Id = world.Squads.NextId(), TemplateId = party.TemplateId, Faction = ninjas, PlatoonId = party.Id };
            world.Squads.Add(squad);
            var outside = At - Vector3.UnitX * 15;
            var scout = W.Spawn(world, "Grey Ribbon Infiltrator", ninjas, outside, squad: squad.Id);
            var cold = world.Characters.Cold(scout.Slot)!;
            cold.Stats = new CharacterStats { [StatsEnumerated.Lockpicking] = 95 };
            squad.Members.Add(scout);
            party.State = PlatoonState.Loaded;
            party.SquadId = squad.Id;

            if (moved)
            {
                var secondAt = At + Vector3.UnitZ * 20000;
                var destination = world.Buildings.AddTown(secondAt, W.Player, "Hidden Stockade");
                FinishShack(world, destination);
                cage.Occupant = CharacterId.None;
                world.Characters.Place(prisoner.Slot, secondAt, 0);
                var newCage = PrisonerDealsWorldTests.Hold(world, prisoner, "new-cell", destination.Number, secondAt);
                party.State = PlatoonState.Unloaded;
                world.RunTicks(170);
                Assert.Contains("searching the original cage", PrisonerRecovery.Inspect());
                var goal = (Vector2?)typeof(Platoon).GetProperty("ModGoal")!.GetValue(party);
                Assert.Equal(Couriers.Flat(cage.Position), goal);
                Assert.NotEqual(Couriers.Flat(newCage.Position), goal);

                party.State = PlatoonState.Loaded;
                world.Characters.Place(scout.Slot, cage.Position, 0);
                world.RunTicks(20);
                Assert.Contains("the old cage", PrisonerRecovery.Inspect());
                Assert.Empty(core.Claims);
                world.Characters.Place(scout.Slot, At + Vector3.UnitX * 12000, 0);
                world.RunTicks(20);
                Assert.True(Pursuit.IsUntagged(party));
                world.GameHours += 12;
                world.RunTicks(450);
                Assert.Single(world.Platoons.All, p => p is not null);  // No one told Kiri.
                W.Spawn(world, "Another bandit", W.Bandits, secondAt + Vector3.UnitZ * 40);
                world.GameHours += 12;
                world.RunTicks(300);
                Assert.Single(world.Platoons.All, p => p is not null);  // Wrong faction.
                W.Spawn(world, "Sand Ninja witness", ninjas, secondAt + Vector3.UnitZ * 40);
                world.RunTicks(300);
                Assert.Single(world.Platoons.All, p => p is not null);  // The report takes time.
                world.GameHours += 7;
                world.RunTicks(450);
                var following = Assert.Single(world.Platoons.All, p => p is not null &&
                    Pursuit.ReadTag(p)?.EndsWith(":lead", StringComparison.Ordinal) == true);
                Assert.Equal(Couriers.Flat(newCage.Position), (Vector2?)typeof(Platoon).GetProperty("ModGoal")!.GetValue(following));
                Assert.True(world.Characters.Cold(prisoner.Slot)!.IsImprisoned);
                return;
            }

            if (caught) world.Characters.Place(guard.Slot, outside + Vector3.UnitZ * 18, 0);
            else
            {
                gate.Lock!.Level = 100;
                cold.Stats[StatsEnumerated.Lockpicking] = 0;
                world.RunTicks(20);
                Assert.Contains("scout reached lock but cannot pick it", PrisonerRecovery.Inspect());
                Assert.Contains("fixture entry", PrisonerRecovery.Inspect());
                Assert.True(gate.Lock.EffectivelyLocked);
                gate.Lock.Level = 15;
                cold.Stats[StatsEnumerated.Lockpicking] = 95;
            }
            world.RunTicks(900);
            Assert.Equal(1f, gate.DoorProgress);
            Assert.True(decoy.Lock!.EffectivelyLocked);
            Assert.True(cage.Lock!.EffectivelyLocked);
            Assert.True(world.Characters.Cold(prisoner.Slot)!.IsImprisoned);
            if (caught)
            {
                Assert.True(gate.Lock!.EffectivelyLocked);
                Assert.Empty(core.Claims);  // Retreat gives the shared hostile slot back immediately.
                world.Characters.Place(scout.Slot, At + Vector3.UnitX * 12000, 0);
                world.RunTicks(20);
                Assert.True(Pursuit.IsUntagged(party));
                Assert.Equal(0, (int)(cold.Orders & CharacterOrders.Sneak));
                return;
            }
            Assert.False(gate.Lock!.EffectivelyLocked);
            Assert.True(inner.Lock!.EffectivelyLocked);
            Assert.NotEqual(0, (int)(cold.Orders & CharacterOrders.Sneak));
            world.Characters.Place(scout.Slot, inner.Position - Vector3.UnitX * 15, 0);
            world.RunTicks(900);
            Assert.False(inner.Lock.EffectivelyLocked);
            Assert.True(cage.Lock.EffectivelyLocked);
            world.Characters.Place(scout.Slot, cage.Position, 0);  // Simulate arrival through the open leaf; this world has no movement system.
            world.RunTicks(900);
            Assert.False(cage.Lock!.EffectivelyLocked);
            Assert.False(world.Characters.Cold(prisoner.Slot)!.IsImprisoned);
            Assert.Equal(UniqueState.Alive, world.Uniques.StateOf(captive.StringId));
            Assert.Empty(core.Claims);
            Assert.Equal(0, (int)(cold.Orders & CharacterOrders.Sneak));
            Assert.DoesNotContain(Notes(world), line => line.Contains("gang freed"));
            Assert.DoesNotContain(world.TakeMessages(), line => line.Contains("gang freed"));
            var barks = (System.Collections.IDictionary)typeof(SimWorld).GetField("_barks",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
            Assert.False(barks.Contains(guard));  // The guard was too far away to find the evidence.
            world.Characters.Place(guard.Slot, gate.Position, 0);
            world.RunTicks(150);
            Assert.Contains("Someone's been at this lock", barks[guard]!.ToString());
        }
        finally
        {
            LwMod.Settings.PrisonerCases = oldCases;
            LwMod.Settings.PrisonerRecovery = oldRecovery;
            LwMod.Settings.CovertRecovery = oldQuiet;
        }
    }

}
