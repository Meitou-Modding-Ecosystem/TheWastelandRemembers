using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Xunit;

[Collection("settings")]
public sealed class RivalIdentityWorldTests
{
    [Fact]
    public void A_saved_rival_uses_its_engine_id_instead_of_the_nearest_matching_name()
    {
        if (!LastingActors.Available) return;
        bool setting = LwMod.Settings.Rivals;
        LwMod.Settings.Rivals = true;
        try
        {
            var data = W.Data([]);
            var rivals = new RivalsSystem(data);
            using var world = W.World(rivals);
            world.GameHours = 0;
            var leaf = W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
            var trueRival = W.Spawn(world, "Hollin", W.Bandits, new Vector3(500, 150, 0));
            var cold = world.Characters.Cold(trueRival.Slot)!;
            rivals.Hit(world, W.Hit(world, leaf, trueRival), cold, world.Characters.Cold(leaf.Slot), "Leaf", leader: true);
            rivals.FightEnded(world, won: true);
            ulong identity = LastingActors.Of(cold);
            Assert.NotEqual(0UL, identity);
            string name = cold.Name;
            world.GameHours = 55;
            world.RunTicks(301);

            using var save = new MemoryStream();
            rivals.Write(save);
            save.Position = 0;
            var loaded = new RivalsSystem(data);
            loaded.Read(save);
            using var next = W.World(loaded);
            next.GameHours = world.GameHours + 13;
            var player = W.Spawn(next, "Leaf", W.Player, Vector3.Zero);
            var decoy = W.Spawn(next, name, W.Bandits, new Vector3(500, 150, 0));
            next.RunTicks(301);  // the real actor is still off screen
            Assert.Equal("hunting", Assert.Single(loaded.Told()).Phase);
            rivals = loaded;
            void Win(CharacterId opponent)
            {
                rivals.Hit(next, W.Hit(next, player, opponent), next.Characters.Cold(opponent.Slot)!,
                    next.Characters.Cold(player.Slot), "Leaf", leader: false);
                rivals.FightEnded(next, won: true);
            }
            Win(decoy);
            Assert.DoesNotContain(W.Page(loaded), l => l.StartsWith("Has come back", StringComparison.Ordinal));
            Assert.DoesNotContain(next.TakeMessages(), m => m.Contains("got away again"));

            var back = new CharacterCold { Name = name, Faction = W.Bandits, RecordId = cold.RecordId, Medical = new MedicalState([]) };
            var slot = typeof(CharacterCold).GetProperty("LastingId")!;
            slot.SetValue(back, Activator.CreateInstance(slot.PropertyType, identity));
            var actual = next.Characters.Spawn(new CharacterHot { Alive = true, Position = new Vector3(3000, 150, 0) }, back, next.Tick);
            Assert.Equal(actual, LastingActors.Find(next, identity));
            next.RunTicks(300);
            Win(actual);
            Assert.Contains(next.TakeMessages(), m => m.Contains("got away again"));
            Assert.Contains("Has come back for your squad once and got away.", W.Page(loaded));
        }
        finally { LwMod.Settings.Rivals = setting; }
    }

    [Fact]
    public void An_older_save_leaves_two_same_name_candidates_unbound()
    {
        var data = W.Data([]);
        var rivals = new RivalsSystem(data);
        using var world = W.World(rivals);
        var leaf = W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
        var first = W.Spawn(world, "Hollin One-Arm", W.Bandits, new Vector3(500, 150, 0));
        W.Spawn(world, "Hollin One-Arm", W.Bandits, new Vector3(700, 150, 0));
        rivals.Read(W.SavedRival("Hollin One-Arm", "lost an arm to your squad", 1, 500, 0));
        world.RunTicks(301);
        rivals.Hit(world, W.Hit(world, leaf, first), world.Characters.Cold(first.Slot)!,
            world.Characters.Cold(leaf.Slot), "Leaf", leader: false);
        rivals.FightEnded(world, won: true);
        Assert.DoesNotContain(W.Page(rivals), l => l.StartsWith("Has come back", StringComparison.Ordinal));
    }
}
