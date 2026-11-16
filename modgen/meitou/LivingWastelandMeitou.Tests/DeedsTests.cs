using LivingWasteland.MeitouMod;
using Xunit;

public class DeedsTests
{
    static readonly TitleRule[] Rules =
    [
        new("dustbane", "Dust-Bane", "member", "kills", "Dust Bandits", null, 3),
        new("butcher", "the Butcher", "member", "kills", null, null, 5),
        new("medic", "Field Medic", "member", "hours:first aid", null, null, 1),
        new("headtakers", "the Headtakers", "squad", "leaders", null, null, 2),
        new("teshin", "Teshin's End", "squad", "named", null, ["Teshin Ro"], 1),
        new("narko", "Narko", "member", "named", null, ["Holy Lord Phoenix"], 1),
        new("prince", "the Prince's End", "member", "named", null, ["Fog Prince|Royal Fog Prince"], 1),
        new("kings", "the End of Kings", "squad", "named", null, ["Holy Lord Phoenix", "Emperor Tengu"], 1, Legendary: true),
    ];

    [Fact]
    public void MemberTitleGoesOnlyToTheKiller()
    {
        var r = new SquadRecord(Rules);
        r.Kill("Ana", "Dust Bandits", false, null);
        r.Kill("Ana", "Dust Bandits", false, null);
        Assert.Empty(r.Member("Ana").Titles);
        var said = r.Kill("Ana", "Dust Bandits", false, null);
        Assert.Contains("Ana is now known as Dust-Bane.", said);
        Assert.True(r.Holds("Ana", "dustbane"));
        Assert.False(r.Holds("Bo", "dustbane"));
    }

    [Fact]
    public void SquadTitleSumsTheCrewAndCoversRecruits()
    {
        var r = new SquadRecord(Rules);
        r.Kill("Ana", "Hungry Bandits", true, null);
        var said = r.Kill("Bo", "Hungry Bandits", true, null);
        Assert.Contains("Your squad is now known as the Headtakers.", said);
        Assert.True(r.Holds("Ana", "headtakers"));
        Assert.True(r.Holds("Newcomer", "headtakers"));
    }

    [Fact]
    public void NamedKillGivesTheSquadItsTitleOnce()
    {
        var r = new SquadRecord(Rules);
        var said = r.Kill("Bo", "Dust Bandits", true, "Teshin Ro");
        Assert.Contains("Your squad is now known as Teshin's End.", said);
        Assert.Equal(["Teshin Ro"], r.NamedDead);
        Assert.DoesNotContain(r.Kill("Ana", "Dust Bandits", false, null), s => s.Contains("Teshin"));
    }

    [Fact]
    public void TimeTitlesCountHours()
    {
        var r = new SquadRecord(Rules);
        r.Spend("Cy", "first aid", 3599, 0);
        Assert.Empty(r.Member("Cy").Titles);
        Assert.Contains("Cy is now known as Field Medic.", r.Spend("Cy", "first aid", 1, 0));
    }

    [Fact]
    public void RecordSurvivesSaveAndLoad()
    {
        var r = new SquadRecord(Rules);
        r.Kill("Ana", "Dust Bandits", true, "Teshin Ro");
        r.Knockout("Ana");
        r.LimbCut("Ana");
        r.Damage("Ana", null, 42.5);
        r.Spend("Ana", "working", 90, 30);
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) r.Write(w);
        ms.Position = 0;
        var back = new SquadRecord(Rules);
        using (var rd = new BinaryReader(ms)) back.Read(rd);
        var a = back.Member("Ana");
        Assert.Equal((1, 1, 1, 1), (a.Kills, a.Knockouts, a.LimbsCut, a.LeadersKilled));
        Assert.Equal(42.5, a.DamageDealt);
        Assert.Equal(90, a.SecondsOf("working"));
        Assert.Equal(r.CrewTitles, back.CrewTitles);
        Assert.Equal(r.HashParts(), back.HashParts());
    }

    [Fact]
    public void HeatFadesOnlyWhenNobodyIsThere()
    {
        var h = new HeatMap();
        h.AddCrime("Stack", "Holy Nation", 20);
        h.Fade(72, new HashSet<string> { "Stack" });
        Assert.Equal(20, h.Here("Stack"), 3);
        h.Fade(72, new HashSet<string>());
        Assert.Equal(10, h.Here("Stack"), 3);
    }

    [Fact]
    public void BloodFadesSlowerThanCrime()
    {
        var h = new HeatMap();
        h.AddCrime("Hub", "The Hub", 10);
        h.AddBlood("Squin", "The Hub", 10);
        h.Fade(72, new HashSet<string>());
        Assert.True(h.Here("Squin") > h.Here("Hub") + 3);
    }

    [Fact]
    public void SisterTownsHearAnEcho()
    {
        var h = new HeatMap();
        h.AddBlood("Stack", "Holy Nation", 30);
        Assert.Equal(30, h.For("Stack", "Holy Nation"), 3);
        Assert.Equal(15, h.For("Blister Hill", "Holy Nation"), 3);
        Assert.Equal(15, h.For(null, "Holy Nation"), 3);
        Assert.Equal(0, h.For("Hub", "The Hub"), 3);
    }

    [Fact]
    public void HeatIsCappedAndForgivenFactionWide()
    {
        var h = new HeatMap();
        h.AddCrime("Stack", "Holy Nation", 100);
        Assert.Equal(HeatMap.Max, h.Here("Stack"));
        h.AddBlood("Bad Teeth", "Holy Nation", 10);
        h.Forgive("Holy Nation", 40);
        Assert.Equal(0, h.Here("Stack"), 3);
        Assert.Equal(0, h.Here("Bad Teeth"), 3);
    }

    [Fact]
    public void HeatSurvivesSaveAndLoad()
    {
        var h = new HeatMap();
        h.AddCrime("Stack", "Holy Nation", 7);
        h.AddBlood("Stack", "Holy Nation", 3);
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) h.Write(w);
        ms.Position = 0;
        var back = new HeatMap();
        using (var r = new BinaryReader(ms)) back.Read(r);
        Assert.Equal(10, back.Here("Stack"), 3);
        Assert.Equal(h.HashParts(), back.HashParts());
    }

    [Theory]
    [InlineData(4, false, "fighting")]
    [InlineData(5, false, "first aid")]
    [InlineData(2, true, "travelling")]
    [InlineData(2, false, null)]
    [InlineData(0, false, null)]
    public void TasksMapToActivities(byte task, bool moving, string? expected) => Assert.Equal(expected, Activities.Of(task, moving));

    [Fact]
    public void PhoenixKillerBecomesNarkoAlone()
    {
        var r = new SquadRecord(Rules);
        var said = r.Kill("Ana", "The Holy Nation", true, "Holy Lord Phoenix");
        Assert.Contains("Ana is now known as Narko.", said);
        Assert.True(r.Holds("Ana", "narko"));
        Assert.False(r.Holds("Bo", "narko"));
    }

    [Fact]
    public void AlternativeNamesMatch()
    {
        var r = new SquadRecord(Rules);
        Assert.Contains("Bo is now known as the Prince's End.", r.Kill("Bo", "Fogmen", true, "Royal Fog Prince"));
    }

    [Fact]
    public void LegendaryNeedsEveryBossAcrossTheSquad()
    {
        var r = new SquadRecord(Rules);
        r.Kill("Ana", "The Holy Nation", true, "Holy Lord Phoenix");
        Assert.False(r.Holds("Ana", "kings"));
        var said = r.Kill("Bo", "United Cities", true, "Emperor Tengu");
        Assert.Contains("Your squad is now known as the End of Kings.", said);
    }

    [Fact]
    public void CatalogListsEveryTitleWithItsHolders()
    {
        var r = new SquadRecord(Rules);
        r.Kill("Ana", "The Holy Nation", true, "Holy Lord Phoenix");
        var c = r.Catalog();
        Assert.Equal($"Titles: 1 of {Rules.Length} earned", c[0]);
        Assert.Contains(c, l => l.StartsWith("* Narko: kill Holy Lord Phoenix") && l.EndsWith("[Ana]"));
        Assert.Contains(c, l => l.Contains("the End of Kings: kill all of: Holy Lord Phoenix, Emperor Tengu (whole squad)"));
        Assert.Contains("-- Legendary --", c);
        Assert.True(c.IndexOf("-- Legendary --") > c.IndexOf("-- Squad --"));
    }

    [Fact]
    public void CraftingCountsByKindAndEarnsItsTitle()
    {
        TitleRule[] rules = [new("smith", "the Smith", "member", "crafted", "weapons", null, 3), new("maker", "the Maker", "member", "crafted", null, null, 4)];
        var r = new SquadRecord(rules);
        r.Made("Cy", "weapons", 2);
        Assert.Empty(r.Member("Cy").Titles);
        Assert.Contains("Cy is now known as the Smith.", r.Made("Cy", "weapons", 1));
        Assert.Contains("Cy is now known as the Maker.", r.Made("Cy", "armour", 1));
        Assert.Equal(4, r.Member("Cy").Crafted);
        Assert.Equal(1, r.Member("Cy").CraftedBy["armour"]);
        Assert.Equal("3 weapons crafted", rules[0].Requirement());
    }

    [Fact]
    public void FoodAndCraftingSurviveSaveAndLoad()
    {
        var r = new SquadRecord(Rules);
        r.Ate("Ana", 50);
        r.Ate("Ana", 25.5);
        r.Made("Ana", "crossbows", 2);
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) r.Write(w);
        ms.Position = 0;
        var back = new SquadRecord(Rules);
        using (var rd = new BinaryReader(ms)) back.Read(rd);
        var a = back.Member("Ana");
        Assert.Equal((2, 75.5, 2), (a.Bites, a.Food, a.Crafted));
        Assert.Equal(2, a.CraftedBy["crossbows"]);
    }
}

public class HiredKillTests
{
    static readonly TitleRule[] Rules =
    [
        new("teshin", "Teshin's End", "squad", "named", null, ["Teshin Ro"], 1),
        new("narko", "Narko", "member", "named", null, ["Holy Lord Phoenix"], 1),
    ];

    [Fact]
    public void Hired_blades_kills_belong_to_the_squad_not_a_member()
    {
        var r = new SquadRecord(Rules);
        r.Member("Beep");
        var said = r.CrewKill("Holy Nation", leader: true, named: "Holy Lord Phoenix");
        Assert.True(r.KilledNamed("Holy Lord Phoenix"));                 // rumours: the squad killed him
        Assert.DoesNotContain("narko", r.Member("Beep").Titles);         // nobody becomes Narko off a merc's blow
        Assert.Equal(0, r.Member("Beep").Kills);
        Assert.Empty(said);
        Assert.Contains(r.HistoryLines(), l => l.Contains("Your hired blades killed Holy Lord Phoenix."));

        said = r.CrewKill("Outlaw", leader: true, named: "Teshin Ro");     // a squad title can still come from it
        Assert.Contains("Your squad is now known as Teshin's End.", said);
    }
}

public class FightEndTests
{
    [Fact]
    public void A_finished_fight_reports_once_whether_it_was_won_and_who_stood()
    {
        var r = new SquadRecord([]);
        r.FightLook(["Beep", "Ruka"], []);
        r.Knockout("Beep");
        Assert.Contains("Ruka", r.Fighters);
        for (int i = 0; i < 4; i++) r.FightLook([], ["Ruka"]);
        Assert.Null(r.TakeFightEnd());                                  // not over yet
        r.FightLook([], ["Ruka"]);
        var end = r.TakeFightEnd();
        Assert.NotNull(end);
        Assert.True(end!.Value.Won);
        Assert.Equal(["Beep"], end.Value.Standing);
        Assert.Null(r.TakeFightEnd());                                  // taken once
    }

    [Fact]
    public void A_fight_stays_open_while_a_downed_foe_gets_back_up()
    {
        // seen in play: the fight "ended" while the beaten lay down; one got up and fought on, and the end counted twice
        var r = new SquadRecord([]);
        r.FightLook(["Leaf"], []);
        r.Knockout("Leaf");
        for (int i = 0; i < 10; i++) r.FightLook([], [], foesUp: 1);   // a foe stands near, nobody fighting yet
        Assert.Null(r.TakeFightEnd());
        r.FightLook(["Leaf"], []);                                      // they come at Leaf again: the same fight
        for (int i = 0; i < 5; i++) r.FightLook([], [], foesUp: 0);
        Assert.NotNull(r.TakeFightEnd());
        Assert.Null(r.TakeFightEnd());
    }

    [Fact]
    public void A_stand_off_still_ends()
    {
        var r = new SquadRecord([]);
        r.FightLook(["Leaf"], []);
        for (int i = 0; i < 59; i++) r.FightLook([], [], foesUp: 2);
        Assert.Null(r.TakeFightEnd());
        r.FightLook([], [], foesUp: 2);
        Assert.NotNull(r.TakeFightEnd());
    }
}

public class ExecutionTests
{
    [Fact]
    public void Finishing_the_fallen_counts_and_earns_the_headsman_and_survives_a_save()
    {
        var r = new SquadRecord([new TitleRule("headsman", "the Headsman", "member", "executions", null, null, 2)]);
        r.Kill("Beep", "Dust Bandits", false, null);
        Assert.Empty(r.Executed("Beep"));
        r.Kill("Beep", "Dust Bandits", false, null);
        Assert.Contains("Beep is now known as the Headsman.", r.Executed("Beep"));
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) r.Write(w);
        ms.Position = 0;
        var back = new SquadRecord(r.Rules);
        using (var rd = new BinaryReader(ms)) Assert.True(back.Read(rd));
        Assert.Equal(2, back.Members["Beep"].Executions);
        Assert.Equal("2 of the fallen finished off", r.Rules[0].Requirement());
    }
}

public class MemorialTests
{
    static SquadRecord Squad() => new([new TitleRule("avenger", "the Avenger", "member", "avenged", null, null, 1)]);

    [Fact]
    public void The_fallen_are_kept_with_their_deeds_and_the_feud()
    {
        var r = Squad();
        r.Seen("Ruka", 24);
        r.Kill("Ruka", "Dust Bandits", false, null);
        r.Died("Ruka", 24 * 11, "Squin", "Dust Bandit", "Dust Bandits", null);
        r.Died("Ruka", 24 * 12, "Squin", null, null, null);                  // once only
        Assert.Single(r.FallenMembers);
        var f = r.FallenMembers[0];
        Assert.Equal(10, f.DaysServed, 3);
        Assert.Equal(1, f.Kills);
        Assert.Contains("Dust Bandits", r.Feuds.Keys);
        Assert.Equal("Squin", r.LastLossPlace);
        Assert.Equal(1, r.DaysSinceLoss(24 * 12), 3);
        Assert.Contains(r.HistoryLines(), l => l.Contains("Ruka fell at Squin, killed by Dust Bandit of Dust Bandits."));
        Assert.Contains(r.FallenLines(), l => l.StartsWith("Ruka: fell on day 11 at Squin"));
    }

    [Fact]
    public void Killing_the_killer_avenges_and_earns_the_title_once()
    {
        var r = Squad();
        r.Died("Ruka", 100, "the wilds", "Orsa Quillon", "Dust Bandits", "346-x.mod");
        Assert.Empty(r.Avenge("Beep", "Orsa Quillon", "Dust Bandits", "999-other.mod"));    // a different record: not her
        Assert.Equal(["Ruka"], r.Avenge("Beep", "Orsa Quillon", "Dust Bandits", "346-x.mod"));
        Assert.Empty(r.Avenge("Beep", "Orsa Quillon", "Dust Bandits", "346-x.mod"));
        Assert.Equal(1, r.Members["Beep"].Avenged);
        Assert.Contains("Beep is now known as the Avenger.", r.Award("Beep"));
        Assert.Contains(r.FallenLines(), l => l.Contains("Avenged by Beep"));
    }

    [Fact]
    public void Memorials_survive_a_save()
    {
        var r = Squad();
        r.Seen("Ruka", 0);
        r.Died("Ruka", 48, "Stack", "Paladin", "The Holy Nation", null);
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) r.Write(w);
        ms.Position = 0;
        var back = Squad();
        using (var rd = new BinaryReader(ms)) Assert.True(back.Read(rd));
        Assert.Equal(r.FallenMembers[0].Name, back.FallenMembers[0].Name);
        Assert.Equal("The Holy Nation", back.FallenMembers[0].KillerFaction);
        Assert.Contains("The Holy Nation", back.Feuds.Keys);
    }
}

public class IdentityTests
{
    [Fact]
    public void A_renamed_character_keeps_its_record_and_namesakes_get_their_own()
    {
        var r = new SquadRecord([]);
        string beep = r.KeyFor("17:char-a", "Beep");
        r.Kill(beep, "Dust Bandits", false, null);
        Assert.Equal(beep, r.KeyFor("17:char-a", "Beepy"));                 // renamed: same record
        Assert.Equal("Beepy", r.Display(beep));
        Assert.Equal(1, r.Members[beep].Kills);
        Assert.Contains(r.HistoryLines(), l => l.Contains("Beep goes by Beepy now."));
        string other = r.KeyFor("18:char-b", "Beep");                         // another Beep: her own record
        Assert.NotEqual(beep, other);
        Assert.Equal("Beep (2)", other);
        Assert.Equal("Beep", r.Display(other));
        Assert.Equal(other, r.PeekKey("18:char-b", "Beep"));
        Assert.Equal("Ruka", r.PeekKey("19:new", "Ruka"));                    // peeking never adds anyone
        Assert.Equal("Ruka", r.KeyFor("19:new", "Ruka"));
    }

    [Fact]
    public void Old_name_keyed_records_are_adopted_and_identities_survive_a_save()
    {
        var r = new SquadRecord([]);
        r.Kill("Ruka", "Dust Bandits", false, null);                          // a record from before identities
        Assert.Equal("Ruka", r.KeyFor("5:char-r", "Ruka"));
        Assert.Equal(1, r.Members["Ruka"].Kills);
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) r.Write(w);
        ms.Position = 0;
        var back = new SquadRecord([]);
        using (var rd = new BinaryReader(ms)) Assert.True(back.Read(rd));
        Assert.Equal("Ruka", back.KeyFor("5:char-r", "Ruka the Red"));
        Assert.Equal("Ruka the Red", back.Display("Ruka"));
    }
}

public class ScarTests
{
    [Fact]
    public void A_town_that_took_a_limb_is_remembered_worst_first_and_saved()
    {
        var r = new SquadRecord([]);
        r.DownedIn("Beep", "Squin");
        Assert.Equal(3, r.ScarHere("Beep", "Squin"));
        r.LimbLost("Beep", "leg", "Squin");
        Assert.Equal(2, r.ScarHere("Beep", "squin"));
        r.LimbLost("Beep", "arm", "Squin");
        Assert.Equal(1, r.ScarHere("Beep", "Squin"));
        r.LimbLost("Beep", "arm", null);                                      // out in the wilds: counted, not a town scar
        Assert.Equal(3, r.Members["Beep"].LimbsLost);
        Assert.Equal(0, r.ScarHere("Beep", "Stack"));
        Assert.Equal(0, r.ScarHere("Nobody", "Squin"));
        Assert.Contains(r.HistoryLines(), l => l.Contains("Beep lost an arm at Squin."));
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) r.Write(w);
        ms.Position = 0;
        var back = new SquadRecord([]);
        using (var rd = new BinaryReader(ms)) Assert.True(back.Read(rd));
        Assert.Equal(1, back.ScarHere("Beep", "Squin"));
    }

    [Fact]
    public void RidingAndSailingAreNotWalking()
    {
        var rules = new TitleRule[] { new("rider", "the Rider", "member", "ridden", null, null, 500), new("walker", "the Walker", "member", "distance", null, null, 500) };
        var r = new SquadRecord(rules);
        var said = r.Travelled("Ana", 1, 600);
        r.Travelled("Ana", 2, 40);
        r.Spend("Ana", "walking", 10, 100);
        var a = r.Member("Ana");
        Assert.Equal((600.0, 40.0, 100.0), (a.Ridden, a.Sailed, a.Distance));
        Assert.Contains("rider", a.Titles);
        Assert.DoesNotContain("walker", a.Titles);
        Assert.NotEmpty(said);
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) r.Write(w);
        ms.Position = 0;
        var back = new SquadRecord(rules);
        using (var rd = new BinaryReader(ms)) back.Read(rd);
        Assert.Equal((600.0, 40.0), (back.Member("Ana").Ridden, back.Member("Ana").Sailed));
    }

    [Fact]
    public void SquadNumbersFromOtherModsEarnTitlesAndSurviveASave()
    {
        var rules = new TitleRule[] { new("gate", "the Gatekeepers", "squad", "raids_repelled", null, null, 3) };
        var r = new SquadRecord(rules);
        Assert.Empty(r.SetCrewStat("raids_repelled", 2));
        Assert.Single(r.SetCrewStat("raids_repelled", 3));
        Assert.Empty(r.SetCrewStat("raids_repelled", 3));                     // said once
        Assert.True(r.NewEvent("raidcommander:(1, 1)"));
        Assert.False(r.NewEvent("raidcommander:(1, 1)"));
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) r.Write(w);
        ms.Position = 0;
        var back = new SquadRecord(rules);
        using (var rd = new BinaryReader(ms)) back.Read(rd);
        Assert.Equal(3, back.CrewStat("raids_repelled"));
        Assert.False(back.NewEvent("raidcommander:(1, 1)"));
        Assert.Contains("gate", back.CrewTitles);
    }

    [Fact]
    public void EveryFallenGetsAnEpitaph()
    {
        var avenged = new Fallen("Ana", 100, "Squin", "Bob", "Dust Bandits", null, 3, 40, [], "Cid");
        var titled = avenged with { AvengedBy = null, Titles = ["the Hard Hand"] };
        var green = titled with { Titles = [], DaysServed = 1 };
        Assert.Equal("Avenged by Cid.", SquadRecord.Epitaph(avenged));
        Assert.Contains("the Hard Hand", SquadRecord.Epitaph(titled));
        Assert.Equal("Joined the squad only days before.", SquadRecord.Epitaph(green));
        Assert.Equal(SquadRecord.Epitaph(green), SquadRecord.Epitaph(green));
    }

    static readonly TitleRule[] Ladder =
    [
        new("hardhand", "the Hard Hand", "member", "kills", null, null, 2),
        new("bonepicker", "Bonepicker", "member", "kills", null, null, 4),
        new("dustbane", "Dust-Bane", "member", "kills", "Dust Bandits", null, 3),
        new("fillers", "the Grave-Fillers", "squad", "kills", null, null, 3),
        new("ashtide", "the Ash Tide", "squad", "kills", null, null, 6),
    ];

    [Fact]
    public void AHigherRungQuietsTheLowerOnes()
    {
        var r = new SquadRecord(Ladder);
        for (int i = 0; i < 2; i++) r.Kill("Ana", "Hungry Bandits", false, null);
        for (int i = 0; i < 2; i++) r.Kill("Bo", "Hungry Bandits", false, null);
        Assert.True(r.HoldsTop("Ana", "hardhand"));
        for (int i = 0; i < 2; i++) r.Kill("Ana", "Dust Bandits", false, null);   // Ana: 4 kills (Bonepicker), only 2 of them Dust Bandits
        Assert.True(r.Holds("Ana", "hardhand"));                                   // still held, still listed
        Assert.False(r.HoldsTop("Ana", "hardhand"));                               // but the world reacts to Bonepicker
        Assert.True(r.HoldsTop("Ana", "bonepicker"));
        Assert.True(r.HoldsTop("Bo", "hardhand"));                                 // Bo has not outgrown it
        Assert.True(r.AnyHoldsTop("hardhand"));                                    // a tale about the Hard Hand: Bo's
        Assert.True(r.Outgrown("Ana", "hardhand"));
        Assert.Contains(r.Catalog(), l => l.Contains("the Hard Hand") && l.Contains("Ana (outgrown)") && l.Contains("Bo]"));
        Assert.False(r.HoldsTop("Ana", "fillers"));                                // the squad has 6 kills: the Ash Tide quiets the Grave-Fillers
        Assert.True(r.HoldsTop("Newcomer", "ashtide"));
        Assert.False(r.AnyHoldsTop("fillers"));
        Assert.Contains(r.Catalog(), l => l.Contains("the Grave-Fillers") && l.Contains("the squad (outgrown)"));
        for (int i = 0; i < 2; i++) r.Kill("Bo", "Hungry Bandits", false, null);
        Assert.False(r.AnyHoldsTop("hardhand"));                                   // Bo is a Bonepicker now too
    }
}
