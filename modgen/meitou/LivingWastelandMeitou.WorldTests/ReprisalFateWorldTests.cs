using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Meitou.Simulation.Factions;
using Meitou.Simulation.Population;
using Xunit;

[Collection("settings")]
public sealed class ReprisalFateWorldTests : IDisposable
{
    const string Squad = "test-reprisal";
    const int Outlaw = 8;
    readonly bool reprisals = LwMod.Settings.OutlawReprisals;
    readonly bool camps = LwMod.Settings.OutlawCamps;

    public void Dispose()
    {
        LwMod.Settings.OutlawReprisals = reprisals;
        LwMod.Settings.OutlawCamps = camps;
    }

    [Theory]
    [InlineData("Dropped", true, false)]
    [InlineData("LostFight", false, false)]
    [InlineData("LostFight", false, true)]
    public void Reprisal_removals_are_told_apart_after_loading_an_old_save(string reason, bool restored, bool saveAfterNotice)
    {
        if (!PlatoonFates.Available) return;
        LwMod.Settings.OutlawReprisals = true;
        LwMod.Settings.OutlawCamps = false;
        var squad = W.Rec(Squad, FcsRecordType.SquadTemplate, "Men sent by Kestrel");
        squad.References["faction"] = [new FcsReference("60-t", 0, 0, 0)];
        var data = W.Data([squad]);
        var system = new OutlawCampsSystem([new(Outlaw, "Kestrel", "kestrel", "camp", [], Squad)], new PopulationSystem(data), data);
        var watch = PlatoonFates.BuildWatch()!;
        using var world = saveAfterNotice ? W.World(system, (ITickSystem)Activator.CreateInstance(watch)!) : W.World(system);
        W.Spawn(world, "Leaf", W.Player, new Vector3(0, 150, 0));
        world.GameHours = 140;

        var at = new Vector2(30000, 40000);
        using var save = new MemoryStream();
        using (var writer = new BinaryWriter(save, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(3);
            writer.Write(0); // camps
            writer.Write(0); // strikes
            writer.Write(1); // reprisals
            writer.Write(Outlaw);
            writer.Write(138.0);
            writer.Write(at.X);
            writer.Write(at.Y);
            writer.Write(0); // answered strikes
        }
        save.Position = 0;
        system.Read(save);
        var foreign = new Platoon { Id = world.Platoons.NextId(), Origin = 0, Key = 1, TemplateId = Squad, Position = at };
        typeof(Platoon).GetProperty("ModTag")!.SetValue(foreign, "other-mod:party");
        world.Platoons.Add(foreign);

        world.RunTicks(301);
        var first = Assert.Single(world.Platoons.All, p => p != foreign && p.TemplateId == Squad);
        var tag = Assert.IsType<string>(Pursuit.ReadTag(first));
        Assert.StartsWith("lw:reprisal:", tag);
        if (saveAfterNotice)
        {
            Assert.True(W.RemovePlatoon(world, first.Id, reason));
            world.RunTicks(1);
            using var written = new MemoryStream();
            system.Write(written);
            written.Position = 0;
            var loaded = new OutlawCampsSystem([new(Outlaw, "Kestrel", "kestrel", "camp", [], Squad)], new PopulationSystem(data), data);
            using var next = W.World(loaded);
            W.Spawn(next, "Leaf", W.Player, new Vector3(0, 150, 0));
            next.GameHours = world.GameHours;
            loaded.Read(written);
            next.RunTicks(301);
            Assert.Empty(next.Platoons.All);
            Assert.Empty(loaded.ReprisalParties());
        }
        else
        {
            Assert.True(world.Platoons.Remove(first.Id));
            PlatoonFates.Tell(world, tag, reason);
            world.RunTicks(600);
        }

        Assert.False(foreign.Persistent);
        if (restored)
        {
            var replacement = Assert.Single(world.Platoons.All, p => p != foreign && p.TemplateId == Squad);
            Assert.NotEqual(first.Id, replacement.Id);
            Assert.Equal(tag, Pursuit.ReadTag(replacement));
            Assert.Single(system.ReprisalParties());
        }
        else
        {
            Assert.Empty(system.ReprisalParties());
            Assert.Same(foreign, Assert.Single(world.Platoons.All));
            Assert.Single(world.TakeMessages(), line => line.Contains("The road finished them first."));
            world.RunTicks(600);
            Assert.Empty(world.TakeMessages());
        }
    }
}
