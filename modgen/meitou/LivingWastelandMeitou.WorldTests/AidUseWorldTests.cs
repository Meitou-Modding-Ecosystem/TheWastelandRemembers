using System.Numerics;
using LivingWasteland.MeitouMod;
using Meitou.Simulation;
using Xunit;

[Collection("settings")]
public sealed class AidUseWorldTests
{
    [Fact]
    public void Medics_keep_their_own_charges_and_only_the_last_user_finishes_the_kit()
    {
        var payload = typeof(World).Assembly.GetType("Meitou.Simulation.Items.ItemUsed");
        var observer = typeof(World).Assembly.GetType("Meitou.Simulation.Items.IItemUseObserver");
        if (payload is null || observer is null) return;

        var adapter = ObserverEmit.Build(observer, "ItemUsedUp", typeof(AidUseWatch).GetMethod(nameof(AidUseWatch.Used))!, "AidUseTest")!;
        var hook = Activator.CreateInstance(adapter)!;
        var emit = observer.GetMethod("ItemUsedUp")!;
        var kind = payload.GetProperty("Kind")!.PropertyType;
        void Use(World world, CharacterId medic, string category, float charges, bool finished)
        {
            var item = Activator.CreateInstance(payload, world.Tick, Enum.Parse(kind, category),
                "firstaid.gamedata", medic, null, charges, finished)!;
            emit.Invoke(hook, [world, item]);
        }

        var before = new LwDeedsSystem([], null);
        using var world = W.World(before);
        var first = W.Spawn(world, "Medic", W.Player, new Vector3(0, 150, 0));
        world.RunTicks(2);
        Use(world, first, "Eaten", 5, true);
        Use(world, first, "FirstAid", 2, false);
        using var saved = new MemoryStream();
        before.Write(saved);
        saved.Position = 0;
        var after = new LwDeedsSystem([], null);
        after.Read(saved);

        using var reloaded = W.World(after);
        W.Spawn(reloaded, "Medic", W.Player, new Vector3(0, 150, 0));
        var second = W.Spawn(reloaded, "Helper", W.Player, new Vector3(0, 150, 0));
        var bystander = W.Spawn(reloaded, "Bystander", W.Bandits, new Vector3(0, 150, 0));
        reloaded.RunTicks(2);
        Use(reloaded, bystander, "FirstAid", 20, true);
        Use(reloaded, second, "FirstAid", float.NaN, true);
        Use(reloaded, second, "FirstAid", 1, true);

        var record = after.Deeds.Record;
        var medic = record.Members.Single(m => m.Key == "Medic").Value;
        var helper = record.Members.Single(m => m.Key == "Helper").Value;
        Assert.Equal((2d, 0), (medic.AidCharges, medic.AidKitsFinished));
        Assert.Equal((1d, 1), (helper.AidCharges, helper.AidKitsFinished));
        Assert.DoesNotContain(record.Members, m => m.Key == "Bystander");
        Assert.Contains(StatsPanel.Member(record, "Helper", helper), s => s.StartsWith("First aid:", StringComparison.Ordinal));
    }
}
