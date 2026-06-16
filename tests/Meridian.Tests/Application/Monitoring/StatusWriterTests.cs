using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Monitoring;
using Meridian.Core.Config;

namespace Meridian.Tests.Application.Monitoring;

public sealed class StatusWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-status-writer-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WriteOnceAsync_WritesReadableStatusSnapshot()
    {
        var path = Path.Combine(_root, "status.json");
        await using var writer = new StatusWriter(
            path,
            () => new AppConfig(Symbols: [new SymbolConfig("SPY", SubscribeTrades: true)]));

        await writer.WriteOnceAsync();

        File.Exists(path).Should().BeTrue();
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        document.RootElement.GetProperty("symbols")[0].GetProperty("symbol").GetString().Should().Be("SPY");
        document.RootElement.GetProperty("metrics").GetProperty("published").GetInt64().Should().Be(0);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
