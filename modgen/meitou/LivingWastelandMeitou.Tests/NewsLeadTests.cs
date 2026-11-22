using System.Reflection;
using Xunit;

// The lead-ins ("Not long ago: ", "Word is, ") lower-case a leading "The" or "A" so the line reads on; a name keeps its capital (play test
// of bg: "Not long ago: the Queen was taken alive").
public class NewsLeadTests
{
    static string Uncap(string s) => (string)typeof(LivingWasteland.MeitouMod.NewsAskRules)
        .GetMethod("Uncap", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [s])!;

    [Theory]
    [InlineData("The Queen was taken alive.", "The Queen was taken alive.")]
    [InlineData("The Hub has fallen.", "The Hub has fallen.")]
    [InlineData("The caravan never arrived.", "the caravan never arrived.")]
    [InlineData("A caravan of salt was lost.", "a caravan of salt was lost.")]
    [InlineData("Okran's Shield has fallen.", "Okran's Shield has fallen.")]
    public void A_name_keeps_its_capital_after_a_lead_in(string text, string expected) => Assert.Equal(expected, Uncap(text));
}
