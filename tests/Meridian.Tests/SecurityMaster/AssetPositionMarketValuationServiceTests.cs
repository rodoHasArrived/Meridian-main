using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster.Valuation;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.AssetOperations;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Unit")]
public sealed class AssetPositionMarketValuationServiceTests
{
    [Fact]
    public async Task ValuesRetainedFractionalUnitsThroughRegisteredKernel_AndPreservesExactDecimalStrings()
    {
        var f = new AssetPositionValuationFixture();
        var result = await f.ValueAsync();
        result.Status.Should().Be("Ready");
        result.MarketValue.Should().Be(24.691357802469135780m);
        result.UnrealizedPnl.Should().Be(4.691357802469135780m);
        result.UnrealizedPnlBasis.Should().Contain("mark-to-carrying");
        result.SelectionReceiptId.Should().Be(f.Selection.SelectionReceiptId);
        result.PriceUnit.Should().Be(SecurityPriceUnit.CurrencyPerUnit);
        result.Inputs.Should().Contain(input => input.Kind == "SecurityTerms" && input.Version == f.Security.Version);
        var json = JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.GetProperty("marketValue").ValueKind.Should().Be(JsonValueKind.String);
        json.GetProperty("unrealizedPnl").GetString().Should().Be("4.691357802469135780");
        await f.Pricing.Received(1).GetGoldenCopyPriceAsOfAsync(f.Security.SecurityId, f.Request.AccountId,
            f.AsOf, Arg.Any<CancellationToken>(), null);
        await f.Store.DidNotReceive().GetSecurityAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadsExactlyTheScopedDatedProjection_AndBlocksChangedEconomicStateBeforePricing()
    {
        var f = new AssetPositionValuationFixture();
        var result = await f.Service.ValueAsync(f.Request with { EconomicStateVersion = 99 }, f.Book);
        result.Blockers.Should().Contain(blocker => blocker.Code == "EconomicStateChanged");
        result.MarketValue.Should().BeNull();
        await f.Store.Received(1).GetAsOfAsync(f.Request.SecurityId, f.Request.LedgerBookId, f.Request.ValuationDate, Arg.Any<CancellationToken>());
        await f.Pricing.DidNotReceive().GetGoldenCopyPriceAsOfAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>());
    }

    [Theory]
    [InlineData("account")]
    [InlineData("book-owner")]
    [InlineData("currency")]
    [InlineData("short")]
    [InlineData("ambiguous")]
    [InlineData("state-date")]
    [InlineData("foreign-source")]
    public async Task RefusesUnprovenPositionScopeAndConventions(string mismatch)
    {
        var f = new AssetPositionValuationFixture();
        var position = f.Position;
        var state = f.State;
        if (mismatch == "account")
            position = position with { PrimaryAccountId = "foreign-account" };
        if (mismatch == "book-owner")
            position = position with { BookContext = position.BookContext with { FundProfileId = "foreign-fund" } };
        if (mismatch == "currency")
            state = state with { Currency = "EUR" };
        if (mismatch == "short")
            position = position with { PositionSide = BookPositionSides.Short };
        if (mismatch == "state-date")
            state = state with { AsOfDate = f.Request.ValuationDate.AddDays(-1) };
        if (mismatch == "foreign-source")
            state = state with { SourceEvent = new EconomicEventReferenceDto(Guid.NewGuid(), "Source", 1, state.AsOfDate, f.AsOf, "test") { SecurityId = Guid.NewGuid() } };
        position = position with { CurrentEconomicState = state };
        f.Snapshot = f.Snapshot with { BookPositions = mismatch == "ambiguous" ? [position, position] : [position], PositionEconomicStates = [state] };
        var result = await f.ValueAsync();
        result.Status.Should().Be("Blocked");
        result.MarketValue.Should().BeNull();
        result.UnrealizedPnl.Should().BeNull();
        result.Blockers.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("Option")]
    [InlineData("Future")]
    [InlineData("Swap")]
    [InlineData("UnknownAsset")]
    public async Task NeverSubstitutesShareArithmeticForUnprovenContractConventions(string assetClass)
    {
        var f = new AssetPositionValuationFixture();
        f.Security = f.Security with { AssetClass = assetClass };
        var result = await f.ValueAsync();
        result.Blockers.Should().Contain(blocker => blocker.Code == "UnsupportedLotConvention");
        result.MarketValue.Should().BeNull();
    }

    [Theory]
    [InlineData("Deposit")]
    [InlineData("Repo")]
    public async Task BalanceQuotedClasses_AreNotPricedFromAQuote(string assetClass)
    {
        // Deposit and Repo are valued at balance plus accrued (quote convention Balance); the kernel
        // has no balance adapter, so it must not fall back to percent-of-par price arithmetic.
        var f = new AssetPositionValuationFixture();
        f.Security = f.Security with { AssetClass = assetClass };
        var result = await f.ValueAsync();
        result.Blockers.Should().Contain(blocker => blocker.Code == "BalanceValuationUnavailable");
        result.MarketValue.Should().BeNull();
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("missing-receipt")]
    [InlineData("changed-receipt")]
    [InlineData("unspecified-unit")]
    public async Task RequiresReplayableTypedFreshPriceEvidence(string failure)
    {
        var f = new AssetPositionValuationFixture();
        if (failure == "stale")
            f.Selection = f.Selection with { IsStaleFallback = true, DaysStale = 90 };
        if (failure == "missing-receipt")
            f.Replay = null;
        if (failure == "changed-receipt")
            f.Replay = f.Selection with { GoldenCopyPrice = 99m };
        if (failure == "unspecified-unit")
            f.Selection = f.Selection with { Unit = SecurityPriceUnit.Unspecified };
        var result = await f.ValueAsync();
        result.Status.Should().Be("Blocked");
        result.MarketValue.Should().BeNull();
        result.UnrealizedPnl.Should().BeNull();
        if (failure == "stale")
            result.FreshnessStatus.Should().Be("Stale");
    }

    [Fact]
    public async Task PreservesDeclaredCleanQuotationAndSettlementEvidenceWithoutReinterpretingUnits()
    {
        var f = new AssetPositionValuationFixture();
        f.Selection = f.Selection with
        {
            Quotation = new SecurityPriceQuotationEvidence(SecurityPriceBasis.Clean,
                SecurityQuotationConvention.CurrencyPerUnit, SecurityQuoteStatus.Evaluated,
                f.Request.ValuationDate.AddDays(1), "Retained regular settlement")
        };
        var result = await f.ValueAsync();
        result.Status.Should().Be("Ready", "the declared currency-per-unit quotation is consistent: {0}", JsonSerializer.Serialize(result.Blockers));
        result.MarketValue.Should().Be(24.691357802469135780m);
        result.Diagnostics.Should().Contain(item => item.Contains("Clean; CurrencyPerUnit; Evaluated")
            && item.Contains("2026-01-02") && item.Contains("Retained regular settlement"));
    }

    [Theory]
    [InlineData("dirty")]
    [InlineData("unit-conflict")]
    [InlineData("missing-settlement")]
    public async Task RefusesDirtyOrInconsistentDeclaredQuotationEvidence(string failure)
    {
        var f = new AssetPositionValuationFixture();
        f.Selection = f.Selection with
        {
            Quotation = new SecurityPriceQuotationEvidence(
                failure == "dirty" ? SecurityPriceBasis.Dirty : SecurityPriceBasis.Clean,
                failure == "unit-conflict" ? SecurityQuotationConvention.Per100Face : SecurityQuotationConvention.CurrencyPerUnit,
                SecurityQuoteStatus.Evaluated, failure == "missing-settlement" ? default : f.Request.ValuationDate,
                "Retained settlement")
        };
        var result = await f.ValueAsync();
        result.Status.Should().Be("Blocked");
        result.MarketValue.Should().BeNull();
        result.UnrealizedPnl.Should().BeNull();
        result.Blockers.Should().Contain(blocker => blocker.Code == (failure == "dirty"
            ? "PriceBasisRequiresNormalization" : "QuotationEvidenceMismatch"));
    }

    [Fact]
    public async Task MissingCarryingAmountDoesNotBecomeAnInventedCostBasis()
    {
        var f = new AssetPositionValuationFixture();
        var state = f.State with { CarryingAmount = null };
        f.Snapshot = f.Snapshot with { BookPositions = [f.Position with { CurrentEconomicState = state }], PositionEconomicStates = [state] };
        var result = await f.ValueAsync();
        result.Status.Should().Be("Partial");
        result.MarketValue.Should().NotBeNull();
        result.UnrealizedPnl.Should().BeNull();
        result.Blockers.Should().Contain(blocker => blocker.Metric == "UnrealizedPnl" && blocker.Code == "CarryingAmountUnavailable");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FaceValueUsesRetainedOriginalFaceOnce_AndDoesNotCompareDirtyValueToUnspecifiedCarryingBasis(bool inconsistentFace)
    {
        var f = new AssetPositionValuationFixture();
        f.Security = ValuationTestSecurities.Bond(f.Security.SecurityId);
        var state = f.State with { Quantity = 7m, OriginalFaceAmount = 1000m, CurrentFaceAmount = inconsistentFace ? 600m : 500m, CurrentFactor = 0.5m, CarryingAmount = 450m };
        f.Snapshot = f.Snapshot with { BookPositions = [f.Position with { CurrentEconomicState = state }], PositionEconomicStates = [state] };
        var dcf = Substitute.For<ISecurityValuationModel>();
        dcf.Kind.Returns(SecurityValuationModelKind.DiscountedCashFlow);
        dcf.ValueAsync(Arg.Any<SecurityValuationRequest>(), Arg.Any<SecurityDetailDto>(), Arg.Any<CancellationToken>())
            .Returns(call => new SecurityValuationResult(f.Request.SecurityId, SecurityValuationModelKind.DiscountedCashFlow,
                "discounted-cash-flow", "v2", f.AsOf, f.AsOf, "USD", 102m, SecurityPriceUnit.PercentOfPar,
                510m, call.Arg<SecurityValuationRequest>().Quantity, 510m, f.AsOf.AddDays(-1), SecurityValuationObservability.ObservableInputs,
                [new SecurityValuationInputDto("DiscountCurveVersion", "USD-OIS", Guid.NewGuid(), 1)], ["DirtyValue: full coupon"], []));
        var service = new AssetPositionMarketValuationService(f.Store, f.Query, [dcf], f.Pricing);
        var result = await service.ValueAsync(f.Request, f.Book);
        if (inconsistentFace)
        {
            result.Status.Should().Be("Blocked");
            result.MarketValue.Should().BeNull();
            result.Blockers.Should().Contain(blocker => blocker.Code == "OutstandingFaceMismatch");
            return;
        }
        result.Status.Should().Be("Partial");
        result.Quantity.Should().Be(1000m);
        result.MarketValue.Should().Be(510m);
        result.UnrealizedPnl.Should().BeNull();
        result.CalculationBasis.Should().Contain("factor applied once").And.Contain("dirty");
        await dcf.Received(1).ValueAsync(Arg.Is<SecurityValuationRequest>(request => request.Quantity == 1000m), Arg.Any<SecurityDetailDto>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("future-effective")]
    [InlineData("missing-retained")]
    public async Task NeverValuesCurrentProjectionOnlyOrFutureEffectiveTerms(string failure)
    {
        var f = new AssetPositionValuationFixture();
        if (failure == "future-effective")
            f.Security = f.Security with { EffectiveFrom = f.AsOf.AddDays(1) };
        else
            f.Query.GetRecordedByIdAsOfAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns((SecurityDetailDto?)null);
        var result = await f.ValueAsync();
        result.Status.Should().Be("Blocked");
        result.MarketValue.Should().BeNull();
        result.Blockers.Should().Contain(blocker => blocker.Code == (failure == "future-effective" ? "SecurityTermsNotEffective" : "SecurityUnavailable"));
        await f.Query.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HistoricalKnowledgeCannotBeClaimedFromCurrentProjectionHistory()
    {
        var f = new AssetPositionValuationFixture();
        var result = await f.Service.ValueAsync(f.Request with { KnownAt = f.AsOf }, f.Book);
        result.Blockers.Should().Contain(blocker => blocker.Code == "HistoricalKnowledgeUnavailable");
        await f.Store.DidNotReceive().GetAsOfAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }
}

internal sealed class AssetPositionValuationFixture
{
    public readonly IInstrumentPositionProjectionStore Store = Substitute.For<IInstrumentPositionProjectionStore>();
    public readonly ISecurityMasterQueryService Query = Substitute.For<ISecurityMasterQueryService>();
    public readonly ISecurityMasterPricingService Pricing = Substitute.For<ISecurityMasterPricingService>();
    public readonly LedgerBookDto Book;
    public readonly BookPositionDto Position;
    public readonly PositionEconomicStateDto State;
    public readonly AssetPositionMarketValuationRequest Request;
    public readonly DateTimeOffset AsOf;
    public SecurityDetailDto Security;
    public SecurityPriceGoldenCopyDto Selection;
    private SecurityPriceGoldenCopyDto? _replayOverride;
    private bool _hasReplayOverride;
    public SecurityPriceGoldenCopyDto? Replay { get => _hasReplayOverride ? _replayOverride : Selection; set { _hasReplayOverride = true; _replayOverride = value; } }
    public InstrumentPositionProjectionSnapshot Snapshot;
    public AssetPositionMarketValuationService Service { get; }

    public AssetPositionValuationFixture()
    {
        var date = new DateOnly(2026, 1, 1);
        AsOf = new DateTimeOffset(date.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        Security = ValuationTestSecurities.Equity();
        Book = new LedgerBookDto(Guid.NewGuid(), "fund-a", Guid.NewGuid(), FundStructureNodeKindDto.Account,
            "Owned book", "USD", AsOf, AsOf);
        var role = new InstrumentRoleDto(Guid.NewGuid(), Security.SecurityId, Book.FundProfileId, "Account", "Holder", "Debit", "Asset", date.AddYears(-1));
        var positionId = Guid.NewGuid();
        State = new PositionEconomicStateDto(Guid.NewGuid(), positionId, date, "USD", 3, Quantity: 2m, CarryingAmount: 20m);
        Position = new BookPositionDto(positionId, Security.SecurityId, role.RoleId,
            new AccountingBookContextDto(Book.LedgerBookId, Book.FundProfileId, Book.FundStructureNodeId, Book.FundStructureNodeKind,
                Book.DisplayName, Book.BaseCurrency, Book.AccountingBasis, Book.AccountingPolicyId, Book.AccountingPolicyVersion),
            "Long", "Open", date.AddYears(-1), Version: 3, PrimaryAccountId: "account-a", CurrentEconomicState: State);
        Request = new AssetPositionMarketValuationRequest(Security.SecurityId, positionId, Book.LedgerBookId, "account-a", date, State.EconomicStateId, State.Version);
        Snapshot = new InstrumentPositionProjectionSnapshot(Security.SecurityId, [role], [Position], [State], []) { LedgerBookId = Book.LedgerBookId, AsOfDate = date };
        Selection = new SecurityPriceGoldenCopyDto(Security.SecurityId, 12.345678901234567890m, SecurityPriceKind.MarketGoldenCopy,
            "Retained vendor", AsOf.AddHours(-4), false, 0, [], SecurityPriceUnit.CurrencyPerUnit, AsOf, AsOf.AddDays(-1), AsOf, Guid.NewGuid());
        Store.GetAsOfAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(_ => Snapshot);
        Query.GetRecordedByIdAsOfAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(_ => Security);
        Pricing.GetGoldenCopyPriceAsOfAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>()).Returns(_ => Selection);
        Pricing.GetGoldenCopySelectionAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => Replay);
        Service = new AssetPositionMarketValuationService(Store, Query, [new QuotedPriceValuationModel(Pricing)], Pricing);
    }

    public Task<AssetPositionMarketValuationDto> ValueAsync() => Service.ValueAsync(Request, Book);
}
