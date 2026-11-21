using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Simulation;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Movement;
using Meitou.Simulation.Population;
using Meitou.Simulation.Saving;
using System.Reflection;
using Xunit;

[Collection("settings")]
public sealed class PrisonerEscortsWorldTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_homecoming_needs_the_actual_captive_and_escort_after_reload(bool originLost)
    {
        if (!Couriers.Available || !PrisonerHandover.Available) return;
        bool previous = LwMod.Settings.PrisonerEscorts, casesSetting = LwMod.Settings.PrisonerCases;
        LwMod.Settings.PrisonerEscorts = LwMod.Settings.PrisonerCases = true;
        try
        {
            const string record = PrisonerCases.WesternQueen;
            var at = PrisonerDealsWorldTests.At;
            var home = new Vector3(at.X * 2, at.Y, at.Z * 2);
            var hive = W.Rec("hive", FcsRecordType.Faction, "Western Hive");
            var town = W.Rec("hive-town", FcsRecordType.Town, "Hive Village");
            town.References["faction"] = [new FcsReference(hive.StringId, 0, 0, 0)];
            var takeover = W.Rec("lost-town", FcsRecordType.Town, "Lost Village");
            takeover.References["faction"] = [new FcsReference("60-t", 0, 0, 0)];
            var data = W.Data([hive, town, takeover], [new TownPlacement("hive-place", town.StringId, home, null)]);
            int faction = data.FactionIndex(hive.StringId);

            (Squad Squad, Platoon Party) Party(Meitou.Simulation.World world, Vector3 place, bool loaded)
            {
                var squad = new Squad { Id = world.Squads.NextId(), Faction = faction, TemplateId = "escort" };
                if (loaded)
                {
                    var guide = W.Spawn(world, "Guide", faction, place, squad: squad.Id);
                    squad.Members.Add(guide);
                    squad.Leader = guide;
                    world.Squads.Add(squad);
                }
                var party = new Platoon { Id = world.Platoons.NextId(), Key = 1, Origin = 0, TemplateId = "escort", Faction = faction,
                    Position = Couriers.Flat(place), State = loaded ? PlatoonState.Loaded : PlatoonState.Unloaded,
                    SquadId = loaded ? squad.Id : -1 };
                world.Platoons.Add(party);
                return (squad, party);
            }

            var cases = new PrisonerCases(data, []);
            var escorts = new PrisonerEscorts(data);
            using var first = W.World(new LwDeedsSystem([], data), cases, escorts);
            var (_, captive) = PrisonerDealsWorldTests.Actors(first, record, faction);
            PrisonerDealsWorldTests.Hold(first, captive);
            var (_, transport) = Party(first, at, loaded: true);
            first.RunTicks(150);
            Assert.True(cases.TryHeld(first, record, out var held));
            Assert.True(PrisonerHandover.Free(first, held));
            Assert.True(PrisonerEscorts.Begin(first, transport, held, hive.StringId, home, town: "town:hive-place"));
            first.RunTicks(150);
            var tag = Pursuit.ReadTag(transport)!;
            Assert.StartsWith("lw:prisoner-escort:", tag);

            using var save = PrisonerDealsWorldTests.Save(escorts);
            var resumed = new PrisonerEscorts(data);
            resumed.Read(save);
            using var next = W.World(new LwDeedsSystem([], data), resumed);
            next.GameHours = first.GameHours;
            W.Spawn(next, "Padding", W.Player, at);
            var (arriving, party) = Party(next, home, loaded: true);
            Pursuit.Mark(party, tag);
            next.Uniques.Bind(record, CharacterId.None);
            if (originLost)
            {
                typeof(Meitou.Simulation.Population.PopulationData).GetMethod("SetReplacement")!.Invoke(data,
                    [0, Meitou.Data.Gameplay.TownData.From(data.Db.Find(takeover.StringId)!)]);
            }
            next.RunTicks(180);
            var history = LwDeeds.For(next)!.Record.History;
            Assert.Empty(history);
            if (originLost)
            {
                Assert.Equal("", Pursuit.ReadTag(party));
                return;
            }

            Assert.Equal(tag, Pursuit.ReadTag(party));
            var wrong = W.Spawn(next, "Prisoner", faction, home);
            next.RunTicks(90);
            Assert.Empty(history);
            var exact = W.Spawn(next, "Prisoner", faction, home);
            Assert.NotEqual(wrong, exact);
            next.Uniques.Bind(record, exact);
            next.RunTicks(180);
            Assert.Equal("The escort brought Prisoner home.", Assert.Single(history).Text);
            Assert.Equal("", Pursuit.ReadTag(party));
            Assert.True(next.Characters.Cold(arriving.Leader.Slot)!.Faction == faction);
        }
        finally
        {
            LwMod.Settings.PrisonerEscorts = previous;
            LwMod.Settings.PrisonerCases = casesSetting;
        }
    }
    [Fact]
    public void A_reloaded_extraction_routes_the_guide_and_captive_back_through_the_entered_gate()
    {
        if (!Couriers.Available || !PrisonerHandover.Available) return;
        bool previous = LwMod.Settings.PrisonerEscorts, casesSetting = LwMod.Settings.PrisonerCases;
        LwMod.Settings.PrisonerEscorts = false;   // The break-in gets out even if long-distance escorts are disabled.
        LwMod.Settings.PrisonerCases = true;
        try
        {
            const string record = "sayo";
            var data = W.Data([]);
            var at = PrisonerDealsWorldTests.At;
            var inside = at + Vector3.UnitX * 300;
            var near = at - Vector3.UnitX * 30;
            var far = at + Vector3.UnitX * 30;
            var home = at + Vector3.UnitZ * 1500;
            var route = new RecoveryGate.Crossing("a-side-gate", near, far);
            (Squad, Platoon) BindParty(World world)
            {
                var squad = new Squad { Id = world.Squads.NextId(), Faction = W.Bandits, TemplateId = "scouts" };
                squad.Leader = W.Spawn(world, "Scout", W.Bandits, inside, squad: squad.Id);
                squad.Members.Add(squad.Leader);
                world.Squads.Add(squad);
                var party = new Platoon { Id = world.Platoons.NextId(), Faction = W.Bandits, TemplateId = squad.TemplateId,
                    State = PlatoonState.Loaded, SquadId = squad.Id, Position = Couriers.Flat(inside), Key = 10, Origin = 0 };
                world.Platoons.Add(party);
                return (squad, party);
            }
            void Captain(World world) => world.Uniques.Bind("kiri", W.Spawn(world, "Kiri", W.Bandits, home));
            var cases = new PrisonerCases(data, [new(0, "Sayo", record, "old", [])]);
            var escorts = new PrisonerEscorts(data);
            using var first = W.World(new LwDeedsSystem([], data), cases, escorts);
            Captain(first);
            var captive = W.Spawn(first, "Sayo", W.Bandits, inside);
            first.Uniques.Bind(record, captive);
            first.Uniques.Captured(record, true);
            PrisonerDealsWorldTests.Hold(first, captive, position: inside);
            var (_, outgoing) = BindParty(first);
            first.RunTicks(150);
            Assert.True(cases.TryHeld(first, record, out var held));
            Assert.True(PrisonerHandover.Free(first, held));
            Assert.True(PrisonerEscorts.Begin(first, outgoing, held, "60-t", home, issuer: "kiri", entry: [route]));

            using var save = PrisonerDealsWorldTests.Save(escorts);
            var resumed = new PrisonerEscorts(data);
            resumed.Read(save);
            var walk = new OpenGroundWalkability((_, _) => inside.Y);
            using var next = new World(new WorldSettings { Threads = 1, PublishSnapshots = false }, walk,
                new ITickSystem[] { new MovementSystem(new PathService(walk, true)), new LwDeedsSystem([], data), resumed });
            next.Player.Faction = W.Player;
            Captain(next);
            var exact = W.Spawn(next, "Sayo", W.Bandits, inside);
            next.Uniques.Bind(record, exact);
            var (scouts, party) = BindParty(next);
            Pursuit.Mark(party, Pursuit.ReadTag(outgoing)!);
            next.RunTicks(20);
            Assert.Equal(Couriers.Flat(near), next.Characters.Next[exact.Slot].Goal);
            Assert.Equal(Couriers.Flat(near), next.Characters.Next[scouts.Leader.Slot].Goal);

            next.Characters.Place(exact.Slot, near, 0);
            next.RunTicks(20);
            Assert.False(Pursuit.IsUntagged(party));  // the scout is still inside
            next.Characters.Place(scouts.Leader.Slot, near, 0);
            next.RunTicks(20);
            Assert.True(Pursuit.IsUntagged(party));
            Assert.Empty(LwDeeds.For(next)!.Record.History);  // getting out is distinct from getting home
        }
        finally
        {
            LwMod.Settings.PrisonerEscorts = previous;
            LwMod.Settings.PrisonerCases = casesSetting;
        }
    }

    [Theory]
    [InlineData(true, "current")]
    [InlineData(false, "current")]
    [InlineData(true, "legacy")]
    [InlineData(true, "disabled")]
    public void A_saved_offscreen_escort_counts_only_the_exact_native_roster_at_home(bool exactRoster, string scenario)
    {
        if (!Couriers.Available || !PrisonerHandover.Available) return;
        bool previous = LwMod.Settings.PrisonerEscorts, casesSetting = LwMod.Settings.PrisonerCases;
        LwMod.Settings.PrisonerEscorts = LwMod.Settings.PrisonerCases = true;
        try
        {
            const string record = "sayo";
            var at = PrisonerDealsWorldTests.At;
            var home = at + Vector3.UnitX * 2400;
            var town = W.Rec("town", FcsRecordType.Town, "Hideout");
            town.References["faction"] = [new FcsReference("60-t", 0, 0, 0)];
            var named = W.Rec(record, FcsRecordType.Character, "Sayo");
            named.Bools["unique"] = true;
            var captain = W.Rec("kiri", FcsRecordType.Character, "Kiri");
            captain.Bools["unique"] = true;
            var scout = W.Rec("r-Scout", FcsRecordType.Character, "Scout");
            var original = W.Rec("native separated captive", FcsRecordType.SquadTemplate, "Separated");
            var escort = W.Rec("scout party", FcsRecordType.SquadTemplate, "Scout party");
            var data = W.Data([town, named, captain, scout, original, escort], [new TownPlacement("town-place", town.StringId, home, null)]);
            var pop = new PopulationSystem(data);
            var cases = new PrisonerCases(data, [new(0, "Sayo", record, "old", [])]);
            var escorts = new PrisonerEscorts(data);
            using var first = W.World(new LwDeedsSystem([], data), cases, escorts);
            first.Uniques.Bind("kiri", W.Spawn(first, "Kiri", W.Bandits, home));
            (Squad Squad, Platoon Party, CharacterId Actor) Add(string name, string template)
            {
                int id = first.Platoons.NextId();
                var party = new Platoon { Id = id, Faction = W.Bandits, TemplateId = template,
                    State = PlatoonState.Loaded, Key = (ulong)id + 1, Origin = 0, Persistent = true,
                    Size = 1, Position = Couriers.Flat(at) };
                var squad = new Squad { Id = first.Squads.NextId(), Faction = W.Bandits, TemplateId = template, PlatoonId = party.Id };
                squad.Leader = W.Spawn(first, name, W.Bandits, at, squad: squad.Id);
                squad.Members.Add(squad.Leader);
                first.Squads.Add(squad);
                party.SquadId = squad.Id;
                first.Platoons.Add(party);
                return (squad, party, squad.Leader);
            }
            var ward = Add("Sayo", "native separated captive");
            var guard = Add("Scout", "scout party");
            first.Characters.Cold(ward.Actor.Slot)!.RecordId = record;
            first.Uniques.Bind(record, ward.Actor);
            first.Uniques.Captured(record, true);
            PrisonerDealsWorldTests.Hold(first, ward.Actor);
            first.RunTicks(150);
            Assert.True(cases.TryHeld(first, record, out var held));
            Assert.True(PrisonerHandover.Free(first, held));
            Assert.True(PrisonerEscorts.Begin(first, guard.Party, held, "60-t", home, issuer: "kiri"));
            first.RunTicks(90);
            string escortTag = Pursuit.ReadTag(guard.Party)!, wardTag = Pursuit.ReadTag(ward.Party)!;
            Assert.EndsWith(":ward", wardTag);

            var unload = typeof(PopulationSystem).GetMethod("Unload", BindingFlags.Instance | BindingFlags.NonPublic,
                null, [typeof(World), typeof(Platoon), typeof(Squad)], null)!;
            unload.Invoke(pop, [first, guard.Party, guard.Squad]);
            unload.Invoke(pop, [first, ward.Party, ward.Squad]);
            Assert.Equal(record, Assert.Single(ward.Party.Stored!.Members).Cold.RecordId);
            Assert.Equal(CharacterId.None, first.Uniques.Find(record)!.Handle);
            first.RunTicks(90);
            Vector2? Goal(Platoon party) => (Vector2?)typeof(Platoon).GetProperty("ModGoal")!.GetValue(party);
            Assert.Equal(Couriers.Flat(home), Goal(ward.Party));

            string? physics = Environment.GetEnvironmentVariable("TWR_TEST_MEITOU_PHYSICS");
            using var save = PrisonerDealsWorldTests.Save(escorts);
            if (scenario == "legacy")
            {
                // The extraction preview wrote format 3 with the same single row and no final Covert field.
                using var writer = new BinaryWriter(save, System.Text.Encoding.UTF8, leaveOpen: true);
                writer.Write(3);
                save.SetLength(save.Length - sizeof(byte));
                save.Position = 0;
            }
            var resumed = new PrisonerEscorts(data);
            if (scenario == "current" && exactRoster)
                PrisonerDealsWorldTests.ReplayPreviewSave(escorts, resumed, "offscreen-escort");
            else
                resumed.Read(save);
            using var next = W.World(new LwDeedsSystem([], data), resumed);
            _ = next.Platoons.NextId();

            if (!string.IsNullOrWhiteSpace(physics))
            {
                // Capturing a complete save also JITs the character physics serializer. The slim release kit omits those DLLs.
                foreach (string dll in new[] { "BepuUtilities", "BepuPhysics" })
                    if (!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == dll))
                        System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(physics, dll + ".dll"));
                SaveLoader.Load(next, SaveCapture.Capture(first, data, new SaveClock(1, 13, 0)), data);
            }
            else
            {
                next.GameHours = first.GameHours;
                next.Uniques.TryClaim(record);
                next.Uniques.TryClaim("kiri");
                RestoreStored(guard.Party, escortTag);
                RestoreStored(ward.Party, wardTag);
            }

            void RestoreStored(Platoon stored, string tag)
            {
                var made = new Platoon { Id = next.Platoons.NextId(), Faction = W.Bandits, TemplateId = stored.TemplateId,
                    State = PlatoonState.Unloaded, Key = stored.Key, Origin = 0, Persistent = true, Size = 1,
                    Position = Couriers.Flat(at), Stored = stored.Stored };
                next.Platoons.Add(made);
                Pursuit.Mark(made, tag);
            }

            var guides = Assert.IsType<Platoon>(EscortStandIns.Tagged(next, escortTag));
            var captive = Assert.IsType<Platoon>(EscortStandIns.Tagged(next, wardTag));
            Assert.NotEqual(ward.Party.Id, captive.Id);
            if (captive.Saved is { } restored)
                Assert.Equal(record, Assert.Single(restored.Members).Source.RecordId);
            if (scenario == "disabled")
            {
                LwMod.Settings.PrisonerEscorts = false;
                next.RunTicks(180);
                Assert.Empty(LwDeeds.For(next)!.Record.History);
                Assert.True(Pursuit.IsUntagged(guides));
                Assert.True(Pursuit.IsUntagged(captive));
                Assert.Null(Goal(guides));
                Assert.Null(Goal(captive));
                Assert.True(EscortStandIns.Eligible(captive, W.Bandits));
                return;
            }
            guides.Position = Couriers.Flat(home);
            next.RunTicks(90);
            var history = LwDeeds.For(next)!.Record.History;
            Assert.Empty(history);
            Assert.Equal(guides.Position, Goal(guides));  // wait for the captive to catch up
            Assert.Equal(guides.Position, Goal(captive));
            // In this fixture we advance the native stand-ins to their routed arrival without loading the zones.
            captive.Position = Couriers.Flat(home);
            if (!exactRoster)
            {
                if (captive.Saved is { } native) native.Members[0].Source.RecordId = "same-name-decoy";
                else captive.Stored!.Members[0].Cold.RecordId = "same-name-decoy";
            }
            next.RunTicks(180);
            if (exactRoster)
            {
                Assert.Equal("The escort brought Sayo home.", Assert.Single(history).Text);
                Assert.True(Pursuit.IsUntagged(captive));
                Assert.True(Pursuit.IsUntagged(guides));
                Assert.True(EscortStandIns.Eligible(captive, W.Bandits));
            }
            else Assert.Empty(history);
        }
        finally
        {
            LwMod.Settings.PrisonerEscorts = previous;
            LwMod.Settings.PrisonerCases = casesSetting;
        }
    }

}
