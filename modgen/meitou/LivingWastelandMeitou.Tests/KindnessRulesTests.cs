using LivingWasteland.MeitouMod;
using Xunit;

public sealed class KindnessRulesTests
{
    static KindnessDeed Prisoner(ulong id) => new(id, KindnessKind.Freed, $"Prisoner {id}", "slave", "Holy Nation Outlaws",
        "Rebirth", "Leaf", 140, new(4, 17, 9), false, false);

    [Fact]
    public void It_keeps_the_six_most_telling_stories_and_tallies_the_whole_group()
    {
        var book = new KindnessBook();
        for (ulong id = 1; id <= 8; id++) Assert.True(book.Add(Prisoner(id)));
        Assert.True(book.Add(Prisoner(9) with { Named = true }));
        Assert.True(book.Add(Prisoner(10) with { Critical = true, Kind = KindnessKind.Saved }));
        Assert.True(book.Add(Prisoner(11) with { HeldHours = 30 }));
        Assert.True(book.Add(Prisoner(12) with { Alone = true }));
        Assert.Equal([10UL, 11UL, 9UL, 12UL, 1UL, 2UL], book.People.Select(deed => deed.Actor));
        Assert.False(book.Add(Prisoner(4) with { Hours = 142 }));
        Assert.Equal(11, book.Freed);
        Assert.Equal(1, book.Saved);
    }

    [Fact]
    public void Its_optional_save_keeps_ranked_stories_and_recent_deeds_without_a_format_break()
    {
        var book = new KindnessBook();
        book.Add(Prisoner(1) with { Captor = "Holy Nation", HeldHours = 72, Alone = true });
        book.NoteSquadDown(2, 134);

        using var save = new MemoryStream();
        using var writer = new BinaryWriter(save);
        book.Write(writer);
        save.Position = 0;
        var loaded = new KindnessBook();
        using var reader = new BinaryReader(save);
        loaded.Read(reader);
        Assert.Equal(book.People, loaded.People);
        Assert.Equal(book.HashParts(), loaded.HashParts());
        Assert.True(loaded.DownedBySquad(2, 145));
        Assert.False(loaded.Add(Prisoner(1) with { Hours = 145 }));
        Assert.True(loaded.Add(Prisoner(1) with { Hours = 145 + KindnessRules.ReceiptHours }));
        using var older = new BinaryReader(new MemoryStream());
        loaded.Read(older);
        Assert.Empty(loaded.People);
        Assert.Equal(0, loaded.Freed);
    }

    [Theory]
    [InlineData(0, "earlier today")]
    [InlineData(24, "yesterday")]
    [InlineData(3 * 24, "3 days ago")]
    [InlineData(30 * 24, "a month or so ago")]
    [InlineData(35 * 24, "35 days ago")]
    public void Phrasing_uses_elapsed_time(double hours, string expected) =>
        Assert.Equal(expected, KindnessRules.HowLongAgo(hours));
}
