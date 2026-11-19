using System.Numerics;
using System.Reflection;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Factions;
using Xunit;
using SimWorld = Meitou.Simulation.World;

[Collection("settings")]
public sealed class EscortSaveWorldTests
{
    static Squad AddSquad(SimWorld world, string name)
    {
        var squad = new Squad { Id = world.Squads.NextId(), Name = name, TemplateId = "test", Faction = W.Player };
        world.Squads.Add(squad);
        squad.Leader = W.Spawn(world, name + " leader", W.Player, Vector3.Zero, squad: squad.Id);
        squad.Members.Add(squad.Leader);
        return squad;
    }

    [Fact]
    public void An_escort_follows_the_same_squad_after_runtime_numbers_change()
    {
        var property = typeof(Squad).GetProperty("ModTags");
        if (!SquadRoles.Available || property is null) return;
        var system = new LwSystem(null, null, null);
        var world = W.World(system);
        var escort = AddSquad(world, "Escort");
        var runner = AddSquad(world, "Runner");
        system.Inputs(world, [new SetEscort(escort.Id, runner.Id) { Tick = world.Tick }]);

        using var save = new MemoryStream();
        system.Write(save);
        save.Position = 0;
        var reloaded = new LwSystem(null, null, null);
        reloaded.Read(save);
        var next = W.World(reloaded);
        next.GameHours = world.GameHours;
        var unrelated = AddSquad(next, "Unrelated");
        var newRunner = AddSquad(next, "Runner renamed");
        var newEscort = AddSquad(next, "Escort");
        Assert.Equal(escort.Id, unrelated.Id);
        Assert.NotEqual(escort.Id, newEscort.Id);

        var tags = typeof(SimWorld).Assembly.GetType("Meitou.Simulation.Saving.ModTagSave")!;
        void ReloadTags(Squad before, Squad after)
        {
            var record = W.Rec("squad-save", FcsRecordType.SquadTemplate, "Squad save");
            tags.GetMethod("Write", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, [record, property.GetValue(before)]);
            tags.GetMethod("Read", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, [record, property.GetValue(after)]);
        }
        ReloadTags(escort, newEscort);
        ReloadTags(runner, newRunner);
        var escortTags = Assert.IsAssignableFrom<IDictionary<string, string>>(property.GetValue(newEscort));
        var runnerTags = Assert.IsAssignableFrom<IDictionary<string, string>>(property.GetValue(newRunner));
        Assert.True(escortTags.TryGetValue("LivingWastelandMeitou", out var escortTag), "The escort has no saved identity");
        Assert.True(runnerTags.TryGetValue("LivingWastelandMeitou", out var runnerTag), "The target has no saved identity");
        Assert.NotEqual(escortTag, runnerTag);

        next.RunTicks(30);
        Assert.True(SquadRoles.Problem is null, SquadRoles.Problem);
        Assert.Equal(newRunner.Id, Assert.Single(SquadRoles.Squads, r => r.Id == newEscort.Id).EscortOf);
        Assert.Equal(-1, Assert.Single(SquadRoles.Squads, r => r.Id == unrelated.Id).EscortOf);
    }

    [Fact]
    public void A_legacy_escort_number_is_not_reused_and_quest_state_still_loads()
    {
        using var save = new MemoryStream();
        using (var writer = new BinaryWriter(save, System.Text.Encoding.UTF8, true))
        {
            writer.Write(2);
            writer.Write("@escort 0"); writer.Write(2);
            writer.Write("Quest A job"); writer.Write(1);
        }
        save.Position = 0;
        var loaded = new LwSystem(null, null, null);
        loaded.Read(save);
        var world = W.World(loaded);
        world.RunTicks(2);
        var core = LwState.For(world).Core;
        Assert.Equal(0, core.Get("@escort 0"));
        Assert.Equal(1, core.Get("Quest A job"));
    }
}
