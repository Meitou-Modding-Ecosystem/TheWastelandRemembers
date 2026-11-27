using LivingWasteland.MeitouMod;
using Xunit;

// Seen in bv: a lone Starving Vagrant offered a 4,400-cat contract.
public class GiverRulesTests
{
    [Theory]
    [InlineData("Starving Vagrant", false)]
    [InlineData("Drifter", false)]
    [InlineData("Escaped Slave", false)]
    [InlineData("Hungry bandit", false)]
    [InlineData("Shek Warrior", true)]
    [InlineData("Holy Nation Outlaw Hunter", true)]
    [InlineData(null, true)]
    public void Only_people_with_standing_offer_work(string? record, bool may) => Assert.Equal(may, GiverRules.MayOffer(record));
}
