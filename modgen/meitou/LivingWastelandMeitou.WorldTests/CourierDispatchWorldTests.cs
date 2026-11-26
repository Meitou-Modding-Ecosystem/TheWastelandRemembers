using LivingWasteland.MeitouMod;
using Meitou.Simulation.Population;
using Xunit;

public sealed class CourierDispatchWorldTests
{
    [Fact]
    public void The_modern_client_can_register_couriers_envoys_recovery_and_escorts()
    {
        var api = typeof(PopulationSystem);
        if (api.Assembly.GetName().Version is not { Major: 0, Minor: >= 23 }) return;
        var spawn = api.GetMethods().Where(m => m.Name == "SpawnPlatoon").ToArray();
        Assert.Contains(spawn, m => m.GetParameters().Length == 9);
        Assert.True(Couriers.Available, string.Join(Environment.NewLine, spawn.AsEnumerable()));
        Assert.True(PrisonerHandover.Available);
    }
}
