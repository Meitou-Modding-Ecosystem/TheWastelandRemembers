using System.IO;
using LivingWasteland.MeitouMod;
using Xunit;

public class NewsBoardTests
{
    [Fact]
    public void A_kind_without_text_uses_its_line()
    {
        Assert.Equal("Okran's Shield has fallen to the United Cities.", NewsBoard.Line("town-fell", "Okran's Shield", "United Cities", ""));
        Assert.Equal("The Holy Nation have laid siege to Stack.", NewsBoard.Line("siege-began", "Stack", "The Holy Nation", ""));
        Assert.Equal("A caravan of the Traders Guild went missing near the Hub.", NewsBoard.Line("caravan-lost", "the Hub", "Traders Guild", ""));
        Assert.Equal("Okran's Shield has fallen.", NewsBoard.Line("town-fell", "Okran's Shield", "", ""));
        Assert.Equal("Stack is under siege.", NewsBoard.Line("siege-began", "Stack", "", ""));
        Assert.Null(NewsBoard.Line("something-new", "Stack", "", ""));                          // an unknown kind needs a text
        Assert.Equal("The Phoenix is dead.", NewsBoard.Line("anything", "Blister Hill", "", "the Phoenix is dead"));
    }

    [Fact]
    public void The_same_news_from_two_mods_is_said_once()
    {
        var b = new NewsBoard();
        Assert.NotNull(b.Accept("town-fell", "Okran's Shield", "United Cities", "", "dustlife", 80, 100, 0, 0, out _));
        Assert.Null(b.Accept("town-fell", "okran's shield", "United Cities", "", "other", 60, 105, 0, 0, out var why));
        Assert.Equal("already reported", why);
        Assert.NotNull(b.Accept("town-fell", "Okran's Shield", "The Holy Nation", "", "dustlife", 80, 130, 0, 0, out _));   // retaken later
        Assert.Null(b.Accept("siege-began", "", "", "", "x", 50, 130, 0, 0, out why));
        Assert.Equal("no place", why);
    }

    [Fact]
    public void Towns_talk_about_the_weightiest_live_news()
    {
        var b = new NewsBoard();
        b.Accept("town-short", "Stack", "", "", "d", 20, 0, 0, 0, out _);
        b.Accept("town-fell", "Bad Teeth", "Shek Kingdom", "", "d", 90, 10, 0, 0, out _);
        b.Accept("caravan-lost", "Hub", "Traders Guild", "", "d", 40, 20, 0, 0, out _);
        b.Accept("siege-began", "Stack", "Shek Kingdom", "", "d", 90, 30, 0, 0, out _);
        var t = b.Talked(40);
        Assert.Equal(["siege-began", "town-fell", "caravan-lost"], t.ConvertAll(i => i.Kind));
        Assert.DoesNotContain(b.Talked(40 + 24 * 8), i => i.Kind == "town-short");                // older than a week: not talked about
        b.Prune(24 * 12);
        Assert.Empty(b.Items);
    }

    [Fact]
    public void News_survives_a_save()
    {
        var b = new NewsBoard();
        var it = b.Accept("town-fell", "Okran's Shield", "United Cities", "", "dustlife", 80, 100, 12.5f, -3, out _)!;
        it.Heard = true; it.HeardHours = 101;
        var ms = new MemoryStream();
        b.Write(new BinaryWriter(ms));
        ms.Position = 0;
        var c = new NewsBoard();
        c.Read(new BinaryReader(ms));
        Assert.Single(c.Items);
        Assert.True(c.Items[0].Heard);
        Assert.Equal("Okran's Shield has fallen to the United Cities.", c.Items[0].Text);
        Assert.Null(c.Accept("town-fell", "Okran's Shield", "United Cities", "", "dustlife", 80, 102, 0, 0, out _));
    }
}
