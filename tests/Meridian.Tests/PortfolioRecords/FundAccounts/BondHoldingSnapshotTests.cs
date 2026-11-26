using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.PortfolioRecords.Accounts;
using Meridian.PortfolioRecords.FundAccounts;

namespace Meridian.Tests.PortfolioRecords.FundAccounts;

public sealed class BondHoldingSnapshotTests
{
    [Fact]
    public void PrincipalFactorAndInflationApplyOnce()
    {
        var currentBasis = Economics(100_000m, .8m, 1.1m, 95m, 500m);
        var originalBasis = currentBasis with { QuoteBasis = BondHoldingQuoteBasis.PercentOfOriginalFace, Quote = 76m };
        Assert.Equal((83_600m, 84_100m), BondHoldingNormalization.Calculate(currentBasis, "USD"));
        Assert.Equal(BondHoldingNormalization.Calculate(currentBasis, "USD"), BondHoldingNormalization.Calculate(originalBasis, "USD"));
        Assert.Throws<ArgumentException>(() => BondHoldingNormalization.Calculate(currentBasis with { CurrentFace = 64_000m }, "USD"));
    }

    [Fact]
    public void DirtyQuoteDoesNotAddAccruedTwiceAndSignedAccruedRemainsSigned()
    {
        var clean = Economics(100m, 1m, 1m, 99m, -1m);
        Assert.Equal((99m, 98m), BondHoldingNormalization.Calculate(clean, "USD"));
        Assert.Equal((99m, 98m), BondHoldingNormalization.Calculate(clean with { Quote = 98m, QuoteIsDirty = true }, "USD"));
    }

    [Fact]
    public void NominalInvoiceDoesNotMultiplyInflationAlreadyIncludedInQuote()
    {
        var real = Economics(100m, .8m, 1.1m, 100m, 0m);
        var nominal = real with { Quote = 110m, InvoiceBasis = BondHoldingInvoiceBasis.Nominal };
        Assert.Equal((88m, 88m), BondHoldingNormalization.Calculate(real, "USD"));
        Assert.Equal(BondHoldingNormalization.Calculate(real, "USD"), BondHoldingNormalization.Calculate(nominal, "USD"));
        Assert.Throws<ArgumentException>(() => BondHoldingNormalization.Calculate(real with { InvoiceBasis = BondHoldingInvoiceBasis.Unknown }, "USD"));
    }

    [Theory]
    [InlineData("JPY", 100.4, 100.49)]
    [InlineData("KWD", 100.0004, 100.00049)]
    public void NativeCashBoundaryUsesCurrencyMinorUnits(string currency, double quote, double observed)
    {
        var economics = Economics(100m, 1m, 1m, (decimal)quote, 0m) with { CleanValue = (decimal)observed };
        BondHoldingNormalization.Calculate(economics, currency);
        Assert.Throws<ArgumentException>(() => BondHoldingNormalization.Calculate(economics with { CleanValue = observed > 101 ? 102m : 101m }, currency));
    }

    [Fact]
    public void MissingQuoteConventionCannotBecomeCleanPercentOfPar()
    {
        Assert.Throws<ArgumentException>(() => BondHoldingNormalization.Calculate(Economics(100m, 1m, 1m, 100m, 0m)
            with
        { QuoteBasis = BondHoldingQuoteBasis.Unknown }, "USD"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<BondHoldingEconomicsDto>(
            "{\"OriginalFace\":100,\"CurrentFace\":100,\"PrincipalFactor\":1,\"InflationRatio\":1,\"Quote\":100}"));
    }

    [Fact]
    public async Task RetryRetainsOriginalReceiptAndConflictingIdentityRefuses()
    {
        var (service, account) = await Service();
        var request = Statement(account, 1);
        var first = await service.IngestCustodianStatementAsync(request);
        var second = await service.IngestCustodianStatementAsync(request);
        Assert.Equal(first, second);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.IngestCustodianStatementAsync(request with
        {
            Lines = [request.Lines[0] with { CostBasis = 123m }]
        }));
        var snapshot = await service.GetCustodianSnapshotAsync(account, request.AsOfDate);
        Assert.Equal(95m, Assert.Single(snapshot!.Lines).CostBasis);
    }

    [Fact]
    public async Task RevisionReplacesEntirePopulationIncludingFlatAccount()
    {
        var (service, account) = await Service();
        var original = Statement(account, 1);
        await service.IngestCustodianStatementAsync(original);
        var replacement = Statement(account, 2) with { Lines = [], Authority = Authority(2, original.BatchId) };
        await service.IngestCustodianStatementAsync(replacement);
        var selected = await service.GetCustodianSnapshotAsync(account, original.AsOfDate.AddDays(1));
        Assert.Equal(replacement.BatchId, selected!.Batch.BatchId);
        Assert.Empty(selected.Lines);
        var source = new AccountBondHoldingsSource(service);
        Assert.Empty((await source.GetHoldingsAsync(account, original.AsOfDate))!.Positions);
    }

    [Fact]
    public async Task CompetingSameRevisionBlocksRatherThanUnionsHoldings()
    {
        var (service, account) = await Service();
        await service.IngestCustodianStatementAsync(Statement(account, 1));
        await service.IngestCustodianStatementAsync(Statement(account, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetCustodianSnapshotAsync(account, new(2026, 11, 12)));
    }

    [Fact]
    public async Task KnowledgeCutoffDoesNotUseLaterCorrectionsOrFutureHoldings()
    {
        var (service, account) = await Service();
        var original = Statement(account, 1);
        var first = await service.IngestCustodianStatementAsync(original);
        var next = Statement(account, 2);
        var corrected = next with
        {
            Authority = Authority(2, first.BatchId),
            Lines = [next.Lines[0] with { SecurityId = original.Lines[0].SecurityId, CostBasis = 123m }]
        };
        await service.IngestCustodianStatementAsync(corrected);
        var historical = await service.GetCustodianSnapshotAsync(account, first.AsOfDate, first.IngestedAt);
        Assert.Equal(first.BatchId, historical!.Batch.BatchId);
        Assert.Equal(original.Lines[0], Assert.Single(historical.Lines));
        Assert.Equal(123m, Assert.Single((await service.GetCustodianSnapshotAsync(account, first.AsOfDate))!.Lines).CostBasis);
        Assert.Null(await service.GetCustodianSnapshotAsync(account, first.AsOfDate.AddDays(-1)));
    }

    [Fact]
    public async Task AccountScopeAndLegacyAcceptanceRemainExplicit()
    {
        var (service, account) = await Service();
        var legacy = Statement(account, 1) with { Authority = null };
        await service.IngestCustodianStatementAsync(legacy);
        Assert.Null(await service.GetCustodianSnapshotAsync(account, legacy.AsOfDate));
        Assert.Null(await service.GetCustodianSnapshotAsync(Guid.NewGuid(), legacy.AsOfDate));
        await Assert.ThrowsAsync<NotSupportedException>(() => ((Meridian.Contracts.AssetOperations.IPortfolioHoldingsSource)
            new AccountBondHoldingsSource(service)).GetHoldingsAsync(legacy.AsOfDate));
    }

    [Fact]
    public async Task AcceptedSnapshotSurvivesRestartWithCostAndSourceEconomics()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meridian-bond-holdings-{Guid.NewGuid():N}.json");
        try
        {
            var (service, account) = await Service(path);
            var request = Statement(account, 1);
            var first = await service.IngestCustodianStatementAsync(request);
            var next = Statement(account, 2);
            var revised = next with
            {
                Authority = Authority(2, first.BatchId),
                Lines = [next.Lines[0] with { SecurityId = request.Lines[0].SecurityId, CostBasis = 123m }]
            };
            await service.IngestCustodianStatementAsync(revised);
            var restarted = new InMemoryFundAccountService(path);
            var selected = await restarted.GetCustodianSnapshotAsync(account, request.AsOfDate);
            Assert.Equal(revised.Lines.Single(), selected!.Lines.Single());
            Assert.Equal(revised.Lines.Single(), Assert.Single(await restarted.GetCustodianPositionsAsync(account, request.AsOfDate)));
            var original = await restarted.GetCustodianSnapshotAsync(account, request.AsOfDate, first.IngestedAt);
            Assert.Equal(request.Lines.Single(), original!.Lines.Single());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static BondHoldingEconomicsDto Economics(decimal original, decimal factor, decimal inflation, decimal quote, decimal accrued)
        => new(original, original * factor, factor, inflation, BondHoldingQuoteBasis.PercentOfCurrentFace, quote, false, accrued,
            InvoiceBasis: BondHoldingInvoiceBasis.IndexedPrincipal);

    private static CustodianStatementAuthorityDto Authority(int revision, Guid? prior = null)
        => new(revision, "synthetic-reviewed-evidence", new string('a', 64), SupersedesBatchId: prior);

    private static IngestCustodianStatementRequest Statement(Guid account, int revision)
    {
        var batch = Guid.NewGuid();
        var date = new DateOnly(2026, 11, 12);
        var economics = Economics(100m, 1m, 1m, 99m, 1m) with { SourceEvidenceId = "synthetic-source", OriginalSourceJson = "{}" };
        var line = new CustodianPositionLineDto(Guid.NewGuid(), batch, account, date, "synthetic-bond", "SecurityId",
            100m, 100m, "USD", "Synthetic bond", "Bond", false, 95m, Guid.NewGuid(), economics);
        return new(batch, account, date, "Synthetic custodian", "synthetic-v1", null, [line], "synthetic-reviewer", Authority: Authority(revision));
    }

    private static async Task<(InMemoryFundAccountService, Guid)> Service(string? path = null)
    {
        var service = new InMemoryFundAccountService(path);
        var account = Guid.NewGuid();
        await service.CreateAccountAsync(new(account, AccountTypeDto.Custody, $"BOND-{account:N}", "Synthetic bonds",
            "USD", DateTimeOffset.UtcNow.AddDays(-1), "synthetic"));
        return (service, account);
    }
}
