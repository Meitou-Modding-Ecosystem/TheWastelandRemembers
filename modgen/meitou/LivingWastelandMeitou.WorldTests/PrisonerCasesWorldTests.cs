using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Construction;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Population;
using Xunit;
using SimWorld = Meitou.Simulation.World;

[Collection("settings")]
public sealed class PrisonerCasesWorldTests : IDisposable
{
    static readonly Vector3 Cell = new(14000, 150, 14000);
    readonly bool previous = LwMod.Settings.PrisonerCases;
    static readonly CampCode[] Known = Enumerable.Range(1, 4)
        .Select(i => new CampCode(i, $"Outlaw {i}", $"t-outlaw-{i}", "", [])).ToArray();

    public void Dispose() => LwMod.Settings.PrisonerCases = previous;

    static PopulationData Data()
    {
        var town = W.Rec("t-town", FcsRecordType.Town, "Squin");
        var leader = W.Rec(PrisonerCases.Esata, FcsRecordType.Character, "Esata the Stone Golem");
        return W.Data([town, leader], [new TownPlacement("i-squin", town.StringId, Cell + new Vector3(10000, 0, 0), null)]);
    }

    static Fixture Cage(SimWorld world, string key, CharacterId id, int owner = W.Player)
    {
        var site = new BuildSite { Id = "building-" + key, Record = "cage-building", Position = Cell, Materials = [], Complete = true, Owner = owner };
        world.Buildings.Add(site);
        var cage = new Fixture { Id = key, Building = site.Id, Kind = FixtureKind.Cage, Position = Cell, Owner = owner, Occupant = id };
        var cold = world.Characters.Cold(id.Slot)!;
        cold.InWhat = key;
        cold.InSomething = HeldState.Imprisoned;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        ((List<Fixture>)typeof(FixtureRegistry).GetField("_all", flags)!.GetValue(world.Fixtures)!).Add(cage);
        ((Dictionary<string, Fixture>)typeof(FixtureRegistry).GetField("_byId", flags)!.GetValue(world.Fixtures)!).Add(key, cage);
        return cage;
    }

    static CharacterId Arrest(SimWorld world, string record, string name)
    {
        var captive = W.Spawn(world, name, W.Bandits, Cell);
        world.Uniques.Bind(record, captive);
        return captive;
    }

    [Fact]
    public void Genuine_leaders_have_first_claim_on_three_cases_and_a_cage_transfer_survives_reload()
    {
        LwMod.Settings.PrisonerCases = true;
        var data = Data();
        var first = new PrisonerCases(data, Known);
        var deeds = new LwDeedsSystem([], data);
        using var world = W.World(deeds, new NewsSystem(data), first);
        var ordinary = Known.Select(c => Arrest(world, c.Character, c.Name)).ToArray();
        for (int i = 0; i < ordinary.Length; i++) Cage(world, $"owned-{i}", ordinary[i]);
        var esata = Arrest(world, PrisonerCases.Esata, "Esata");
        var imposter = Arrest(world, "t-esata-copy", "Esata the Stone Golem");
        Cage(world, "owned-imposter", imposter);
        Cage(world, "foreign-esata", esata, W.Bandits);
        world.RunTicks(150);
        Assert.Empty(first.AtlasRows());
        world.GameHours += 7;
        world.RunTicks(150);
        Assert.Equal(Known.Take(3).Select(c => c.Name), first.AtlasRows().Select(c => c.Name));
        Cage(world, "owned-esata", esata);
        world.RunTicks(150);
        Assert.Equal(new[] { "Esata the Stone Golem", "Outlaw 1", "Outlaw 2" }, first.AtlasRows().Select(c => c.Name));
        Assert.Single(LwDeeds.For(world)!.Record.History, h => h.Text == "The squad took Esata the Stone Golem alive.");
        Assert.DoesNotContain(world.Platoons.All, p => p is not null);

        using var dataSave = new MemoryStream();
        first.Write(dataSave);
        dataSave.Position = 0;
        var restored = new PrisonerCases(data, Known);
        restored.Read(dataSave);
        using var next = W.World(new LwDeedsSystem([], data), new NewsSystem(data), restored);
        next.GameHours = world.GameHours + 1;
        W.Spawn(next, "Leaf", W.Player, Cell);
        next.RunTicks(150);
        Assert.Empty(restored.AtlasRows()); // missing from the loaded area is not a release
        var reloaded = Arrest(next, PrisonerCases.Esata, "Esata");
        next.Characters.Cold(reloaded.Slot)!.InSomething = HeldState.Imprisoned;
        next.Characters.Cold(reloaded.Slot)!.InWhat = "new-cell";
        next.RunTicks(150);
        Assert.Empty(restored.AtlasRows());
        Cage(next, "new-cell", reloaded);
        next.RunTicks(150);
        Assert.Equal(PrisonerCases.Esata, Assert.Single(restored.AtlasRows()).Key);
        Assert.Empty(LwDeeds.For(next)!.Record.History);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Execution_gossip_requires_a_confirmed_player_lethal_execution_and_survives_a_pending_save(bool execution)
    {
        LwMod.Settings.PrisonerCases = true;
        var data = Data();
        var cases = new PrisonerCases(data, []);
        using var world = W.World(new LwDeedsSystem([], data), cases);
        var keeper = W.Spawn(world, "Leaf", W.Player, Cell);
        var captive = Arrest(world, PrisonerCases.Esata, "Esata");
        Cage(world, "owned", captive);
        world.RunTicks(150);

        var medical = world.Characters.Cold(captive.Slot)!.Medical!;
        typeof(MedicalState).GetProperty(nameof(MedicalState.Dead))!.SetValue(medical, true);
        var hit = new CombatHit(world.Tick, execution ? CombatHitSource.Execution : CombatHitSource.Melee, keeper, captive,
            0, 0, 0, default, default, Cell, default, 0, -1, false, true, true, true);
        if (PrisonerCases.BuildDeathWatch() is { } adapter)
        {
            var report = adapter.GetMethod("Died")!;
            var payload = report.GetParameters()[1].ParameterType.GetElementType()!;
            var site = Enum.Parse(payload.GetProperty("Site")!.PropertyType, execution ? "Execution" : "Melee");
            var cause = Enum.Parse(payload.GetProperty("Cause")!.PropertyType, "None");
            report.Invoke(Activator.CreateInstance(adapter)!, [world,
                Activator.CreateInstance(payload, world.Tick, captive, PrisonerCases.Esata, W.Bandits, -1, cause, site, keeper, W.Player)]);
        }
        else cases.HitResolved(world, hit);

        using var save = new MemoryStream();
        cases.Write(save);
        save.Position = 0;
        var reloaded = new PrisonerCases(data, []);
        reloaded.Read(save);
        using var next = W.World(new LwDeedsSystem([], data), new NewsSystem(data), reloaded);
        next.GameHours = world.GameHours + 1;
        Arrest(next, PrisonerCases.Esata, "Esata");
        next.Uniques.Find(PrisonerCases.Esata)!.State = Meitou.Simulation.Factions.UniqueState.Dead;
        next.RunTicks(300);
        Assert.Empty(reloaded.AtlasRows());
        var lines = LwDeeds.For(next)!.Record.History.Select(h => h.Text);
        Assert.Single(lines, line => line == (execution
            ? "The squad executed Esata the Stone Golem in custody."
            : "The custody case closed when Esata the Stone Golem died."));
        Assert.DoesNotContain(next.Platoons.All, p => p is not null);
    }
}
