using NetWatch.Server.MikroTik;

namespace NetWatch.Server.Tests.MikroTik;

public sealed class RouterOsVersionTests
{
    [Theory]
    [InlineData("7.19.3", 7)]
    [InlineData("7.20beta2", 7)]
    [InlineData("7", 7)]
    [InlineData("6.49.18 (long-term)", 6)]
    public void TryParse_RecognizesRouterOsVersions(string value, int expectedMajor)
    {
        Assert.True(RouterOsVersion.TryParse(value, out var version));
        Assert.Equal(expectedMajor, version.Major);
    }

    [Theory]
    [InlineData(6, false)]
    [InlineData(7, true)]
    [InlineData(8, true)]
    public void MajorVersion_EnforcesRouterOsV7Minimum(int major, bool supported)
    {
        Assert.Equal(supported, new RouterOsVersion(major).IsSupported);
    }
}
