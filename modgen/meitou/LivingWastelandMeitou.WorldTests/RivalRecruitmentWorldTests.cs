using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Population;
using Rivalry;
using Xunit;
using SimWorld = Meitou.Simulation.World;

public sealed record RecruitmentTarget(CharacterId Id, string Name);
public sealed record RecruitmentMenu(RecruitmentTarget Target, IReadOnlyList<CharacterId> Selected, long Tick, Action<SimCommand> Send)
{
    public void Enqueue(SimCommand command) => Send(command);
}

public sealed class RecruitmentRegistration
{
    public Func<RecruitmentMenu, IEnumerable<FakeVerb>>? Verbs;
    public void AddCharacterMenuVerbs(Func<RecruitmentMenu, IEnumerable<FakeVerb>> verbs) => Verbs = verbs;
}

[Collection("settings")]
public sealed class RivalRecruitmentWorldTests : IDisposable
{
    const string Name = "Hollin One-Arm";
    readonly bool previous = LwMod.Settings.RecruitRivals;

    public void Dispose() => LwMod.Settings.RecruitRivals = previous;

    sealed class Scene
    {
        public readonly SimWorld World;
        public readonly LwDeedsSystem Deeds;
        public readonly RivalsSystem Rivals;
        public readonly FakeRivalry Core = new();
        public readonly CharacterId Player, Rival;
        public readonly Squad PlayerSquad, EnemySquad;
        public readonly RecruitmentRegistration Ui = new();

        public object? LastingId => typeof(CharacterCold).GetProperty("LastingId")?.GetValue(World.Characters.Cold(Rival.Slot));

        public Scene(Stream save, double hours = 0, bool padding = false, object? lasting = null)
        {
            var data = W.Data([]);
            Deeds = new LwDeedsSystem([], data);
            Rivals = new RivalsSystem(data) { Population = new PopulationSystem(data) };
            World = W.World(Core, Deeds, Rivals);
            World.GameHours = hours;
            Rivals.Read(save);
            PlayerSquad = new Squad { Id = World.Squads.NextId(), TemplateId = "p", Faction = W.Player };
            World.Squads.Add(PlayerSquad);
            EnemySquad = new Squad { Id = World.Squads.NextId(), TemplateId = "e", Faction = W.Bandits };
            World.Squads.Add(EnemySquad);
            if (padding) W.Spawn(World, "Someone else", W.Bandits, new Vector3(500, 150, 0));
            Player = Spawn("Leaf", W.Player, 1001, PlayerSquad, new Vector3(0, 150, 0));
            Rival = Spawn(Name, W.Bandits, 1002, EnemySquad, new Vector3(150, 150, 0), lasting);
            var platoon = new Platoon
            {
                Id = World.Platoons.NextId(), Origin = 0, Key = 1, TemplateId = "e", Faction = W.Bandits,
                Position = new Vector2(150, 0), SquadId = EnemySquad.Id, State = PlatoonState.Loaded, Persistent = true,
            };
            Pursuit.Mark(platoon, "lw:rival:" + Name);
            World.Platoons.Add(platoon);
            RivalOffers.RegisterUi(Ui);
            World.RunTicks(301);
        }

        CharacterId Spawn(string name, int faction, uint serial, Squad squad, Vector3 at, object? lasting = null)
        {
            var cold = new CharacterCold
            {
                Name = name, Faction = faction, IsPlayer = faction == W.Player, RecordId = "r-" + name,
                PortraitSerial = serial, Medical = new MedicalState([]), SquadId = squad.Id,
            };
            if (lasting is not null) typeof(CharacterCold).GetProperty("LastingId")!.SetValue(cold, lasting);
            var id = World.Characters.Spawn(new CharacterHot { Alive = true, Position = at }, cold, World.Tick);
            squad.Members.Add(id);
            squad.Leader = id;
            return id;
        }

        public void Defeat(bool real = true, bool duplicate = false)
        {
            var cold = World.Characters.Cold(Rival.Slot)!;
            var property = typeof(MedicalState).GetProperty(nameof(MedicalState.Unconscious))!;
            property.SetValue(cold.Medical, true);
            var hit = W.Hit(World, Player, Rival) with { VictimWasIncapacitated = !real };
            Rivals.Hit(World, hit, cold, World.Characters.Cold(Player.Slot), "Leaf", false);
            Rivals.FightEnded(World, won: true);
            if (duplicate)
            {
                Rivals.Hit(World, hit, cold, World.Characters.Cold(Player.Slot), "Leaf", false);
                Rivals.FightEnded(World, won: true);
            }
            property.SetValue(cold.Medical, false);
            World.RunTicks(300);
        }

        public void NextHunt()
        {
            World.GameHours += 97;
            World.RunTicks(300);
            Assert.Contains(Core.Claims, c => c.Key == PartyClaims.Rival(Name));
        }

        public MemoryStream Save()
        {
            var memory = new MemoryStream();
            Rivals.Write(memory);
            memory.Position = 0;
            return memory;
        }

        public FakeVerb[] Verbs(Action<SimCommand> enqueue) =>
            Ui.Verbs?.Invoke(new RecruitmentMenu(new(Rival, Name), [Player], World.Tick, enqueue)).ToArray() ?? [];

        public void Apply(SimCommand command) => Rivals.Inputs(World, [command]);
    }

    static MemoryStream Legacy(int returns = 0) => W.SavedRival(Name, "lost an arm to your squad", 1, 150, 0, returns);

    [Fact]
    public void Three_distinct_defeats_open_an_offer_and_the_same_actor_joins_after_reload()
    {
        if (!RivalOffers.CanJoin || !Pursuit.HasTags) return;
        LwMod.Settings.RecruitRivals = true;
        var scene = new Scene(Legacy(returns: 8));
        Assert.Empty(scene.Verbs(scene.Apply)); // Old returns do not prove a defeat.
        scene.Defeat(real: false);
        scene.NextHunt();
        scene.Defeat(duplicate: true);
        scene.NextHunt();
        scene.Defeat();
        Assert.Empty(scene.Verbs(scene.Apply));

        var afterTwo = new Scene(scene.Save(), scene.World.GameHours, padding: true, lasting: scene.LastingId);
        afterTwo.NextHunt();
        afterTwo.Defeat();
        Assert.Empty(afterTwo.Core.Claims);
        SimCommand? stale = null;
        var oldOffer = afterTwo.Verbs(command => stale = command).Single(v => v.Caption == "Take them in");
        var reloaded = new Scene(afterTwo.Save(), afterTwo.World.GameHours, padding: true, lasting: afterTwo.LastingId);
        Assert.Equal(afterTwo.Rival, reloaded.Rival); // The handle and offer deadline alone cannot distinguish these worlds.
        oldOffer.Choose();
        Assert.NotNull(stale);
        reloaded.Apply(stale);
        Assert.DoesNotContain(reloaded.Rival, reloaded.PlayerSquad.Members);
        Assert.Contains(reloaded.Rival, reloaded.EnemySquad.Members);

        reloaded.Verbs(reloaded.Apply).Single(v => v.Caption == oldOffer.Caption).Choose();
        Assert.Contains(reloaded.Rival, reloaded.PlayerSquad.Members);
        Assert.DoesNotContain(reloaded.Rival, reloaded.EnemySquad.Members);
        Assert.True(reloaded.World.Characters.Cold(reloaded.Rival.Slot)!.IsPlayer);
        Assert.Contains(W.Page(reloaded.Rivals), line => line == "Joined your squad.");
        Assert.Empty(reloaded.Verbs(reloaded.Apply));
        var saved = new RivalsSystem(null);
        saved.Read(reloaded.Save());
        Assert.Contains(W.Page(saved), line => line == "Joined your squad.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Only_a_confirmed_kill_bars_recruitment_across_reload(bool bleedOut)
    {
        if (!RivalOffers.CanJoin || !Pursuit.HasTags) return;
        LwMod.Settings.RecruitRivals = true;
        var scene = new Scene(Legacy());
        var dead = W.Spawn(scene.World, "Kite", W.Player, new Vector3(300, 150, 0));
        var victim = scene.World.Characters.Cold(dead.Slot)!;
        victim.PortraitSerial = 1003;
        var death = typeof(MedicalState).GetProperty(nameof(MedicalState.Dead))!;
        if (!bleedOut) death.SetValue(victim.Medical, true);
        scene.Deeds.HitResolved(scene.World, W.Hit(scene.World, scene.Rival, dead));
        var loaded = new Scene(scene.Save(), scene.World.GameHours, padding: true, lasting: scene.LastingId);
        if (bleedOut)
        {
            var restored = W.Spawn(loaded.World, "Kite", W.Player, new Vector3(300, 150, 0));
            var body = loaded.World.Characters.Cold(restored.Slot)!;
            body.PortraitSerial = 1003;
            death.SetValue(body.Medical, true);
        }
        loaded.Defeat();
        loaded.NextHunt();
        loaded.Defeat();
        loaded.NextHunt();
        loaded.Defeat();
        if (bleedOut) Assert.Equal(2, loaded.Verbs(loaded.Apply).Length);
        else
        {
            Assert.Empty(loaded.Verbs(loaded.Apply));
            loaded.NextHunt();
        }
    }

    [Fact]
    public void Declining_an_offer_leaves_the_slot_free_until_the_next_hunt()
    {
        if (!RivalOffers.CanJoin || !Pursuit.HasTags) return;
        Assert.False(new LwSettings().RecruitRivals);
        LwMod.Settings.RecruitRivals = true;
        var scene = new Scene(Legacy());
        for (int i = 0; i < 2; i++) { scene.Defeat(); scene.NextHunt(); }
        scene.Defeat();
        Assert.Empty(scene.Core.Claims);
        scene.Verbs(scene.Apply).Single(v => v.Caption == "Turn them away").Choose();
        Assert.Empty(scene.Verbs(scene.Apply));
        Assert.DoesNotContain(scene.Rival, scene.PlayerSquad.Members);
        scene.Core.Claims.Add(("another.crew", "hunting"));
        scene.World.GameHours += 97;
        scene.World.RunTicks(300);
        Assert.All(scene.Core.Claims, c => Assert.Equal("another.crew", c.Source));
        scene.Core.Claims.Clear();
        scene.World.RunTicks(300);
        Assert.Contains(scene.Core.Claims, c => c.Key == PartyClaims.Rival(Name));
    }
}
