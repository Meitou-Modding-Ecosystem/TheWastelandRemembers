using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Simulation.Ai;
using Xunit;

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
}
