using System.Numerics;
using LivingWasteland.MeitouMod;
using Xunit;

[Collection("settings")]
public sealed class LastingActorAllocationWorldTests
{
    [Fact]
    public void Repeated_lasting_id_reads_and_grants_keep_the_identity_without_boxing()
    {
        if (!LastingActors.Available) return;
        using var world = W.World();
        var actor = W.Spawn(world, "Leaf", W.Player, Vector3.Zero);
        var cold = world.Characters.Cold(actor.Slot)!;
        ulong id = LastingActors.Grant(world, actor);
        Assert.NotEqual(0UL, id);
        Assert.Equal(id, LastingActors.Of(cold));
        Assert.Equal(actor, LastingActors.Find(world, id));

        const int repetitions = 1000;
        ulong sum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < repetitions; i++) sum += LastingActors.Grant(world, actor) + LastingActors.Of(cold);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(unchecked(id * (2 * repetitions)), sum);
        Assert.True(allocated <= 256, $"The {repetitions} pairs allocated {allocated} bytes.");
    }
}
