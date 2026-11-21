using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Simulation;
using Meitou.Simulation.Construction;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Locks;
using Xunit;
using SimWorld = Meitou.Simulation.World;

public sealed class RecoveryGateWorldTests
{
    static readonly Vector3 At = new(1500, 150, 1500);

    sealed class Leaves : IDoorGate
    {
        public void SetDoorsBlocked(string buildingInstance, bool blocked) { }
        public void ResetDoors() { }
        public DoorSpot? DoorSpotOf(string buildingRecordId, int doorIndex) => new(Vector3.Zero, Vector3.UnitX, 32);
    }

    internal static Fixture Gate(SimWorld world, string id, int town, Vector3 at, bool isGate = true)
    {
        world.Fixtures.Doors = new Leaves();
        var building = new BuildSite { Id = id + "-building", Record = "gate", Owner = W.Player, Town = town,
            Complete = true, Position = at, Materials = [] };
        world.Buildings.Add(building);
        var door = new Fixture { Id = id, Building = building.Id, Position = at, IsGate = isGate,
            Kind = FixtureKind.Door, Owner = W.Player, Lock = new LockState { Locked = true, Level = 15 } };
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        ((List<Fixture>)typeof(FixtureRegistry).GetField("_all", flags)!.GetValue(world.Fixtures)!).Add(door);
        ((Dictionary<string, Fixture>)typeof(FixtureRegistry).GetField("_byId", flags)!.GetValue(world.Fixtures)!).Add(id, door);
        ((Dictionary<string, List<Fixture>>)typeof(FixtureRegistry).GetField("_byBuilding", flags)!.GetValue(world.Fixtures)!)[building.Id] = [door];
        return door;
    }

    sealed class OffsetRegions(params GateLink[] gates) : IGateRegions
    {
        public IReadOnlyList<GateLink> Gates => gates;
        public int CodeAt(Vector3 at) => at.X < At.X ? 0 : 1;
    }

    [Fact]
    public void The_native_region_route_finds_an_offset_gate_and_ignores_a_straight_line_decoy()
    {
        var data = W.Data([]);
        var locks = new LockSystem(data.Bodies.Constants, data.BodyOptions);
        using var world = W.World(locks);
        var offset = Gate(world, "bend", 1, At + Vector3.UnitZ * 180);
        var decoy = Gate(world, "straight", 1, At);
        var outside = At - Vector3.UnitX * 150;
        var inside = At + Vector3.UnitX * 150;
        Assert.Same(decoy, RecoveryGate.Blocking(world, 1, outside, inside, lockedOnly: false));
        locks.Inputs(world, [new GateRegionsCommand(new OffsetRegions(new GateLink(offset.Building, [0, 1]))) { Tick = world.Tick }]);
        Assert.Same(offset, RecoveryGate.Entrance(world, locks, 1, outside, inside));
        var cross = Assert.IsType<RecoveryGate.Crossing>(RecoveryGate.Passage(world, offset, outside));
        Assert.Equal(offset.Id, cross.Door);
        Assert.Equal(0, locks.Regions!.CodeAt(cross.Near));
        Assert.Equal(1, locks.Regions.CodeAt(cross.Far));
        Assert.Null(RecoveryGate.Entrance(world, locks, 2, outside, inside));
        Assert.Null(RecoveryGate.Entrance(world, locks, 1, cross.Far, inside));
    }

    [Fact]
    public void The_siege_assigns_two_bashers_and_falls_back_when_half_the_party_is_unfit()
    {
        using var world = W.World();
        var squad = new Squad { Id = world.Squads.NextId(), TemplateId = "reprisal", Faction = W.Bandits };
        world.Squads.Add(squad);
        for (int i = 0; i < 4; i++) squad.Members.Add(W.Spawn(world, $"Fighter {i}", W.Bandits, At, squad: squad.Id));
        world.RunTicks(1);
        Assert.Equal(squad.Members.Take(2).Select(m => m.Slot), PrisonerSiege.Bashers(world, squad).Order());
        Assert.False(PrisonerSiege.HeavyLosses(world, squad, 4));
        world.Characters.Cold(squad.Members[0].Slot)!.InWhat = "held";
        world.Characters.Cold(squad.Members[0].Slot)!.InSomething = HeldState.Imprisoned;
        world.Characters.Cold(squad.Members[1].Slot)!.InWhat = "held";
        world.Characters.Cold(squad.Members[1].Slot)!.InSomething = HeldState.Imprisoned;
        Assert.True(PrisonerSiege.HeavyLosses(world, squad, 4));
        Assert.Equal(squad.Members[2].Slot, Assert.Single(PrisonerSiege.Bashers(world, squad)));
        Assert.True(PrisonerSiege.FallBack(world, squad, At + Vector3.UnitX * 750));
        Assert.False(PrisonerSiege.FallBack(world, squad, At));
    }

    [Fact]
    public void Oshars_fighters_bash_the_blocking_gate_of_the_captives_actual_outpost()
    {
        var data = W.Data([]);
        var locks = new LockSystem(data.Bodies.Constants, data.BodyOptions);
        var combat = new CombatSystem(data.Combat.Techniques, data.Combat.Constants, data.Bodies.Constants, data.BodyOptions, new());
        typeof(LockSystem).GetProperty("Combat", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(locks, combat);
        using var world = W.World(locks, combat);
        Assert.Equal(PrisonerRecovery.Approach.Siege, PrisonerRecovery.MethodFor("Kral's Chosen", "Maruka Tal-Khesh"));
        Assert.Null(PrisonerRecovery.MethodFor("Shek Kingdom", "Esata"));
        var decoy = Gate(world, "foreign", 2, At - Vector3.UnitX * 10);
        _ = Gate(world, "off-route", 1, At + Vector3.UnitZ * 100);
        var gate = Gate(world, "target", 1, At);
        var outside = At - Vector3.UnitX * 30;
        var inside = At + Vector3.UnitX * 100;
        var raider = W.Spawn(world, "Kral's Chosen", W.Bandits, outside);
        Assert.Same(gate, RecoveryGate.Blocking(world, 1, outside, inside, lockedOnly: false));
        Assert.True(RecoveryGate.Bash(world, raider.Slot, gate));
        Assert.Equal(gate.Id, world.Characters.Cold(raider.Slot)!.Bashing);
        world.RunTicks(240);
        Assert.True(gate.DoorBroken || gate.DoorProgress < 1);
        Assert.False(decoy.DoorBroken);
        Assert.Equal(1f, decoy.DoorProgress);
        Assert.Empty(world.Player.Selection);
    }
}
