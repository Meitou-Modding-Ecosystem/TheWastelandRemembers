using LivingWasteland.MeitouMod;
using Xunit;

public class AtlasTests
{
    [Fact]
    public void MatcherReadsTokensAndSubjects()
    {
        var m = new AtlasMatcher([
            ("Orsa Quillon", "Orsa Quillon's lot are camped /LWDIR_0/ /LWNEAR_0/. Nobody's gone to look."),
            ("", "/CANNIBALHA/! Who brave now? Everybody taste brave inside."),
            ("Bugmaster", "Spider King dead! The Shek cry and laugh at once."),
        ]);
        Assert.Equal("Orsa Quillon", m.Match("Orsa Quillon's lot are camped east of Squin. Nobody's gone to look."));
        Assert.Equal("", m.Match("Hee hee! Who brave now? Everybody taste brave inside."));
        Assert.Equal("Bugmaster", m.Match("  Spider King dead! The Shek cry and laugh at once. "));
        Assert.Null(m.Match("Orsa Quillon's lot are camped east of Squin."));      // only part of the line
        Assert.Null(m.Match("Something nobody wrote."));
    }

    [Fact]
    public void BookKeepsTheNewestAndCountsAll()
    {
        var b = new AtlasBook();
        for (int i = 0; i < 7; i++) Assert.True(b.Note("Moll", new Heard($"line {i}", "Stack", i * 24)));
        Assert.False(b.Note("Moll", new Heard("line 6", "Stack", 200)));          // already among the kept ones
        Assert.Equal(AtlasBook.Keep, b.Of("Moll").Count);
        Assert.Equal(7, b.CountOf("Moll"));
        Assert.Equal("line 2", b.Of("Moll")[0].Text);
        Assert.Equal("Stack, day 6", AtlasBook.When(b.Of("Moll")[^1]));
    }

    [Fact]
    public void HiddenPeopleSharpenWithRumours()
    {
        var b = new AtlasBook();
        var bug = new AtlasPerson(Hidden: true);
        var moll = new AtlasPerson();
        Assert.Equal(0, b.Level("Bugmaster", bug, placeKnown: true));             // a known place does not give a hidden one away
        b.Note("Bugmaster", new Heard("a", "Squin", 1));
        Assert.Equal(1, b.Level("Bugmaster", bug, true));
        b.Note("Bugmaster", new Heard("b", "Squin", 2));
        Assert.Equal(1, b.Level("Bugmaster", bug, false));                        // still a blur after two
        b.Note("Bugmaster", new Heard("c", "Squin", 3));
        Assert.Equal(2, b.Level("Bugmaster", bug, false));                        // known after three
        b.Meet("Bugmaster");
        Assert.Equal(3, b.Level("Bugmaster", bug, false));
        Assert.Equal(0, b.Level("Moll", moll, false));
        Assert.Equal(2, b.Level("Moll", moll, true));                              // common knowledge once her place is on the map
        b.Note("Moll", new Heard("x", "Stack", 1));
        Assert.Equal(0, b.Level("Moll", moll, false));                            // one passing mention is not enough
        b.Note("Moll", new Heard("y", "Stack", 2));
        Assert.Equal(2, b.Level("Moll", moll, false));
    }

    [Fact]
    public void BookSurvivesASave()
    {
        var b = new AtlasBook();
        b.Note("@Squin", new Heard("x y z", "Squin", 30));
        b.Meet("Seto");
        b.Saw("Seto", "Squin", 50);
        b.Saw("Seto", "Admag", 90);
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) b.Write(w);
        ms.Position = 0;
        var back = new AtlasBook();
        using (var r = new BinaryReader(ms)) back.Read(r);
        Assert.Equal(b.Of("@Squin"), back.Of("@Squin"));
        Assert.True(back.Met("Seto"));
        Assert.Equal(new Heard("", "Admag", 90), back.LastSeen("Seto"));
        Assert.Equal(1, back.CountOf("@Squin"));
        var empty = new AtlasBook();
        using (var r = new BinaryReader(new MemoryStream())) empty.Read(r);       // a save from before the Atlas
        Assert.Equal(0, empty.CountOf("@Squin"));
    }
}
