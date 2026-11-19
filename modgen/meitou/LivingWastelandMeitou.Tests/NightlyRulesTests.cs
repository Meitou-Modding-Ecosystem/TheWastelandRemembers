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
        Assert.Equal("\tOutlaw's camp, raiding", Assert.Single(marks, m => m.Kind == MarkKind.Outlaw).Tooltip);
        var news = Assert.Single(marks, m => m.Kind == MarkKind.News);                              // last week's news only
        Assert.Equal("", news.Label);                                                                // the place has a label of its own
        Assert.Equal("\tNews from Okran's Shield, day 2\n\tOkran's Shield has fallen.", news.Tooltip);   // rows with no label
        Assert.Equal("\tRival, hunting your squad\n\tLost an arm to your squad", marks.First(m => m.Label == "Hollin One-Arm").Tooltip);
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

    [Fact]
    public void News_about_the_squad_comes_last_and_is_told_as_such()
    {
        var live = new List<(string, float, float, int, double)>
        {
            ("Knife defeated Ressa Vane at Scratch.", 0, 0, 65, 10),
            ("A caravan of the Traders Guild went missing.", 0, 0, 40, 5),
        };
        string[] squad = ["Knife", "Zoo"];
        var (line, i) = NewsAskRules.Answer(live, 0, 0, 50000, 1, squad);
        Assert.Equal(1, i);                                                         // other news first, though lighter
        var (own, j) = NewsAskRules.Answer(live[..1], 0, 0, 50000, 1, squad);
        Assert.Equal(0, j);
        Assert.Equal("Everyone's on about your lot. Knife defeated Ressa Vane at Scratch.", own);
        Assert.False(NewsAskRules.Names("Knifeman Pell went missing.", squad));     // whole names only
        Assert.True(NewsAskRules.Names("They say Zoo did it.", squad));
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

public class NewsAskRepeatTests
{
    [Fact]
    public void A_piece_told_lately_is_not_told_again_and_people_say_nothing_new()
    {
        var live = new List<(string, float, float, int, double)> { ("A caravan of the Traders Guild went missing.", 0, 0, 40, 10) };
        var (_, first) = NewsAskRules.Answer(live, 0, 0, 50000, 1, null, [double.MinValue], now: 20);
        Assert.Equal(0, first);
        var (line, again) = NewsAskRules.Answer(live, 0, 0, 50000, 1, null, [20], now: 22);      // asked two hours later, next door
        Assert.Equal(-1, again);
        Assert.Equal("Nothing you haven't heard.", line);
        Assert.Equal(0, NewsAskRules.Answer(live, 0, 0, 50000, 1, null, [20], now: 20 + NewsAskRules.TellAgainAfter).Index);
        string[] squad = ["Knife"];
        var ours = new List<(string, float, float, int, double)> { ("Knife defeated Ressa Vane at Scratch.", 0, 0, 65, 10) };
        Assert.Equal(-1, NewsAskRules.Answer(ours, 0, 0, 50000, 1, squad, [20], now: 20 + NewsAskRules.TellAgainAfter).Index);   // ours waits a day
        Assert.Equal("Haven't heard a thing.", NewsAskRules.Answer([], 0, 0, 50000, 2).Line);  // nothing at all: as before
    }
}
