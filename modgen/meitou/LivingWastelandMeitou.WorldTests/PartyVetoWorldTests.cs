using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Population;
using Xunit;

[Collection("settings")]
public sealed class PartyVetoWorldTests
{
    const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Type? removal = typeof(World).Assembly.GetType("Meitou.Simulation.Bodies.RemovalKind");

    [Fact]
    public void The_veto_reuses_the_existing_platoon_watch_instead_of_adding_a_tick_system()
    {
        if (removal is null) return;
        Assert.Same(PlatoonFates.BuildWatch(), PlatoonRetention.BuildWatch());
    }

    [Theory]
    [InlineData("bounty", "PlatoonDespawned")]
    [InlineData("bounty", "PlatoonOrphaned")]
    [InlineData("reprisal", "PlatoonDespawned")]
    [InlineData("reprisal", "PlatoonOrphaned")]
    [InlineData("rival", "PlatoonDespawned")]
    [InlineData("rival", "PlatoonOrphaned")]
    public void Engine_keeps_only_the_tagged_party_with_a_live_job(string job, string kind)
    {
        if (removal is null) return;
        var settings = LwMod.Settings;
        var old = (settings.BountyHunters, settings.OutlawReprisals, settings.Rivals, settings.OutlawCamps);
        try
        {
            (settings.BountyHunters, settings.OutlawReprisals, settings.Rivals, settings.OutlawCamps) = (true, true, true, false);
            var record = W.Rec("party", FcsRecordType.SquadTemplate, "Test party");
            var data = W.Data([record]);
            var population = new PopulationSystem(data) { KeepRoamers = false };
            var hunters = new HuntersSystem(record.StringId, population, data);
            var camps = new OutlawCampsSystem([new CampCode(8, "Kestrel", "kestrel", "camp", [], record.StringId)], population, data);
            var rivals = new RivalsSystem(data);
            var watch = typeof(Pursuit).Assembly.GetType("LivingWasteland.MeitouMod.PlatoonRetention")
                ?.GetMethod("BuildWatch", Static)?.Invoke(null, null) as Type;
            ITickSystem[] systems = job switch
            {
                "bounty" => [hunters],
                "reprisal" => [camps],
                _ => [rivals],
            };
            using var world = W.World(watch is null ? systems : [.. systems, (ITickSystem)Activator.CreateInstance(watch)!]);
            W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
            world.GameHours = 140;
            var party = new Platoon
            {
                Id = world.Platoons.NextId(), Origin = 0, Key = 1, Faction = W.Bandits,
                TemplateId = record.StringId, Position = new Vector2(30000, 40000),
            };
            string tag = job switch { "bounty" => "lw:bounty:Squin:138", "reprisal" => "lw:reprisal:8:138", _ => "lw:rival:Hollin One-Arm" };
            Assert.True(Pursuit.Mark(party, tag));
            world.Platoons.Add(party);

            switch (job)
            {
                case "bounty":
                    Read(hunters, b =>
                    {
                        b.Write(1); b.Write(1); b.Write("Squin"); b.Write(138.0);
                        b.Write(party.Position.X); b.Write(party.Position.Y); b.Write(0);
                    });
                    break;
                case "reprisal":
                    Read(camps, b =>
                    {
                        b.Write(3); b.Write(0); b.Write(0); b.Write(1); b.Write(8); b.Write(138.0);
                        b.Write(party.Position.X); b.Write(party.Position.Y); b.Write(0);
                    });
                    break;
                default:
                    using (var save = W.SavedRival("Hollin One-Arm", "lost an arm to your squad", 1, 30000, 40000, next: 145))
                        rivals.Read(save);
                    break;
            }
            Assert.False(EngineKeeps(world, party.Id, kind));
            world.RunTicks(301);
            Assert.True(EngineKeeps(world, party.Id, kind));
            Assert.False(EngineKeeps(world, party.Id, "CorpseDecayed"));

            // A matching string on another object carries no claim.
            var copy = new Platoon { Id = world.Platoons.NextId(), Origin = 0, Key = 2, TemplateId = record.StringId };
            Pursuit.Mark(copy, tag);
            world.Platoons.Add(copy);
            Assert.False(EngineKeeps(world, copy.Id, kind));
            Assert.True(world.Platoons.Remove(copy.Id));
            world.GameHours = 500;
            Assert.False(EngineKeeps(world, party.Id, kind));
            world.RunTicks(301);
            Assert.False(EngineKeeps(world, party.Id, kind));
        }
        finally { (settings.BountyHunters, settings.OutlawReprisals, settings.Rivals, settings.OutlawCamps) = old; }
    }

    static bool EngineKeeps(World world, int id, string kind)
    {
        object dispatcher = typeof(World).GetProperty("RemovalVetoes", Instance)!.GetValue(world)!;
        return (bool)dispatcher.GetType().GetMethod("KeepsPlatoon", Instance)!.Invoke(dispatcher,
            [world, id, Enum.Parse(removal!, kind)])!;
    }

    static void Read(Meitou.Modding.IModSaveData system, Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using (var b = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true)) write(b);
        stream.Position = 0;
        system.Read(stream);
    }
}
