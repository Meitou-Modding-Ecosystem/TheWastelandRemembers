using LivingWasteland.MeitouMod;
using Xunit;

[Collection("settings")]
public sealed class DailyLogsWorldTests
{
    [Fact]
    public void A_same_day_load_does_not_mix_daily_counts_from_two_worlds()
    {
        var output = new List<string>();
        var previous = LwMod.Log;
        LwMod.Log = output.Add;
        try
        {
            using var first = W.World(new LwSystem(null, null, null));
            first.GameHours = 51;
            first.RunTicks(11);
            LogTally.Add("earlier world deeds", 7);

            using var loaded = W.World(new LwSystem(null, null, null));
            loaded.GameHours = 52;
            loaded.RunTicks(11);
            LogTally.Add("loaded world deeds", 1);
            loaded.GameHours = 73;
            loaded.RunTicks(60);

            var line = Assert.Single(output, text => text.StartsWith("summary of day 2"));
            Assert.Contains("loaded world deeds 1", line);
            Assert.DoesNotContain("earlier world deeds", line);
        }
        finally { LwMod.Log = previous; }
    }
}
