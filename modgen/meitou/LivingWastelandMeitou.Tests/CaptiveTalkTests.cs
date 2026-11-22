using LivingWasteland.MeitouMod;
using Xunit;

// A captive asked for news answers in their own people's voice; a Hive Queen screams (play test of bg: the Queen spoke a plain line).
public class CaptiveTalkTests
{
    [Fact]
    public void Each_captive_answers_in_their_own_voice()
    {
        Assert.Equal("EEEEEH!", CaptiveTalk.Line("96351-Dialogue.mod", "Bloodraiders", 0));
        Assert.Equal("EEEEEH!", CaptiveTalk.Line("57334-Newwworld.mod", null, 3));
        Assert.Contains("Okran", CaptiveTalk.Line("x", "The Holy Nation", 0));
        Assert.Contains("Shek", CaptiveTalk.Line("x", "Shek Kingdom", 0));
        Assert.Contains("unit", CaptiveTalk.Line("x", "Skin Bandits", 1));
        Assert.Equal("Open the cage and I might remember something.", CaptiveTalk.Line("x", "Some Mod's Faction", 5));
        Assert.Equal("Open the cage and I might remember something.", CaptiveTalk.Line(null, null, 0));
    }
}
