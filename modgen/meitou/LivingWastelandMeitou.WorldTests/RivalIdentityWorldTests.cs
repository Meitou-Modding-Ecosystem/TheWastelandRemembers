using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.Save;
using Meitou.Data.World;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Population;
using Meitou.Simulation.Saving;
using Xunit;

[Collection("settings")]
public sealed class RivalIdentityWorldTests
{
    [Fact]
    public void A_saved_party_with_the_exact_rival_beats_the_stored_namesake_and_dead_members()
    {
        if (!LastingActors.Available) return;
        using var world = W.World();
        var actor = W.Spawn(world, "Hollin", W.Bandits, Vector3.Zero);
        ulong lasting = LastingActors.Grant(world, actor);
        Assert.NotEqual(0UL, lasting);
        var rival = new RivalsSystem.Rival { Name = "Hollin", ActorId = lasting };
        var real = world.Characters.Cold(actor.Slot)!;
        real.Name = "Hollin One-Arm";  // the same actor can acquire another displayed name

        var namesake = new Platoon { Id = world.Platoons.NextId(), Origin = -1, Key = 1, TemplateId = "test", Stored = new StoredSquad { Name = "test" } };
        namesake.Stored.Members.Add(new StoredMember(new CharacterCold { Name = rival.Name, Medical = new MedicalState([]) }, Vector3.Zero, 0, true));
        world.Platoons.Add(namesake);

        var saved = new SavedPlatoon { Pinned = false, Source = new SavePlatoon(W.Rec("fixture", FcsRecordType.SquadTemplate, "Fixture"), null) };
        var member = new SavedMember { Source = new SaveCharacter(saved.Source, new FcsInstance()) };
        typeof(SavedMember).GetProperty("Cold")!.SetValue(member, real);
        saved.Members.Add(member);
        var exact = new Platoon { Id = world.Platoons.NextId(), Origin = -1, Key = 2, TemplateId = "test", Saved = saved };
        world.Platoons.Add(exact);

        Assert.NotNull(RivalsSystem.Holding(world, namesake, rival));
        Assert.Same(exact, RivalsSystem.FindHolding(world, rival));
        member.Dead = true;
        Assert.Null(RivalsSystem.FindHolding(world, rival));
        Assert.NotNull(RivalsSystem.Holding(world, exact, rival));
    }

    [Fact]
    public void A_party_restored_from_disk_can_be_found_before_its_rival_is_materialised()
    {
        if (!LastingActors.Available) return;
        using var world = W.World();
        const ulong actorId = 258;
        var quick = new SaveFile();
        quick.Data.Records.Add(W.Rec("camera", FcsRecordType.Camera, "camera"));
        var game = new SaveGame(quick);
        var source = new SavePlatoon(W.Rec("fixture", FcsRecordType.SquadTemplate, "Fixture"), null);
        game.Platoons.Add(source);
        var character = new SaveCharacter(source, new FcsInstance { Target = "r-hollin" })
        {
            GameState = W.Rec("state", FcsRecordType.Character, "Fixture"),
        };
        character.Handle = new Hand(1, 2, 3, 4, 5);
        character.GameState.Strings["name"] = "Renamed Hollin";
        source.Characters.Add(character);
        // Restore the engine's on-disk map without instantiating the saved character.
        var ids = typeof(World).GetProperty("LastingIds")!.GetValue(world)!;
        var load = ids.GetType().GetMethod("Load", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var contents = load.GetParameters()[0].ParameterType;
        var entryType = contents.GetProperty("Entries")!.PropertyType.GetGenericArguments()[0];
        var entries = (System.Collections.IList)Activator.CreateInstance(contents.GetProperty("Entries")!.PropertyType)!;
        entries.Add(Activator.CreateInstance(entryType, character.Handle, actorId, character.RecordId));
        load.Invoke(ids, [Activator.CreateInstance(contents, entries, 0u, 3UL), game]);
        var saved = new SavedPlatoon { Pinned = false, Source = source };
        saved.Members.Add(new SavedMember { Source = character });
        var party = new Platoon { Id = world.Platoons.NextId(), Origin = -1, Key = 1, TemplateId = "test", Saved = saved };
        world.Platoons.Add(party);

        Assert.True(LastingActors.Find(world, actorId).IsNone);
        Assert.Same(party, RivalsSystem.FindHolding(world, new RivalsSystem.Rival { Name = "Hollin", ActorId = actorId }));
    }

    [Fact]
    public void Meitou_can_split_a_living_downed_rival_when_its_mates_unload()
    {
        var keepDowned = typeof(PopulationSystem).GetProperty("KeepDowned");
        if (keepDowned is null || !LastingActors.Available) return;
        var population = new PopulationSystem(W.Data([]));
        keepDowned.SetValue(population, true);
        using var world = W.World(population);
        var squad = new Squad { Id = world.Squads.NextId(), TemplateId = "test", Faction = W.Bandits };
        world.Squads.Add(squad);
        var at = new Vector3(2000, 150, 2000);
        var down = W.Spawn(world, "Hollin", W.Bandits, at, squad: squad.Id);
        var mate = W.Spawn(world, "Hunter", W.Bandits, at + Vector3.UnitX * 20000, squad: squad.Id);
        squad.Members.AddRange([mate, down]);
        squad.Leader = mate;
        typeof(MedicalState).GetProperty(nameof(MedicalState.Unconscious))!.SetValue(world.Characters.Cold(down.Slot)!.Medical, true);
        var parent = new Platoon { Id = world.Platoons.NextId(), Origin = -1, Key = 1, TemplateId = squad.TemplateId, Faction = W.Bandits,
            SquadId = squad.Id, State = PlatoonState.Loaded, Persistent = true };
        world.Platoons.Add(parent);
        squad.PlatoonId = parent.Id;
        var rival = new RivalsSystem.Rival { Name = "Hollin", ActorId = LastingActors.Grant(world, down), Id = down };

        // Open just the zone where the rival was left. The engine's real unload owns the split and roster.
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        var active = (ICollection<ZoneCoordinate>)typeof(PopulationSystem).GetField("_active", instance)!.GetValue(population)!;
        active.Add(WorldLayout.ZoneOf(at.X, at.Z));
        var unload = typeof(PopulationSystem).GetMethod("Unload", instance, [typeof(World), typeof(Platoon), typeof(Squad)]);
        unload!.Invoke(population, [world, parent, squad]);

        Assert.NotNull(RivalsSystem.Holding(world, parent, rival));
        var separated = Assert.Single(world.Platoons.All, p => p.Id != parent.Id);
        Assert.True(separated.Persistent);
        Assert.Equal(down, LastingActors.Find(world, rival.ActorId));
        Assert.Same(separated, RivalsSystem.FindHolding(world, rival));
    }

    [Fact]
    public void A_saved_rival_uses_its_engine_id_instead_of_the_nearest_matching_name()
    {
        if (!LastingActors.Available) return;
        bool setting = LwMod.Settings.Rivals;
        LwMod.Settings.Rivals = true;
        try
        {
            var data = W.Data([]);
            var rivals = new RivalsSystem(data);
            using var world = W.World(rivals);
            world.GameHours = 0;
            var leaf = W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
            var trueRival = W.Spawn(world, "Hollin", W.Bandits, new Vector3(500, 150, 0));
            var cold = world.Characters.Cold(trueRival.Slot)!;
            rivals.Hit(world, W.Hit(world, leaf, trueRival), cold, world.Characters.Cold(leaf.Slot), "Leaf", leader: true);
            rivals.FightEnded(world, won: true);
            ulong identity = LastingActors.Of(cold);
            Assert.NotEqual(0UL, identity);
            string name = cold.Name;
            world.GameHours = 55;
            world.RunTicks(301);

            using var save = new MemoryStream();
            rivals.Write(save);
            save.Position = 0;
            var loaded = new RivalsSystem(data);
            loaded.Read(save);
            using var next = W.World(loaded);
            next.GameHours = world.GameHours + 13;
            var player = W.Spawn(next, "Leaf", W.Player, Vector3.Zero);
            var decoy = W.Spawn(next, name, W.Bandits, new Vector3(500, 150, 0));
            next.RunTicks(301);  // the real actor is still off screen
            Assert.Equal("hunting", Assert.Single(loaded.Told()).Phase);
            rivals = loaded;
            void Win(CharacterId opponent)
            {
                rivals.Hit(next, W.Hit(next, player, opponent), next.Characters.Cold(opponent.Slot)!,
                    next.Characters.Cold(player.Slot), "Leaf", leader: false);
                rivals.FightEnded(next, won: true);
            }
            Win(decoy);
            Assert.DoesNotContain(W.Page(loaded), l => l.StartsWith("Has come back", StringComparison.Ordinal));
            Assert.DoesNotContain(next.TakeMessages(), m => m.Contains("got away again"));

            var back = new CharacterCold { Name = name, Faction = W.Bandits, RecordId = cold.RecordId, Medical = new MedicalState([]) };
            var slot = typeof(CharacterCold).GetProperty("LastingId")!;
            slot.SetValue(back, Activator.CreateInstance(slot.PropertyType, identity));
            var actual = next.Characters.Spawn(new CharacterHot { Alive = true, Position = new Vector3(3000, 150, 0) }, back, next.Tick);
            Assert.Equal(actual, LastingActors.Find(next, identity));
            next.RunTicks(300);
            Win(actual);
            Assert.Contains(next.TakeMessages(), m => m.Contains("got away again"));
            Assert.Contains("Has come back for your squad once and got away.", W.Page(loaded));
        }
        finally { LwMod.Settings.Rivals = setting; }
    }

    [Fact]
    public void An_older_save_leaves_two_same_name_candidates_unbound()
    {
        var data = W.Data([]);
        var rivals = new RivalsSystem(data);
        using var world = W.World(rivals);
        var leaf = W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
        var first = W.Spawn(world, "Hollin One-Arm", W.Bandits, new Vector3(500, 150, 0));
        W.Spawn(world, "Hollin One-Arm", W.Bandits, new Vector3(700, 150, 0));
        rivals.Read(W.SavedRival("Hollin One-Arm", "lost an arm to your squad", 1, 500, 0));
        world.RunTicks(301);
        rivals.Hit(world, W.Hit(world, leaf, first), world.Characters.Cold(first.Slot)!,
            world.Characters.Cold(leaf.Slot), "Leaf", leader: false);
        rivals.FightEnded(world, won: true);
        Assert.DoesNotContain(W.Page(rivals), l => l.StartsWith("Has come back", StringComparison.Ordinal));
    }
}
