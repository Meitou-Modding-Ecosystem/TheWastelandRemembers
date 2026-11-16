using LivingWasteland.MeitouMod;
using Xunit;

public class LogTallyTests
{
    [Fact]
    public void One_line_a_day_with_the_counts()
    {
        Assert.Null(LogTally.Day(10));
        LogTally.Count("kill: Bo killed X (Dust Bandits)");
        LogTally.Count("kill: Bo killed Y (Dust Bandits)");
        LogTally.Count("crier: had nothing to say in Squin");
        LogTally.Count("victory line: Bo is alone, says nothing");
        LogTally.Count("victory line: Bo spoke over X");
        LogTally.Add("bar lines");
        Assert.Null(LogTally.Day(20));
        Assert.Equal("summary of day 0: kills 2, crier silent 1, victory lines 1, bar lines 1", LogTally.Day(25));
        Assert.Equal("summary of day 1: nothing logged", LogTally.Day(49));
    }
}
