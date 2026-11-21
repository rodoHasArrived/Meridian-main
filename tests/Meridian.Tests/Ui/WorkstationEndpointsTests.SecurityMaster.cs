using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Monitoring;
using Meridian.Application.ProviderRouting;
using Meridian.Application.SecurityMaster;
using Meridian.Application.Services;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.DataIntegration.Monitoring;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Identity.Auth;
using Meridian.ProviderSdk;
using Meridian.Reporting;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using IReconciliationApiService = Meridian.Contracts.Reconciliation.IReconciliationApiService;
using ISecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;
using ReconciliationQueueAccountStatusDto = Meridian.Contracts.Reconciliation.ReconciliationQueueAccountStatusDto;
using StatementImportSummaryDto = Meridian.Contracts.Reconciliation.StatementImportSummaryDto;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterSearch_ShouldRequireQuery()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<ISecurityMasterQueryService>(new StubSecurityMasterQueryService());
        });

        var client = app.GetTestClient();
        var response = await client.GetAsync("/api/workstation/security-master/securities");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterHistory_ShouldReturnHistoryPayload()
    {
        var securityId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var queryService = new StubSecurityMasterQueryService();
        queryService.RegisterHistory(
            securityId,
            [
                new SecurityMasterEventEnvelope(
                    GlobalSequence: 101,
                    SecurityId: securityId,
                    StreamVersion: 2,
                    EventType: "SecurityAmended",
                    EventTimestamp: new DateTimeOffset(2026, 3, 22, 12, 0, 0, TimeSpan.Zero),
                    Actor: "governance.user",
                    CorrelationId: null,
                    CausationId: null,
                    Payload: JsonSerializer.SerializeToElement(new { field = "displayName" }),
                    Metadata: JsonSerializer.SerializeToElement(new { source = "test" })),
                new SecurityMasterEventEnvelope(
                    GlobalSequence: 100,
                    SecurityId: securityId,
                    StreamVersion: 1,
                    EventType: "SecurityCreated",
                    EventTimestamp: new DateTimeOffset(2026, 3, 21, 12, 0, 0, TimeSpan.Zero),
                    Actor: "governance.user",
                    CorrelationId: null,
                    CausationId: null,
                    Payload: JsonSerializer.SerializeToElement(new { displayName = "Sample Security" }),
                    Metadata: JsonSerializer.SerializeToElement(new { source = "test" }))
            ]);

        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<ISecurityMasterQueryService>(queryService);
        });

        var client = app.GetTestClient();
        using var history = await ReadJsonAsync(client, $"/api/workstation/security-master/securities/{securityId}/history?take=1");
        history.RootElement.GetArrayLength().Should().Be(1);
        history.RootElement[0].GetProperty("eventType").GetString().Should().Be("SecurityAmended");
        history.RootElement[0].GetProperty("streamVersion").GetInt64().Should().Be(2);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterHistory_ShouldReturnNotFoundWithoutEvents()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<ISecurityMasterQueryService>(new StubSecurityMasterQueryService());
        });

        var client = app.GetTestClient();
        var response = await client.GetAsync($"/api/workstation/security-master/securities/{Guid.NewGuid()}/history");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterTrustSnapshot_ShouldReturnForbiddenWithoutViewSecurityMasterPermission()
    {
        var securityId = Guid.Parse("60606060-6060-6060-6060-606060606060");
        var queryService = new StubSecurityMasterQueryService();
        queryService.RegisterSecurity(
            CreateSecuritySummary(securityId, "Locked Security", "LOCK"),
            CreateSecurityDetail(securityId, "Locked Security", "LOCK"));

        await using var app = await CreateAppAsync(
            services => RegisterSecurityMasterWorkbenchServices(services, queryService, new StubSecurityMasterConflictService([])),
            currentUserPermissions: UserPermission.ViewStrategies);

        var client = app.GetTestClient();
        var response = await client.GetAsync($"/api/workstation/security-master/securities/{securityId}/trust-snapshot");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterTrustSnapshot_ShouldReturnTypedWinningSourceProvenance()
    {
        var securityId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var queryService = new StubSecurityMasterQueryService();
        queryService.RegisterSecurity(
            CreateSecuritySummary(securityId, "Apple Inc.", "AAPL"),
            CreateSecurityDetail(securityId, "Apple Inc.", "AAPL"));
        queryService.RegisterEconomicDefinition(
            securityId,
            CreateEconomicDefinitionRecord(
                securityId,
                "Apple Inc.",
                "AAPL",
                JsonSerializer.SerializeToElement(new
                {
                    sourceSystem = "golden-edm",
                    sourceRecordId = "EDM-123",
                    asOf = "2026-04-20T09:30:00Z",
                    updatedBy = "workflow.bot",
                    reason = "golden-copy"
                })));
        queryService.RegisterTradingParameters(securityId, CreateTradingParameters(securityId));

        await using var app = await CreateAppAsync(services =>
        {
            RegisterSecurityMasterWorkbenchServices(services, queryService, new StubSecurityMasterConflictService([]));
        });

        var client = app.GetTestClient();
        var response = await client.GetAsync($"/api/workstation/security-master/securities/{securityId}/trust-snapshot");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var snapshot = await response.Content.ReadFromJsonAsync<SecurityMasterTrustSnapshotDto>(ServerJsonOptions);

        snapshot.Should().NotBeNull();
        snapshot!.EconomicDefinition.WinningSourceSystem.Should().Be("golden-edm");
        snapshot.EconomicDefinition.WinningSourceRecordId.Should().Be("EDM-123");
        snapshot.EconomicDefinition.WinningSourceUpdatedBy.Should().Be("workflow.bot");
        snapshot.ProvenanceCandidates.Should().ContainSingle(candidate =>
            candidate.IsWinningSource &&
            candidate.SourceSystem == "golden-edm" &&
            candidate.SourceRecordId == "EDM-123");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterInstrumentPassport_ShouldReturnProviderConfidenceAndOperationsWorkbench()
    {
        var securityId = Guid.Parse("69696969-6969-6969-6969-696969696969");
        var effectiveFrom = new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero);
        var queryService = new StubSecurityMasterQueryService();
        queryService.RegisterSecurity(
            CreateSecuritySummary(securityId, "Apple Inc.", "AAPL"),
            new SecurityDetailDto(
                SecurityId: securityId,
                AssetClass: "Equity",
                Status: SecurityStatusDto.Active,
                DisplayName: "Apple Inc.",
                Currency: "USD",
                CommonTerms: CreateEmptyJson(),
                AssetSpecificTerms: CreateEmptyJson(),
                Identifiers:
                [
                    new SecurityIdentifierDto(
                        SecurityIdentifierKind.Ticker,
                        "AAPL",
                        true,
                        effectiveFrom,
                        Provider: "alpaca")
                ],
                Aliases:
                [
                    new SecurityAliasDto(
                        AliasId: Guid.Parse("F7AA8916-C40E-44F9-B427-9351F09F6C95"),
                        SecurityId: securityId,
                        AliasKind: "ProviderSymbol",
                        AliasValue: "AAPL",
                        Provider: "alpaca",
                        Scope: SecurityAliasScope.Execution,
                        Reason: "Broker symbol mapping",
                        CreatedBy: "ops",
                        CreatedAt: effectiveFrom,
                        ValidFrom: effectiveFrom,
                        ValidTo: null,
                        IsEnabled: true)
                ],
                Version: 4,
                EffectiveFrom: effectiveFrom,
                EffectiveTo: null));

        await using var app = await CreateAppAsync(services =>
        {
            RegisterSecurityMasterWorkbenchServices(services, queryService, new StubSecurityMasterConflictService([]));
        });

        var client = app.GetTestClient();
        var response = await client.GetAsync($"/api/workstation/security-master/securities/{securityId}/passport");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var passport = await response.Content.ReadFromJsonAsync<InstrumentPassportDto>(ServerJsonOptions);

        passport.Should().NotBeNull();
        passport!.SecurityId.Should().Be(securityId);
        passport.ProviderConfidence.Should().ContainSingle(item =>
            item.Provider == "alpaca" &&
            item.Symbol == "AAPL" &&
            item.ProviderSource == "Identifier" &&
            item.ConfidenceScore >= 85m);
        passport.ReferenceDataWorkbench.Should().NotBeNull();
        passport.ReferenceDataWorkbench!.Sections.Should().Contain(section =>
            section.SectionId == "provider-evidence" &&
            section.Status == "Ready");
        passport.ReferenceDataWorkbench.Sections.Should().Contain(section =>
            section.SectionId == "identifier-confidence" &&
            section.Summary.Contains("identifier", StringComparison.OrdinalIgnoreCase));
        passport.ReferenceDataWorkbench.OperationsHandoffs.Should().NotBeEmpty();
        passport.OperationsWorkbench.Should().NotBeNull();
        passport.OperationsWorkbench!.Panels.Select(panel => panel.PanelId).Should().Contain(
            [
                "identity",
                "provider-evidence",
                "terms",
                "operations-readiness",
                "handoff"
            ]);
        passport.OperationsWorkbench.Readiness.Should().Contain(readiness =>
            readiness.ReadinessId == "valuation" &&
            readiness.Label == "Valuation-ready");
        passport.OperationsWorkbench.Readiness.Should().Contain(readiness =>
            readiness.ReadinessId == "ledger" &&
            readiness.Label == "Ledger-ready");
        passport.OperationsWorkbench.Handoffs.Should().Contain(handoff =>
            handoff.Owner == "Security Master steward" &&
            handoff.ImpactedOutputs.Contains("Security Master") &&
            handoff.Route!.StartsWith("/workstation/accounting/security-master", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task MapWorkstationEndpoints_SecurityMasterTrustSnapshot_ShouldReturnValidationIdentifierAndSchemaProjections(
        bool isShort,
        bool legacyLotWithoutDirection,
        bool hasCompositeBasis)
    {
        var securityId = Guid.Parse("67676767-6767-6767-6767-676767676767");
        var queryService = new StubSecurityMasterQueryService();
        queryService.RegisterSecurity(
            CreateSecuritySummary(securityId, "Acme Funding 2031-A1", "ACME31"),
            new SecurityDetailDto(
                SecurityId: securityId,
                AssetClass: "Bond",
                Status: SecurityStatusDto.Active,
                DisplayName: "Acme Funding 2031-A1",
                Currency: "USD",
                CommonTerms: JsonSerializer.SerializeToElement(new
                {
                    issuerName = "Acme Funding Trust",
                    countryOfRisk = "US",
                    primaryListingMic = "OTCM",
                    settlementCycleDays = 2
                }),
                AssetSpecificTerms: JsonSerializer.SerializeToElement(new
                {
                    schemaVersion = 1,
                    maturity = "2031-12-15",
                    couponType = "Fixed",
                    couponRate = 5.125,
                    issueDate = "2026-01-15",
                    subclass = "AssetBacked"
                }),
                Identifiers:
                [
                    new SecurityIdentifierDto(
                        SecurityIdentifierKind.Cusip,
                        "000000AA1",
                        true,
                        new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero)),
                    new SecurityIdentifierDto(
                        SecurityIdentifierKind.ProviderSymbol,
                        "ACME31 TRACE",
                        false,
                        new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
                        null,
                        "TRACE")
                ],
                Aliases:
                [
                    new SecurityAliasDto(
                        AliasId: Guid.Parse("11111111-aaaa-4444-bbbb-111111111111"),
                        SecurityId: securityId,
                        AliasKind: "Ticker",
                        AliasValue: "ACME31",
                        Provider: "Bloomberg",
                        Scope: SecurityAliasScope.Operations,
                        Reason: "desk alias",
                        CreatedBy: "ops",
                        CreatedAt: new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
                        ValidFrom: new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
                        ValidTo: null,
                        IsEnabled: true)
                ],
                Version: 7,
                EffectiveFrom: new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
                EffectiveTo: null));
        queryService.RegisterEconomicDefinition(
            securityId,
            new SecurityEconomicDefinitionRecord(
                SecurityId: securityId,
                AssetClass: "FixedIncome",
                AssetFamily: "StructuredCredit",
                SubType: "AssetBackedSecurity",
                TypeName: "ABS PassThrough",
                IssuerType: "Trust",
                RiskCountry: "US",
                Status: SecurityStatusDto.Active,
                DisplayName: "Acme Funding 2031-A1",
                Currency: "USD",
                Classification: JsonSerializer.SerializeToElement(new
                {
                    assetClass = "FixedIncome",
                    assetFamily = "StructuredCredit",
                    subType = "AssetBackedSecurity"
                }),
                CommonTerms: JsonSerializer.SerializeToElement(new
                {
                    issuerName = "Acme Funding Trust",
                    countryOfRisk = "US"
                }),
                EconomicTerms: JsonSerializer.SerializeToElement(new
                {
                    schemaVersion = 2,
                    maturity = new
                    {
                        issueDate = "2026-01-15",
                        maturityDate = "2031-12-15"
                    },
                    call = new
                    {
                        firstCallDate = "2028-06-15"
                    },
                    coupon = new
                    {
                        couponType = "Fixed",
                        couponRate = 5.125
                    },
                    cashflowSchedule = new object[]
                    {
                        new
                        {
                            eventId = "cf-20260625",
                            eventType = "InterestPayment",
                            effectiveDate = "2026-06-25",
                            paymentDate = "2026-06-25",
                            expectedAmount = 12500.00m,
                            currency = "USD",
                            postingStatus = "Projected",
                            sourceSystem = "trustee-feed",
                            sourceRecordId = "cashflow-20260625",
                            asOf = "2026-05-20T10:00:00Z",
                            updatedBy = "trustee.ops",
                            reason = "scheduled-interest"
                        }
                    },
                    structuredProduct = new
                    {
                        factor = 0.9825,
                        factorDate = "2026-05-01",
                        collateralType = "AutoLoan",
                        factorHistory = new object[]
                        {
                            new
                            {
                                pointId = "factor-20260401",
                                effectiveDate = "2026-04-01",
                                factor = 0.9910m,
                                sourceSystem = "trustee-feed",
                                sourceRecordId = "factor-20260401",
                                asOf = "2026-04-01T10:00:00Z",
                                updatedBy = "trustee.ops",
                                reason = "monthly-factor"
                            },
                            new
                            {
                                pointId = "factor-20260501",
                                effectiveDate = "2026-05-01",
                                factor = 0.9825m,
                                previousFactor = 0.9910m,
                                sourceSystem = "trustee-feed",
                                sourceRecordId = "factor-20260501",
                                asOf = "2026-05-01T10:00:00Z",
                                updatedBy = "trustee.ops",
                                reason = "monthly-factor"
                            }
                        }
                    }
                }),
                Provenance: JsonSerializer.SerializeToElement(new
                {
                    sourceSystem = "golden-edm",
                    sourceRecordId = "ABS-2031-A1",
                    asOf = "2026-05-20T10:00:00Z",
                    updatedBy = "workflow.bot",
                    reason = "governed merge"
                }),
                Version: 7,
                EffectiveFrom: new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
                EffectiveTo: null,
                Identifiers:
                [
                    new SecurityIdentifierDto(
                        SecurityIdentifierKind.Cusip,
                        "000000AA1",
                        true,
                        new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero))
                ],
                LegacyAssetClass: "Bond",
                LegacyAssetSpecificTerms: JsonSerializer.SerializeToElement(new
                {
                    schemaVersion = 1,
                    maturity = "2031-12-15"
                })));
        queryService.RegisterTradingParameters(securityId, CreateTradingParameters(securityId));
        queryService.RegisterCorporateActions(
            securityId,
            [
                new CorporateActionDto(
                    CorpActId: Guid.Parse("22222222-bbbb-4444-cccc-222222222222"),
                    SecurityId: securityId,
                    EventType: "PrincipalPaydown",
                    ExDate: new DateOnly(2026, 5, 25),
                    PayDate: new DateOnly(2026, 5, 27),
                    DividendPerShare: null,
                    Currency: "USD",
                    SplitRatio: null,
                    NewSecurityId: null,
                    DistributionRatio: 0.0085m,
                    AcquirerSecurityId: null,
                    ExchangeRatio: null,
                    SubscriptionPricePerShare: null,
                    RightsPerShare: null)
            ]);
        queryService.RegisterHistory(
            securityId,
            [
                new SecurityMasterEventEnvelope(
                    GlobalSequence: 10,
                    SecurityId: securityId,
                    StreamVersion: 8,
                    EventType: "FactorScheduleRefreshed",
                    EventTimestamp: new DateTimeOffset(2026, 5, 20, 10, 15, 0, TimeSpan.Zero),
                    Actor: "trustee.bot",
                    CorrelationId: Guid.Parse("10000000-0000-0000-0000-000000000001"),
                    CausationId: Guid.Parse("10000000-0000-0000-0000-000000000002"),
                    Payload: JsonSerializer.SerializeToElement(new
                    {
                        factor = 0.9825,
                        factorDate = "2026-05-01",
                        paymentDate = "2026-06-25"
                    }),
                    Metadata: JsonSerializer.SerializeToElement(new
                    {
                        sourceSystem = "trustee-feed",
                        sourceRecordId = "factor-20260501",
                        reason = "monthly remittance"
                    }))
            ]);

        var validationService = new StubSecurityValidationService();
        validationService.RegisterReport(
            securityId,
            new SecurityValidationReportDto(
                SecurityId: securityId,
                Scope: "Security",
                EvaluatedAtUtc: new DateTimeOffset(2026, 5, 20, 10, 5, 0, TimeSpan.Zero),
                HasBlockingIssues: true,
                CriticalIssueCount: 0,
                ErrorIssueCount: 1,
                Issues:
                [
                    new SecurityValidationIssueDto(
                        SecurityValidationSeverityDto.Error,
                        "SM_IDENTIFIER_DUPLICATE_ACTIVE",
                        "Duplicate active identifier exists on the record",
                        "Provider mapping must be reviewed before downstream use.",
                        ["identifiers.ProviderSymbol"],
                        "Expire the duplicate provider symbol and confirm the winning map.",
                        [])
                ]));

        await using var app = await CreateAppAsync(services =>
        {
            RegisterSecurityMasterWorkbenchServices(
                services,
                queryService,
                new StubSecurityMasterConflictService([]),
                validationService);
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "run-structured-lots",
            strategyId: "structured-income",
            strategyName: "Structured Income",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero),
            datasetReference: "dataset/structured-credit",
            feedReference: "synthetic:structured-credit",
            fundProfileId: "alpha-credit").Complete(BuildBacktestResultWithOpenLots(
                symbol: "ACME31",
                quantity: 1_000_000L,
                entryPrice: hasCompositeBasis ? 0.95m : 0.97m,
                accountId: "structured-income-account",
                accountDisplayName: "Structured Income Account",
                isShort: isShort,
                legacyLotWithoutDirection: legacyLotWithoutDirection,
                basisComponents: hasCompositeBasis
                    ? [
                        new OpenLotBasisComponent(
                            Guid.Parse("10000000-cccc-4444-dddd-333333333333"),
                            Guid.Parse("10000000-dddd-4444-eeee-444444444444"),
                            new DateTimeOffset(2026, 5, 13, 14, 0, 0, TimeSpan.Zero),
                            500_000m,
                            480_000m),
                        new OpenLotBasisComponent(
                            Guid.Parse("20000000-cccc-4444-dddd-333333333333"),
                            Guid.Parse("20000000-dddd-4444-eeee-444444444444"),
                            new DateTimeOffset(2026, 5, 14, 14, 0, 0, TimeSpan.Zero),
                            500_000m,
                            490_000m)
                    ]
                    : null)));

        var client = app.GetTestClient();
        var response = await client.GetAsync($"/api/workstation/security-master/securities/{securityId}/trust-snapshot?fundProfileId=alpha-credit");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var snapshot = await response.Content.ReadFromJsonAsync<SecurityMasterTrustSnapshotDto>(ServerJsonOptions);

        snapshot.Should().NotBeNull();
        snapshot!.TrustPosture.Tone.Should().Be(SecurityMasterTrustTone.Blocked);
        snapshot.ValidationReport.Should().NotBeNull();
        snapshot.ValidationReport!.HasBlockingIssues.Should().BeTrue();
        snapshot.ValidationReport.ErrorIssueCount.Should().Be(1);
        snapshot.IdentifierSummary.Should().NotBeNull();
        snapshot.IdentifierSummary!.ProviderMappingCount.Should().Be(2);
        snapshot.IdentifierSummary.DistinctProviderCount.Should().Be(2);
        snapshot.IdentifierSummary.HasProviderMappings.Should().BeTrue();
        snapshot.SchemaCompatibility.Should().NotBeNull();
        snapshot.SchemaCompatibility!.LegacyAssetSpecificTermsSchemaVersion.Should().Be(1);
        snapshot.SchemaCompatibility.EconomicTermsSchemaVersion.Should().Be(2);
        snapshot.SchemaCompatibility.HasLegacyAssetSpecificTerms.Should().BeTrue();
        snapshot.SchemaCompatibility.HasEconomicTerms.Should().BeTrue();
        snapshot.ChangeHistory.Should().NotBeNull();
        snapshot.ChangeHistory!.Should().ContainSingle(item =>
            item.EventType == "FactorScheduleRefreshed" &&
            item.SourceSystem == "trustee-feed" &&
            item.Origin == "Vendor");
        snapshot.ScheduleSummary.Should().NotBeNull();
        snapshot.ScheduleSummary!.SupportsCashflowSchedule.Should().BeTrue();
        snapshot.ScheduleSummary.SupportsFactorHistory.Should().BeTrue();
        snapshot.ScheduleSummary.CurrentFactor.Should().Be(0.9825m);
        snapshot.ScheduleSummary.CurrentFactorDate.Should().Be(new DateOnly(2026, 5, 1));
        snapshot.LotModel.Should().NotBeNull();
        snapshot.LotModel!.QuantityModel.Should().Be("FactorAdjustedFace");
        snapshot.LotModel.UsesFaceValue.Should().BeTrue();
        snapshot.LotModel.SupportsFactorAdjustedExposure.Should().BeTrue();
        snapshot.ScheduleBook.Should().NotBeNull();
        snapshot.ScheduleBook!.FactorHistory.Should().HaveCount(2);
        snapshot.ScheduleBook.Events.Should().Contain(item =>
            item.EventType == "InterestPayment" &&
            item.EffectiveDate == new DateOnly(2026, 6, 25) &&
            item.ExpectedAmount == 12500.00m &&
            item.SourceSystem == "trustee-feed");
        snapshot.ScheduleBook.Events.Should().Contain(item =>
            item.EventType == "PrincipalPaydown" &&
            item.EffectiveDate == new DateOnly(2026, 5, 25) &&
            item.FactorEnd == 0.0085m);
        snapshot.ScheduleBook.ProvenanceHistory.Should().Contain(item =>
            item.Category == "History" &&
            item.EventType == "FactorScheduleRefreshed" &&
            item.SourceSystem == "trustee-feed");
        snapshot.OpenLotReadModel.Should().NotBeNull();
        snapshot.OpenLotReadModel!.SupportsFactorAdjustedExposure.Should().BeTrue();
        snapshot.OpenLotReadModel.CurrentFactor.Should().Be(0.9825m);
        snapshot.OpenLotReadModel.Lots.Should().ContainSingle();
        snapshot.OpenLotReadModel.ProvenanceHistory.Should().ContainSingle(item =>
            item.RunId == "run-structured-lots" &&
            item.SourceSystem == "strategy-run-snapshot");
        var openLot = snapshot.OpenLotReadModel.Lots[0];
        openLot.SecurityId.Should().Be(securityId);
        openLot.PortfolioId.Should().Be("structured-income-backtest-portfolio");
        openLot.RunId.Should().Be("run-structured-lots");
        openLot.AccountScopeId.Should().Be("structured-income-account");
        openLot.AccountScopeDisplayName.Should().Be("Structured Income Account");
        openLot.LotId.Should().NotBeNullOrWhiteSpace();
        openLot.Symbol.Should().Be("ACME31");
        openLot.OriginalQuantity.Should().Be(1_000_000m);
        openLot.CurrentQuantity.Should().Be(1_000_000m);
        openLot.OriginalFace.Should().Be(1_000_000m);
        openLot.CurrentFace.Should().Be(982_500m);
        openLot.FactorAdjustedQuantity.Should().Be(982_500m);
        openLot.FactorAdjustedFace.Should().Be(982_500m);
        openLot.CostBasis.Should().Be(970_000m);
        openLot.EntryPrice.Should().Be(hasCompositeBasis ? 0.95m : 0.97m);
        openLot.IsShort.Should().Be(isShort);
        openLot.UnrealizedPnl.Should().Be(12_500m,
            "retained lot basis must govern both directions, including a legacy short whose direction is recovered from its account position");
        openLot.SettleDate.Should().Be(new DateTimeOffset(2026, 5, 16, 14, 0, 0, TimeSpan.Zero));
        snapshot.RecommendedActions.Should().Contain(action =>
            action.Kind == SecurityMasterRecommendedActionKind.EditSelectedSecurity &&
            action.Detail.Contains("blocking validation issue", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterTrustSnapshot_ShouldFlagUnsupportedSchemaCompatibilityInSummary()
    {
        var securityId = Guid.Parse("57575757-5757-5757-5757-575757575757");
        var queryService = new StubSecurityMasterQueryService();
        queryService.RegisterSecurity(
            CreateSecuritySummary(securityId, "Legacy Structured Note", "LSN-01"),
            new SecurityDetailDto(
                SecurityId: securityId,
                AssetClass: "Bond",
                Status: SecurityStatusDto.Active,
                DisplayName: "Legacy Structured Note",
                Currency: "USD",
                CommonTerms: JsonSerializer.SerializeToElement(new
                {
                    issuerName = "Legacy Trust",
                    countryOfRisk = "US"
                }),
                AssetSpecificTerms: JsonSerializer.SerializeToElement(new
                {
                    schemaVersion = 5,
                    maturity = "2031-12-15"
                }),
                Identifiers:
                [
                    new SecurityIdentifierDto(
                        SecurityIdentifierKind.Cusip,
                        "000000AA1",
                        true,
                        new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero))
                ],
                Aliases: [],
                Version: 4,
                EffectiveFrom: new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
                EffectiveTo: null));
        queryService.RegisterEconomicDefinition(
            securityId,
            new SecurityEconomicDefinitionRecord(
                SecurityId: securityId,
                AssetClass: "FixedIncome",
                AssetFamily: "StructuredCredit",
                SubType: "StructuredNote",
                TypeName: "Legacy Structured Note",
                IssuerType: "Trust",
                RiskCountry: "US",
                Status: SecurityStatusDto.Active,
                DisplayName: "Legacy Structured Note",
                Currency: "USD",
                Classification: JsonSerializer.SerializeToElement(new
                {
                    assetClass = "FixedIncome"
                }),
                CommonTerms: JsonSerializer.SerializeToElement(new
                {
                    issuerName = "Legacy Trust",
                    countryOfRisk = "US"
                }),
                EconomicTerms: JsonSerializer.SerializeToElement(new
                {
                    schemaVersion = 9,
                    maturity = new
                    {
                        maturityDate = "2031-12-15"
                    }
                }),
                Provenance: JsonSerializer.SerializeToElement(new
                {
                    sourceSystem = "legacy-edm",
                    asOf = "2026-05-20T10:00:00Z",
                    updatedBy = "workflow.bot"
                }),
                Version: 4,
                EffectiveFrom: new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
                EffectiveTo: null,
                Identifiers:
                [
                    new SecurityIdentifierDto(
                        SecurityIdentifierKind.Cusip,
                        "000000AA1",
                        true,
                        new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero))
                ],
                LegacyAssetClass: "Bond",
                LegacyAssetSpecificTerms: JsonSerializer.SerializeToElement(new
                {
                    schemaVersion = 5,
                    maturity = "2031-12-15"
                })));
        queryService.RegisterTradingParameters(securityId, CreateTradingParameters(securityId));

        await using var app = await CreateAppAsync(services =>
        {
            RegisterSecurityMasterWorkbenchServices(
                services,
                queryService,
                new StubSecurityMasterConflictService([]),
                new StubSecurityValidationService());
        });

        var client = app.GetTestClient();
        var response = await client.GetAsync($"/api/workstation/security-master/securities/{securityId}/trust-snapshot");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var snapshot = await response.Content.ReadFromJsonAsync<SecurityMasterTrustSnapshotDto>(ServerJsonOptions);

        snapshot.Should().NotBeNull();
        snapshot!.SchemaCompatibility.Should().NotBeNull();
        snapshot.SchemaCompatibility!.LegacyAssetSpecificTermsSchemaVersion.Should().Be(5);
        snapshot.SchemaCompatibility.EconomicTermsSchemaVersion.Should().Be(9);
        snapshot.SchemaCompatibility.Summary.Should().ContainEquivalentOf("compatibility review is required");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterTrustSnapshot_ShouldNotRunReconciliationDuringReadOnlyImpactQuery()
    {
        var securityId = Guid.Parse("78787878-7878-7878-7878-787878787878");
        var queryService = new StubSecurityMasterQueryService();
        queryService.RegisterSecurity(
            CreateSecuritySummary(securityId, "Apple Inc.", "AAPL"),
            CreateSecurityDetail(securityId, "Apple Inc.", "AAPL"));
        queryService.RegisterEconomicDefinition(
            securityId,
            CreateEconomicDefinitionRecord(
                securityId,
                "Apple Inc.",
                "AAPL",
                JsonSerializer.SerializeToElement(new { sourceSystem = "golden-edm" })));
        queryService.RegisterTradingParameters(securityId, CreateTradingParameters(securityId));

        var reconciliationService = new RecordingReconciliationRunService();

        await using var app = await CreateAppAsync(services =>
        {
            RegisterSecurityMasterWorkbenchServices(services, queryService, new StubSecurityMasterConflictService([]));
            services.RemoveAll<IReconciliationRunService>();
            services.AddSingleton<IReconciliationRunService>(reconciliationService);
        });

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "sm-readonly-impact",
            strategyId: "security-impact",
            strategyName: "Security impact",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero),
            fundProfileId: "fund-alpha"));

        var client = app.GetTestClient();
        var response = await client.GetAsync($"/api/workstation/security-master/securities/{securityId}/trust-snapshot?fundProfileId=fund-alpha");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var snapshot = await response.Content.ReadFromJsonAsync<SecurityMasterTrustSnapshotDto>(ServerJsonOptions);

        snapshot.Should().NotBeNull();
        snapshot!.DownstreamImpact.Severity.Should().Be(SecurityMasterImpactSeverity.Unknown);
        snapshot.DownstreamImpact.ReconciliationExposureSummary.Should().Contain("not been materialized");
        snapshot.DownstreamImpact.Summary.Should().Contain("not been materialized");
        reconciliationService.GetLatestForRunCallCount.Should().Be(1);
        reconciliationService.RunAsyncCallCount.Should().Be(0);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterTrustSnapshot_ShouldReturnOnlySelectedSecurityChallengers()
    {
        var selectedSecurityId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var otherSecurityId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var selectedConflictId = Guid.Parse("99999999-9999-9999-9999-999999999999");

        var queryService = new StubSecurityMasterQueryService();
        queryService.RegisterSecurity(
            CreateSecuritySummary(selectedSecurityId, "Apple Inc.", "AAPL"),
            CreateSecurityDetail(selectedSecurityId, "Apple Inc.", "AAPL"));
        queryService.RegisterSecurity(
            CreateSecuritySummary(otherSecurityId, "Microsoft Corp.", "MSFT"),
            CreateSecurityDetail(otherSecurityId, "Microsoft Corp.", "MSFT"));
        queryService.RegisterEconomicDefinition(
            selectedSecurityId,
            CreateEconomicDefinitionRecord(
                selectedSecurityId,
                "Apple Inc.",
                "AAPL",
                JsonSerializer.SerializeToElement(new { sourceSystem = "golden-edm" })));

        var conflicts = new[]
        {
            new SecurityMasterConflict(
                ConflictId: selectedConflictId,
                SecurityId: selectedSecurityId,
                ConflictKind: "IdentifierMismatch",
                FieldPath: "Identifiers.Primary",
                ProviderA: "golden-edm",
                ValueA: "AAPL",
                ProviderB: "vendor-b",
                ValueB: "AAPL.O",
                DetectedAt: new DateTimeOffset(2026, 4, 20, 10, 0, 0, TimeSpan.Zero),
                Status: "Open"),
            new SecurityMasterConflict(
                ConflictId: Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111"),
                SecurityId: otherSecurityId,
                ConflictKind: "IdentifierMismatch",
                FieldPath: "Identifiers.Primary",
                ProviderA: "golden-edm",
                ValueA: "MSFT",
                ProviderB: "vendor-c",
                ValueB: "MSFT.O",
                DetectedAt: new DateTimeOffset(2026, 4, 20, 11, 0, 0, TimeSpan.Zero),
                Status: "Open")
        };

        await using var app = await CreateAppAsync(services =>
        {
            RegisterSecurityMasterWorkbenchServices(services, queryService, new StubSecurityMasterConflictService(conflicts));
        });

        var client = app.GetTestClient();
        var response = await client.GetAsync($"/api/workstation/security-master/securities/{selectedSecurityId}/trust-snapshot");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var snapshot = await response.Content.ReadFromJsonAsync<SecurityMasterTrustSnapshotDto>(ServerJsonOptions);

        snapshot.Should().NotBeNull();
        snapshot!.ConflictAssessments.Should().ContainSingle();
        snapshot.ProvenanceCandidates.Count(candidate => !candidate.IsWinningSource).Should().Be(1);
        snapshot.ProvenanceCandidates.Should().OnlyContain(candidate =>
            candidate.IsWinningSource || candidate.ConflictId == selectedConflictId);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterTrustSnapshot_ShouldPreserveWinnerByDefault()
    {
        var securityId = Guid.Parse("bbbbbbbb-1111-1111-1111-111111111111");
        var conflictId = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");

        var queryService = new StubSecurityMasterQueryService();
        queryService.RegisterSecurity(
            CreateSecuritySummary(securityId, "Apple Inc.", "AAPL US"),
            CreateSecurityDetail(securityId, "Apple Inc.", "AAPL US"));
        queryService.RegisterEconomicDefinition(
            securityId,
            CreateEconomicDefinitionRecord(
                securityId,
                "Apple Inc.",
                "AAPL US",
                JsonSerializer.SerializeToElement(new { sourceSystem = "golden-edm" })));

        var conflict = new SecurityMasterConflict(
            ConflictId: conflictId,
            SecurityId: securityId,
            ConflictKind: "IdentifierMismatch",
            FieldPath: "Identifiers.Primary",
            ProviderA: "golden-edm",
            ValueA: "AAPL US",
            ProviderB: "vendor-b",
            ValueB: "AAPL UW",
            DetectedAt: new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero),
            Status: "Open");

        await using var app = await CreateAppAsync(services =>
        {
            RegisterSecurityMasterWorkbenchServices(services, queryService, new StubSecurityMasterConflictService([conflict]));
        });

        var client = app.GetTestClient();
        var response = await client.GetAsync($"/api/workstation/security-master/securities/{securityId}/trust-snapshot?fundProfileId=fund-alpha");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var snapshot = await response.Content.ReadFromJsonAsync<SecurityMasterTrustSnapshotDto>(ServerJsonOptions);

        snapshot.Should().NotBeNull();
        snapshot!.ConflictAssessments.Should().ContainSingle();
        snapshot.ConflictAssessments[0].Recommendation.Should().Be(SecurityMasterConflictRecommendationKind.PreserveWinner);
        snapshot.ConflictAssessments[0].RecommendedResolution.Should().Be("AcceptA");
        snapshot.ConflictAssessments[0].RecommendedWinner.Should().Contain("Preserve golden-edm");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterTrustSnapshot_ShouldDismissEquivalentNormalizedValues()
    {
        var securityId = Guid.Parse("cccccccc-1111-1111-1111-111111111111");
        var conflictId = Guid.Parse("cccccccc-2222-2222-2222-222222222222");

        var queryService = new StubSecurityMasterQueryService();
        queryService.RegisterSecurity(
            CreateSecuritySummary(securityId, "Berkshire Hathaway", "BRK-B"),
            CreateSecurityDetail(securityId, "Berkshire Hathaway", "BRK-B"));
        queryService.RegisterEconomicDefinition(
            securityId,
            CreateEconomicDefinitionRecord(
                securityId,
                "Berkshire Hathaway",
                "BRK-B",
                JsonSerializer.SerializeToElement(new { sourceSystem = "golden-edm" })));

        var conflict = new SecurityMasterConflict(
            ConflictId: conflictId,
            SecurityId: securityId,
            ConflictKind: "IdentifierMismatch",
            FieldPath: "Identifiers.Primary",
            ProviderA: "golden-edm",
            ValueA: "BRK-B",
            ProviderB: "vendor-b",
            ValueB: "BRK/B",
            DetectedAt: new DateTimeOffset(2026, 4, 20, 12, 30, 0, TimeSpan.Zero),
            Status: "Open");

        await using var app = await CreateAppAsync(services =>
        {
            RegisterSecurityMasterWorkbenchServices(services, queryService, new StubSecurityMasterConflictService([conflict]));
        });

        var client = app.GetTestClient();
        var response = await client.GetAsync($"/api/workstation/security-master/securities/{securityId}/trust-snapshot?fundProfileId=fund-alpha");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var snapshot = await response.Content.ReadFromJsonAsync<SecurityMasterTrustSnapshotDto>(ServerJsonOptions);

        snapshot.Should().NotBeNull();
        snapshot!.ConflictAssessments.Should().ContainSingle();
        snapshot.ConflictAssessments[0].Recommendation.Should().Be(SecurityMasterConflictRecommendationKind.DismissAsEquivalent);
        snapshot.ConflictAssessments[0].RecommendedResolution.Should().Be("Dismiss");
        snapshot.ConflictAssessments[0].IsBulkEligible.Should().BeTrue();
    }

    [Fact]
    public async Task MapWorkstationEndpoints_SecurityMasterBulkResolve_ShouldResolveOnlyEligibleConflicts()
    {
        var securityId = Guid.Parse("dddddddd-1111-1111-1111-111111111111");
        var equivalentConflictId = Guid.Parse("dddddddd-2222-2222-2222-222222222222");
        var blankWinnerConflictId = Guid.Parse("dddddddd-3333-3333-3333-333333333333");
        var skippedConflictId = Guid.Parse("dddddddd-4444-4444-4444-444444444444");

        var queryService = new StubSecurityMasterQueryService();
        queryService.RegisterSecurity(
            CreateSecuritySummary(securityId, "Apple Inc.", "BRK-B"),
            CreateSecurityDetail(securityId, string.Empty, "BRK-B"));
        queryService.RegisterEconomicDefinition(
            securityId,
            CreateEconomicDefinitionRecord(
                securityId,
                string.Empty,
                "BRK-B",
                JsonSerializer.SerializeToElement(new { sourceSystem = "golden-edm" })));

        var conflictService = new StubSecurityMasterConflictService(
        [
            new SecurityMasterConflict(
                ConflictId: equivalentConflictId,
                SecurityId: securityId,
                ConflictKind: "IdentifierMismatch",
                FieldPath: "Identifiers.Primary",
                ProviderA: "golden-edm",
                ValueA: "BRK-B",
                ProviderB: "vendor-b",
                ValueB: "BRK/B",
                DetectedAt: new DateTimeOffset(2026, 4, 20, 13, 0, 0, TimeSpan.Zero),
                Status: "Open"),
            new SecurityMasterConflict(
                ConflictId: blankWinnerConflictId,
                SecurityId: securityId,
                ConflictKind: "FieldMismatch",
                FieldPath: "DisplayName",
                ProviderA: "golden-edm",
                ValueA: "",
                ProviderB: "vendor-b",
                ValueB: "Apple Inc.",
                DetectedAt: new DateTimeOffset(2026, 4, 20, 13, 5, 0, TimeSpan.Zero),
                Status: "Open"),
            new SecurityMasterConflict(
                ConflictId: skippedConflictId,
                SecurityId: securityId,
                ConflictKind: "IdentifierMismatch",
                FieldPath: "Identifiers.Primary",
                ProviderA: "golden-edm",
                ValueA: "BRK-B",
                ProviderB: "vendor-b",
                ValueB: "MSFT",
                DetectedAt: new DateTimeOffset(2026, 4, 20, 13, 10, 0, TimeSpan.Zero),
                Status: "Open")
        ]);

        await using var app = await CreateAppAsync(services =>
        {
            RegisterSecurityMasterWorkbenchServices(services, queryService, conflictService);
        });

        var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync(
            "/api/workstation/security-master/conflicts/bulk-resolve",
            new BulkResolveSecurityMasterConflictsRequest(
                ConflictIds: [equivalentConflictId, blankWinnerConflictId, skippedConflictId],
                ResolvedBy: "desktop-user",
                Reason: "bulk assist",
                FundProfileId: "fund-alpha"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<BulkResolveSecurityMasterConflictsResult>(ServerJsonOptions);

        result.Should().NotBeNull();
        result!.Requested.Should().Be(3);
        result.Eligible.Should().Be(2);
        result.Resolved.Should().Be(2);
        result.Skipped.Should().Be(1);
        result.ResolvedConflictIds.Should().Contain([equivalentConflictId, blankWinnerConflictId]);
        result.SkippedReasons.Should().ContainKey(skippedConflictId);
        conflictService.ResolvedRequests.Select(request => request.ConflictId)
            .Should()
            .Contain([equivalentConflictId, blankWinnerConflictId]);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_CompareRuns_ShouldReturnMetricsForEachRun()
    {
        await using var app = await CreateAppAsync(services =>
        {
            RegisterRunReadServices(services);
        }, currentUserPermissions: UserPermission.ViewStrategies);

        var store = app.Services.GetRequiredService<IStrategyRepository>();
        await store.RecordRunAsync(BuildRun(
            runId: "cmp-1",
            strategyId: "s1",
            strategyName: "Alpha Strategy",
            runType: RunType.Paper,
            startedAt: new DateTimeOffset(2026, 3, 21, 10, 0, 0, TimeSpan.Zero)));
        await store.RecordRunAsync(BuildRun(
            runId: "cmp-2",
            strategyId: "s2",
            strategyName: "Beta Strategy",
            runType: RunType.Backtest,
            startedAt: new DateTimeOffset(2026, 3, 21, 9, 0, 0, TimeSpan.Zero)));

        var client = app.GetTestClient();
        var response = await client.PostAsJsonAsync("/api/workstation/runs/compare", new { runIds = new[] { "cmp-1", "cmp-2" } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var rows = doc.RootElement.EnumerateArray().ToArray();
        rows.Should().HaveCount(2);
        rows.Select(r => r.GetProperty("runId").GetString()).Should().Contain("cmp-1").And.Contain("cmp-2");
        rows.Select(r => r.GetProperty("strategyName").GetString()).Should().Contain("Alpha Strategy").And.Contain("Beta Strategy");
        rows.Should().OnlyContain(row => row.GetProperty("artifactCompleteness").ValueKind == JsonValueKind.Object);
        rows.Should().OnlyContain(row => row.GetProperty("compatibilityWarnings").ValueKind == JsonValueKind.Array);
    }
}
