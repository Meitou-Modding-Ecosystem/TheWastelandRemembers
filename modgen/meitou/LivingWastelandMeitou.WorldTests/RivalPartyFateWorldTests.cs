using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Simulation;
using Meitou.Simulation.Factions;
using Xunit;

[Collection("settings")]
public sealed class RivalPartyFateWorldTests
{
    [Theory]
    [InlineData("LostFight", true, "dead")]
    [InlineData("LostFight", false, "hunting")]
    [InlineData("Despawned", true, "hunting")]
    public void An_offscreen_rival_wipe_needs_the_exact_actor_on_the_removed_party(string reason, bool actorWasThere, string outcome)
    {
        if (!LastingActors.Available || PlatoonFates.BuildWatch() is not { } watch) return;
        bool setting = LwMod.Settings.Rivals;
        LwMod.Settings.Rivals = true;
        try
        {
            var data = W.Data([]);
            var rivals = new RivalsSystem(data);
            using var before = W.World(rivals);
            before.GameHours = 0;
            var leaf = W.Spawn(before, "Leaf", W.Player, Vector3.Zero);
            var actor = W.Spawn(before, "Hollin", W.Bandits, new Vector3(500, 150, 0));
            var cold = before.Characters.Cold(actor.Slot)!;
            rivals.Hit(before, W.Hit(before, leaf, actor), cold, before.Characters.Cold(leaf.Slot), "Leaf", leader: true);
            rivals.FightEnded(before, won: true);
            Assert.NotEqual(0UL, LastingActors.Of(cold));
            before.GameHours = 10;
            before.RunTicks(301);
            before.GameHours = 55;
            before.RunTicks(301);
            Assert.Equal("hunting", Assert.Single(rivals.Told()).Phase);

            using var save = new MemoryStream();
            rivals.Write(save);
            save.Position = 0;
            var loaded = new RivalsSystem(data);
            loaded.Read(save);
            using var world = W.World(loaded, (ITickSystem)Activator.CreateInstance(watch)!);
            W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
            world.GameHours = before.GameHours;
            CharacterCold member = actorWasThere ? cold : new CharacterCold { Name = cold.Name, Faction = cold.Faction, RecordId = cold.RecordId };
            var roster = new StoredSquad { Name = "Rival's party" };
            roster.Members.Add(new StoredMember(member, Vector3.Zero, 0, true));
            var party = new Platoon { Id = world.Platoons.NextId(), Key = 7, Origin = 0, TemplateId = "rival-party", Faction = W.Bandits,
                Position = new Vector2(50000, 0), Stored = roster };
            Assert.True(Pursuit.Mark(party, "lw:rival:" + cold.Name));
            world.Platoons.Add(party);
            world.RunTicks(301);
            Assert.NotNull(party.Point);

            var winner = new Platoon { Id = world.Platoons.NextId(), Key = 8, Origin = 0, TemplateId = "winner-party", Faction = W.Bandits };
            world.Platoons.Add(winner);
            Assert.True(W.RemovePlatoon(world, party.Id, reason, winner.Id));
            world.RunTicks(1);

            var rival = Assert.Single(loaded.Told());
            Assert.Equal(outcome, rival.Phase);
            Assert.Equal(0, rival.Returns);
            var news = world.TakeMessages().Where(line => line.Contains("The road finished them first.")).ToArray();
            Assert.Equal(outcome == "dead" ? 1 : 0, news.Length);
            if (outcome == "dead") Assert.Contains("killed by Dust Bandits", news[0]);
            world.RunTicks(300);
            Assert.Empty(world.TakeMessages());
        }
        finally { LwMod.Settings.Rivals = setting; }
    }
}
