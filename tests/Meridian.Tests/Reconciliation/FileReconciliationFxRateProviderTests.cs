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
            { "pivotCurrency": "USD", "quotes": [ { "from": "EUR", "to": "USD", "rate": 1.085, "asOf": "2026-01-01" } ] }
            """);

        var provider = FileReconciliationFxRateProvider.Load(_root);

        provider.TryGetRate("EUR", "USD", AsOf, out var direct).Should().BeTrue();
        direct.Should().Be(1.085m);
        provider.TryGetRate("USD", "EUR", AsOf, out var inverse).Should().BeTrue();
        inverse.Should().BeApproximately(1m / 1.085m, 0.0000001m);
    }

    [Fact]
    public void Load_SelectsRateEffectiveAtOrBeforeAsOf()
    {
        WriteTable("""
            { "quotes": [
                { "from": "EUR", "to": "USD", "rate": 1.05, "asOf": "2026-01-01" },
                { "from": "EUR", "to": "USD", "rate": 1.09, "asOf": "2026-05-01" },
                { "from": "EUR", "to": "USD", "rate": 1.20, "asOf": "2026-12-01" }
            ] }
            """);

        var provider = FileReconciliationFxRateProvider.Load(_root);

        // AsOf is 2026-05-31: the 2026-05-01 quote is the latest effective at or before it.
        provider.TryGetRate("EUR", "USD", AsOf, out var rate).Should().BeTrue();
        rate.Should().Be(1.09m);
    }

    [Fact]
    public void Load_WhenAllRatesAreFuture_FailsClosed()
    {
        WriteTable("""
            { "quotes": [ { "from": "EUR", "to": "USD", "rate": 1.09, "asOf": "2026-07-01" } ] }
            """);

        var provider = FileReconciliationFxRateProvider.Load(_root);

        // AsOf (2026-05-31) precedes every quote, so converting at the future rate would leak
        // look-ahead information into a backdated run; the provider fails closed instead.
        provider.TryGetRate("EUR", "USD", AsOf, out _).Should().BeFalse();
    }

    [Fact]
    public void Load_IgnoresNonPositiveRates()
    {
        WriteTable("""
            { "quotes": [
                { "from": "EUR", "to": "USD", "rate": 0, "asOf": "2026-01-01" },
                { "from": "GBP", "to": "USD", "rate": -1.3, "asOf": "2026-01-01" }
            ] }
            """);

        var provider = FileReconciliationFxRateProvider.Load(_root);

        provider.TryGetRate("EUR", "USD", AsOf, out _).Should().BeFalse("a zero rate must not convert every value to zero");
        provider.TryGetRate("GBP", "USD", AsOf, out _).Should().BeFalse("a negative rate must not invert cash signs");
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
