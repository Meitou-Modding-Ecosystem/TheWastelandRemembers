using System.Numerics;
using LivingWasteland.MeitouMod;
using Xunit;

// WastelandApi version 5: one squad member's titles for another mod (PZA's Service Record shows them on its Fighter page, RivalryCore#140).
[Collection("settings")]
public class ApiV5WorldTests
{
    static readonly TitleRule[] Rules =
    [
        new("butcher", "the Butcher", "member", "kills", null, null, 2),
        new("headtakers", "the Headtakers", "squad", "leaders", null, null, 1),
    ];

    [Fact]
    public void Titles_lists_a_members_own_titles_then_the_squads_by_name()
    {
        Assert.True(WastelandApi.Version >= 5);
        var world = W.World();
        var deeds = new LwDeeds(Rules, W.Data([]));
        LwDeeds.Register(world, deeds);
        var r = deeds.Record;
        r.Kill("Leaf", "Dust Bandits", true, null);
        r.Kill("Leaf", "Dust Bandits", false, null);
        r.Award("Leaf");
        r.Kill("Ruka", "Dust Bandits", false, null);

        var leaf = WastelandApi.Titles("Leaf");
        Assert.Equal(["butcher", "headtakers"], leaf.Select(t => (string)t["Id"]));
        Assert.Equal(["the Butcher", "the Headtakers"], leaf.Select(t => (string)t["Name"]));
        Assert.Equal([false, true], leaf.Select(t => (bool)t["Squad"]));
        Assert.Equal(["headtakers"], WastelandApi.Titles("Ruka").Select(t => (string)t["Id"]));    // the squad's title, shared
        Assert.Empty(WastelandApi.Titles("Nobody"));
        Assert.Empty(WastelandApi.Titles(""));
    }
}
