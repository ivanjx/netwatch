using System.Net;

using Microsoft.Extensions.Logging.Abstractions;

using NetWatch.Server.Configuration;
using NetWatch.Server.Data;

namespace NetWatch.Server.Tests.Data;

public sealed class SqliteSpikeStoreTests
{
    [Fact]
    public async Task WriteAndReadAsync_PersistsAcrossStoreInstances()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netwatch-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(directory, "netwatch.db");
        var options = new SpikeOptions(databasePath, IPAddress.Loopback, 2055, null, null, null, false, "all");

        try
        {
            var firstStore = new SqliteSpikeStore(options, NullLogger<SqliteSpikeStore>.Instance);
            var secondStore = new SqliteSpikeStore(options, NullLogger<SqliteSpikeStore>.Instance);

            var first = await firstStore.WriteAndReadAsync(CancellationToken.None);
            var second = await secondStore.WriteAndReadAsync(CancellationToken.None);

            Assert.Equal(1, first.WriteCount);
            Assert.Equal(2, second.WriteCount);
            Assert.True(File.Exists(databasePath));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
