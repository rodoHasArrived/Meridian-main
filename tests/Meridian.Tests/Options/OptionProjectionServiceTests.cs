using FluentAssertions;
using Meridian.Instruments.Options;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.Options;

public sealed class OptionProjectionServiceTests
{
    [Fact]
    public async Task GetContractAsync_NormalizesContractSymbol_BeforeStoreLookup()
    {
        var expiry = new DateOnly(2026, 12, 19);
        var store = Substitute.For<IOptionReferenceProjectionStore>();
        store.GetContractAsync("TST-20261219-C-100", Arg.Any<CancellationToken>())
            .Returns(CreateProjection("TST-20261219-C-100", "TST-20261219", "TST", "Call", 100m, expiry));
        var service = new OptionProjectionService(store);

        var result = await service.GetContractAsync("  tst-20261219-c-100  ");

        result.Should().NotBeNull();
        await store.Received(1).GetContractAsync("TST-20261219-C-100", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSeriesAsync_NormalizesOptionChainId_BeforeStoreLookup()
    {
        var expiry = new DateOnly(2026, 12, 19);
        var store = Substitute.For<IOptionReferenceProjectionStore>();
        store.GetSeriesContractsAsync("TST-20261219", expiry, Arg.Any<CancellationToken>())
            .Returns([CreateProjection("TST-20261219-C-100", "TST-20261219", "TST", "Call", 100m, expiry)]);
        var service = new OptionProjectionService(store);

        var result = await service.GetSeriesAsync("  tst-20261219  ", expiry);

        result.Should().NotBeNull();
        result!.OptionChainId.Should().Be("TST-20261219");
        await store.Received(1).GetSeriesContractsAsync("TST-20261219", expiry, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportSnapshotAsync_ReturnsNormalizedUnderlyingAndHydratedSeries()
    {
        var expiry = new DateOnly(2026, 12, 19);
        var snapshot = new OptionChainSnapshot(
            DateTimeOffset.Parse("2026-05-20T14:00:00Z"),
            "  tst  ",
            101m,
            expiry,
            [100m],
            [CreateQuote("tst-20261219-c-100", "tst", OptionRight.Call, 100m, expiry)],
            []);
        var store = new InMemoryOptionReferenceProjectionStore([]);
        var service = new OptionProjectionService(store);

        var imported = await service.ImportSnapshotAsync(snapshot);

        imported.OptionChainId.Should().Be("TST-20261219");
        imported.UnderlyingSymbol.Should().Be("TST");
        imported.Series.Should().ContainSingle();
        imported.Series[0].Contracts.Should().ContainSingle(c => c.ContractSymbol == "TST-20261219-C-100");
    }

    [Fact]
    public async Task GetUnderlyingLinkageAsync_ReturnsNull_WhenUnderlyingSymbolMissing()
    {
        var store = new InMemoryOptionReferenceProjectionStore(
        [
            new OptionContractProjectionRow(
                ContractSymbol: "TEST-20261219-C-100",
                SecurityId: Guid.NewGuid(),
                OptionChainId: "TEST-20261219",
                UnderlyingSymbol: "",
                UnderlyingSecurityId: null,
                PutCall: "Call",
                Strike: 100m,
                ExpiryDate: new DateOnly(2026, 12, 19),
                Multiplier: 100m,
                IsAdjusted: false,
                LastTradingDate: new DateOnly(2026, 12, 19),
                LifecycleStat: "Active",
                LastUpdatedUtc: DateTimeOffset.UtcNow,
                Version: 1)
        ]);
        var service = new OptionProjectionService(store);

        var result = await service.GetUnderlyingLinkageAsync("TEST-20261219-C-100");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSeriesAsync_GroupsAndSortsContracts_ByStrikeThenPutCall()
    {
        var expiry = new DateOnly(2026, 12, 19);
        var store = new InMemoryOptionReferenceProjectionStore(
        [
            CreateProjection("TST-20261219-P-105", "TST-20261219", "TST", "Put", 105m, expiry),
            CreateProjection("TST-20261219-C-100", "TST-20261219", "TST", "Call", 100m, expiry),
            CreateProjection("TST-20261219-P-100", "TST-20261219", "TST", "Put", 100m, expiry),
            CreateProjection("TST-20261219-C-105", "TST-20261219", "TST", "Call", 105m, expiry)
        ]);
        var service = new OptionProjectionService(store);

        var series = await service.GetSeriesAsync("TST-20261219", expiry);

        series.Should().NotBeNull();
        series!.Contracts.Select(c => c.ContractSymbol).Should().ContainInOrder(
        [
            "TST-20261219-C-100",
            "TST-20261219-P-100",
            "TST-20261219-C-105",
            "TST-20261219-P-105"
        ]);
    }

    private static OptionContractProjectionRow CreateProjection(
        string contractSymbol,
        string chainId,
        string underlying,
        string putCall,
        decimal strike,
        DateOnly expiry)
        => new(
            contractSymbol,
            null,
            chainId,
            underlying,
            null,
            putCall,
            strike,
            expiry,
            100m,
            false,
            expiry,
            "Active",
            DateTimeOffset.UtcNow,
            1);

    private static OptionQuote CreateQuote(
        string symbol,
        string underlying,
        OptionRight right,
        decimal strike,
        DateOnly expiry)
        => new(
            DateTimeOffset.Parse("2026-05-20T14:00:00Z"),
            symbol,
            new OptionContractSpec(underlying, strike, expiry, right),
            1m,
            10,
            1.25m,
            12,
            101m);

    private sealed class InMemoryOptionReferenceProjectionStore : IOptionReferenceProjectionStore
    {
        private readonly Dictionary<string, OptionContractProjectionRow> _rows;

        public InMemoryOptionReferenceProjectionStore(IEnumerable<OptionContractProjectionRow> rows)
        {
            _rows = rows.ToDictionary(row => row.ContractSymbol, StringComparer.OrdinalIgnoreCase);
        }

        public Task<OptionContractProjectionRow?> GetContractAsync(string contractSymbol, CancellationToken ct = default)
        {
            _rows.TryGetValue(contractSymbol, out var row);
            return Task.FromResult(row);
        }

        public Task<IReadOnlyList<OptionContractProjectionRow>> GetSeriesContractsAsync(string optionChainId, DateOnly expiryDate, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OptionContractProjectionRow>>(
                _rows.Values
                    .Where(row => string.Equals(row.OptionChainId, optionChainId, StringComparison.OrdinalIgnoreCase) && row.ExpiryDate == expiryDate)
                    .ToArray());

        public Task<IReadOnlyList<OptionContractProjectionRow>> GetUnderlyingContractsAsync(string underlyingSymbol, DateOnly expiryDate, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OptionContractProjectionRow>>(
                _rows.Values
                    .Where(row => string.Equals(row.UnderlyingSymbol, underlyingSymbol, StringComparison.OrdinalIgnoreCase) && row.ExpiryDate == expiryDate)
                    .ToArray());

        public Task<IReadOnlyList<DateOnly>> GetExpiryLadderAsync(string underlyingSymbol, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DateOnly>>(
                _rows.Values
                    .Where(row => string.Equals(row.UnderlyingSymbol, underlyingSymbol, StringComparison.OrdinalIgnoreCase))
                    .Select(row => row.ExpiryDate)
                    .Distinct()
                    .OrderBy(date => date)
                    .ToArray());

        public Task UpsertChainSnapshotAsync(OptionChainSnapshot snapshot, CancellationToken ct = default)
        {
            var optionChainId = $"{snapshot.UnderlyingSymbol.Trim().ToUpperInvariant()}-{snapshot.Expiration:yyyyMMdd}";
            foreach (var call in snapshot.Calls)
            {
                UpsertQuote(snapshot, optionChainId, call, OptionRight.Call);
            }

            foreach (var put in snapshot.Puts)
            {
                UpsertQuote(snapshot, optionChainId, put, OptionRight.Put);
            }

            return Task.CompletedTask;
        }

        private void UpsertQuote(OptionChainSnapshot snapshot, string optionChainId, OptionQuote quote, OptionRight right)
        {
            var symbol = string.IsNullOrWhiteSpace(quote.Contract.OccSymbol)
                ? quote.Symbol
                : quote.Contract.OccSymbol!;
            var normalized = symbol.Trim().ToUpperInvariant();
            _rows[normalized] = new OptionContractProjectionRow(
                normalized,
                null,
                optionChainId,
                snapshot.UnderlyingSymbol.Trim().ToUpperInvariant(),
                null,
                right.ToString(),
                quote.Contract.Strike,
                quote.Contract.Expiration,
                quote.Contract.Multiplier,
                false,
                snapshot.Expiration,
                snapshot.Expiration < DateOnly.FromDateTime(DateTime.UtcNow) ? "Expired" : "Active",
                snapshot.Timestamp,
                _rows.TryGetValue(normalized, out var existing) ? existing.Version + 1 : 1);
        }
    }
}
