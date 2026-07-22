using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Meridian.Reporting;
using NSubstitute;
using Xunit;
using SecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.Reporting;

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

    [Fact]
    public async Task GenerateAsync_WithLineDimensions_RetainsCanonicalLedgerDimensionEnvelope()
    {
        var securityId = Guid.NewGuid();
        var instrumentId = Guid.Parse("0f92e649-013f-4e7f-99bf-2b14396701e8");
        var positionId = Guid.Parse("5e78f0cb-8412-4c6e-9a88-d9a64b5c3f0d");
        var query = new StubSecurityMasterQueryService(
            detailsBySymbol: new Dictionary<string, SecurityDetailDto>(StringComparer.OrdinalIgnoreCase)
            {
                ["AAPL"] = BuildDetail(securityId, "AAPL", "Equity")
            },
            economicsBySecurityId: new Dictionary<Guid, SecurityEconomicDefinitionRecord>());
        var service = new ReportGenerationService(query);
        var dimensions = new LedgerLineDimensionSet(
            FundId: "fund-1",
            EntityId: "entity-master",
            SleeveId: "sleeve-credit",
            StrategyId: "strategy-income",
            InvestorId: "investor-lp",
            CapitalAccountId: "capital-account-alpha",
            InstrumentId: instrumentId,
            TaxLotId: "tax-lot-alpha",
            CostCenterId: "fund-accounting",
            CounterpartyId: "administrator",
            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Department"] = "FundAccounting"
            },
            OrganizationId: "organization-alpha",
            PortfolioId: "portfolio-credit",
            BookId: "book-gaap",
            AccountId: "account-investments",
            CustomerId: "customer-alpha",
            VendorId: "vendor-admin",
            ProjectId: "project-close")
        {
            PositionId = positionId
        };
        var ledgerBook = new FundLedgerBook("fund-1");
        ledgerBook.FundLedger.PostLines(
            new DateTimeOffset(2026, 4, 10, 14, 0, 0, TimeSpan.Zero),
            "Dimensioned investment activity",
            [
                (new LedgerAccount("Position AAPL", LedgerAccountType.Asset, "AAPL"), 100m, 0m, dimensions),
                (new LedgerAccount("Capital", LedgerAccountType.Equity), 0m, 100m, dimensions)
            ]);

        var report = await service.GenerateAsync(new ReportRequest(
            FundId: "fund-1",
            AsOf: new DateTimeOffset(2026, 4, 11, 0, 0, 0, TimeSpan.Zero),
            FundLedger: ledgerBook));

        var row = report.TrialBalance.Single(r => string.Equals(r.Symbol, "AAPL", StringComparison.OrdinalIgnoreCase));
        row.Dimensions.Should().BeEquivalentTo(new LedgerDimensionSetDto(
            FundId: "fund-1",
            EntityId: "entity-master",
            SleeveId: "sleeve-credit",
            StrategyId: "strategy-income",
            InvestorId: "investor-lp",
            CapitalAccountId: "capital-account-alpha",
            InstrumentId: instrumentId,
            TaxLotId: "tax-lot-alpha",
            CostCenterId: "fund-accounting",
            CounterpartyId: "administrator",
            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Department"] = "FundAccounting"
            },
            OrganizationId: "organization-alpha",
            PortfolioId: "portfolio-credit",
            BookId: "book-gaap",
            AccountId: "account-investments",
            CustomerId: "customer-alpha",
            VendorId: "vendor-admin",
            ProjectId: "project-close")
        {
            PositionId = positionId
        });
    }

    [Fact]
    public async Task GenerateAsync_UsesOneAsOfBoundaryForBalancesDimensionsAndReceipt()
    {
        var securityId = Guid.NewGuid();
        var query = new StubSecurityMasterQueryService(
            new Dictionary<string, SecurityDetailDto>(StringComparer.OrdinalIgnoreCase)
            {
                ["AAPL"] = BuildDetail(securityId, "AAPL", "Equity")
            },
            new Dictionary<Guid, SecurityEconomicDefinitionRecord>
            {
                [securityId] = BuildEconomicDefinition(
                    securityId,
                    "Equity",
                    "PublicEquity",
                    "CommonStock",
                    "Corporate",
                    "US",
                    SecurityIdentifierKind.Isin,
                    "US0378331005")
            });
        var service = new ReportGenerationService(query);
        var asOf = new DateTimeOffset(2026, 4, 11, 12, 0, 0, TimeSpan.Zero);
        var pastDimensions = new LedgerLineDimensionSet(EntityId: "entity-as-of");
        var futureDimensions = new LedgerLineDimensionSet(EntityId: "entity-future");
        var position = new LedgerAccount("Position AAPL", LedgerAccountType.Asset, "AAPL");
        var capital = new LedgerAccount("Capital", LedgerAccountType.Equity);
        var ledgerBook = new FundLedgerBook("fund-1");
        ledgerBook.FundLedger.PostLines(
            asOf.AddHours(-1),
            "As-of activity",
            [
                (position, 100m, 0m, pastDimensions),
                (capital, 0m, 100m, pastDimensions)
            ]);
        ledgerBook.FundLedger.PostLines(
            asOf.AddHours(1),
            "Future activity",
            [
                (position, 75m, 0m, futureDimensions),
                (capital, 0m, 75m, futureDimensions)
            ]);

        var report = await service.GenerateAsync(new ReportRequest(
            "fund-1",
            asOf,
            ledgerBook,
            SnapshotSource: new ReportingSnapshotSourceContext(
                "durable-ledger",
                "global-sequence:17",
                IsAuthoritative: true)));

        var row = report.TrialBalance.Single(item => item.Symbol == "AAPL");
        row.NetBalance.Should().Be(100m);
        row.Dimensions!.EntityId.Should().Be("entity-as-of");
        report.SnapshotReceipt.Should().NotBeNull();
        report.SnapshotReceipt!.LedgerEntryCount.Should().Be(1);
        report.SnapshotReceipt.LedgerLineCount.Should().Be(2);
        report.SnapshotReceipt.CertificationStatus.Should().Be(
            ReportingSnapshotCertificationStatus.Certifiable);
    }

    [Fact]
    public async Task GenerateAsync_FreezesHistoricalReferenceDataAndProducesDeterministicReceipt()
    {
        var securityId = Guid.NewGuid();
        var historicalDetail = BuildDetail(securityId, "AAPL", "Equity") with
        {
            DisplayName = "Apple Historical",
            Currency = "GBP",
            Version = 3
        };
        var historicalEconomic = BuildEconomicDefinition(
            securityId,
            "Equity",
            "HistoricalFamily",
            "HistoricalCommonStock",
            "HistoricalIssuer",
            "GB",
            SecurityIdentifierKind.Isin,
            "GB0000000001") with
        {
            Version = 3
        };
        var asOf = new DateTimeOffset(2026, 4, 11, 0, 0, 0, TimeSpan.Zero);
        var historicalReference = new SecurityMasterReportingReference(
            historicalDetail,
            historicalEconomic,
            asOf,
            SecurityMasterReportingResolutionMode.HistoricalEvent,
            EventGlobalSequence: 41,
            EventStreamVersion: 3,
            EventTimestamp: asOf.AddDays(-1));
        var query = new StubSecurityMasterQueryService(
            new Dictionary<string, SecurityDetailDto>(StringComparer.OrdinalIgnoreCase)
            {
                ["AAPL"] = BuildDetail(securityId, "AAPL", "Equity") with
                {
                    DisplayName = "Apple Current",
                    Currency = "USD",
                    Version = 9
                }
            },
            new Dictionary<Guid, SecurityEconomicDefinitionRecord>
            {
                [securityId] = historicalEconomic with
                {
                    AssetFamily = "CurrentFamily",
                    Version = 9
                }
            },
            new Dictionary<string, SecurityMasterReportingReference>(StringComparer.OrdinalIgnoreCase)
            {
                ["AAPL"] = historicalReference
            });
        var service = new ReportGenerationService(query);
        var ledgerBook = BuildLedgerBookWithSymbols(["AAPL"]);
        var request = new ReportRequest(
            "fund-1",
            asOf,
            ledgerBook,
            SnapshotSource: new ReportingSnapshotSourceContext(
                "durable-ledger",
                "global-sequence:41",
                IsAuthoritative: true));

        var first = await service.GenerateAsync(request);
        var second = await service.GenerateAsync(request);

        var row = first.TrialBalance.Single(item => item.Symbol == "AAPL");
        row.DisplayName.Should().Be("Apple Historical");
        row.Currency.Should().Be("GBP");
        row.AssetFamily.Should().Be("HistoricalFamily");
        row.SubType.Should().Be("HistoricalCommonStock");
        row.PrimaryIdentifierValue.Should().Be("GB0000000001");
        first.SnapshotReceipt!.CertificationStatus.Should().Be(
            ReportingSnapshotCertificationStatus.Certifiable);
        first.SnapshotReceipt.ContentHash.Should().Be(second.SnapshotReceipt!.ContentHash);
        first.SnapshotReceipt.SnapshotId.Should().Be(second.SnapshotReceipt.SnapshotId);
        query.ReportingReferenceCalls.Should().Be(2);
        query.LegacyDetailCalls.Should().Be(0);
        query.LegacyEconomicCalls.Should().Be(0);
        query.LastReportingAsOf.Should().Be(asOf);
    }

    [Fact]
    public async Task GenerateAsync_NamedSourceWithoutExplicitAuthorityOrCheckpoint_RemainsNonCertifiable()
    {
        var query = new StubSecurityMasterQueryService(
            new Dictionary<string, SecurityDetailDto>(),
            new Dictionary<Guid, SecurityEconomicDefinitionRecord>());
        var service = new ReportGenerationService(query);
        var asOf = new DateTimeOffset(2026, 4, 11, 0, 0, 0, TimeSpan.Zero);
        var ledgerBook = new FundLedgerBook("fund-1");
        ledgerBook.FundLedger.PostLines(
            asOf.AddHours(-1),
            "Cash seed",
            [
                (new LedgerAccount("Cash", LedgerAccountType.Asset), 100m, 0m),
                (new LedgerAccount("Capital", LedgerAccountType.Equity), 0m, 100m)
            ]);

        var report = await service.GenerateAsync(new ReportRequest(
            "fund-1",
            asOf,
            ledgerBook,
            SnapshotSource: new ReportingSnapshotSourceContext("named-only")));

        report.SnapshotReceipt!.CertificationStatus.Should().Be(
            ReportingSnapshotCertificationStatus.NonCertifiable);
        report.SnapshotReceipt.CertificationBlockers.Should().Contain(
            "ledger-source-non-authoritative:named-only");
        report.SnapshotReceipt.CertificationBlockers.Should().Contain(
            "ledger-source-checkpoint-missing:named-only");
    }

    private static FundLedgerBook BuildLedgerBookWithSymbols(IReadOnlyList<string> symbols)
    {
        var ledgerBook = new FundLedgerBook("fund-1");
        var timestamp = new DateTimeOffset(2026, 4, 10, 14, 0, 0, TimeSpan.Zero);

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

    private sealed class StubSecurityMasterQueryService :
        SecurityMasterQueryService,
        ISecurityMasterReportingQueryService
    {
        private readonly IReadOnlyDictionary<string, SecurityDetailDto> _detailsBySymbol;
        private readonly IReadOnlyDictionary<Guid, SecurityEconomicDefinitionRecord> _economicsBySecurityId;
        private readonly IReadOnlyDictionary<string, SecurityMasterReportingReference>? _reportingReferencesBySymbol;

        public StubSecurityMasterQueryService(
            IReadOnlyDictionary<string, SecurityDetailDto> detailsBySymbol,
            IReadOnlyDictionary<Guid, SecurityEconomicDefinitionRecord> economicsBySecurityId,
            IReadOnlyDictionary<string, SecurityMasterReportingReference>? reportingReferencesBySymbol = null)
        {
            _detailsBySymbol = detailsBySymbol;
            _economicsBySecurityId = economicsBySecurityId;
            _reportingReferencesBySymbol = reportingReferencesBySymbol;
        }

        public int ReportingReferenceCalls { get; private set; }

        public int LegacyDetailCalls { get; private set; }

        public int LegacyEconomicCalls { get; private set; }

        public DateTimeOffset? LastReportingAsOf { get; private set; }

        public Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<SecurityDetailDto?>(_detailsBySymbol.Values.FirstOrDefault(detail => detail.SecurityId == securityId));

        public Task<SecurityDetailDto?> GetByIdAsOfAsync(Guid securityId, DateTimeOffset asOfUtc, CancellationToken ct = default)
            => GetByIdAsync(securityId, ct);

        public Task<SecurityDetailDto?> GetByIdentifierAsync(SecurityIdentifierKind identifierKind, string identifierValue, string? provider, CancellationToken ct = default, DateTimeOffset? asOfUtc = null)
        {
            LegacyDetailCalls++;
            return Task.FromResult(_detailsBySymbol.TryGetValue(identifierValue, out var detail) ? detail : null);
        }

        public Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecuritySummaryDto>>([]);

        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(SecurityHistoryRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>([]);

        public Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(Guid securityId, CancellationToken ct = default)
        {
            LegacyEconomicCalls++;
            return Task.FromResult(_economicsBySecurityId.TryGetValue(securityId, out var definition) ? definition : null);
        }

        public Task<SecurityMasterReportingReference?> GetReportingReferenceByIdentifierAsOfAsync(
            SecurityIdentifierKind identifierKind,
            string identifierValue,
            string? provider,
            DateTimeOffset asOfUtc,
            CancellationToken ct = default)
        {
            ReportingReferenceCalls++;
            LastReportingAsOf = asOfUtc;
            if (_reportingReferencesBySymbol is not null)
            {
                return Task.FromResult(
                    _reportingReferencesBySymbol.TryGetValue(identifierValue, out var reference)
                        ? reference
                        : null);
            }

            if (!_detailsBySymbol.TryGetValue(identifierValue, out var detail))
            {
                return Task.FromResult<SecurityMasterReportingReference?>(null);
            }

            _economicsBySecurityId.TryGetValue(detail.SecurityId, out var economicDefinition);
            return Task.FromResult<SecurityMasterReportingReference?>(new SecurityMasterReportingReference(
                detail,
                economicDefinition,
                asOfUtc,
                SecurityMasterReportingResolutionMode.HistoricalEvent,
                EventGlobalSequence: detail.Version,
                EventStreamVersion: detail.Version,
                EventTimestamp: detail.EffectiveFrom));
        }

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
            .GetByIdentifierAsync(
                SecurityIdentifierKind.Ticker,
                "UNKN",
                null,
                Arg.Any<CancellationToken>(),
                Arg.Any<DateTimeOffset?>())
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

        report.TrialBalance.Should().ContainSingle(r => r.Symbol == "UNKN");
        var row = report.TrialBalance.Single(r => r.Symbol == "UNKN");
        row.Symbol.Should().Be("UNKN");
        row.AssetClass.Should().BeNull();
        row.DisplayName.Should().BeNull();
        row.Currency.Should().BeNull();

        report.AssetClassSections.Should().ContainSingle();
        report.AssetClassSections[0].AssetClass.Should().Be("Unclassified");
        report.AssetClassSections[0].Rows.Should().ContainSingle(r => r.Symbol == "UNKN");
        report.SnapshotReceipt!.CertificationStatus.Should().Be(
            ReportingSnapshotCertificationStatus.NonCertifiable);
        report.SnapshotReceipt.CertificationBlockers.Should().Contain(
            "security-master-non-historical:UNKN:LegacyQueryFallback");
    }

    [Fact]
    public async Task GenerateAsync_PreservesIdentifierMappedRows_AndNullHandlingAcrossMultipleSymbols()
    {
        var securityMaster = Substitute.For<SecurityMasterQueryService>();
        securityMaster
            .GetByIdentifierAsync(
                SecurityIdentifierKind.Ticker,
                "AAPL",
                null,
                Arg.Any<CancellationToken>(),
                Arg.Any<DateTimeOffset?>())
            .Returns(BuildDetail("Equity", "Apple Inc.", "USD"));
        securityMaster
            .GetByIdentifierAsync(
                SecurityIdentifierKind.Ticker,
                "CUSIP_037833100",
                null,
                Arg.Any<CancellationToken>(),
                Arg.Any<DateTimeOffset?>())
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

        report.TrialBalance.Where(row => row.Symbol is not null).Should().HaveCount(2);
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
        var asOf = new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero);

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
            Currency: currency!,
            CommonTerms: JsonSerializer.SerializeToElement(new { }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new { }),
            Identifiers: [],
            Aliases: [],
            Version: 1,
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-5),
            EffectiveTo: null);
}
