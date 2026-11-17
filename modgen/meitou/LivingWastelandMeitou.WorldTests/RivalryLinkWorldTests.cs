using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Simulation;
using Rivalry;
using Xunit;

// A stand-in for Rivalry Core's state with the same names and shapes as its public surface (MeitouMods RivalryCore/IRivalryApi.cs,
// Parties.cs): RivalryLink finds it by those names, as it finds the real one. One hostile slot, as Rivalry Core's cap.
namespace Rivalry
{
    public enum PartyKind { None, Hostile, Friendly }

    public sealed record PartyClaim(string Source, PartyKind Kind, string Key, double Hours, int Faction = -1);

    public sealed record PartyView(PartyKind Kind, string Source, string What, string Key, int Faction, int Run);

    public interface IRivalryState
    {
        IReadOnlyList<PartyView> Parties { get; }
        bool ClaimParty(PartyClaim claim);
        bool ReleaseParty(string source, string key);
    }

    public sealed class FakeRivalry : ITickSystem, IRivalryState
    {
        public readonly List<(string Source, string Key)> Claims = [];

        public IReadOnlyList<PartyView> Parties => Claims.Select(c => new PartyView(PartyKind.Hostile, c.Source, "claim", c.Key, -1, 0)).ToList();

        public bool ClaimParty(PartyClaim claim)
        {
            if (Claims.Contains((claim.Source, claim.Key))) return true;
            if (Claims.Count >= 1) return false;
            Claims.Add((claim.Source, claim.Key));
            return true;
        }

        public bool ReleaseParty(string source, string key) => Claims.Remove((source, key));
    }
}

public class RivalryLinkWorldTests
{
    [Fact]
    public void Without_Rivalry_Core_our_hunters_are_sent_as_before()
    {
        var world = W.World();
        Assert.True(RivalryLink.MaySend(world, PartyClaims.Crew("Squin", 10)));
        RivalryLink.Keep(world, "crew:", []);
    }

    [Fact]
    public void Our_parties_claim_the_one_hostile_slot_renew_it_and_release_it()
    {
        var rivalry = new FakeRivalry();
        var world = W.World(rivalry);
        string crew = PartyClaims.Crew("Squin", 10), reprisal = PartyClaims.Reprisal("Pirate Captain", 12);

        Assert.True(RivalryLink.MaySend(world, crew));
        Assert.Contains((RivalryLink.Source, crew), rivalry.Claims);
        Assert.False(RivalryLink.MaySend(world, reprisal));             // one hostile party at a time, ours included
        RivalryLink.Keep(world, "crew:", [crew]);                        // still out: kept
        Assert.Single(rivalry.Claims);
        RivalryLink.Keep(world, "reprisal:", []);                        // another kind's pass leaves the crew's claim alone
        Assert.Single(rivalry.Claims);
        RivalryLink.Keep(world, "crew:", []);                            // the crew ended: released
        Assert.Empty(rivalry.Claims);
        Assert.True(RivalryLink.MaySend(world, reprisal));
    }

    [Fact]
    public void A_rival_waits_while_another_mods_party_is_out_then_hunts()
    {
        var rivalry = new FakeRivalry();
        rivalry.Claims.Add(("ashen.hand.hunters", "Leaf"));
        var rivals = new RivalsSystem(null);
        var world = W.World(rivalry, rivals);
        W.Spawn(world, "Leaf", W.Player, new Vector3(0, 150, 0));
        W.Spawn(world, "Hollin One-Arm", W.Bandits, new Vector3(500, 150, 0));
        rivals.Read(W.SavedRival("Hollin One-Arm", "lost an arm to your squad", phase: 0, 500, 0, next: 0));

        world.RunTicks(301);
        Assert.DoesNotContain(rivalry.Claims, c => c.Source == RivalryLink.Source);

        rivalry.Claims.Clear();                                          // the other party ended
        world.RunTicks(300);
        Assert.Contains((RivalryLink.Source, PartyClaims.Rival("Hollin One-Arm")), rivalry.Claims);
    }
}
