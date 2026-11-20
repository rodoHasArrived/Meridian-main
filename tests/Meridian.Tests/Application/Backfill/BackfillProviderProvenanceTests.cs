using System.Text.Json;
using FluentAssertions;
using Meridian.Application.UI;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Config;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage.Backfill;
using Meridian.Tests.Infrastructure;
using Xunit;
using Coordinator = Meridian.Application.Backfill.BackfillCoordinator;
using Request = Meridian.Application.Backfill.BackfillRequest;

namespace Meridian.Tests.Application.Backfill;

public sealed class BackfillProviderProvenanceTests : TempDirectoryTestBase
{
    private static readonly DateOnly Session = new(2024, 1, 3);
    private const string Tenant = "receipt-test-tenant";
    private const string VendorError = "vendor payload with test-key=not-for-export";

    [Fact]
    public async Task DirectAndFallbackReceiptsSurviveReloadAndAreVisibleOnlyWithinScope()
    {
        var (config, primary, fallback, registry) = await CreateProvidersAsync();
        using var coordinator = new Coordinator(config, registry);

        var direct = await coordinator.RunAsync(new Request(fallback.Name, ["SPY"], Session, Session), Tenant, default);
        var routed = await coordinator.RunAsync(new Request("composite", ["SPY"], Session, Session), Tenant, default);
        var store = new BackfillStatusStore(TestDataRoot);
        var receipt = store.TryReadRetainedResult(routed.ProviderProvenance!.ReceiptId)!.Receipt;

        direct.ProviderProvenance!.FallbackOccurred.Should().BeFalse();
        receipt.ReceiptId.Should().NotBe(direct.ProviderProvenance.ReceiptId);
        receipt.FallbackOccurred.Should().BeTrue();
        receipt.FallbackChain.Should().Equal(primary.Name, fallback.Name);
        receipt.Providers[0].Unavailable.Should().Be(1);
        receipt.Providers[1].ResponsesWithData.Should().Be(1);
        receipt.Providers[1].StaleToleranceDays.Should().Be(14);
        direct.ProviderProvenance.ValidationProfile.StaleToleranceDays.Should().Be(14);
        receipt.Request.Symbols.Should().Equal("SPY");
        receipt.ValidationProfile.StaleToleranceDays.Should().Be(BackfillBarValidation.DefaultStaleToleranceDays);
        JsonSerializer.Serialize(receipt).Should().NotContain(VendorError).And.NotContain(Tenant);

        var rows = store.ReadRecentRetainedResults(10, Tenant);
        rows.Should().HaveCount(2);
        store.ReadRecentRetainedResults(10, Tenant, rows[0].Receipt.ReceiptId).Should().ContainSingle();
        store.ReadRecentRetainedResults(10, "different-tenant").Should().BeEmpty();
        store.TryRead()!.ForTenant("different-tenant").ProviderProvenance.Should().BeNull();
        store.TryRead()!.ForTenant("different-tenant").QuotaReceipt.Should().BeNull();
        (await coordinator.GetClonePlanAsync(receipt.ReceiptId, "different-tenant")).Should().BeNull();

        var retained = store.TryReadRetainedResult(receipt.ReceiptId)!;
        await store.WriteRetainedResultAsync(retained);
        var rewrite = () => store.WriteRetainedResultAsync(retained with { BarsWritten = retained.BarsWritten + 1 });
        await rewrite.Should().ThrowAsync<IOException>();
        store.TryReadRetainedResult(receipt.ReceiptId)!.BarsWritten.Should().Be(retained.BarsWritten);
    }

    [Fact]
    public async Task CloneRechecksHealthAndBlocksLostConfigurationWithoutChangingOriginalReceipt()
    {
        var (config, primary, fallback, registry) = await CreateProvidersAsync();
        using var coordinator = new Coordinator(config, registry);
        var original = await coordinator.RunAsync(new Request("composite", ["SPY"], Session, Session,
            MaxConcurrentSymbols: 2), Tenant, default);
        var source = original.ProviderProvenance!;
        var oldPlan = (await coordinator.GetClonePlanAsync(source.ReceiptId, Tenant))!;
        oldPlan.Status.Should().Be("ReconfirmationRequired");
        oldPlan.Request.MaxConcurrentSymbols.Should().Be(2);
        oldPlan.Request.Symbols.Should().Equal("SPY");

        primary.Available = true;
        var stale = () => coordinator.RunCloneAsync(source.ReceiptId, oldPlan.ConfirmationCode!, Tenant);
        var conflict = await stale.Should().ThrowAsync<Meridian.Application.Backfill.BackfillCloneConflictException>();
        var clone = await coordinator.RunCloneAsync(source.ReceiptId, conflict.Which.Plan.ConfirmationCode!, Tenant);
        clone.ProviderProvenance!.ReceiptId.Should().NotBe(source.ReceiptId);
        fallback.LastRequested.Should().Be(("SPY", (DateOnly?)Session, (DateOnly?)Session));

        var originalConfig = config.Load();
        await config.SaveAsync(originalConfig with
        {
            Backfill = originalConfig.Backfill! with
            {
                ValidationProfiles = new Dictionary<string, ProviderValidationProfile>
                {
                    ["yahoo"] = new(StaleToleranceDays: 21)
                }
            }
        });
        var changedPolicy = await coordinator.GetClonePlanAsync(source.ReceiptId, Tenant);
        changedPolicy!.Status.Should().Be("Unavailable");
        changedPolicy.ConfirmationCode.Should().BeNull();

        File.Delete(config.ConfigPath);
        var unavailable = await coordinator.GetClonePlanAsync(source.ReceiptId, Tenant);
        unavailable!.Status.Should().Be("Unavailable");
        unavailable.ConfirmationCode.Should().BeNull();
        new BackfillStatusStore(TestDataRoot).TryReadRetainedResult(source.ReceiptId)!.Receipt.FallbackOccurred.Should().BeTrue();
    }

    private async Task<(ConfigStore Config, TestProvider Primary, TestProvider Fallback, ProviderRegistry Registry)> CreateProvidersAsync()
    {
        var config = new ConfigStore(Path.Combine(TestDataRoot, "appsettings.json"));
        await config.SaveAsync(new AppConfig(DataRoot: TestDataRoot, Backfill: new BackfillConfig(
            EnableSymbolResolution: false,
            ValidationProfiles: new Dictionary<string, ProviderValidationProfile>
            {
                ["yahoo"] = new(StaleToleranceDays: 14)
            })));
        var primary = new TestProvider("stooq", 1) { Available = false };
        var fallback = new TestProvider("yahoo", 2);
        var registry = new ProviderRegistry();
        registry.Register(primary);
        registry.Register(fallback);
        return (config, primary, fallback, registry);
    }

    private sealed class TestProvider(string name, int priority) : IHistoricalDataProvider
    {
        public string Name => name;
        public string DisplayName => name;
        public string Description => string.Empty;
        public int Priority => priority;
        public bool Available { get; set; } = true;
        public (string Symbol, DateOnly? From, DateOnly? To) LastRequested { get; private set; }

        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(Available);

        public Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(string symbol, DateOnly? from, DateOnly? to,
            CancellationToken ct = default)
        {
            LastRequested = (symbol, from, to);
            if (!Available)
                throw new InvalidOperationException(VendorError);
            return Task.FromResult<IReadOnlyList<HistoricalBar>>([new(symbol, Session, 10, 10, 10, 10, 10, name)]);
        }
    }
}
