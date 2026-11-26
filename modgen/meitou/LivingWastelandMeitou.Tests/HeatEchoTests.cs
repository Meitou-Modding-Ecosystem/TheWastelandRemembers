using LivingWasteland.MeitouMod;
using Xunit;

public sealed class HeatEchoTests
{
    [Fact]
    public void The_echo_follows_lowered_heat_takeover_forgiveness_decay_and_loading()
    {
        var heat = new HeatMap();
        heat.AddCrime("Stack", "Holy Nation", 24);
        heat.AddCrime("Bad Teeth", "Holy Nation", 18);
        Assert.Equal(12, heat.For(null, "Holy Nation"));
        heat.AddCrime("Stack", "Holy Nation", -16);
        Assert.Equal(9, heat.For(null, "Holy Nation"));

        heat.AddCrime("Bad Teeth", "Shek Kingdom", 0);
        Assert.Equal(4, heat.For(null, "Holy Nation"));
        Assert.Equal(9, heat.For(null, "Shek Kingdom"));
        Assert.Equal(18, heat.For("Bad Teeth", "Holy Nation")); // local heat takes precedence
        heat.Forgive("Shek Kingdom", 5);
        heat.Fade(72, new HashSet<string> { "Stack" });
        Assert.Equal(4, heat.For(null, "Holy Nation"));
        Assert.Equal(3.25, heat.For(null, "Shek Kingdom"));

        using var save = new MemoryStream();
        using (var writer = new BinaryWriter(save, System.Text.Encoding.UTF8, leaveOpen: true)) heat.Write(writer);
        save.Position = 0;
        var loaded = new HeatMap();
        loaded.AddBlood("Heft", "United Cities", 40);
        using var reader = new BinaryReader(save);
        loaded.Read(reader);
        Assert.Equal(0, loaded.For(null, "United Cities"));
        Assert.Equal(4, loaded.For(null, "Holy Nation"));
        Assert.Equal(3.25, loaded.For(null, "Shek Kingdom"));
        Assert.Equal(heat.HashParts(), loaded.HashParts());
        loaded.Forgive("Shek Kingdom", 40);
        loaded.Fade(72, new HashSet<string>());
        Assert.Equal(0, loaded.For(null, "Shek Kingdom"));
        Assert.Equal(2, loaded.For(null, "Holy Nation"));
    }

    [Fact]
    public void Repeated_dialogue_heat_queries_allocate_no_memory()
    {
        var heat = new HeatMap();
        for (int i = 0; i < 100; i++) heat.AddCrime("Town " + i, "Shek Kingdom", 24);
        _ = heat.For(null, "Shek Kingdom");
        long start = GC.GetAllocatedBytesForCurrentThread();
        double sum = 0;
        for (int i = 0; i < 1000; i++) sum += heat.For(null, "Shek Kingdom");
        long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Equal(12_000, sum);
        Assert.Equal(0, bytes);
    }
}
