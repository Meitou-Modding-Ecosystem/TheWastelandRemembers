using System.Numerics;
using LivingWasteland.MeitouMod;
using Xunit;

namespace Recap
{
    public static class RecapApi
    {
        [ThreadStatic] public static List<(Vector3? At, bool Picture)>? Captured;
        public static bool AddMoment(string caption, Vector3? position, bool picture)
        {
            Captured?.Add((position, picture));
            return false;
        }
    }
}

[Collection("settings")]
public sealed class RecapBoundaryWorldTests
{
    [Fact]
    public void A_recap_without_a_location_cannot_take_a_photo_in_the_previous_world()
    {
        var enabled = RecapLink.Enabled;
        Recap.RecapApi.Captured = [];
        RecapLink.Enabled = () => true;
        try
        {
            var location = new Vector3(50000, 150, 90000);
            var first = W.World(new LwDeedsSystem([], null));
            W.Spawn(first, "Leaf", W.Player, location);
            first.RunTicks(31);
            RecapLink.Moment("A moment in the first world", "the wilds", 70);
            Assert.Equal((location, true), Assert.Single(Recap.RecapApi.Captured));

            var loaded = W.World(new LwDeedsSystem([], null));
            loaded.RunTicks(31);    // Nothing has published a squad position in this world.
            Recap.RecapApi.Captured!.Clear();
            RecapLink.Moment("A moment just after the load", "the wilds", 70);
            Assert.Equal((null, false), Assert.Single(Recap.RecapApi.Captured));

            var actual = new Vector3(1, 150, 2);
            Recap.RecapApi.Captured.Clear();
            RecapLink.Moment("An explicitly located moment", "the wilds", 70, actual);
            Assert.Equal((actual, true), Assert.Single(Recap.RecapApi.Captured));
        }
        finally
        {
            Recap.RecapApi.Captured = null;
            RecapLink.Enabled = enabled;
        }
    }
}
