using FluentAssertions;
using Meridian.FinancialOperations.Reconciliation;
using Xunit;

namespace Meridian.Tests.Reconciliation;

public sealed class FileReconciliationFxRateProviderTests : IDisposable
{
    private static readonly DateOnly AsOf = new(2026, 5, 31);
    private readonly string _root;

    public FileReconciliationFxRateProviderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"meridian-fx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup of the per-test temp directory.
        }
    }

    [Fact]
    public void Load_WithoutFile_FailsClosedToSameCurrencyOnly()
    {
        var provider = FileReconciliationFxRateProvider.Load(_root);

        provider.TryGetRate("USD", "USD", AsOf, out var same).Should().BeTrue();
        same.Should().Be(1m);
        provider.TryGetRate("EUR", "USD", AsOf, out _).Should().BeFalse(
            "no rate table means cross-currency lines fail closed to a break rather than a fabricated rate");
    }

    [Fact]
    public void Load_WithRateTable_ResolvesConfiguredAndInverseRates()
    {
        WriteTable("""
            { "pivotCurrency": "USD", "quotes": [ { "from": "EUR", "to": "USD", "rate": 1.085 } ] }
            """);

        var provider = FileReconciliationFxRateProvider.Load(_root);

        provider.TryGetRate("EUR", "USD", AsOf, out var direct).Should().BeTrue();
        direct.Should().Be(1.085m);
        provider.TryGetRate("USD", "EUR", AsOf, out var inverse).Should().BeTrue();
        inverse.Should().BeApproximately(1m / 1.085m, 0.0000001m);
    }

    [Fact]
    public void Load_WithMalformedFile_FailsClosed()
    {
        WriteTable("{ not valid json");

        var provider = FileReconciliationFxRateProvider.Load(_root);

        provider.TryGetRate("EUR", "USD", AsOf, out _).Should().BeFalse(
            "a malformed rate table must fail closed rather than fabricate a rate");
    }

    private void WriteTable(string json)
    {
        var path = Path.Combine(_root, "reconciliation", "fx-rates.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }
}
