using LivingWasteland.MeitouMod;
using Xunit;

public sealed class PlaytestJournalTests
{
    [Fact]
    public void Readers_get_a_bounded_snapshot_with_an_explicit_gap_and_safe_columns()
    {
        var journal = new PlaytestJournal(capacity: 2);
        journal.Add(1, 30, 12, "courier", "report:1", "dispatch", "old");
        journal.Add(1, 60, 13, "prisoner", "case:2", "recovery", "line\t1\r\nline\\2");
        var held = journal.Read();
        journal.Add(2, 1, 12.3, "world", "", "save loaded", "");

        Assert.Equal(new long[] { 1, 2 }, held.Events.Select(row => row.Number));
        var latest = journal.Read();
        Assert.Equal(1L, latest.Discarded);
        Assert.Equal(new long[] { 2, 3 }, latest.Events.Select(row => row.Number));
        var rows = PlaytestJournal.Format(latest).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("Older events discarded 1", rows[0]);
        Assert.Equal(4, rows.Length);
        Assert.Equal(9, rows[2].Split('\t').Length);
        Assert.EndsWith("line\\t1\\r\\nline\\\\2", rows[2]);
        Assert.Equal("12.3000", rows[3].Split('\t')[4]);
    }

    [Fact]
    public void Ui_and_simulation_writers_can_overlap_without_duplicate_sequence_numbers()
    {
        const int count = 256;
        var journal = new PlaytestJournal(count);
        Parallel.For(0, count, n =>
        {
            journal.Add(n % 2 == 0 ? 2 : null, n, null, "test", "", "mark", "");
            _ = journal.Read();
        });
        var saved = journal.Read();
        Assert.Equal(0, saved.Discarded);
        Assert.Equal(Enumerable.Range(1, count).Select(n => (long)n), saved.Events.Select(row => row.Number));
    }
}
