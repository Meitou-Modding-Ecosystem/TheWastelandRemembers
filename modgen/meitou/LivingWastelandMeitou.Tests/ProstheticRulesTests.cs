using LivingWasteland.MeitouMod;
using Xunit;
using static LivingWasteland.MeitouMod.ProstheticRules;

// A returning rival's replacement limbs follow what their people can afford: starving bandits get a shoddy Economy Leg and keep a lost arm
// lost; the middling a standard leg and arm; Tech Hunters and the like a high-grade Skeleton leg and arm.
public class ProstheticRulesTests
{
    [Theory]
    [InlineData("Starving Bandits", Tier.Poor)]
    [InlineData("Hungry Bandits", Tier.Poor)]
    [InlineData("Dust Bandits", Tier.Poor)]
    [InlineData("Some Mod's Faction", Tier.Poor)]
    [InlineData(null, Tier.Poor)]
    [InlineData("Shek Kingdom", Tier.Middle)]
    [InlineData("united cities", Tier.Middle)]
    [InlineData("Tech Hunters", Tier.Wealthy)]
    [InlineData("Traders Guild", Tier.Wealthy)]
    public void A_faction_is_poor_middling_or_wealthy(string? faction, Tier tier) => Assert.Equal(tier, Of(faction));

    [Fact]
    public void The_poor_get_a_shoddy_economy_leg_and_no_arm()
    {
        Assert.Equal(("96136-Newwworld.mod", 20), Limb(Tier.Poor, 2));
        Assert.Equal(("96135-Newwworld.mod", 20), Limb(Tier.Poor, 3));
        Assert.Null(Limb(Tier.Poor, 0));
        Assert.Null(Limb(Tier.Poor, 1));
    }

    [Fact]
    public void The_middling_get_standard_economy_limbs_arms_included()
    {
        Assert.Equal(("96136-Newwworld.mod", 40), Limb(Tier.Middle, 2));
        Assert.Equal(("96134-Newwworld.mod", 40), Limb(Tier.Middle, 0));
        Assert.Equal(("96137-Newwworld.mod", 40), Limb(Tier.Middle, 1));
    }

    [Fact]
    public void The_wealthy_get_high_grade_skeleton_limbs()
    {
        Assert.Equal(("95872-rebirth.mod", 60), Limb(Tier.Wealthy, 2));
        Assert.Equal(("96022-rebirth.mod", 60), Limb(Tier.Wealthy, 3));
        Assert.Equal(("95747-Newwworld.mod", 60), Limb(Tier.Wealthy, 0));
        Assert.Null(Limb(Tier.Wealthy, 4));
    }

    [Fact]
    public void The_road_says_what_they_came_back_on()
    {
        Assert.Equal(", on a shoddy iron leg", Arrival(Tier.Poor, true, false));
        Assert.Equal(", on a new iron leg and with a new iron arm", Arrival(Tier.Middle, true, true));
        Assert.Equal(", with a fine skeleton arm", Arrival(Tier.Wealthy, false, true));
        Assert.Equal("", Arrival(Tier.Wealthy, false, false));
    }
}

// What a rival says about the limbs they came back with: a wealthy one in their own faction's voice, and a replaced arm is never denied.
public class RivalLimbLinesTests
{
    [Fact]
    public void A_wealthy_rival_with_a_new_limb_talks_about_it_in_their_factions_voice()
    {
        var tech = Enumerable.Range(0, 2).Select(k => BarkLines.Rival("lost a leg to your squad", 0, 0, k, "Tech Hunters", Tier.Wealthy, legFitted: true)).ToList();
        Assert.All(tech, l => Assert.Contains(l, new[] { "World's End fitted me with ancient work. It hits harder than the leg you took.",
            "Skeleton make, dug out of a ruin. Better than the one you cost me." }));
        Assert.StartsWith("Sold three", BarkLines.Rival("lost an arm to your squad", 0, 0, 0, "Slave Traders", Tier.Wealthy, armFitted: true));
        Assert.Equal("Best arm in the market. You'll fetch less.",
            BarkLines.Rival("lost an arm to your squad", 0, 0, 1, "Slave Traders", Tier.Wealthy, armFitted: true));
        Assert.Equal("Best leg in the market. You'll fetch less.",           // both fitted: the one the squad took
            BarkLines.Rival("lost a leg to your squad", 0, 0, 1, "Slave Traders", Tier.Wealthy, armFitted: true, legFitted: true));
        Assert.Equal("Best arm in the market. You'll fetch less.",
            BarkLines.Rival("lost an arm to your squad", 0, 0, 1, "Slave Traders", Tier.Wealthy, armFitted: true, legFitted: true));
        Assert.DoesNotContain("{limb}", string.Join(" ", Enumerable.Range(0, 2).SelectMany(k => new[] { "Tech Hunters", "Traders Guild", "Trade Ninjas",
            "Nobles", "Mercenary Guild", "Manhunters", "Slave Traders" }.Select(f => BarkLines.Rival("lost a leg to your squad", 0, 0, k, f, Tier.Wealthy, legFitted: true)))));
    }

    [Fact]
    public void Without_a_new_limb_or_lines_of_their_own_they_say_what_they_said_before()
    {
        string before = BarkLines.Rival("lost a leg to your squad", 0, 0, 0);
        Assert.Equal(before, BarkLines.Rival("lost a leg to your squad", 0, 0, 0, "Tech Hunters", Tier.Wealthy));            // nothing fitted yet
        Assert.Equal(before, BarkLines.Rival("lost a leg to your squad", 0, 0, 0, "Second Empire", Tier.Wealthy, legFitted: true));   // no lines
        Assert.Equal(before, BarkLines.Rival("lost a leg to your squad", 0, 0, 0, "Dust Bandits", Tier.Poor, legFitted: true));
        Assert.StartsWith("Back for another", BarkLines.Rival("lost a leg to your squad", 0, 1, 0, "Tech Hunters", Tier.Wealthy, legFitted: true));
    }

    [Fact]
    public void A_rival_whose_arm_was_replaced_never_says_they_have_one_arm()
    {
        foreach (int k in new[] { 0, 1 })
        {
            string line = BarkLines.Rival("lost an arm to your squad", 0, 0, k, "Shek Kingdom", Tier.Middle, armFitted: true);
            Assert.DoesNotContain("One arm", line);
            Assert.Contains(line, new[] { "Remember me? You took my arm. I've a new one.", "New arm. Same grudge." });
        }
        Assert.Contains(BarkLines.Rival("lost an arm to your squad", 0, 0, 1), new[] { "Remember me? You took my arm.", "One arm's enough for you lot." });
    }
}
