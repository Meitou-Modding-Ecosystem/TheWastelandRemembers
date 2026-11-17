using LivingWasteland.MeitouMod;
using Xunit;

public class MapMarkRulesTests
{
    [Fact]
    public void Only_what_the_squad_knows_and_only_rivals_still_about()
    {
        var marks = MapMarkRules.Build(
            [("Hollin One-Arm", "hunting", 10, 20, "lost an arm to your squad"), ("Varro the Burnt", "dead", 30, 40, "burnt"),
             ("Kesh Twice-Beaten", "recovering", 50, 60, "beaten twice")],
            [("Bad Pete", "raiding", 70, 80)],
            [("Okran's Shield has fallen.", "Okran's Shield", 90, 100, 24), ("Old news.", "Stack", 1, 2, 24 - 24 * 8)],
            hoursNow: 48);
        Assert.Equal(["Hollin One-Arm", "Kesh Twice-Beaten"], marks.Where(m => m.Kind == MarkKind.Rival).Select(m => m.Label));
        Assert.Contains("hunting your squad", marks.First(m => m.Label == "Hollin One-Arm").Tooltip);
        Assert.Equal("Outlaw's camp\traiding", Assert.Single(marks, m => m.Kind == MarkKind.Outlaw).Tooltip);
        Assert.Equal("Okran's Shield", Assert.Single(marks, m => m.Kind == MarkKind.News).Label);   // last week's news only
    }

    [Fact]
    public void Each_kind_has_a_group_from_the_games_marker_set()
    {
        string[] groups = ["Town", "Village", "Outpost", "Ruin", "Camp", "Nest", "Workcamp", "SmallPlace", "Character", "Movement", "NewDiscovered"];
        foreach (var k in Enum.GetValues<MarkKind>()) Assert.Contains(MapMarkRules.Look(k).Group, groups);
    }
}

public class NewsAskRulesTests
{
    [Fact]
    public void Townsfolk_can_be_asked_but_not_the_squad_or_someone_fighting_it()
    {
        Assert.True(NewsAskRules.Offer(isPlayer: false, fightsPlayer: false, 1000));
        Assert.False(NewsAskRules.Offer(isPlayer: true, fightsPlayer: false, 1000));
        Assert.False(NewsAskRules.Offer(isPlayer: false, fightsPlayer: true, 1000));
        Assert.False(NewsAskRules.Offer(isPlayer: false, fightsPlayer: false, NewsAskRules.AskRange + 1));   // out in the wilds
    }

    [Fact]
    public void The_weightiest_news_in_reach_is_told_and_nothing_otherwise()
    {
        var live = new List<(string, float, float, int, double)>
        {
            ("A caravan of the Traders Guild went missing.", 0, 0, 40, 10),
            ("Okran's Shield has fallen to the United Cities.", 100, 0, 80, 5),
            ("Far news.", 900000, 0, 99, 20),
        };
        var (line, i) = NewsAskRules.Answer(live, 0, 0, reach: 50000, roll: 1);
        Assert.Equal(1, i);
        Assert.Equal("Word is, Okran's Shield has fallen to the United Cities.", line);
        Assert.Equal("Word is, a caravan of the Traders Guild went missing.", NewsAskRules.Answer(live[..1], 0, 0, 50000, 1).Line);
        Assert.Equal(-1, NewsAskRules.Answer([], 0, 0, 50000, 0).Index);
    }
}

public class RelationRulesTests
{
    [Fact]
    public void A_change_says_who_and_why()
    {
        Assert.Equal("The Shek Kingdom turned against the squad, after a killing.", RelationRules.Line("Shek Kingdom", true, "Deed", "Killed"));
        Assert.Equal("The Holy Nation warmed to the squad, now that the war is over.", RelationRules.Line("The Holy Nation", false, "WarEnded", ""));
        Assert.Equal("The United Cities turned against the squad, after a crime they saw.", RelationRules.Line("United Cities", true, "Crime", ""));
    }

    [Fact]
    public void Debug_changes_and_unknown_causes_say_nothing()
    {
        Assert.Null(RelationRules.Line("Shek Kingdom", true, "Debug", ""));
        Assert.Null(RelationRules.Line("Shek Kingdom", true, "SomethingNew", ""));
        Assert.Null(RelationRules.Line("", true, "Deed", "Killed"));
    }
}
