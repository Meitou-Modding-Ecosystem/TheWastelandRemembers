using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Data.Fcs;
using Meitou.Simulation;
using Xunit;

[Collection("settings")]
public sealed class RoadDeathWorldTests
{
    [Fact]
    public void A_confirmed_offscreen_outlaw_death_overrides_saved_player_credit()
    {
        if (DeathNotices.BuildWatch() is not { } watch) return;
        var outlaw = W.Rec("outlaw-t", FcsRecordType.Character, "Kestrel");
        outlaw.Bools["unique"] = true;
        var data = W.Data([outlaw]);
        var deeds = new LwDeedsSystem([], data);
        using var before = W.World(deeds);
        var leaf = W.Spawn(before, "Leaf", W.Player, Vector3.Zero);
        var target = W.Spawn(before, outlaw.Name, W.Bandits, Vector3.One);
        before.Uniques.Bind(outlaw.StringId, target);
        before.RunTicks(31);
        deeds.HitResolved(before, W.Hit(before, leaf, target));
        using var save = new MemoryStream();
        deeds.Write(save);
        save.Position = 0;

        var loaded = new LwDeedsSystem([], data);
        loaded.Read(save);
        var camps = new OutlawCampsSystem([new(8, outlaw.Name, outlaw.StringId, "camp", [])], null, data);
        using var world = W.World(new LwSystem(null, null, data), loaded, camps);
        world.GameHours = before.GameHours;
        world.RunTicks(2);
        string previousAtlas = RecordHost.AtlasList;
        try
        {
            RecordHost.AtlasList = "@@w:Kestrel|";
            Report(watch, world, "OffScreenFight", CharacterId.None, outlaw.StringId, CharacterId.None, W.Bandits);
            world.Uniques.Died(outlaw.StringId, byPlayer: false, faction: W.Bandits);
            Report(watch, world, "OffScreenFight", CharacterId.None, outlaw.StringId, CharacterId.None, W.Bandits);
            world.RunTicks(31);

            Assert.Equal(0, loaded.Deeds.Record.Members.Values.Sum(m => m.Kills));
            Assert.Single(loaded.Deeds.Record.HistoryLines(), line => line.Contains("Kestrel") && line.Contains("Dust Bandits"));
        }
        finally { RecordHost.AtlasList = previousAtlas; }
    }

    [Fact]
    public void A_known_rivals_death_notice_names_the_other_killers_faction_without_player_credit()
    {
        if (DeathNotices.BuildWatch() is not { } watch || !LastingActors.Available) return;
        bool setting = LwMod.Settings.Rivals;
        LwMod.Settings.Rivals = true;
        try
        {
            var data = W.Data([]);
            var deeds = new LwDeedsSystem([], data);
            var rivals = new RivalsSystem(data);
            using var world = W.World(rivals, deeds);
            world.GameHours = 0;
            var leaf = W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
            var rival = W.Spawn(world, "Hollin", W.Bandits, new Vector3(500, 150, 0));
            var killer = W.Spawn(world, "Scrapper", W.Bandits, new Vector3(510, 150, 0));
            var cold = world.Characters.Cold(rival.Slot)!;
            rivals.Hit(world, W.Hit(world, leaf, rival), cold, world.Characters.Cold(leaf.Slot), "Leaf", leader: true);
            rivals.FightEnded(world, won: true);
            world.GameHours = 10;
            world.RunTicks(301);
            Assert.Single(rivals.Told());
            deeds.HitResolved(world, W.Hit(world, leaf, rival));
            cold.Medical!.GetType().GetProperty("Dead")!.SetValue(cold.Medical, true);

            Report(watch, world, "Bolt", rival, cold.RecordId, killer, W.Bandits);
            world.RunTicks(31);

            Assert.Equal("dead", Assert.Single(rivals.Told()).Phase);
            Assert.Equal(0, deeds.Deeds.Record.Members.Values.Sum(m => m.Kills));
            Assert.Single(deeds.Deeds.Record.HistoryLines(), line => line.Contains(cold.Name) && line.Contains("killed by Dust Bandits"));
        }
        finally { LwMod.Settings.Rivals = setting; }
    }

    static void Report(Type watch, World world, string site, CharacterId victim, string record, CharacterId killer, int killerFaction)
    {
        var receive = watch.GetInterface("ICharacterDeathObserver")!.GetMethod("Died")!;
        var payload = receive.GetParameters()[1].ParameterType.GetElementType()!;
        var cause = Enum.Parse(payload.GetProperty("Cause")!.PropertyType, "None");
        var at = Enum.Parse(payload.GetProperty("Site")!.PropertyType, site);
        var notice = payload.GetConstructors().Single(c => c.GetParameters().Length == 9)
            .Invoke([world.Tick, victim, record, W.Bandits, -1, cause, at, killer, killerFaction]);
        receive.Invoke(Activator.CreateInstance(watch)!, [world, notice]);
    }
}
