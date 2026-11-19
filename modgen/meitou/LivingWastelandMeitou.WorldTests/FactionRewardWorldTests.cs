using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Xunit;

[Collection("settings")]
public sealed class FactionRewardWorldTests
{
    const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

    [Fact]
    public void A_contract_favour_credits_the_speakers_faction_and_respects_the_cap()
    {
        var data = W.Data([]);
        var world = W.World(new LwSystem(null, null, data));
        var speaker = W.Spawn(world, "Broker", W.Bandits, Vector3.Zero);
        world.RunTicks(2);
        var apply = typeof(LwMod).GetMethod("Favour", PrivateStatic)!;
        data.Relations.Set(W.Bandits, W.Player, 38);
        apply.Invoke(null, [world, speaker, 4, 40]);
        Assert.Equal(40, data.Relations.Get(W.Bandits, W.Player));
        data.Relations.Set(W.Bandits, W.Player, -90);
        apply.Invoke(null, [world, speaker, 4, 40]);
        Assert.Equal(-90, data.Relations.Get(W.Bandits, W.Player));
    }

    [Fact]
    public void A_raid_reward_credits_the_faction_named_in_the_content()
    {
        var data = W.Data([]);
        var world = W.World();
        typeof(RaidsSystem).GetMethod("Thank", PrivateStatic)!.Invoke(null, [world, data, "Dust Bandits", 5]);
        Assert.Equal(5, data.Relations.Get(W.Bandits, W.Player));
    }
}
