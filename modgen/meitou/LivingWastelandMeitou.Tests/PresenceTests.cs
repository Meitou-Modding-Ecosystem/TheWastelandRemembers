using LivingWasteland.MeitouMod;
using Xunit;
using V = LivingWasteland.MeitouMod.Presence<string>.Verdict;

public class PresenceTests
{
    [Fact]
    public void Missing_before_seen_is_placed_again_once_then_gone()
    {
        var p = new Presence<string>(); var w = new object();
        Assert.Equal(V.PlaceAgain, p.Check(w, "crew", false));
        Assert.Equal(V.Gone, p.Check(w, "crew", false));
    }

    [Fact]
    public void Seen_then_missing_is_beaten_not_respawned()
    {
        // seen in play: all four of a crew came back at once after they were killed
        var p = new Presence<string>(); var w = new object();
        Assert.Equal(V.Here, p.Check(w, "crew", true));
        Assert.Equal(V.Gone, p.Check(w, "crew", false));
    }

    [Fact]
    public void A_failed_placing_asks_again()
    {
        var p = new Presence<string>(); var w = new object();
        Assert.Equal(V.PlaceAgain, p.Check(w, "crew", false));
        p.Forget("crew");
        Assert.Equal(V.PlaceAgain, p.Check(w, "crew", false));
    }

    [Fact]
    public void Another_loaded_game_starts_again()
    {
        var p = new Presence<string>();
        Assert.Equal(V.Here, p.Check(new object(), "crew", true));
        Assert.Equal(V.PlaceAgain, p.Check(new object(), "crew", false));   // reloaded: not in that save, not beaten
    }
}
