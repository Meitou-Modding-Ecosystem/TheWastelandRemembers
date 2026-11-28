using System.Security.Cryptography;
using LivingWasteland.MeitouMod;
using Xunit;

public sealed class ShippedRuntimeWorldTests
{
    [Fact]
    public void The_world_suite_uses_the_bytes_of_the_shipped_helper()
    {
        var shipped = Environment.GetEnvironmentVariable("TWR_SHIPPED_HELPER_DLL");
        if (string.IsNullOrWhiteSpace(shipped)) return;

        using var expected = File.OpenRead(shipped);
        using var executing = File.OpenRead(typeof(LwMod).Assembly.Location);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(expected)), Convert.ToHexString(SHA256.HashData(executing)));
    }
}
