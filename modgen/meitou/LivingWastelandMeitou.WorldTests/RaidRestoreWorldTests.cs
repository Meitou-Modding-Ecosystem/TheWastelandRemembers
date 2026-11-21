using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Data.World;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Population;
using Xunit;
using SimWorld = Meitou.Simulation.World;

[Collection("settings")]
public sealed class RaidRestoreWorldTests : IDisposable
{
    const string RaidSquad = "test-raiders";
    static readonly RaidCode[] Codes = [new("flotsam", RaidSquad, "Dust Bandits", 1, 2, "Flotsam Ninjas")];
    readonly List<string> towns = OutlawCampsSystem.NewsTowns;
    readonly bool setting = LwMod.Settings.HarassmentRaids;

    public void Dispose()
    {
        OutlawCampsSystem.NewsTowns = towns;
        LwMod.Settings.HarassmentRaids = setting;
    }

    static PopulationData Data()
    {
        var squad = W.Rec(RaidSquad, FcsRecordType.SquadTemplate, "Raiders");
        squad.References["faction"] = [new FcsReference("60-t", 0, 0, 0)];
        var town = W.Rec("t-squin", FcsRecordType.Town, "Squin");
        town.References["faction"] = [new FcsReference("60-t", 0, 0, 0)];
        return W.Data([squad, town], [new TownPlacement("squin", "t-squin", new Vector3(50000, 150, 50000), null)]);
    }

    static (SimWorld World, RaidsSystem Raids) Scene(PopulationData data)
    {
        var raids = new RaidsSystem(Codes, new PopulationSystem(data), data);
        var world = W.World(raids);
        return (world, raids);
    }

    static IEnumerable<Platoon> Parties(SimWorld world) => world.Platoons.All.Where(p => p is not null && p.TemplateId == RaidSquad);

    [Fact]
    public void A_raid_missing_before_it_ever_loaded_is_restored_without_being_reported_caught()
    {
        OutlawCampsSystem.NewsTowns = ["Squin"];
        LwMod.Settings.HarassmentRaids = true;
        var data = Data();
        var (first, raids) = Scene(data);
        W.Spawn(first, "Leaf", W.Player, new Vector3(0, 150, 0));
        first.GameHours = RaidRules.NextStart(first.Seed, 0, 0) + 1;
        first.RunTicks(301);
        var party = Assert.Single(Parties(first));
        Assert.Equal(PlatoonState.Unloaded, party.State);
        Assert.Equal(1, raids.StateOf(first, 0));

        using var save = new MemoryStream();
        raids.Write(save);
        save.Position = 0;
        var (next, loaded) = Scene(data);
        loaded.Read(save);
        next.GameHours = first.GameHours;
        // Engine saves omit the off-screen party that never loaded. A nearby player initially blocks its old position.
        var scout = W.Spawn(next, "Scout", W.Player, new Vector3(party.Position.X, 150, party.Position.Y));
        next.RunTicks(301);
        Assert.Equal(1, loaded.StateOf(next, 0));
        Assert.Empty(Parties(next));

        // A later pass can restore it once without restarting the raid's clock.
        next.Characters.Cold(scout.Slot)!.IsPlayer = false;
        W.Spawn(next, "Leaf", W.Player, new Vector3(0, 150, 0));
        next.RunTicks(300);
        var restored = Assert.Single(Parties(next));
        Assert.Equal(1, loaded.StateOf(next, 0));
        next.RunTicks(300);
        Assert.Single(Parties(next));

        restored.Dead = true;
        next.RunTicks(300);
        Assert.Equal(2, loaded.StateOf(next, 0));
        Assert.Single(Parties(next));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Saved_raids_ignore_other_parties_with_the_same_template(bool exactPartyInSave)
    {
        var modTag = typeof(Platoon).GetProperty("ModTag");
        if (modTag is null) return;
        OutlawCampsSystem.NewsTowns = ["Squin"];
        LwMod.Settings.HarassmentRaids = true;
        var data = Data();
        var (first, raids) = Scene(data);
        W.Spawn(first, "Leaf", W.Player, new Vector3(0, 150, 0));
        first.GameHours = RaidRules.NextStart(first.Seed, 0, 0) + 1;
        first.RunTicks(301);
        var original = Assert.Single(Parties(first));
        string tag = Assert.IsType<string>(modTag.GetValue(original));
        Assert.StartsWith("lw:raid:flotsam:", tag);

        using var save = new MemoryStream();
        raids.Write(save);
        save.Position = 0;
        var (next, loaded) = Scene(data);
        loaded.Read(save);
        next.GameHours = first.GameHours;
        W.Spawn(next, "Leaf", W.Player, new Vector3(0, 150, 0));

        Platoon AddParty(string? owner, Vector2 at, bool dead = false)
        {
            int id = next.Platoons.NextId();
            var party = new Platoon { Id = id, Key = (ulong)id, Origin = 0, TemplateId = RaidSquad, Position = at, Dead = dead, Persistent = true };
            if (owner is not null) modTag.SetValue(party, owner);
            next.Platoons.Add(party);
            return party;
        }

        var foreign = AddParty("other-mod:raid", original.Position);
        var untagged = AddParty(null, original.Position);
        AddParty(null, original.Position, dead: true);
        var exact = exactPartyInSave ? AddParty(tag, original.Position + new Vector2(6000, 0)) : null;
        next.RunTicks(301);
        var owned = Assert.Single(Parties(next), party => Equals(modTag.GetValue(party), tag));
        if (exact is not null) Assert.Same(exact, owned);
        else Assert.Equal(original.Key, owned.Key);
        Assert.Equal(1, loaded.StateOf(next, 0));

        owned.Dead = true;
        next.RunTicks(300);
        Assert.Equal(2, loaded.StateOf(next, 0));
        Assert.True(foreign.Persistent);
        Assert.True(untagged.Persistent);
    }
}
