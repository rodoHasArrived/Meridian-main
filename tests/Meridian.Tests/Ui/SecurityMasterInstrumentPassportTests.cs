using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using NSubstitute;
using ContractSecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.Ui;

public sealed class SecurityMasterInstrumentPassportTests
{
    [Fact]
    public async Task GetInstrumentPassportAsync_ComposesIdentifiersLifecycleCorporateActionsPricingAndUsage()
    {
        var securityId = Guid.Parse("6A74606E-AEB1-40D9-8374-3216D59B16D4");
        var queryService = new StubSecurityMasterQueryService(securityId);
        var service = CreateService(queryService);

        var passport = await service.GetInstrumentPassportAsync(securityId, fundProfileId: null);

        passport.Should().NotBeNull();
        passport!.SecurityId.Should().Be(securityId);
        passport.Identity.DisplayName.Should().Be("Apple Inc.");
        passport.IdentifierSummary.HasPrimaryIdentifier.Should().BeTrue();
        passport.ProviderMappings.Should().Contain(mapping =>
            mapping.Provider == "alpaca" &&
            mapping.Value == "AAPL");
        var providerConfidence = passport.ProviderConfidence.Should().ContainSingle(item =>
            item.Provider == "alpaca" &&
            item.Symbol == "AAPL").Subject;
        providerConfidence.ProviderSource.Should().Be("Identifier");
        providerConfidence.ConfidenceScore.Should().BeGreaterThanOrEqualTo(85m);
        providerConfidence.FreshnessAsOf.Should().NotBeNull();
        providerConfidence.IdentifierConflictIds.Should().BeEmpty();
        providerConfidence.OverrideHistory.Should().ContainSingle(item => item.EventType == "SecurityOverrideApproved");
        passport.LifecycleEvents.Should().ContainSingle(item =>
            item.EventType == "SecurityCreated" &&
            item.SourceSystem == "golden-edm");
        passport.CorporateActions.Should().ContainSingle(action =>
            action.EventType == "Dividend" &&
            action.DividendPerShare == 0.25m);
        passport.Pricing.Status.Should().Be("Ready");
        passport.Pricing.TickSize.Should().Be(0.01m);
        passport.Usage.IsScoped.Should().BeFalse();
        passport.TrustPosture.TrustScore.Should().BeGreaterThan(0);
        passport.ReferenceDataWorkbench.Should().NotBeNull();
        passport.ReferenceDataWorkbench!.Sections.Select(section => section.SectionId).Should().Contain(
            [
                "provider-evidence",
                "identifier-confidence",
                "terms-obligations",
                "cash-flow-readiness",
                "ledger-classification",
                "operations-handoff"
            ]);
        passport.ReferenceDataWorkbench.Sections.Should().Contain(section =>
            section.SectionId == "provider-evidence" &&
            section.Summary.Contains("provider evidence", StringComparison.OrdinalIgnoreCase));
        passport.ReferenceDataWorkbench.Sections.Should().Contain(section =>
            section.SectionId == "ledger-classification" &&
            section.Summary.Contains("Equity", StringComparison.OrdinalIgnoreCase));
        passport.OperationsWorkbench.Should().NotBeNull();
        passport.OperationsWorkbench!.Panels.Select(panel => panel.PanelId).Should().Contain(
            [
                "identity",
                "provider-evidence",
                "terms",
                "operations-readiness",
                "handoff"
            ]);
        passport.OperationsWorkbench.Readiness.Select(readiness => readiness.ReadinessId).Should().Contain(
            [
                "valuation",
                "reconciliation",
                "ledger",
                "close",
                "report"
            ]);
        passport.OperationsWorkbench.Panels.Should().Contain(panel =>
            panel.PanelId == "identity" &&
            panel.Items.Any(item => item.ItemId == "primary-identifier" && item.Value == "AAPL"));
        passport.OperationsWorkbench.Panels.Should().Contain(panel =>
            panel.PanelId == "provider-evidence" &&
            panel.Items.Any(item =>
                item.ItemId.StartsWith("source-record-", StringComparison.Ordinal) &&
                item.Detail.Contains("Source record", StringComparison.OrdinalIgnoreCase) &&
                item.Detail.Contains("as of", StringComparison.OrdinalIgnoreCase) &&
                item.Detail.Contains("updated by", StringComparison.OrdinalIgnoreCase) &&
                item.Route!.StartsWith("/workstation/accounting/security-master#source", StringComparison.Ordinal)));
        passport.OperationsWorkbench.Readiness.Should().Contain(readiness =>
            readiness.ReadinessId == "ledger" &&
            readiness.Summary.Contains("ledger", StringComparison.OrdinalIgnoreCase));
        passport.OperationsWorkbench.Handoffs.Should().OnlyContain(handoff =>
            !string.IsNullOrWhiteSpace(handoff.Owner) &&
            !string.IsNullOrWhiteSpace(handoff.BlockerReason) &&
            handoff.ImpactedOutputs.Count > 0 &&
            !string.IsNullOrWhiteSpace(handoff.Route));
    }

    [Fact]
    public async Task GetTrustSnapshotAsync_AttachesInstrumentPassportForSharedClients()
    {
        var securityId = Guid.Parse("E59D43D7-A357-4D9F-9398-CE2707B81555");
        var service = CreateService(new StubSecurityMasterQueryService(securityId));

        var snapshot = await service.GetTrustSnapshotAsync(securityId, fundProfileId: null);

        snapshot.Should().NotBeNull();
        snapshot!.InstrumentPassport.Should().NotBeNull();
        snapshot.InstrumentPassport!.SecurityId.Should().Be(securityId);
        snapshot.InstrumentPassport.IdentifierSummary.PrimaryIdentifierValue.Should().Be("AAPL");
    }

    [Fact]
    public async Task GetInstrumentPassportAsync_SurfacesProviderIdentifierConflictsInConfidenceRows()
    {
        var securityId = Guid.Parse("59C19103-626D-4B3A-9011-33D635D6D406");
        var conflictId = Guid.Parse("84753E51-E431-46C6-9792-FEFB03DB2544");
        var queryService = new StubSecurityMasterQueryService(securityId);
        var service = CreateService(
            queryService,
            new StubSecurityMasterConflictService(
            [
                new SecurityMasterConflict(
                    conflictId,
                    securityId,
                    "IdentifierAmbiguity",
                    "Identifiers.Ticker",
                    "alpaca",
                    "AAPL",
                    "polygon",
                    "AAPL.O",
                    DateTimeOffset.UtcNow.AddMinutes(-4),
                    "Open")
            ]));

        var passport = await service.GetInstrumentPassportAsync(securityId, fundProfileId: null);

        var providerConfidence = passport!.ProviderConfidence.Should().ContainSingle(item =>
            item.Provider == "alpaca" &&
            item.Symbol == "AAPL").Subject;
        providerConfidence.ConfidenceScore.Should().BeLessThan(60m);
        providerConfidence.IdentifierConflictIds.Should().ContainSingle().Which.Should().Be(conflictId);
        providerConfidence.IdentifierConflictSummaries.Should().Contain(summary =>
            summary.Contains("identifier", StringComparison.OrdinalIgnoreCase) ||
            summary.Contains("conflict", StringComparison.OrdinalIgnoreCase));
        providerConfidence.ConfidenceReason.Should().Contain("open identifier conflict");
        passport.OperationsWorkbench!.Panels.Should().Contain(panel =>
            panel.PanelId == "provider-evidence" &&
            panel.Items.Any(item =>
                item.ItemId.StartsWith("provider-conflict-", StringComparison.Ordinal) &&
                item.Value.Contains("AAPL.O", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task GetInstrumentPassportAsync_SurfacesConflictingTermsInOperationsWorkbench()
    {
        var securityId = Guid.Parse("9B4F28C7-489F-4D0F-A0F9-BD3300665688");
        var conflictId = Guid.Parse("6872AFA1-4E1A-46B7-9482-4E2F7D7437D4");
        var queryService = new StubSecurityMasterQueryService(securityId);
        var service = CreateService(
            queryService,
            new StubSecurityMasterConflictService(
            [
                new SecurityMasterConflict(
                    conflictId,
                    securityId,
                    "EconomicTermsMismatch",
                    "EconomicDefinition.MaturityDate",
                    "fund-admin",
                    "2031-06-15",
                    "custodian",
                    "2031-09-15",
                    DateTimeOffset.UtcNow.AddMinutes(-7),
                    "Open")
            ]));

        var passport = await service.GetInstrumentPassportAsync(securityId, fundProfileId: null);

        passport!.OperationsWorkbench.Should().NotBeNull();
        var termsPanel = passport.OperationsWorkbench!.Panels.Should().ContainSingle(panel => panel.PanelId == "terms").Subject;
        termsPanel.Status.Should().Be("Review");
        termsPanel.Items.Should().Contain(item =>
            item.ItemId.StartsWith("terms-conflict-", StringComparison.Ordinal) &&
            item.Label == "EconomicDefinition.MaturityDate" &&
            item.Value.Contains("2031-06-15", StringComparison.OrdinalIgnoreCase) &&
            item.Value.Contains("2031-09-15", StringComparison.OrdinalIgnoreCase) &&
            item.Route == $"/workstation/accounting/security-master#conflict-{conflictId:D}");
        passport.OperationsWorkbench.Readiness.Should().Contain(readiness =>
            readiness.ReadinessId == "reconciliation" &&
            readiness.Status == "Review" &&
            readiness.NextAction.Contains("maturity", StringComparison.OrdinalIgnoreCase));
        passport.OperationsWorkbench.Handoffs.Should().Contain(handoff =>
            handoff.LinkedCases.Contains(conflictId.ToString("D")) &&
            handoff.Route!.Contains(conflictId.ToString("D"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetInstrumentPassportAsync_AttachesClearwaterControlSectionsWhenServicesAreRegistered()
    {
        var securityId = Guid.Parse("D85825FA-C568-4F9A-A7B8-2D988D975B6D");
        var queryService = new StubSecurityMasterQueryService(securityId);
        var now = DateTimeOffset.UtcNow;
        var pricingService = Substitute.For<ISecurityMasterPricingService>();
        pricingService.GetGoldenCopyPriceAsync(securityId, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SecurityPriceGoldenCopyDto?>(new SecurityPriceGoldenCopyDto(
                securityId,
                150.25m,
                SecurityPriceKind.MarketGoldenCopy,
                "refinitiv",
                now.AddHours(-2),
                false,
                0,
                [new SecurityComparisonPriceDto("bloomberg", 150.10m, now.AddHours(-3), -0.0998m)])));
        pricingService.GetPricingHierarchyAsync(securityId, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SecurityPricingHierarchyDto?>(new SecurityPricingHierarchyDto(
                securityId,
                null,
                [
                    new PricingHierarchyEntryDto(1, "refinitiv", "LSEG/Refinitiv", 3),
                    new PricingHierarchyEntryDto(2, "bloomberg", "Bloomberg BVAL", 5)
                ],
                now,
                "pricing-ops")));

        var cashFlowService = Substitute.For<ISecurityMasterCashFlowService>();
        cashFlowService.GetCashFlowSourceAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SecurityCashFlowSourceDto?>(new SecurityCashFlowSourceDto(
                securityId,
                StructuredCashFlowSourceKind.ClientProvided,
                now.AddDays(-1),
                true,
                "controller",
                now.AddDays(-1))));

        var entitlementService = Substitute.For<IDataVendorEntitlementService>();
        entitlementService.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DataVendorEntitlementDto>>(
            [
                new DataVendorEntitlementDto(
                    Guid.Parse("F7266BDE-6389-4BE8-9987-62E85AF34949"),
                    "LSEG/Refinitiv",
                    DataVendorDataType.Pricing,
                    "LSEG-2026",
                    now.AddMonths(-1),
                    now.AddMonths(11),
                    null,
                    true,
                    "licensing@example.test",
                    30,
                    DataVendorEntitlementStatus.Active,
                    "licensing-ops",
                    now.AddMonths(-1))
            ]));

        var dataQualityService = Substitute.For<ISecurityMasterDataQualityService>();
        dataQualityService.GetLatestReportAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SecurityMasterQualityReportDto?>(new SecurityMasterQualityReportDto(
                now,
                1,
                0,
                [])));
        var service = CreateService(
            queryService,
            pricingService: pricingService,
            cashFlowService: cashFlowService,
            entitlementService: entitlementService,
            dataQualityService: dataQualityService);

        var passport = await service.GetInstrumentPassportAsync(securityId, fundProfileId: null);

        var sections = passport!.ReferenceDataWorkbench!.Sections.ToDictionary(section => section.SectionId);
        sections.Keys.Should().Contain(
            [
                "pricing-hierarchy",
                "cash-flow-source-governance",
                "vendor-entitlements",
                "data-quality-controls",
                "manual-change-review"
            ]);
        sections["pricing-hierarchy"].Summary.Should().Contain("Golden-copy price");
        sections["cash-flow-source-governance"].Summary.Should().Contain("ClientProvided");
        sections["vendor-entitlements"].Summary.Should().Contain("direct-client contract");
        sections["data-quality-controls"].Status.Should().Be("Ready");
        sections["manual-change-review"].Summary.Should().Contain("manual change");
        passport.OperatingModel.Should().NotBeNull();
        passport.OperatingModel!.ManualChangeApproval.PolicyKey.Should().Be("operations-continuity.security-master-override");
        passport.OperatingModel.ManualChangeApproval.Gate.Should().Be(OperationsGateKeyDto.SecurityMaster);
        passport.OperatingModel.ManualChangeApproval.RequiresIndependentReviewer.Should().BeTrue();
        passport.OperatingModel.Controls.Select(section => section.SectionId).Should().Contain("vendor-entitlements");
    }

    [Fact]
    public async Task GetInstrumentPassportAsync_MarksFundScopedVendorEntitlementAsMostSpecific()
    {
        var securityId = Guid.Parse("E31F12C6-6C68-4B45-A661-9BA1CE911A35");
        var queryService = new StubSecurityMasterQueryService(securityId);
        var now = DateTimeOffset.UtcNow;
        var globalEntitlement = new DataVendorEntitlementDto(
            Guid.Parse("5E1570BC-5A94-4567-90D8-E2E2E239536D"),
            "LSEG/Refinitiv",
            DataVendorDataType.Pricing,
            "LSEG-GLOBAL",
            now.AddMonths(-1),
            now.AddMonths(11),
            null,
            false,
            "licensing@example.test",
            30,
            DataVendorEntitlementStatus.Active,
            "licensing-ops",
            now.AddMonths(-1))
        {
            SourceCategory = "Global pricing feed",
            OperatorMetadata = "Global fallback hierarchy."
        };
        var fundEntitlement = globalEntitlement with
        {
            EntitlementId = Guid.Parse("88BB266F-896D-4221-8254-0CB432F22E58"),
            ContractReference = "LSEG-FUND-ALPHA",
            FundProfileId = "fund-alpha",
            RequiresDirectClientContract = true,
            SourceCategory = "Fund pricing feed",
            ExpectedRefreshCadence = "Daily close",
            DefaultMaxDaysStale = 1,
            OperatorMetadata = "Fund Alpha direct-client pricing metadata."
        };
        var entitlementService = Substitute.For<IDataVendorEntitlementService>();
        entitlementService.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DataVendorEntitlementDto>>([globalEntitlement, fundEntitlement]));
        var service = CreateService(queryService, entitlementService: entitlementService);

        var passport = await service.GetInstrumentPassportAsync(securityId, fundProfileId: "fund-alpha");

        passport!.OperatingModel.Should().NotBeNull();
        var operatingModel = passport.OperatingModel!;
        operatingModel.FundProfileId.Should().Be("fund-alpha");
        var applicable = operatingModel.EntitlementApplicability
            .Where(item => item.IsApplicable && item.VendorName == "LSEG/Refinitiv" && item.DataType == DataVendorDataType.Pricing)
            .ToArray();
        applicable.Should().HaveCount(2);
        applicable.Single(item => item.Scope == "FundProfile").IsMostSpecific.Should().BeTrue();
        applicable.Single(item => item.Scope == "Global").IsMostSpecific.Should().BeFalse();
        operatingModel.OperatorMetadata.Should().Contain(item =>
            item.MetadataId == $"entitlement-{fundEntitlement.EntitlementId:N}" &&
            item.ExpectedRefreshCadence == "Daily close" &&
            item.OperatorMetadata == "Fund Alpha direct-client pricing metadata.");
    }

    [Fact]
    public async Task GetOperatingModelAsync_ReturnsSharedPassportOperatingModel()
    {
        var securityId = Guid.Parse("7C7C3052-8342-49A4-A635-4D79098B92FC");
        var queryService = new StubSecurityMasterQueryService(securityId);
        var now = DateTimeOffset.UtcNow;
        var entitlementService = Substitute.For<IDataVendorEntitlementService>();
        entitlementService.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DataVendorEntitlementDto>>(
            [
                new DataVendorEntitlementDto(
                    Guid.Parse("88CF6C48-A1EF-4982-9DEB-D083B5D752D6"),
                    "LSEG/Refinitiv",
                    DataVendorDataType.Pricing,
                    "LSEG-FUND-ALPHA",
                    now.AddMonths(-1),
                    now.AddMonths(11),
                    null,
                    true,
                    "licensing@example.test",
                    30,
                    DataVendorEntitlementStatus.Active,
                    "licensing-ops",
                    now.AddMonths(-1))
                {
                    FundProfileId = "fund-alpha",
                    SourceCategory = "Fund pricing feed",
                    OperatorMetadata = "Fund Alpha direct-client pricing metadata."
                }
            ]));
        var service = CreateService(queryService, entitlementService: entitlementService);

        var operatingModel = await service.GetOperatingModelAsync(securityId, fundProfileId: "fund-alpha");

        operatingModel.Should().NotBeNull();
        operatingModel!.SecurityId.Should().Be(securityId);
        operatingModel.FundProfileId.Should().Be("fund-alpha");
        operatingModel.Stages.Should().Contain(stage => stage.StageId == "reconcile");
        operatingModel.EntitlementApplicability.Should().Contain(item =>
            item.IsApplicable &&
            item.IsMostSpecific &&
            item.Scope == "FundProfile");
        operatingModel.ManualChangeApproval.PolicyKey.Should().Be("operations-continuity.security-master-override");
    }

    private static SecurityMasterWorkbenchQueryService CreateService(
        StubSecurityMasterQueryService queryService,
        ISecurityMasterConflictService? conflictService = null,
        ISecurityMasterPricingService? pricingService = null,
        ISecurityMasterCashFlowService? cashFlowService = null,
        IDataVendorEntitlementService? entitlementService = null,
        ISecurityMasterDataQualityService? dataQualityService = null) =>
        new(
            queryService,
            new EmptySecurityValidationService(),
            conflictService ?? new StubSecurityMasterConflictService([]),
            new EmptySecurityMasterIngestStatusService(),
            Substitute.For<IStrategyRepository>(),
            new PortfolioReadService(),
            new LedgerReadService(),
            new ReportGenerationService(queryService),
            reconciliationRunService: null,
            pricingService,
            cashFlowService,
            entitlementService,
            dataQualityService);

    private sealed class StubSecurityMasterQueryService(Guid securityId)
        : ContractSecurityMasterQueryService,
          Meridian.Application.SecurityMaster.ISecurityMasterQueryService
    {
        private readonly DateTimeOffset _effectiveFrom = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

        public Task<SecurityDetailDto?> GetByIdAsync(Guid requestedSecurityId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(requestedSecurityId == securityId ? CreateDetail() : null);
        }

        public Task<SecurityDetailDto?> GetByIdentifierAsync(
            SecurityIdentifierKind identifierKind,
            string identifierValue,
            string? provider,
            CancellationToken ct = default,
            DateTimeOffset? asOfUtc = null)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<SecurityDetailDto?>(CreateDetail());
        }

        public Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<SecuritySummaryDto>>(
            [
                new SecuritySummaryDto(securityId, "Equity", SecurityStatusDto.Active, "Apple Inc.", "AAPL", "USD", 7)
            ]);
        }

        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(SecurityHistoryRequest request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>(
            [
                new SecurityMasterEventEnvelope(
                    GlobalSequence: 10,
                    SecurityId: securityId,
                    StreamVersion: 1,
                    EventType: "SecurityCreated",
                    EventTimestamp: _effectiveFrom,
                    Actor: "reference-data",
                    CorrelationId: null,
                    CausationId: null,
                    Payload: Json("""{"identifiers":[{"kind":"Ticker","value":"AAPL"}],"assetClass":"Equity"}"""),
                    Metadata: Json("""{"sourceSystem":"golden-edm","sourceRecordId":"edm-aapl"}"""))
                ,
                new SecurityMasterEventEnvelope(
                    GlobalSequence: 11,
                    SecurityId: securityId,
                    StreamVersion: 2,
                    EventType: "SecurityOverrideApproved",
                    EventTimestamp: _effectiveFrom.AddDays(1),
                    Actor: "security-steward",
                    CorrelationId: null,
                    CausationId: null,
                    Payload: Json("""{"operatorOverrides":{"sector":"Technology"},"reason":"provider symbol confirmed"}"""),
                    Metadata: Json("""{"sourceSystem":"wpf-ui","sourceRecordId":"override-aapl","reason":"override approval"}"""))
            ]);
        }

        public Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(Guid requestedSecurityId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<SecurityEconomicDefinitionRecord?>(requestedSecurityId == securityId
                ? new SecurityEconomicDefinitionRecord(
                    SecurityId: securityId,
                    AssetClass: "Equity",
                    AssetFamily: "Public Equity",
                    SubType: "CommonShare",
                    TypeName: "Common Share",
                    IssuerType: "Corporate",
                    RiskCountry: "US",
                    Status: SecurityStatusDto.Active,
                    DisplayName: "Apple Inc.",
                    Currency: "USD",
                    Classification: Json("""{"assetFamily":"Public Equity"}"""),
                    CommonTerms: Json("""{"issuerName":"Apple Inc.","countryOfRisk":"US","primaryListingMic":"XNAS"}"""),
                    EconomicTerms: Json("""{"pricingSource":"nasdaq"}"""),
                    Provenance: Json("""{"winningSource":{"sourceSystem":"golden-edm","sourceRecordId":"edm-aapl","asOf":"2026-05-28T00:00:00Z","updatedBy":"steward","reason":"primary source"}}"""),
                    Version: 7,
                    EffectiveFrom: _effectiveFrom,
                    EffectiveTo: null,
                    Identifiers: CreateIdentifiers(),
                    LegacyAssetClass: null,
                    LegacyAssetSpecificTerms: null)
                : null);
        }

        public Task<TradingParametersDto?> GetTradingParametersAsync(Guid requestedSecurityId, DateTimeOffset asOf, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<TradingParametersDto?>(requestedSecurityId == securityId
                ? new TradingParametersDto(securityId, 1m, 0.01m, 1m, 0.25m, "14:30-21:00", 0.10m, asOf)
                : null);
        }

        public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(Guid requestedSecurityId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<CorporateActionDto>>(requestedSecurityId == securityId
                ?
                [
                    new CorporateActionDto(
                        Guid.Parse("58D3590E-62D5-4E41-B4E8-99510296DB82"),
                        securityId,
                        "Dividend",
                        new DateOnly(2026, 5, 10),
                        new DateOnly(2026, 5, 20),
                        0.25m,
                        "USD",
                        SplitRatio: null,
                        NewSecurityId: null,
                        DistributionRatio: null,
                        AcquirerSecurityId: null,
                        ExchangeRatio: null,
                        SubscriptionPricePerShare: null,
                        RightsPerShare: null)
                ]
                : []);
        }

        public Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(Guid securityId, CancellationToken ct = default) =>
            Task.FromResult<PreferredEquityTermsDto?>(null);

        public Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(Guid securityId, CancellationToken ct = default) =>
            Task.FromResult<ConvertibleEquityTermsDto?>(null);

        private SecurityDetailDto CreateDetail() =>
            new(
                SecurityId: securityId,
                AssetClass: "Equity",
                Status: SecurityStatusDto.Active,
                DisplayName: "Apple Inc.",
                Currency: "USD",
                CommonTerms: Json("""{"issuerName":"Apple Inc.","countryOfRisk":"US","primaryListingMic":"XNAS","settlementCycleDays":1}"""),
                AssetSpecificTerms: Json("""{}"""),
                Identifiers: CreateIdentifiers(),
                Aliases:
                [
                    new SecurityAliasDto(
                        AliasId: Guid.Parse("0936C6F5-C876-4ED1-A38E-C46269F0C0C4"),
                        SecurityId: securityId,
                        AliasKind: "ProviderSymbol",
                        AliasValue: "AAPL",
                        Provider: "alpaca",
                        Scope: SecurityAliasScope.Execution,
                        Reason: "Broker symbol mapping",
                        CreatedBy: "ops",
                        CreatedAt: _effectiveFrom,
                        ValidFrom: _effectiveFrom,
                        ValidTo: null,
                        IsEnabled: true)
                ],
                Version: 7,
                EffectiveFrom: _effectiveFrom,
                EffectiveTo: null);

        private IReadOnlyList<SecurityIdentifierDto> CreateIdentifiers() =>
        [
            new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "AAPL", true, _effectiveFrom, Provider: "alpaca"),
            new SecurityIdentifierDto(SecurityIdentifierKind.Isin, "US0378331005", false, _effectiveFrom)
        ];
    }

    private sealed class EmptySecurityValidationService : ISecurityValidationService
    {
        public Task<SecurityValidationReportDto> ValidateSecurityAsync(Guid securityId, CancellationToken ct = default) =>
            Task.FromResult(new SecurityValidationReportDto(securityId, "Security", DateTimeOffset.UtcNow, false, 0, 0, []));

        public Task<SecurityValidationReportDto> ValidateUniverseAsync(CancellationToken ct = default) =>
            Task.FromResult(new SecurityValidationReportDto(null, "Universe", DateTimeOffset.UtcNow, false, 0, 0, []));

        public SecurityValidationReportDto ValidateRecord(
            SecurityProjectionRecord record,
            IReadOnlyList<SecurityProjectionRecord> universe,
            DateTimeOffset? evaluatedAtUtc = null) =>
            new(record.SecurityId, "Security", evaluatedAtUtc ?? DateTimeOffset.UtcNow, false, 0, 0, []);
    }

    private sealed class StubSecurityMasterConflictService(IReadOnlyList<SecurityMasterConflict> conflicts)
        : ISecurityMasterConflictService
    {
        public Task<IReadOnlyList<SecurityMasterConflict>> GetOpenConflictsAsync(CancellationToken ct) =>
            Task.FromResult(conflicts);

        public Task<SecurityMasterConflict?> GetConflictAsync(Guid conflictId, CancellationToken ct) =>
            Task.FromResult(conflicts.FirstOrDefault(conflict => conflict.ConflictId == conflictId));

        public Task<SecurityMasterConflict?> ResolveAsync(ResolveConflictRequest request, CancellationToken ct) =>
            Task.FromResult(conflicts.FirstOrDefault(conflict => conflict.ConflictId == request.ConflictId));

        public Task RecordConflictsForProjectionAsync(SecurityProjectionRecord projection, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class EmptySecurityMasterIngestStatusService : ISecurityMasterIngestStatusService
    {
        public SecurityMasterIngestStatusSnapshot GetSnapshot() => new(null, null);
    }

    private static JsonElement Json(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();
}
