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
    // the patch 102 census: slavers matched "Slave" inside "Slaver" and left the Slave Traders without givers
    [InlineData("Slaver Guard", true)]
    [InlineData("Slaver Caravan Boss", true)]
    [InlineData("Anti-Slaver Jonin", true)]
    [InlineData("Slavemonger", true)]
    [InlineData("Slave Master Haga", true)]
    [InlineData("Slave Trader", true)]
    [InlineData("Slave Market Master", true)]
    [InlineData("Rebirth Slave", false)]
    [InlineData("Garret's slave", false)]
    [InlineData("Slave escapee Holy Nation", false)]
    [InlineData("Flotsam Refugee", false)]
    public void Only_people_with_standing_offer_work(string? record, bool may) => Assert.Equal(may, GiverRules.MayOffer(record));
}
