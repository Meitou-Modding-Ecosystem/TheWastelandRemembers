using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Simulation.Ai;
using System.Numerics;
using Meitou.Simulation;
using Meitou.Simulation.Factions;
using Xunit;
using SimWorld = Meitou.Simulation.World;

// AiCompat: the engine AI calls whose shape changed in Meitou's AI rewrite (API 3). The shipped DLL is built against 0.17.3, so a call
// compiled against the old PlatoonJobs.For failed to bind on 0.22.2-nightly.20261121.2 (RefCheck); these run on every client release_check
// covers and fail if a client has a shape AiCompat doesn't know (hunters, couriers and outlaw camps would get no package there).
public class AiCompatWorldTests
{
    [Fact]
    public void PlatoonJobs_For_is_found_on_this_client_and_runs()
    {
        Assert.NotEqual("none", AiCompat.JobsShape);
        var crew = W.Rec("crew-c", FcsRecordType.SquadTemplate, "Bounty crew");
        crew.References["faction"] = [new FcsReference("60-t", 0, 0, 0)];
        var data = W.Data([crew]);
        var direct = typeof(PlatoonJobs).GetMethods().Where(m => m.Name == "For").ToList();
        Assert.NotEmpty(direct);
        var (package, _) = AiCompat.JobsFor(data.Ai, SquadTemplate.From(data.Db.Find("crew-c")!));
        Assert.Null(package);                                        // no packages in this world: none, without throwing
    }

    [Fact]
    public void The_assault_order_is_reported_as_the_client_has_it()
    {
        bool has = typeof(AiMind).GetProperty("ForcedAssault") is not null && typeof(AiSystem).GetMethod("OrderAssault") is not null;
        Assert.Equal(has, AiCompat.CanAssault);
    }

    static Squad AddSquad(SimWorld world, string name, int faction, Vector3 at)
    {
        var squad = new Squad { Id = world.Squads.NextId(), Name = name, TemplateId = "test", Faction = faction };
        world.Squads.Add(squad);
        squad.Leader = W.Spawn(world, name + " leader", faction, at, squad: squad.Id);
        squad.Members.Add(squad.Leader);
        return squad;
    }

    // On API 3 the assault order is gone; a stalled bounty crew was then left following for good. The order queue's Assault row (DefeatSquad)
    // takes its place: the crew member gets the order, held in its queue until it can fight. Fails on bh's code (no order on API 3).
    [Fact]
    public void A_crew_member_can_be_ordered_to_attack_on_every_client()
    {
        Assert.NotEqual("none", AiCompat.AttackOrder);
        Assert.True(AiCompat.CanOrderAttack);
        if (AiCompat.CanAssault) return;                             // older clients: the assault order, as before (the hunters' tests)
        var data = W.Data([]);
        data.Relations.Add(W.Bandits, W.Player, -100);
        var world = W.World(new AiSystem(data));
        var player = AddSquad(world, "Leaf", W.Player, new Vector3(0, 150, 0));
        var crew = AddSquad(world, "Crew", W.Bandits, new Vector3(30, 150, 0));
        Assert.True(AiCompat.OrderAttack(null, world, crew.Leader, player.Leader));
        Assert.True(world.Characters.TryResolveNext(crew.Leader, out int slot));
        var cold = world.Characters.Cold(slot)!;                      // the queue is API 3 only: read by reflection (the tests build on 0.17.3 too)
        var queue = (System.Collections.IEnumerable)cold.GetType().GetProperty("OrderQueue")!.GetValue(cold)!;
        Assert.Contains(queue.Cast<object>(), o => o.GetType().GetProperty("Row")!.GetValue(o)!.ToString() == "DefeatSquad"
            && (CharacterId)o.GetType().GetProperty("Target")!.GetValue(o)! == player.Leader);
    }
}
