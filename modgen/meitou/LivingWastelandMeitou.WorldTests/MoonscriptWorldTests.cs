using System.Reflection;
using System.Runtime.Loader;
using LivingWasteland.MeitouMod;
using Xunit;

namespace PZA.Moonscript
{
    public interface IPackExtensionHost
    {
        bool RegisterPackCapability(string owner, string capability, IReadOnlyList<HostFunction> functions);
    }

    public sealed record HostFunction(string Table, string Name, Func<IReadOnlyList<Value>, Value> Call);
    public sealed record Value(string Text)
    {
        public static Value Of(string text) => new(text);
    }
}

[Collection("settings")]
public sealed class MoonscriptWorldTests
{
    sealed class TestHost : PZA.Moonscript.IPackExtensionHost
    {
        public string Capability = "";
        public IReadOnlyList<PZA.Moonscript.HostFunction> Functions = [];
        public bool RegisterPackCapability(string owner, string capability, IReadOnlyList<PZA.Moonscript.HostFunction> functions)
        {
            Assert.Equal("LivingWastelandMeitou", owner);
            Capability = capability;
            Functions = functions;
            return true;
        }
    }

    static MethodInfo Hook()
    {
        var link = typeof(LwMod).Assembly.GetType("LivingWasteland.MeitouMod.MoonscriptLink");
        Assert.NotNull(link);
        return link.GetMethod("TryRegister", BindingFlags.NonPublic | BindingFlags.Static)!;
    }

    [Fact]
    public void The_pack_can_read_published_text_without_a_world()
    {
        var hook = Hook();
        var host = new TestHost();
        Assert.Equal(true, hook.Invoke(null, [host, "LivingWastelandMeitou"]));
        Assert.Equal("twr.read", host.Capability);
        Assert.All(host.Functions, function => Assert.Equal("twr", function.Table));
        Assert.Equal(new[] { "heat", "hunters", "news", "rivals", "titles" }, host.Functions.Select(f => f.Name).Order());

        var saved = (RecordHost.Panel, RecordHost.CatalogText, RecordHost.HistoryText, RecordHost.FallenText);
        var latest = (HuntersSystem.Latest, RivalsSystem.Latest, NewsSystem.Latest);
        try
        {
            RecordHost.Panel = "-- The squad --\nLeaf\n\n-- Heat --\nSquin hears about you.";
            RecordHost.CatalogText = "The squad earned a title.";
            PZA.Moonscript.Value Read(string key) => host.Functions.Single(f => f.Name == key).Call([]);
            Assert.Equal("-- Heat --\nSquin hears about you.", Read("heat").Text);
            Assert.Equal("The squad earned a title.", Read("titles").Text);
            using var world = W.World();
            WorldReady.Ready(world, true);
            Assert.Equal("No world yet.", Read("heat").Text);
            Assert.Equal("No world yet.", Read("titles").Text);
        }
        finally
        {
            (RecordHost.Panel, RecordHost.CatalogText, RecordHost.HistoryText, RecordHost.FallenText) = saved;
            (HuntersSystem.Latest, RivalsSystem.Latest, NewsSystem.Latest) = latest;
        }
    }

    [Fact]
    public void The_shipped_optional_host_accepts_the_read_only_capability_when_provided()
    {
        if (Environment.GetEnvironmentVariable("TWR_TEST_MOONSCRIPT_DLL") is not { Length: > 0 } path) return;
        Assembly asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        var contract = asm.GetType("PZA.Moonscript.IPackExtensionHost", throwOnError: true)!;
        var hostType = asm.GetType("PZA.Moonscript.HostMod", throwOnError: true)!;
        Assert.True(contract.IsAssignableFrom(hostType));
        var host = Activator.CreateInstance(hostType)!;
        Assert.Equal(true, Hook().Invoke(null, [host, "LivingWastelandMeitou"]));
    }
}
