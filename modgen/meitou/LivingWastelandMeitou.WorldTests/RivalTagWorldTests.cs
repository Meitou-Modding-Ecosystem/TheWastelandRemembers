using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Simulation.Factions;
using Xunit;

[Collection("settings")]
public sealed class RivalTagWorldTests
{
    const string Namespace = "LivingWastelandMeitou";
    const string RivalTag = "lw:rival:Hollin One-Arm";

    [Fact]
    public void A_rival_load_uses_the_saved_namespace_and_leaves_a_foreign_only_party_alone()
    {
        var tags = typeof(Platoon).GetProperty("ModTags");
        var legacy = typeof(Platoon).GetProperty("ModTag");
        if (tags is null || legacy is null) return;

        var rivals = new RivalsSystem(W.Data([]));
        using var world = W.World(rivals);
        W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
        var foreign = new Platoon { Id = world.Platoons.NextId(), Origin = 0, Key = 1, Faction = W.Bandits,
            TemplateId = "rival-squad", Position = new Vector2(49000, 0) };
        world.Platoons.Add(foreign);
        ((IDictionary<string, string>)tags.GetValue(foreign)!)["OtherMod"] = RivalTag;
        var original = new Platoon { Id = foreign.Id, Origin = 0, Key = 1, Faction = W.Bandits, TemplateId = "rival-squad" };
        var originalTags = (IDictionary<string, string>)tags.GetValue(original)!;
        originalTags[Namespace] = RivalTag;
        originalTags["OtherMod"] = "traveller";
        legacy.SetValue(original, "other-mod:shared");
        var saveApi = typeof(Platoon).Assembly.GetType("Meitou.Simulation.Saving.ModPlatoonSave")!;
        var record = new FcsRecord();
        saveApi.GetMethod("Write")!.Invoke(null, [record, original]);
        var own = new Platoon { Id = world.Platoons.NextId(), Origin = 0, Key = 1, Faction = W.Bandits,
            TemplateId = "rival-squad", Position = new Vector2(50000, 0) };
        legacy.SetValue(own, saveApi.GetMethod("Tag")!.Invoke(null, [record]));
        saveApi.GetMethod("Read")!.Invoke(null, [record, own]);
        world.Platoons.Add(own);
        var ownTags = (IDictionary<string, string>)tags.GetValue(own)!;
        rivals.Read(W.SavedRival("Hollin One-Arm", "lost an arm to your squad", 1, 50000, 0, platoonId: foreign.Id));

        world.RunTicks(301);

        Assert.NotNull(own.Point);
        Assert.Null(foreign.Point);
        Assert.Equal("traveller", ownTags["OtherMod"]);
        Assert.Equal("other-mod:shared", legacy.GetValue(own));
        Assert.False(Pursuit.IsUntagged(foreign));
    }

    [Fact]
    public void Clearing_a_twr_tag_leaves_foreign_tags_in_place()
    {
        var tags = typeof(Platoon).GetProperty("ModTags");
        var legacy = typeof(Platoon).GetProperty("ModTag");
        if (tags is null || legacy is null) return;
        var party = new Platoon { Id = 1, Origin = 0, Key = 1, TemplateId = "rival-squad" };
        var entries = (IDictionary<string, string>)tags.GetValue(party)!;
        entries["OtherMod"] = "traveller";
        legacy.SetValue(party, "other-mod:shared");

        Assert.True(Pursuit.Mark(party, RivalTag));
        Assert.Equal(RivalTag, entries[Namespace]);
        Assert.Equal("other-mod:shared", legacy.GetValue(party));
        using var world = W.World();
        world.Platoons.Add(party);
        Couriers.Release(world, party);

        Assert.False(entries.ContainsKey(Namespace));
        Assert.Equal("traveller", entries["OtherMod"]);
        Assert.Equal("other-mod:shared", legacy.GetValue(party));
    }

    [Fact]
    public void A_rivals_platoon_keeps_the_tag_written_by_another_mod()
    {
        var tag = typeof(Platoon).GetProperty("ModTag");
        if (tag is null) return;
        var rivals = new RivalsSystem(W.Data([]));
        var world = W.World(rivals);
        W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
        var squad = new Squad { Id = world.Squads.NextId(), Faction = W.Bandits, TemplateId = "other-party" };
        world.Squads.Add(squad);
        var actor = W.Spawn(world, "Hollin One-Arm", W.Bandits, new Vector3(5000, 150, 0), squad: squad.Id);
        squad.Members.Add(actor);
        squad.Leader = actor;
        var party = new Platoon { Id = world.Platoons.NextId(), Origin = 0, Key = 1, TemplateId = squad.TemplateId,
            Faction = W.Bandits, SquadId = squad.Id, Position = new Vector2(5000, 0) };
        world.Platoons.Add(party);
        tag.SetValue(party, "other-mod:party-47");

        rivals.Read(W.SavedRival("Hollin One-Arm", "lost an arm to your squad", 1, 5000, 0));
        world.RunTicks(301);
        Assert.Equal("other-mod:party-47", tag.GetValue(party));
        if (typeof(Platoon).GetProperty("ModTags")?.GetValue(party) is IDictionary<string, string> entries)
            Assert.Equal(RivalTag, entries[Namespace]);
        Assert.Equal(Vector2.Zero, party.Point);
    }
}
