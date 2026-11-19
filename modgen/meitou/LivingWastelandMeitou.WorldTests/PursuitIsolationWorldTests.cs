using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Simulation.Factions;
using Xunit;

[Collection("settings")]
public class PursuitIsolationWorldTests
{
    [Fact]
    public void A_pursuer_in_a_new_world_does_not_move_from_the_old_platoons_coordinates()
    {
        var first = W.World();
        var second = W.World();
        static Platoon StandIn(Meitou.Simulation.World world, float x)
        {
            var platoon = new Platoon { Id = world.Platoons.NextId(), Origin = 0, TemplateId = "crew", Key = 1,
                Position = new Vector2(x, 0), State = PlatoonState.Unloaded };
            world.Platoons.Add(platoon);
            return platoon;
        }
        var old = StandIn(first, 1000);
        var reloaded = StandIn(second, 9000);
        Assert.Equal(old.Id, reloaded.Id);

        Pursuit.Steer(first, old, [(0, 0)]);
        Pursuit.Steer(second, reloaded, [(0, 0)]);
        Assert.Equal(new Vector2(9000, 0), reloaded.Position);
        Pursuit.Steer(second, reloaded, [(0, 0)]);
        Assert.Equal(new Vector2(8750, 0), reloaded.Position); // The pacing fallback still works across passes.
    }
}
