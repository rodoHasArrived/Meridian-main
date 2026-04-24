using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Application.Services;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using NSubstitute;
using Xunit;
using SecurityMasterQueryService = Meridian.Application.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.Application.Services;

public sealed class ReportGenerationServiceTests
{
    [Fact]
    public async Task GenerateAsync_WithEconomicDefinition_MapsGovernanceFieldsAndResolvedQuality()
    {
        var securityId = Guid.NewGuid();
        var query = new StubSecurityMasterQueryService(
            detailsBySymbol: new Dictionary<string, SecurityDetailDto>(StringComparer.OrdinalIgnoreCase)
            {
                ["AAPL"] = BuildDetail(securityId, "AAPL", "Equity")
            },
            economicsBySecurityId: new Dictionary<Guid, SecurityEconomicDefinitionRecord>
            {
                [securityId] = BuildEconomicDefinition(
                    securityId,
                    assetClass: "Equity",
                    assetFamily: "PublicEquity",
                    subType: "CommonStock",
                    issuerType: "Corporate",
                    riskCountry: "US",
                    primaryKind: SecurityIdentifierKind.Isin,
                    primaryValue: "US0378331005")
            });
        var service = new ReportGenerationService(query);
        var ledgerBook = BuildLedgerBookWithSymbols(["AAPL"]);

        var report = await service.GenerateAsync(new ReportRequest(
            FundId: "fund-1",
            AsOf: new DateTimeOffset(2026, 4, 11, 0, 0, 0, TimeSpan.Zero),
            FundLedger: ledgerBook));

        var row = report.TrialBalance.Single(r => string.Equals(r.Symbol, "AAPL", StringComparison.OrdinalIgnoreCase));
        row.PrimaryIdentifierKind.Should().Be(SecurityIdentifierKind.Isin.ToString());
        row.PrimaryIdentifierValue.Should().Be("US0378331005");
        row.SubType.Should().Be("CommonStock");
        row.AssetFamily.Should().Be("PublicEquity");
        row.IssuerType.Should().Be("Corporate");
        row.RiskCountry.Should().Be("US");
        row.LookupQuality.Should().Be("resolved");
    }

    [Fact]
    public async Task GenerateAsync_WithMissingEconomicDefinition_UsesPartialLookupQuality()
    {
        var securityId = Guid.NewGuid();
        var query = new StubSecurityMasterQueryService(
            detailsBySymbol: new Dictionary<string, SecurityDetailDto>(StringComparer.OrdinalIgnoreCase)
            {
                ["AAPL"] = BuildDetail(securityId, "AAPL", "Equity")
            },
            economicsBySecurityId: new Dictionary<Guid, SecurityEconomicDefinitionRecord>());
        var service = new ReportGenerationService(query);
        var ledgerBook = BuildLedgerBookWithSymbols(["AAPL"]);

        var report = await service.GenerateAsync(new ReportRequest(
            FundId: "fund-1",
            AsOf: new DateTimeOffset(2026, 4, 11, 0, 0, 0, TimeSpan.Zero),
            FundLedger: ledgerBook));

        var row = report.TrialBalance.Single(r => string.Equals(r.Symbol, "AAPL", StringComparison.OrdinalIgnoreCase));
        row.LookupQuality.Should().Be("partial");
        row.AssetFamily.Should().BeNull();
    }

    [Fact]
    public async Task GenerateAsync_GroupsByAssetFamilyThenAssetClassFallback()
    {
        var equitySecurityId = Guid.NewGuid();
        var creditSecurityId = Guid.NewGuid();
        var query = new StubSecurityMasterQueryService(
            detailsBySymbol: new Dictionary<string, SecurityDetailDto>(StringComparer.OrdinalIgnoreCase)
            {
                ["AAPL"] = BuildDetail(equitySecurityId, "AAPL", "Equity"),
                ["TLT"] = BuildDetail(creditSecurityId, "TLT", "FixedIncome")
            },
            economicsBySecurityId: new Dictionary<Guid, SecurityEconomicDefinitionRecord>
            {
                [equitySecurityId] = BuildEconomicDefinition(
                    equitySecurityId,
                    assetClass: "Equity",
                    assetFamily: "PublicEquity",
                    subType: "CommonStock",
                    issuerType: "Corporate",
                    riskCountry: "US",
                    primaryKind: SecurityIdentifierKind.Isin,
                    primaryValue: "US0378331005"),
                [creditSecurityId] = BuildEconomicDefinition(
                    creditSecurityId,
                    assetClass: "FixedIncome",
                    assetFamily: null,
                    subType: "Treasury",
                    issuerType: "Sovereign",
                    riskCountry: "US",
                    primaryKind: SecurityIdentifierKind.Isin,
                    primaryValue: "US912810TM09")
            });
        var service = new ReportGenerationService(query);
        var ledgerBook = BuildLedgerBookWithSymbols(["AAPL", "TLT"]);

        var report = await service.GenerateAsync(new ReportRequest(
            FundId: "fund-1",
            AsOf: new DateTimeOffset(2026, 4, 11, 0, 0, 0, TimeSpan.Zero),
            FundLedger: ledgerBook));

        report.AssetClassSections.Select(section => section.AssetClass).Should().Contain("PublicEquity");
        report.AssetClassSections.Select(section => section.AssetClass).Should().Contain("FixedIncome");
    }

    private static FundLedgerBook BuildLedgerBookWithSymbols(IReadOnlyList<string> symbols)
    {
        var ledgerBook = new FundLedgerBook("fund-1");
        var timestamp = new DateTimeOffset(2026, 4, 11, 14, 0, 0, TimeSpan.Zero);

        foreach (var symbol in symbols)
        {
            ledgerBook.FundLedger.PostLines(
                timestamp,
                $"Seed {symbol}",
                [
                    (new LedgerAccount($"Position {symbol}", LedgerAccountType.Asset, symbol), 100m, 0m),
                    (new LedgerAccount("Capital", LedgerAccountType.Equity), 0m, 100m)
                ]);
        }

        return ledgerBook;
    }

    private static SecurityDetailDto BuildDetail(Guid securityId, string symbol, string assetClass)
        => new(
            SecurityId: securityId,
            AssetClass: assetClass,
            Status: SecurityStatusDto.Active,
            DisplayName: symbol,
            Currency: "USD",
            CommonTerms: EmptyJsonElement(),
            AssetSpecificTerms: EmptyJsonElement(),
            Identifiers:
            [
                new SecurityIdentifierDto(
                    Kind: SecurityIdentifierKind.Ticker,
                    Value: symbol,
                    IsPrimary: true,
                    ValidFrom: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero))
            ],
            Aliases: [],
            Version: 1,
            EffectiveFrom: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EffectiveTo: null);

    private static SecurityEconomicDefinitionRecord BuildEconomicDefinition(
        Guid securityId,
        string assetClass,
        string? assetFamily,
        string subType,
        string issuerType,
        string riskCountry,
        SecurityIdentifierKind primaryKind,
        string primaryValue)
        => new(
            SecurityId: securityId,
            AssetClass: assetClass,
            AssetFamily: assetFamily,
            SubType: subType,
            TypeName: subType,
            IssuerType: issuerType,
            RiskCountry: riskCountry,
            Status: SecurityStatusDto.Active,
            DisplayName: "Security",
            Currency: "USD",
            Classification: EmptyJsonElement(),
            CommonTerms: EmptyJsonElement(),
            EconomicTerms: EmptyJsonElement(),
            Provenance: EmptyJsonElement(),
            Version: 1,
            EffectiveFrom: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EffectiveTo: null,
            Identifiers:
            [
                new SecurityIdentifierDto(
                    Kind: primaryKind,
                    Value: primaryValue,
                    IsPrimary: true,
                    ValidFrom: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero))
            ],
            LegacyAssetClass: null,
            LegacyAssetSpecificTerms: null);

    private static JsonElement EmptyJsonElement() => JsonDocument.Parse("{}").RootElement.Clone();

    private sealed class StubSecurityMasterQueryService : SecurityMasterQueryService
    {
        private readonly IReadOnlyDictionary<string, SecurityDetailDto> _detailsBySymbol;
        private readonly IReadOnlyDictionary<Guid, SecurityEconomicDefinitionRecord> _economicsBySecurityId;

        public StubSecurityMasterQueryService(
            IReadOnlyDictionary<string, SecurityDetailDto> detailsBySymbol,
            IReadOnlyDictionary<Guid, SecurityEconomicDefinitionRecord> economicsBySecurityId)
        {
            _detailsBySymbol = detailsBySymbol;
            _economicsBySecurityId = economicsBySecurityId;
        }

        public Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<SecurityDetailDto?>(_detailsBySymbol.Values.FirstOrDefault(detail => detail.SecurityId == securityId));

        public Task<SecurityDetailDto?> GetByIdentifierAsync(SecurityIdentifierKind identifierKind, string identifierValue, string? provider, CancellationToken ct = default)
            => Task.FromResult(_detailsBySymbol.TryGetValue(identifierValue, out var detail) ? detail : null);

        public Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecuritySummaryDto>>([]);

        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(SecurityHistoryRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>([]);

        public Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult(_economicsBySecurityId.TryGetValue(securityId, out var definition) ? definition : null);

        public Task<TradingParametersDto?> GetTradingParametersAsync(Guid securityId, DateTimeOffset asOf, CancellationToken ct = default)
            => Task.FromResult<TradingParametersDto?>(null);

        public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CorporateActionDto>>([]);

        public Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<PreferredEquityTermsDto?>(null);

        public Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<ConvertibleEquityTermsDto?>(null);
    }
    [Fact]
    public async Task GenerateAsync_WithMissingSecurityMetadata_UsesDeterministicUnclassifiedFallback()
    {
        var securityMaster = Substitute.For<SecurityMasterQueryService>();
        securityMaster
            .GetByIdentifierAsync(SecurityIdentifierKind.Ticker, "UNKN", null, Arg.Any<CancellationToken>())
            .Returns(new SecurityDetailDto(
                SecurityId: Guid.NewGuid(),
                AssetClass: null!,
                Status: SecurityStatusDto.Active,
                DisplayName: null!,
                Currency: null!,
                CommonTerms: JsonSerializer.SerializeToElement(new { }),
                AssetSpecificTerms: JsonSerializer.SerializeToElement(new { }),
                Identifiers: [],
                Aliases: [],
                Version: 1,
                EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-10),
                EffectiveTo: null));

        var service = new ReportGenerationService(securityMaster);
        var book = BuildFundLedger("fund-report-1", ("UNKN", 125m));

        var report = await service.GenerateAsync(new ReportRequest(
            FundId: "fund-report-1",
            AsOf: new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero),
            FundLedger: book));

        report.TrialBalance.Should().ContainSingle();
        var row = report.TrialBalance.Single();
        row.Symbol.Should().Be("UNKN");
        row.AssetClass.Should().BeNull();
        row.DisplayName.Should().BeNull();
        row.Currency.Should().BeNull();

        report.AssetClassSections.Should().ContainSingle();
        report.AssetClassSections[0].AssetClass.Should().Be("Unclassified");
        report.AssetClassSections[0].Rows.Should().ContainSingle(r => r.Symbol == "UNKN");
    }

    [Fact]
    public async Task GenerateAsync_PreservesIdentifierMappedRows_AndNullHandlingAcrossMultipleSymbols()
    {
        var securityMaster = Substitute.For<SecurityMasterQueryService>();
        securityMaster
            .GetByIdentifierAsync(SecurityIdentifierKind.Ticker, "AAPL", null, Arg.Any<CancellationToken>())
            .Returns(BuildDetail("Equity", "Apple Inc.", "USD"));
        securityMaster
            .GetByIdentifierAsync(SecurityIdentifierKind.Ticker, "CUSIP_037833100", null, Arg.Any<CancellationToken>())
            .Returns(BuildDetail("Equity", "Apple Legacy Line", null));

        var service = new ReportGenerationService(securityMaster);
        var book = BuildFundLedger(
            "fund-report-2",
            ("AAPL", 300m),
            ("CUSIP_037833100", 50m));

        var report = await service.GenerateAsync(new ReportRequest(
            FundId: "fund-report-2",
            AsOf: new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero),
            FundLedger: book));

        report.TrialBalance.Should().HaveCount(2);
        report.TrialBalance.Should().Contain(row =>
            row.Symbol == "AAPL" &&
            row.AssetClass == "Equity" &&
            row.DisplayName == "Apple Inc." &&
            row.Currency == "USD");
        report.TrialBalance.Should().Contain(row =>
            row.Symbol == "CUSIP_037833100" &&
            row.AssetClass == "Equity" &&
            row.DisplayName == "Apple Legacy Line" &&
            row.Currency == null);
    }

    private static FundLedgerBook BuildFundLedger(string fundId, params (string Symbol, decimal Balance)[] rows)
    {
        var book = new FundLedgerBook(fundId);
        var asOf = new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero);

        foreach (var (symbol, balance) in rows)
        {
            var securityAccount = new LedgerAccount($"Security:{symbol}", LedgerAccountType.Asset, symbol);
            var offsetAccount = new LedgerAccount($"Offset:{symbol}", LedgerAccountType.Equity);
            book.FundLedger.PostLines(asOf, $"seed-{symbol}", [(securityAccount, balance, 0m), (offsetAccount, 0m, balance)]);
        }

        return book;
    }

    private static SecurityDetailDto BuildDetail(string assetClass, string displayName, string? currency)
        => new(
            SecurityId: Guid.NewGuid(),
            AssetClass: assetClass,
            Status: SecurityStatusDto.Active,
            DisplayName: displayName,
            Currency: currency,
            CommonTerms: JsonSerializer.SerializeToElement(new { }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new { }),
            Identifiers: [],
            Aliases: [],
            Version: 1,
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-5),
            EffectiveTo: null);
}
