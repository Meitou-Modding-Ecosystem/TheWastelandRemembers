using LivingWasteland.MeitouMod;
using Xunit;

public sealed class LongPlaytestJournalTests
{
    [Fact]
    public void A_long_playtest_keeps_its_start_and_latest_feature_events()
    {
        var journal = new PlaytestJournal();
        journal.Add(1, 1, 0, "world", "", "new game", "");
        journal.Add(null, null, null, "playtest", "", "player marked test point", "start of run");
        for (int hour = 0; hour < 45 * 24; hour++)
        {
            for (int receipt = 0; receipt < 4; receipt++)
                journal.Add(1, hour, hour, "shop reputation", "market", "purchase completed", "");
            journal.Add(1, hour, hour, "fixture", $"hour:{hour}", "reported", "");
        }
        journal.Add(null, null, null, "playtest", "", "player marked test point", "end of run");

        var snapshot = journal.Read();
        Assert.Equal(4096, snapshot.Events.Length);
        Assert.Equal(45 * 24 * 5 + 3 - 4096, snapshot.Discarded);
        Assert.Equal("new game", snapshot.Events[0].Phase);
        Assert.Equal("start of run", snapshot.Events[1].Detail);
        Assert.Equal("end of run", snapshot.Events[^1].Detail);
        Assert.Contains(snapshot.Events, row => row.Key == "hour:1079");
    }
}
