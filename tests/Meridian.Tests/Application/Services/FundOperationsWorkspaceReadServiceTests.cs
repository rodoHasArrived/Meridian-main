using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Application.Services;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Reporting;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Application.Services;

public sealed class FundOperationsWorkspaceReadServiceTests
{
    [Fact]
    public async Task GetWorkspaceAsync_WithRunsAccountsAndBanking_ReturnsAggregatedWorkspace()
    {
        var fundProfileId = $"fund-{Guid.NewGuid():N}";
        var fundId = TranslateFundProfileId(fundProfileId);
        var siblingFundProfileId = $"fund-sibling-{Guid.NewGuid():N}";
        var siblingFundId = TranslateFundProfileId(siblingFundProfileId);
        var siblingEntityId = Guid.NewGuid();
        var accountService = new InMemoryFundAccountService();
        var repository = new StrategyRunStore();
        var portfolioReadService = new PortfolioReadService();
        var securityMaster = new NullSecurityMasterQueryService();
        var service = new FundOperationsWorkspaceReadService(
            accountService,
            repository,
            portfolioReadService,
            new NavAttributionService(securityMaster),
            new ReportGenerationService(securityMaster));

        var bankAccount = await accountService.CreateAccountAsync(new CreateAccountRequest(
            AccountId: Guid.NewGuid(),
            AccountType: AccountTypeDto.Bank,
            AccountCode: "BANK-001",
            DisplayName: "Operating Cash",
            BaseCurrency: "USD",
            EffectiveFrom: new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero),
            CreatedBy: "test",
            FundId: fundId,
            LedgerReference: "FUND-TB",
            BankDetails: new BankAccountDetailsDto(
                AccountNumber: "1234567890",
                BankName: "Meridian Bank",
                BranchName: null,
                Iban: null,
                BicSwift: null,
                RoutingNumber: null,
                SortCode: null,
                IntermediaryBankBic: null,
                IntermediaryBankName: null,
                BeneficiaryName: null,
                BeneficiaryAddress: null)));
        var custodyAccount = await accountService.CreateAccountAsync(new CreateAccountRequest(
            AccountId: Guid.NewGuid(),
            AccountType: AccountTypeDto.Custody,
            AccountCode: "CUST-001",
            DisplayName: "Core Custody",
            BaseCurrency: "USD",
            EffectiveFrom: new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero),
            CreatedBy: "test",
            FundId: fundId,
            LedgerReference: "FUND-TB"));
        var siblingAccount = await accountService.CreateAccountAsync(new CreateAccountRequest(
            AccountId: Guid.NewGuid(),
            AccountType: AccountTypeDto.Custody,
            AccountCode: "CUST-002",
            DisplayName: "Sibling Custody",
            BaseCurrency: "USD",
            EffectiveFrom: new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero),
            CreatedBy: "test",
            FundId: siblingFundId,
            EntityId: siblingEntityId,
            LedgerReference: "SIBLING-TB"));

        await accountService.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
            AccountId: bankAccount.AccountId,
            AsOfDate: new DateOnly(2026, 4, 11),
            Currency: "USD",
            CashBalance: 2_500m,
            Source: "bank",
            RecordedBy: "test",
            PendingSettlement: 150m));
        await accountService.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
            AccountId: custodyAccount.AccountId,
            AsOfDate: new DateOnly(2026, 4, 11),
            Currency: "USD",
            CashBalance: 750m,
            Source: "custody",
            RecordedBy: "test",
            SecuritiesMarketValue: 400m));
        await accountService.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
            AccountId: siblingAccount.AccountId,
            AsOfDate: new DateOnly(2026, 4, 11),
            Currency: "USD",
            CashBalance: 1_000m,
            Source: "custody",
            RecordedBy: "test",
            SecuritiesMarketValue: 200m));
        await accountService.IngestBankStatementAsync(new IngestBankStatementRequest(
            BatchId: Guid.NewGuid(),
            AccountId: bankAccount.AccountId,
            StatementDate: new DateOnly(2026, 4, 11),
            BankName: "Meridian Bank",
            Notes: "test",
            Lines:
            [
                new BankStatementLineDto(
                    LineId: Guid.NewGuid(),
                    BatchId: Guid.NewGuid(),
                    AccountId: bankAccount.AccountId,
                    TransactionDate: new DateOnly(2026, 4, 11),
                    ValueDate: new DateOnly(2026, 4, 11),
                    Amount: 250m,
                    Currency: "USD",
                    TransactionType: "Contribution",
                    Description: "Capital contribution",
                    Reference: "BANK-REF-001",
                    ClosingBalance: 2_500m)
            ],
            LoadedBy: "test"));

        await repository.RecordRunAsync(BuildRun(
            runId: "run-governance-001",
            strategyId: "carry-1",
            strategyName: "Carry Strategy",
            fundProfileId: fundProfileId,
            fundDisplayName: "Alpha Income Fund",
            realizedPnl: 20m,
            unrealizedPnl: 30m,
            positionPnl: new Dictionary<string, (decimal RealizedPnl, decimal UnrealizedPnl)>(StringComparer.OrdinalIgnoreCase)
            {
                ["AAPL"] = (30m, 30m),
                ["HEDGE"] = (-10m, 0m)
            }));
        await repository.RecordRunAsync(BuildRun(
            runId: "run-sibling-001",
            strategyId: "sibling-1",
            strategyName: "Sibling Strategy",
            fundProfileId: siblingFundProfileId,
            fundDisplayName: "Sibling Income Fund",
            realizedPnl: 15m,
            unrealizedPnl: 30m));

        var workspace = await service.GetWorkspaceAsync(new FundOperationsWorkspaceQuery(
            FundProfileId: fundProfileId,
            AsOf: new DateTimeOffset(2026, 4, 11, 16, 0, 0, TimeSpan.Zero),
            Currency: "USD"));

        workspace.DisplayName.Should().Be("Alpha Income Fund");
        workspace.RecordedRunCount.Should().Be(1);
        workspace.RelatedRunIds.Should().ContainSingle().Which.Should().Be("run-governance-001");
        workspace.Accounts.Should().HaveCount(2);
        workspace.BankSnapshots.Should().ContainSingle(snapshot => snapshot.AccountId == bankAccount.AccountId);
        workspace.CashFinancing.PendingSettlement.Should().Be(150m);
        workspace.Ledger.JournalEntryCount.Should().BeGreaterThan(0);
        workspace.Ledger.TrialBalance.Should().NotBeEmpty();
        workspace.LedgerReconciliationSnapshot.AsOf.Should().Be(new DateTimeOffset(2026, 4, 11, 16, 0, 0, TimeSpan.Zero));
        workspace.LedgerReconciliationSnapshot.Consolidated.JournalEntryCount.Should().BeGreaterThan(0);
        workspace.LedgerReconciliationSnapshot.Consolidated.LedgerEntryCount.Should().BeGreaterThan(0);
        workspace.LedgerReconciliationSnapshot.Consolidated.Balances.Should().NotBeEmpty();
        workspace.LedgerReconciliationSnapshot.Entities.Should().BeEmpty();
        workspace.LedgerReconciliationSnapshot.Sleeves.Should().BeEmpty();
        workspace.LedgerReconciliationSnapshot.Vehicles.Should().BeEmpty();
        workspace.Nav.ComponentCount.Should().BeGreaterThan(0);
        workspace.Reporting.ProfileCount.Should().BeGreaterThan(0);
        workspace.Reporting.FundProfileId.Should().Be(fundProfileId);
        workspace.Reporting.BrandingThemes.Should().NotBeNull();
        workspace.Reporting.BrandingThemes!.Should().Contain(theme =>
            theme.ThemeId == "meridian-standard" &&
            theme.IsBuiltIn &&
            theme.PrimaryColor == "#195E63");
        workspace.Reporting.PortfolioCuts.Should().NotBeNull();
        workspace.Reporting.PortfolioCuts!.Should().Contain(cut =>
            cut.Kind == PortfolioReportingCutKindDto.Fund &&
            cut.CutId == "fund:consolidated" &&
            cut.GrossExposure == workspace.CashFinancing.GrossExposure &&
            cut.TotalPnl == 50m &&
            cut.ShadowNav == workspace.Nav.TotalNav);
        workspace.Reporting.PortfolioCuts.Should().Contain(cut =>
            cut.Kind == PortfolioReportingCutKindDto.Strategy &&
            cut.Label == "Carry Strategy" &&
            cut.SourceCount == 1 &&
            cut.TotalPnl == 50m &&
            cut.ShadowNav == 1_150m &&
            cut.Tags.Contains("run-governance-001"));
        workspace.Reporting.PortfolioCuts.Should().Contain(cut =>
            cut.Kind == PortfolioReportingCutKindDto.UserTag &&
            cut.SourceCount == 2 &&
            cut.TotalCash == 3_250m &&
            cut.ShadowNav == 3_650m);
        workspace.Reporting.PnlSlices.Should().NotBeNull();
        workspace.Reporting.PnlSlices!.Should().HaveCount(4);
        workspace.Reporting.PnlSlices.Select(slice => slice.Period).Should().BeEquivalentTo(
            [
                PortfolioReportingPnlSlicePeriodDto.Daily,
                PortfolioReportingPnlSlicePeriodDto.Weekly,
                PortfolioReportingPnlSlicePeriodDto.Monthly,
                PortfolioReportingPnlSlicePeriodDto.Yearly
            ]);
        var dailyPnlSlice = workspace.Reporting.PnlSlices.Should().ContainSingle(slice =>
                slice.Period == PortfolioReportingPnlSlicePeriodDto.Daily)
            .Which;
        dailyPnlSlice.StartDate.Should().Be(new DateOnly(2026, 4, 11));
        dailyPnlSlice.EndDate.Should().Be(new DateOnly(2026, 4, 11));
        dailyPnlSlice.RealizedPnl.Should().Be(20m);
        dailyPnlSlice.UnrealizedPnl.Should().Be(30m);
        dailyPnlSlice.TotalPnl.Should().Be(50m);
        dailyPnlSlice.PnlChange.Should().Be(50m);
        dailyPnlSlice.SourceCount.Should().Be(1);
        dailyPnlSlice.Route.Should().Be("/api/workstation/reporting?pnlSlice=daily");
        dailyPnlSlice.ReadinessSummary.Should().Contain("1 source-backed run(s) in the daily window");
        workspace.Reporting.PnlSlices.Should().Contain(slice =>
            slice.Period == PortfolioReportingPnlSlicePeriodDto.Weekly &&
            slice.StartDate == new DateOnly(2026, 4, 5) &&
            slice.EndDate == new DateOnly(2026, 4, 11) &&
            slice.TotalPnl == 50m);
        workspace.Reporting.PnlSlices.Should().Contain(slice =>
            slice.Period == PortfolioReportingPnlSlicePeriodDto.Monthly &&
            slice.StartDate == new DateOnly(2026, 4, 1) &&
            slice.EndDate == new DateOnly(2026, 4, 11) &&
            slice.TotalPnl == 50m);
        workspace.Reporting.PnlSlices.Should().Contain(slice =>
            slice.Period == PortfolioReportingPnlSlicePeriodDto.Yearly &&
            slice.StartDate == new DateOnly(2026, 1, 1) &&
            slice.EndDate == new DateOnly(2026, 4, 11) &&
            slice.TotalPnl == 50m &&
            slice.VersionStamp == "pnl-slice:20260411160000:yearly:sources-1:prior-0");
        workspace.Reporting.AnalyticsRows.Should().NotBeNull();
        workspace.Reporting.AnalyticsRows!.Should().Contain(row =>
            row.Kind == PortfolioReportingAnalyticsKindDto.TopWinner &&
            row.Scope == PortfolioReportingAnalyticsScopeDto.Security &&
            row.Rank == 1 &&
            row.Symbol == "AAPL" &&
            row.TotalPnl == 60m &&
            row.ContributionPercent == 120m &&
            row.HeatMapIntensity == 85.7143m &&
            row.Route == "/api/workstation/reporting?analyticsId=analytics%3Atopwinner%3Asecurity%3Aaapl");
        workspace.Reporting.AnalyticsRows.Should().Contain(row =>
            row.Kind == PortfolioReportingAnalyticsKindDto.TopLaggard &&
            row.Scope == PortfolioReportingAnalyticsScopeDto.Security &&
            row.Rank == 1 &&
            row.Symbol == "HEDGE" &&
            row.TotalPnl == -10m &&
            row.ContributionPercent == -20m &&
            row.HeatMapIntensity == 14.2857m);
        workspace.Reporting.AnalyticsRows.Should().Contain(row =>
            row.Kind == PortfolioReportingAnalyticsKindDto.Contribution &&
            row.Scope == PortfolioReportingAnalyticsScopeDto.Strategy &&
            row.Label == "Carry Strategy" &&
            row.TotalPnl == 50m &&
            row.ReadinessSummary.Contains("contribution is 100% of portfolio P&L", StringComparison.Ordinal));
        workspace.Reporting.LivePortfolioViews.Should().NotBeNull();
        var fundLiveView = workspace.Reporting.LivePortfolioViews!.Should().ContainSingle(view =>
                view.ViewId == "live:fund:consolidated")
            .Which;
        fundLiveView.State.Should().Be(PortfolioReportingLiveViewStateDto.SourceBacked);
        fundLiveView.SourceCount.Should().Be(3);
        fundLiveView.SourceAsOfUtc.Should().Be(new DateTimeOffset(2026, 4, 11, 14, 30, 0, TimeSpan.Zero));
        fundLiveView.FreshnessPolicy.Should().NotBeNull();
        fundLiveView.FreshnessPolicy!.PolicyName.Should().Be("LivePortfolioView");
        fundLiveView.FreshnessPolicy.SourceAgeSeconds.Should().Be(5400);
        fundLiveView.FreshnessPolicy.LiveLinkWindowSeconds.Should().Be(300);
        fundLiveView.FreshnessPolicy.StaleWindowSeconds.Should().Be(86400);
        fundLiveView.FreshnessPolicy.IsWithinLiveLinkWindow.Should().BeFalse();
        fundLiveView.FreshnessPolicy.IsBeyondStaleWindow.Should().BeFalse();
        fundLiveView.FreshnessPolicy.Reason.Should().Contain("outside the live-link window");
        fundLiveView.ReadinessBlockers.Should().BeEmpty();
        fundLiveView.Route.Should().Be("/api/workstation/portfolio/summary?fundAccountId=all&strategyId=all&entity=portfolio");
        fundLiveView.LiquiditySummary.Should().Contain("pending settlement");
        var strategyLiveView = workspace.Reporting.LivePortfolioViews.Should().ContainSingle(view =>
                view.ViewId == "live:strategy:carry-1")
            .Which;
        strategyLiveView.State.Should().Be(PortfolioReportingLiveViewStateDto.SourceBacked);
        strategyLiveView.CashLadderRoute.Should().Be("/api/portfolio/run-governance-001/cash-flows");
        strategyLiveView.Route.Should().Contain("strategyId=carry-1");
        workspace.Reporting.CrossFundConsolidations.Should().NotBeNull();
        var companyConsolidation = workspace.Reporting.CrossFundConsolidations!.Should().ContainSingle(row =>
                row.ConsolidationId == "cross-fund:company")
            .Which;
        companyConsolidation.IsReady.Should().BeTrue();
        companyConsolidation.FundCount.Should().Be(2);
        companyConsolidation.EntityCount.Should().Be(1);
        companyConsolidation.AccountCount.Should().Be(3);
        companyConsolidation.RunCount.Should().Be(2);
        companyConsolidation.SourceCount.Should().Be(5);
        companyConsolidation.TotalPnl.Should().Be(95m);
        companyConsolidation.Route.Should().Be("/api/workstation/reporting?consolidationId=cross-fund%3Acompany");
        workspace.Reporting.CrossFundConsolidations.Should().Contain(row =>
            row.Scope == CrossFundReportingConsolidationScopeDto.Fund &&
            row.Label == "Sibling Income Fund" &&
            row.RunCount == 1 &&
            row.AccountCount == 1);
        workspace.Reporting.CrossFundConsolidations.Should().Contain(row =>
            row.Scope == CrossFundReportingConsolidationScopeDto.Entity &&
            row.EntityCount == 1 &&
            row.AccountCount == 1);
        workspace.Reporting.ReportWriterDatasetSources.Should().NotBeNull();
        workspace.Reporting.ReportWriterDatasetSources!.Select(static source => source.SourceId).Should().Contain(
            [
                "retained-reporting-rows",
                "portfolio-reporting-cuts",
                "topn-contribution-analytics",
                "cross-fund-consolidation",
                "certified-operational-data-mart"
            ]);
        var reportWriterSource = workspace.Reporting.ReportWriterDatasetSources!.Should()
            .ContainSingle(static source => source.SourceId == "retained-reporting-rows")
            .Subject;
        reportWriterSource.SourceId.Should().Be("retained-reporting-rows");
        reportWriterSource.RowCount.Should().Be(
            workspace.Reporting.PortfolioCuts!.Count
            + workspace.Reporting.AnalyticsRows!.Count
            + workspace.Reporting.CrossFundConsolidations!.Count);
        var expectedGrossExposure = workspace.CashFinancing.GrossExposure.ToString(CultureInfo.InvariantCulture);
        reportWriterSource.Rows.Any(IsExpectedPortfolioDatasetRow).Should().BeTrue();
        reportWriterSource.Rows.Any(IsExpectedAnalyticsDatasetRow).Should().BeTrue();
        reportWriterSource.Rows.Any(IsExpectedCrossFundDatasetRow).Should().BeTrue();
        var reportWriterFieldNames = reportWriterSource.Fields.Select(static field => field.Name).ToArray();
        reportWriterFieldNames.Should().Contain("grossExposure");
        reportWriterFieldNames.Should().Contain("contributionPercent");
        reportWriterFieldNames.Should().Contain("shadowNav");

        var portfolioDatasetSource = workspace.Reporting.ReportWriterDatasetSources!.Should()
            .ContainSingle(static source => source.SourceId == "portfolio-reporting-cuts")
            .Subject;
        portfolioDatasetSource.RowCount.Should().Be(workspace.Reporting.PortfolioCuts!.Count);
        portfolioDatasetSource.Rows.All(static row => HasDataset(row, "portfolio-cut")).Should().BeTrue();

        var analyticsDatasetSource = workspace.Reporting.ReportWriterDatasetSources!.Should()
            .ContainSingle(static source => source.SourceId == "topn-contribution-analytics")
            .Subject;
        analyticsDatasetSource.RowCount.Should().Be(workspace.Reporting.AnalyticsRows!.Count);
        analyticsDatasetSource.Rows.All(static row => HasDataset(row, "portfolio-analytics")).Should().BeTrue();

        var crossFundDatasetSource = workspace.Reporting.ReportWriterDatasetSources!.Should()
            .ContainSingle(static source => source.SourceId == "cross-fund-consolidation")
            .Subject;
        crossFundDatasetSource.RowCount.Should().Be(workspace.Reporting.CrossFundConsolidations!.Count);
        crossFundDatasetSource.Rows.All(static row => HasDataset(row, "cross-fund-consolidation")).Should().BeTrue();

        var certifiedMartSource = workspace.Reporting.ReportWriterDatasetSources!.Should()
            .ContainSingle(static source => source.SourceId == "certified-operational-data-mart")
            .Subject;
        certifiedMartSource.CertificationState.Should().Be("SourceBacked");
        certifiedMartSource.ValidationState.Should().Be("Passed");
        certifiedMartSource.ReconciliationState.Should().Be("Linked");
        certifiedMartSource.LineageManifest.Should().Contain("datasetSourceId=certified-operational-data-mart");
        certifiedMartSource.SourceRunIds.Should().NotBeNullOrEmpty();
        certifiedMartSource.PermittedConsumers.Should().Contain("DataWarehouse");
        var hasCertifiedMartEvidenceRow = certifiedMartSource.Rows.Any(static row =>
            row.TryGetValue("rowLineageKey", out var rowLineageKey) &&
            rowLineageKey.Contains("certified-operational-data-mart:portfolio-cut:", StringComparison.Ordinal) &&
            row.TryGetValue("evidenceIndex", out var evidenceIndex) &&
            evidenceIndex.Contains("/api/workstation/evidence/search", StringComparison.Ordinal) &&
            row.TryGetValue("certificationState", out var certificationState) &&
            certificationState == "SourceBacked");
        hasCertifiedMartEvidenceRow.Should().BeTrue("certified operational data mart rows should retain lineage and evidence search metadata");

        static bool HasDataset(IReadOnlyDictionary<string, string> row, string expectedDataset) =>
            row.TryGetValue("dataset", out var dataset) && dataset == expectedDataset;

        bool IsExpectedPortfolioDatasetRow(IReadOnlyDictionary<string, string> row) =>
            row.TryGetValue("dataset", out var dataset) && dataset == "portfolio-cut" &&
            row.TryGetValue("cutId", out var cutId) && cutId == "fund:consolidated" &&
            row.TryGetValue("grossExposure", out var grossExposure) && grossExposure == expectedGrossExposure &&
            row.TryGetValue("totalPnl", out var totalPnl) && totalPnl == "50";

        static bool IsExpectedAnalyticsDatasetRow(IReadOnlyDictionary<string, string> row) =>
            row.TryGetValue("dataset", out var dataset) && dataset == "portfolio-analytics" &&
            row.TryGetValue("analyticsId", out var analyticsId) && analyticsId == "analytics:topwinner:security:aapl" &&
            row.TryGetValue("contributionPercent", out var contributionPercent) && contributionPercent == "120";

        static bool IsExpectedCrossFundDatasetRow(IReadOnlyDictionary<string, string> row) =>
            row.TryGetValue("dataset", out var dataset) && dataset == "cross-fund-consolidation" &&
            row.TryGetValue("consolidationId", out var consolidationId) && consolidationId == "cross-fund:company" &&
            row.TryGetValue("totalPnl", out var totalPnl) && totalPnl == "95";
        workspace.Reporting.StructuredExports.Should().NotBeNull();
        workspace.Reporting.StructuredExports!.Should().Contain(export =>
            export.ExportId == "regulatory-trial-balance" &&
            export.Purpose == StructuredReportingExportPurposeDto.Regulatory &&
            export.Format == GovernanceReportArtifactFormatDto.Csv &&
            export.RowCount == workspace.Ledger.TrialBalance.Count &&
            export.RowLineageCount == export.RowCount &&
            export.IsReady);
        workspace.Reporting.StructuredExports.Should().Contain(export =>
            export.ExportId == "warehouse-ledger-facts" &&
            export.Purpose == StructuredReportingExportPurposeDto.DataWarehouse &&
            export.Format == GovernanceReportArtifactFormatDto.Json &&
            export.Dataset == "ledger-reconciliation-facts" &&
            export.RowCount == workspace.LedgerReconciliationSnapshot.Consolidated.Balances.Count &&
            export.RowLineageCount == export.RowCount &&
            export.FieldCount == 9 &&
            export.Route.Contains("warehouse-ledger-facts", StringComparison.Ordinal) &&
            export.ValidationSummary!.Contains("downstream warehouse loading", StringComparison.Ordinal) &&
            export.IsReady);
        workspace.Reporting.StructuredExports.Should().Contain(export =>
            export.ExportId == "investment-portfolio-cuts" &&
            export.Purpose == StructuredReportingExportPurposeDto.InvestmentDecision &&
            export.Format == GovernanceReportArtifactFormatDto.Xlsx &&
            export.RowCount == workspace.Reporting.PortfolioCuts!.Count &&
            export.RowLineageCount == export.RowCount &&
            export.Route.Contains("fundProfileId=", StringComparison.Ordinal) &&
            export.Route.Contains("format=xlsx", StringComparison.Ordinal) &&
            export.VersionStamp!.StartsWith("structured-export:20260411160000", StringComparison.Ordinal));
        workspace.Reporting.StructuredExports.Should().Contain(export =>
            export.ExportId == "investment-topn-contribution-analytics" &&
            export.Purpose == StructuredReportingExportPurposeDto.InvestmentDecision &&
            export.Format == GovernanceReportArtifactFormatDto.Csv &&
            export.RowCount == workspace.Reporting.AnalyticsRows!.Count &&
            export.RowLineageCount == export.RowCount &&
            export.FieldCount == 18 &&
            export.IsReady);
        workspace.Reporting.StructuredExports.Should().Contain(export =>
            export.ExportId == "cross-fund-consolidation" &&
            export.Purpose == StructuredReportingExportPurposeDto.InvestmentDecision &&
            export.Format == GovernanceReportArtifactFormatDto.Xlsx &&
            export.Route.Contains("format=xlsx", StringComparison.Ordinal) &&
            export.RowCount == workspace.Reporting.CrossFundConsolidations!.Count &&
            export.RowLineageCount == export.RowCount &&
            export.SourceCount == companyConsolidation.SourceCount &&
            export.IsReady);
        var portfolioExport = await service.GetStructuredReportingExportAsync(new StructuredReportingExportRequestDto(
            fundProfileId,
            "investment-portfolio-cuts",
            new DateTimeOffset(2026, 4, 11, 16, 0, 0, TimeSpan.Zero),
            "USD"));
        portfolioExport.Export.ExportId.Should().Be("investment-portfolio-cuts");
        portfolioExport.Columns.Should().Contain(column => column.Name == "shadowNav");
        portfolioExport.Rows.Should().Contain(row =>
            row["cutId"] == "fund:consolidated" &&
            row["totalPnl"] == "50" &&
            row["shadowNav"] == "2000");
        portfolioExport.RowLineage.Should().NotBeNull();
        portfolioExport.Export.RowLineageCount.Should().Be(portfolioExport.RowLineage!.Count);
        var warehouseExport = await service.GetStructuredReportingExportAsync(new StructuredReportingExportRequestDto(
            fundProfileId,
            "warehouse-ledger-facts",
            new DateTimeOffset(2026, 4, 11, 16, 0, 0, TimeSpan.Zero),
            "USD"));
        warehouseExport.Export.ExportId.Should().Be("warehouse-ledger-facts");
        warehouseExport.Export.Purpose.Should().Be(StructuredReportingExportPurposeDto.DataWarehouse);
        warehouseExport.Columns.Select(static column => column.Name).Should().ContainInOrder(
            "scope",
            "accountName",
            "accountType",
            "symbol",
            "financialAccountId",
            "balance",
            "journalEntryCount",
            "ledgerEntryCount",
            "sourceAsOfUtc");
        warehouseExport.Rows.Should().Contain(row =>
            row["scope"] == "Consolidated" &&
            row["accountName"] == "Cash" &&
            row["journalEntryCount"] == workspace.LedgerReconciliationSnapshot.Consolidated.JournalEntryCount.ToString() &&
            row["ledgerEntryCount"] == workspace.LedgerReconciliationSnapshot.Consolidated.LedgerEntryCount.ToString());
        warehouseExport.RowLineage.Should().NotBeNull();
        warehouseExport.Export.RowLineageCount.Should().Be(warehouseExport.RowLineage!.Count);
        var analyticsExport = await service.GetStructuredReportingExportAsync(new StructuredReportingExportRequestDto(
            fundProfileId,
            "investment-topn-contribution-analytics",
            new DateTimeOffset(2026, 4, 11, 16, 0, 0, TimeSpan.Zero),
            "USD"));
        analyticsExport.Export.ExportId.Should().Be("investment-topn-contribution-analytics");
        analyticsExport.Columns.Should().Contain(column => column.Name == "contributionPercent");
        analyticsExport.Columns.Should().Contain(column => column.Name == "heatMapIntensity");
        analyticsExport.Rows.Should().Contain(row =>
            row["analyticsId"] == "analytics:topwinner:security:aapl" &&
            row["kind"] == "TopWinner" &&
            row["scope"] == "Security" &&
            row["symbol"] == "AAPL" &&
            row["totalPnl"] == "60" &&
            row["contributionPercent"] == "120.0" &&
            row["heatMapIntensity"] == "85.7143");
        analyticsExport.RowLineage.Should().NotBeNull();
        analyticsExport.Export.RowLineageCount.Should().Be(analyticsExport.RowLineage!.Count);
        var crossFundExport = await service.GetStructuredReportingExportAsync(new StructuredReportingExportRequestDto(
            fundProfileId,
            "cross-fund-consolidation",
            new DateTimeOffset(2026, 4, 11, 16, 0, 0, TimeSpan.Zero),
            "USD"));
        crossFundExport.Export.ExportId.Should().Be("cross-fund-consolidation");
        crossFundExport.Columns.Should().Contain(column => column.Name == "fundCount");
        crossFundExport.Rows.Should().Contain(row =>
            row["consolidationId"] == "cross-fund:company" &&
            row["fundCount"] == "2" &&
            row["sourceCount"] == "5");
        crossFundExport.RowLineage.Should().NotBeNull();
        crossFundExport.Export.RowLineageCount.Should().Be(crossFundExport.RowLineage!.Count);
        Func<Task> missingExport = () => service.GetStructuredReportingExportAsync(new StructuredReportingExportRequestDto(
            fundProfileId,
            "missing-export",
            new DateTimeOffset(2026, 4, 11, 16, 0, 0, TimeSpan.Zero),
            "USD"));
        await missingExport.Should().ThrowAsync<KeyNotFoundException>();
        workspace.Workspace.TotalAccounts.Should().Be(2);
        workspace.Governance.Should().NotBeNull();
        workspace.Governance!.DecisionPosture.Should().NotBeNullOrWhiteSpace();
        workspace.Governance.DecisionPosture.Should().Contain("shared Accounting state");
        workspace.Governance.DecisionPosture.Should().NotContain("shared governance state");
        workspace.Governance.SignoffPosture.Should().NotBeNullOrWhiteSpace();
        workspace.Governance.CloseReadiness.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetWorkspaceAsync_WithNoPortfolioSources_MarksLivePortfolioViewBlocked()
    {
        var service = new FundOperationsWorkspaceReadService(
            new InMemoryFundAccountService(),
            new StrategyRunStore(),
            new PortfolioReadService(),
            new NavAttributionService(new NullSecurityMasterQueryService()),
            new ReportGenerationService(new NullSecurityMasterQueryService()));

        var workspace = await service.GetWorkspaceAsync(new FundOperationsWorkspaceQuery(
            FundProfileId: $"fund-empty-live-{Guid.NewGuid():N}",
            AsOf: new DateTimeOffset(2026, 4, 11, 16, 0, 0, TimeSpan.Zero),
            Currency: "USD"));

        var liveView = workspace.Reporting.LivePortfolioViews.Should().ContainSingle(view =>
                view.ViewId == "live:fund:consolidated")
            .Which;
        liveView.State.Should().Be(PortfolioReportingLiveViewStateDto.Blocked);
        liveView.SourceCount.Should().Be(0);
        liveView.SourceAsOfUtc.Should().BeNull();
        liveView.FreshnessPolicy.Should().NotBeNull();
        liveView.FreshnessPolicy!.SourceAgeSeconds.Should().BeNull();
        liveView.FreshnessPolicy.IsWithinLiveLinkWindow.Should().BeFalse();
        liveView.FreshnessPolicy.IsBeyondStaleWindow.Should().BeFalse();
        liveView.FreshnessPolicy.Reason.Should().Contain("fails closed");
        liveView.TelemetrySummary.Should().Contain("No source-backed portfolio records");
        liveView.ReadinessBlockers.Should().ContainSingle(blocker =>
            blocker.Contains("No source-backed portfolio records", StringComparison.Ordinal));
        workspace.Reporting.PnlSlices.Should().NotBeNull();
        workspace.Reporting.PnlSlices!.Should().HaveCount(4);
        workspace.Reporting.PnlSlices.Should().OnlyContain(slice =>
            slice.SourceCount == 0 &&
            slice.TotalPnl == 0m &&
            slice.ReadinessSummary.StartsWith("Blocked: no source-backed P&L runs", StringComparison.Ordinal));
        workspace.Reporting.AnalyticsRows.Should().ContainSingle(row =>
            row.AnalyticsId == "analytics:blocked" &&
            row.SourceCount == 0 &&
            row.TotalPnl == 0m &&
            row.ReadinessSummary.StartsWith("Blocked: no source-backed portfolio runs", StringComparison.Ordinal));
        workspace.Reporting.StructuredExports.Should().Contain(export =>
            export.ExportId == "investment-topn-contribution-analytics" &&
            !export.IsReady &&
            export.RowCount == 1 &&
            export.SourceCount == 0 &&
            export.ValidationSummary!.StartsWith("No source-backed Top-N or contribution analytics rows", StringComparison.Ordinal));
        var crossFundRow = workspace.Reporting.CrossFundConsolidations.Should().ContainSingle(row =>
                row.ConsolidationId == "cross-fund:company")
            .Which;
        crossFundRow.IsReady.Should().BeFalse();
        crossFundRow.SourceCount.Should().Be(0);
        crossFundRow.ReadinessSummary.Should().Contain("No active fund accounts");
    }

    [Fact]
    public async Task GetWorkspaceAsync_WithFreshPortfolioSource_MarksLivePortfolioViewLiveLinked()
    {
        var fundProfileId = $"fund-live-linked-{Guid.NewGuid():N}";
        var repository = new StrategyRunStore();
        var service = new FundOperationsWorkspaceReadService(
            new InMemoryFundAccountService(),
            repository,
            new PortfolioReadService(),
            new NavAttributionService(new NullSecurityMasterQueryService()),
            new ReportGenerationService(new NullSecurityMasterQueryService()));

        await repository.RecordRunAsync(BuildRun(
            runId: "run-live-linked-001",
            strategyId: "live-1",
            strategyName: "Live Strategy",
            fundProfileId: fundProfileId,
            fundDisplayName: "Live Linked Fund",
            startedAtUtc: new DateTimeOffset(2026, 4, 11, 15, 25, 0, TimeSpan.Zero)));

        var workspace = await service.GetWorkspaceAsync(new FundOperationsWorkspaceQuery(
            FundProfileId: fundProfileId,
            AsOf: new DateTimeOffset(2026, 4, 11, 16, 0, 0, TimeSpan.Zero),
            Currency: "USD"));

        var fundLiveView = workspace.Reporting.LivePortfolioViews.Should().ContainSingle(view =>
                view.ViewId == "live:fund:consolidated")
            .Which;
        fundLiveView.State.Should().Be(PortfolioReportingLiveViewStateDto.LiveLinked);
        fundLiveView.SourceAsOfUtc.Should().Be(new DateTimeOffset(2026, 4, 11, 15, 55, 0, TimeSpan.Zero));
        fundLiveView.FreshnessPolicy.Should().NotBeNull();
        fundLiveView.FreshnessPolicy!.SourceAgeSeconds.Should().Be(300);
        fundLiveView.FreshnessPolicy.IsWithinLiveLinkWindow.Should().BeTrue();
        fundLiveView.FreshnessPolicy.IsBeyondStaleWindow.Should().BeFalse();
        fundLiveView.FreshnessPolicy.Reason.Should().Contain("inside the 5-minute live-link window");
        fundLiveView.TelemetrySummary.Should().Contain("Live-linked portfolio telemetry is current through");
        fundLiveView.ReadinessBlockers.Should().BeEmpty();

        var strategyLiveView = workspace.Reporting.LivePortfolioViews.Should().ContainSingle(view =>
                view.ViewId == "live:strategy:live-1")
            .Which;
        strategyLiveView.State.Should().Be(PortfolioReportingLiveViewStateDto.LiveLinked);
        strategyLiveView.CashLadderRoute.Should().Be("/api/portfolio/run-live-linked-001/cash-flows");
    }

    [Fact]
    public async Task GetWorkspaceAsync_WithReportPackWorkflowService_ReturnsRestatementRecordsInReportingSummary()
    {
        var fundProfileId = $"fund-{Guid.NewGuid():N}";
        var fundId = TranslateFundProfileId(fundProfileId);
        var siblingFundProfileId = $"fund-sibling-{Guid.NewGuid():N}";
        var siblingFundId = TranslateFundProfileId(siblingFundProfileId);
        var siblingEntityId = Guid.NewGuid();
        var accountService = new InMemoryFundAccountService();
        var repository = new StrategyRunStore();
        var portfolioReadService = new PortfolioReadService();
        var securityMaster = new NullSecurityMasterQueryService();
        var workflowService = new ReportPackWorkflowService();
        var account = await accountService.CreateAccountAsync(new CreateAccountRequest(
            AccountId: Guid.NewGuid(),
            AccountType: AccountTypeDto.Custody,
            AccountCode: "CUST-RPT",
            DisplayName: "Report custody",
            BaseCurrency: "USD",
            EffectiveFrom: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            CreatedBy: "test",
            FundId: fundId,
            LedgerReference: "RPT-TB"));

        var report = workflowService.Create(
            fundProfileId,
            account.AccountId.ToString("D"),
            "2026-05",
            new VersionedReportTemplateIdDto("monthly-board-pack", 1),
            "reporter",
            [
                new ReportPackLineProvenanceDto(
                    "nav.total",
                    "ledger",
                    "ledger-entry-1",
                    "evidence-ledger-1",
                    RunId: "run-restatement-1",
                    LedgerEntryId: "ledger-entry-1",
                    ReportValue: "1250000",
                    ReconciliationRunId: "reconciliation-run-nav-1",
                    ProviderEventId: "provider-event-nav-1",
                    SecurityMasterId: "security-master-nav-1",
                    ReconciliationOutcome: "matched",
                    ApprovalId: "approval-nav-1")
            ]);
        workflowService.Transition(report.ReportId, ReportPackWorkflowStateDto.Validated, "reviewer", "reviewer");
        workflowService.Transition(report.ReportId, ReportPackWorkflowStateDto.PendingApproval, "reviewer", "reviewer");
        workflowService.Transition(report.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
        workflowService.Publish(
            report.ReportId,
            "publisher",
            "publisher",
            "fund-controller",
            "sha256:report-pack",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            [
                new ReportPackEvidenceLinkDto("evidence-ledger-1", "Ledger line", "/reporting/evidence?subject=report-pack", "ledger"),
                new ReportPackEvidenceLinkDto("ledger-entry-1", "Ledger entry", "/reporting/evidence?subject=ledger-entry", "ledger"),
                new ReportPackEvidenceLinkDto("run-restatement-1", "Strategy run", "/strategy/runs/run-restatement-1", "strategy"),
                new ReportPackEvidenceLinkDto("reconciliation-run-nav-1", "Reconciliation run", "/accounting/reconciliation/reconciliation-run-nav-1", "reconciliation"),
                new ReportPackEvidenceLinkDto("provider-event-nav-1", "Provider event", "/data/provider-events/provider-event-nav-1", "provider"),
                new ReportPackEvidenceLinkDto("security-master-nav-1", "Security Master record", "/data/security-master/security-master-nav-1", "security-master"),
                new ReportPackEvidenceLinkDto("approval-nav-1", "Approval record", "/accounting/approvals/approval-nav-1", "approval")
            ]);
        var restated = workflowService.Restate(
            report.ReportId,
            "approver",
            "approver",
            "pricing-correction",
            "fund-controller",
            report.ReportId,
            [
                new ReportPackChangedLineDto(
                    "nav.total",
                    "1250000",
                    "1249500",
                    [new ReportPackEvidenceLinkDto("pricing-evidence-1", "Pricing override", "/reporting/evidence?subject=pricing", "pricing")])
            ]);
        var service = new FundOperationsWorkspaceReadService(
            accountService,
            repository,
            portfolioReadService,
            new NavAttributionService(securityMaster),
            new ReportGenerationService(securityMaster),
            reportPackWorkflowService: workflowService);

        var workspace = await service.GetWorkspaceAsync(new FundOperationsWorkspaceQuery(
            FundProfileId: fundProfileId,
            AsOf: new DateTimeOffset(2026, 5, 28, 16, 0, 0, TimeSpan.Zero),
            Currency: "USD"));

        workspace.Reporting.WorkflowRecords.Should().ContainSingle();
        var workflow = workspace.Reporting.WorkflowRecords!.Single();
        workflow.ReportId.Should().Be(restated.ReportId);
        workflow.State.Should().Be(ReportPackWorkflowStateDto.Restated);
        workflow.Restatement.Should().NotBeNull();
        workflow.Restatement!.ReasonCode.Should().Be("pricing-correction");
        workflow.Restatement.ChangedLines.Should().ContainSingle(line =>
            line.LineKey == "nav.total" &&
            line.PreviousValue == "1250000" &&
            line.CurrentValue == "1249500" &&
            line.EvidenceLinks!.Any(link => link.EvidenceId == "pricing-evidence-1"));
    }

    [Fact]
    public void ProjectReconciliationSnapshot_MapsConsolidatedAndPerDimensionSnapshots()
    {
        var asOf = new DateTimeOffset(2026, 4, 11, 16, 0, 0, TimeSpan.Zero);
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);
        var book = new FundLedgerBook("fund-projection");

        book.EntityLedger("entity-a").PostLines(asOf, "entity-sale", [(cash, 70m, 0m), (revenue, 0m, 70m)]);
        book.SleeveLedger("sleeve-core").PostLines(asOf, "sleeve-sale", [(cash, 20m, 0m), (revenue, 0m, 20m)]);
        book.VehicleLedger("vehicle-master").PostLines(asOf, "vehicle-sale", [(cash, 10m, 0m), (revenue, 0m, 10m)]);

        var projected = FundOperationsWorkspaceReadService.ProjectReconciliationSnapshot(
            book.ReconciliationSnapshot(asOf));

        projected.FundProfileId.Should().Be("fund-projection");
        projected.Consolidated.JournalEntryCount.Should().Be(3);
        projected.Consolidated.LedgerEntryCount.Should().Be(6);
        projected.Consolidated.Balances.Should().ContainSingle(line => line.AccountName == "Cash" && line.Balance == 100m);
        projected.Entities.Should().ContainKey("entity-a");
        projected.Entities["entity-a"].Balances.Should().ContainSingle(line => line.AccountName == "Cash" && line.Balance == 70m);
        projected.Sleeves.Should().ContainKey("sleeve-core");
        projected.Vehicles.Should().ContainKey("vehicle-master");
    }

    [Fact]
    public async Task GetWorkspaceAsync_WithBlankFundProfileId_ThrowsArgumentException()
    {
        var accountService = new InMemoryFundAccountService();
        var repository = new StrategyRunStore();
        var portfolioReadService = new PortfolioReadService();
        var securityMaster = new NullSecurityMasterQueryService();
        var service = new FundOperationsWorkspaceReadService(
            accountService,
            repository,
            portfolioReadService,
            new NavAttributionService(securityMaster),
            new ReportGenerationService(securityMaster));

        var act = () => service.GetWorkspaceAsync(new FundOperationsWorkspaceQuery(" "));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetWorkspaceAsync_WithSelectedLedgerIds_ConstrainsWorkspaceToSelection()
    {
        var fundProfileId = $"fund-{Guid.NewGuid():N}";
        var accountService = new InMemoryFundAccountService();
        var repository = new StrategyRunStore();
        var portfolioReadService = new PortfolioReadService();
        var securityMaster = new NullSecurityMasterQueryService();
        var service = new FundOperationsWorkspaceReadService(
            accountService,
            repository,
            portfolioReadService,
            new NavAttributionService(securityMaster),
            new ReportGenerationService(securityMaster));

        await repository.RecordRunAsync(BuildRun(
            runId: "run-selection-001",
            strategyId: "carry-1",
            strategyName: "Carry Strategy",
            fundProfileId: fundProfileId,
            fundDisplayName: "Alpha Income Fund"));
        await repository.RecordRunAsync(BuildRun(
            runId: "run-selection-002",
            strategyId: "carry-2",
            strategyName: "Carry Strategy 2",
            fundProfileId: fundProfileId,
            fundDisplayName: "Alpha Income Fund"));
        await repository.RecordRunAsync(BuildRun(
            runId: "run-selection-003",
            strategyId: "carry-3",
            strategyName: "Carry Strategy 3",
            fundProfileId: fundProfileId,
            fundDisplayName: "Alpha Income Fund"));

        var workspace = await service.GetWorkspaceAsync(new FundOperationsWorkspaceQuery(
            FundProfileId: fundProfileId,
            SelectedLedgerIds: ["run-selection-001", "run-selection-003"]));

        workspace.RecordedRunCount.Should().Be(2);
        workspace.RelatedRunIds.Should().BeEquivalentTo(["run-selection-001", "run-selection-003"]);
        workspace.Ledger.JournalEntryCount.Should().Be(4);
    }

    [Fact]
    public async Task GetWorkspaceAsync_WithEmptySelectedLedgerIds_MatchesUnfilteredWorkspace()
    {
        var fundProfileId = $"fund-{Guid.NewGuid():N}";
        var accountService = new InMemoryFundAccountService();
        var repository = new StrategyRunStore();
        var portfolioReadService = new PortfolioReadService();
        var securityMaster = new NullSecurityMasterQueryService();
        var service = new FundOperationsWorkspaceReadService(
            accountService,
            repository,
            portfolioReadService,
            new NavAttributionService(securityMaster),
            new ReportGenerationService(securityMaster));

        await repository.RecordRunAsync(BuildRun(
            runId: "run-all-001",
            strategyId: "carry-1",
            strategyName: "Carry Strategy",
            fundProfileId: fundProfileId,
            fundDisplayName: "Alpha Income Fund"));
        await repository.RecordRunAsync(BuildRun(
            runId: "run-all-002",
            strategyId: "carry-2",
            strategyName: "Carry Strategy 2",
            fundProfileId: fundProfileId,
            fundDisplayName: "Alpha Income Fund"));

        var fullWorkspace = await service.GetWorkspaceAsync(new FundOperationsWorkspaceQuery(FundProfileId: fundProfileId));
        var explicitEmptySelection = await service.GetWorkspaceAsync(new FundOperationsWorkspaceQuery(
            FundProfileId: fundProfileId,
            SelectedLedgerIds: Array.Empty<string>()));

        explicitEmptySelection.RecordedRunCount.Should().Be(fullWorkspace.RecordedRunCount);
        explicitEmptySelection.RelatedRunIds.Should().BeEquivalentTo(fullWorkspace.RelatedRunIds);
        explicitEmptySelection.Ledger.JournalEntryCount.Should().Be(fullWorkspace.Ledger.JournalEntryCount);
        explicitEmptySelection.Ledger.AssetBalance.Should().Be(fullWorkspace.Ledger.AssetBalance);
    }

    [Fact]
    public async Task GetWorkspaceAsync_WithUnknownSelectedLedgerIds_ReturnsEmptyLedgerProjection()
    {
        var fundProfileId = $"fund-{Guid.NewGuid():N}";
        var accountService = new InMemoryFundAccountService();
        var repository = new StrategyRunStore();
        var portfolioReadService = new PortfolioReadService();
        var securityMaster = new NullSecurityMasterQueryService();
        var service = new FundOperationsWorkspaceReadService(
            accountService,
            repository,
            portfolioReadService,
            new NavAttributionService(securityMaster),
            new ReportGenerationService(securityMaster));

        await repository.RecordRunAsync(BuildRun(
            runId: "run-known-001",
            strategyId: "carry-1",
            strategyName: "Carry Strategy",
            fundProfileId: fundProfileId,
            fundDisplayName: "Alpha Income Fund"));

        var workspace = await service.GetWorkspaceAsync(new FundOperationsWorkspaceQuery(
            FundProfileId: fundProfileId,
            SelectedLedgerIds: ["run-does-not-exist"]));

        workspace.RecordedRunCount.Should().Be(0);
        workspace.RelatedRunIds.Should().BeEmpty();
        workspace.Ledger.JournalEntryCount.Should().Be(0);
        workspace.Ledger.TrialBalance.Should().BeEmpty();
    }

    [Fact]
    public async Task PreviewReportPackAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        var accountService = new InMemoryFundAccountService();
        var repository = new StrategyRunStore();
        var portfolioReadService = new PortfolioReadService();
        var securityMaster = new NullSecurityMasterQueryService();
        var service = new FundOperationsWorkspaceReadService(
            accountService,
            repository,
            portfolioReadService,
            new NavAttributionService(securityMaster),
            new ReportGenerationService(securityMaster));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => service.PreviewReportPackAsync(
            new FundReportPackPreviewRequestDto("fund-cancel"),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GenerateReportPackAsync_WithDefaultFormats_WritesManifestProvenanceArtifactsAndChecksums()
    {
        var fundProfileId = $"fund-report-{Guid.NewGuid():N}";
        var accountService = new InMemoryFundAccountService();
        var strategyRepository = new StrategyRunStore();
        await strategyRepository.RecordRunAsync(BuildRun(
            runId: "run-report-001",
            strategyId: "report-1",
            strategyName: "Report Strategy",
            fundProfileId: fundProfileId,
            fundDisplayName: "Governed Report Fund"));

        var tempRoot = CreateTempDirectory();
        try
        {
            var repository = CreateReportPackRepository(tempRoot);
            var service = CreateReportPackService(accountService, strategyRepository, repository);

            var snapshot = await service.GenerateReportPackAsync(new FundReportPackGenerateRequestDto(
                FundProfileId: fundProfileId,
                AuditActor: "unit-test",
                AsOf: new DateTimeOffset(2026, 4, 11, 16, 0, 0, TimeSpan.Zero),
                CorrelationId: "corr-report-001",
                ExpectedSchemaVersion: GovernanceReportPackContract.CurrentSchemaVersion));

            snapshot.FundProfileId.Should().Be(fundProfileId);
            snapshot.ContractName.Should().Be(GovernanceReportPackContract.ContractName);
            snapshot.SchemaVersion.Should().Be(GovernanceReportPackContract.CurrentSchemaVersion);
            snapshot.AuditActor.Should().Be("unit-test");
            snapshot.CorrelationId.Should().Be("corr-report-001");
            snapshot.Status.Should().Be(GovernanceReportPackStatusDto.ReviewRequired);
            snapshot.ValidationIssues.Should().Contain(issue =>
                issue.Code == "report-pack.missing-security-master-classification" &&
                issue.Severity == GovernanceReportValidationSeverityDto.Warning);
            snapshot.LifecycleEvents.Select(static lifecycle => (
                    lifecycle.FromStatus,
                    lifecycle.ToStatus,
                    lifecycle.Actor,
                    lifecycle.CorrelationId))
                .Should()
                .ContainInOrder(
                    (GovernanceReportPackStatusDto.Draft, GovernanceReportPackStatusDto.Generated, "unit-test", "corr-report-001"),
                    (GovernanceReportPackStatusDto.Generated, GovernanceReportPackStatusDto.ReviewRequired, "unit-test", "corr-report-001"));
            snapshot.Provenance.RelatedRunIds.Should().ContainSingle().Which.Should().Be("run-report-001");
            snapshot.Provenance.SchemaVersion.Should().Be(GovernanceReportPackContract.CurrentSchemaVersion);
            snapshot.Provenance.JournalEntryCount.Should().BeGreaterThan(0);
            snapshot.Provenance.LedgerEntryCount.Should().BeGreaterThan(0);
            snapshot.Provenance.SourceSnapshotHash.Should().MatchRegex("^[a-f0-9]{64}$");
            snapshot.Provenance.LineagePointers.Should().Contain(pointer =>
                pointer.ScopeType == "report" &&
                pointer.ScopeKey == "summary" &&
                pointer.EvidenceType == "run" &&
                pointer.EvidenceId == "run-report-001" &&
                pointer.DisplayLabel == "Report Strategy (run-report-001)" &&
                pointer.Route == UiApiRoutes.WithParam(UiApiRoutes.RunsContinuity, "runId", "run-report-001") &&
                pointer.SourceSystem == "strategy-run");
            snapshot.Provenance.LineagePointers.Should().Contain(pointer =>
                pointer.ScopeType == "line" &&
                pointer.ScopeKey == "Securities:AAPL" &&
                pointer.EvidenceType == "ledger-account" &&
                pointer.DisplayLabel == "Securities / AAPL ledger line" &&
                pointer.Route == UiApiRoutes.WithQuery(
                    UiApiRoutes.WithParam(UiApiRoutes.RunsLedgerTrialBalance, "runId", "run-report-001"),
                    "accountName=Securities&symbol=AAPL") &&
                pointer.SourceSystem == "ledger");
            var securitiesLedgerPointer = snapshot.Provenance.LineagePointers.Should().ContainSingle(pointer =>
                    pointer.ScopeType == "line" &&
                    pointer.ScopeKey == "Securities:AAPL" &&
                    pointer.EvidenceType == "ledger-account")
                .Which;
            securitiesLedgerPointer.RelatedEvidenceIds.Should().ContainSingle(id => IsGuidString(id));
            securitiesLedgerPointer.EvidenceCount.Should().Be(1);
            securitiesLedgerPointer.Amount.Should().Be(400m);
            securitiesLedgerPointer.CapturedAt.Should().Be(new DateTimeOffset(2026, 4, 11, 14, 10, 0, TimeSpan.Zero));
            snapshot.Provenance.LineagePointers.Should().Contain(pointer =>
                pointer.ScopeType == "line" &&
                pointer.ScopeKey == "Securities:AAPL" &&
                pointer.EvidenceType == "security" &&
                pointer.EvidenceId == "AAPL" &&
                pointer.DisplayLabel == "AAPL" &&
                pointer.Route == UiApiRoutes.WithQuery(UiApiRoutes.WorkstationSecurityMasterSearch, "query=AAPL") &&
                pointer.SourceSystem == "security-master");
            var securityMasterPointer = snapshot.Provenance.LineagePointers.Should().ContainSingle(pointer =>
                    pointer.ScopeType == "line" &&
                    pointer.ScopeKey == "Securities:AAPL" &&
                    pointer.EvidenceType == "security")
                .Which;
            securityMasterPointer.RelatedEvidenceIds.Should().ContainSingle(id => IsGuidString(id));
            securityMasterPointer.EvidenceCount.Should().Be(1);
            securityMasterPointer.Amount.Should().Be(400m);
            securityMasterPointer.CapturedAt.Should().Be(new DateTimeOffset(2026, 4, 11, 14, 10, 0, TimeSpan.Zero));
            snapshot.Artifacts.Should().OnlyContain(artifact =>
                artifact.SchemaVersion == GovernanceReportPackContract.CurrentSchemaVersion);
            snapshot.Artifacts.Should().Contain(artifact => artifact.ArtifactKind == "trial-balance" && artifact.Format == GovernanceReportArtifactFormatDto.Json);
            snapshot.Artifacts.Should().Contain(artifact => artifact.ArtifactKind == "trial-balance" && artifact.Format == GovernanceReportArtifactFormatDto.Csv);
            snapshot.Artifacts.Should().Contain(artifact => artifact.ArtifactKind == "asset-class-sections" && artifact.Format == GovernanceReportArtifactFormatDto.Json);
            snapshot.Artifacts.Should().Contain(artifact => artifact.ArtifactKind == "asset-class-sections" && artifact.Format == GovernanceReportArtifactFormatDto.Csv);
            snapshot.Artifacts.Should().Contain(artifact => artifact.ArtifactKind == "workbook" && artifact.Format == GovernanceReportArtifactFormatDto.Xlsx);
            snapshot.Artifacts.Should().Contain(artifact => artifact.ArtifactKind == "provenance" && artifact.Format == GovernanceReportArtifactFormatDto.Json);
            snapshot.Artifacts.Should().Contain(artifact => artifact.ArtifactKind == "rendered-statement" && artifact.Format == GovernanceReportArtifactFormatDto.Html);
            snapshot.Artifacts.Should().Contain(artifact => artifact.ArtifactKind == "rendered-statement" && artifact.Format == GovernanceReportArtifactFormatDto.Pdf);
            snapshot.AuditPackReadiness.Should().NotBeNull();
            snapshot.AuditPackReadiness!.SlaTargetSeconds.Should().Be(60);
            snapshot.AuditPackReadiness.GeneratedInSeconds.Should().BeGreaterThanOrEqualTo(0);
            snapshot.AuditPackReadiness.SlaMet.Should().BeTrue();
            snapshot.AuditPackReadiness.IsComplete.Should().BeFalse("generated packs still require approval or publication before audit completion");
            snapshot.AuditPackReadiness.MissingEvidenceCategories.Should().Contain(FundAuditEvidenceCategoryKeyDto.Approvals);
            snapshot.AuditPackReadiness.EvidenceCategorySummaries
                .Select(static category => category.Key)
                .Should().BeEquivalentTo(Enum.GetValues<FundAuditEvidenceCategoryKeyDto>());
            snapshot.AuditPackReadiness.EvidenceCategorySummaries.Should().Contain(category =>
                category.Key == FundAuditEvidenceCategoryKeyDto.LedgerEvidence &&
                category.IsComplete &&
                category.EvidenceCount > 0);
            snapshot.AuditPackReadiness.EvidenceCategorySummaries.Should().Contain(category =>
                category.Key == FundAuditEvidenceCategoryKeyDto.Exports &&
                category.IsComplete &&
                category.EvidenceCount == 7);

            foreach (var artifact in snapshot.Artifacts)
            {
                var path = ResolveArtifactPath(tempRoot, artifact);
                File.Exists(path).Should().BeTrue(path);
                var bytes = await File.ReadAllBytesAsync(path);
                bytes.LongLength.Should().Be(artifact.SizeBytes);
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant().Should().Be(artifact.ChecksumSha256);
            }

            var manifestPath = Directory.EnumerateFiles(
                    Path.Combine(tempRoot, "governance-report-packs"),
                    "manifest.json",
                    SearchOption.AllDirectories)
                .Should()
                .ContainSingle()
                .Which;
            File.ReadAllText(manifestPath).Should().Contain(snapshot.ReportId.ToString());
            File.ReadAllText(manifestPath).Should().Contain("\"schemaVersion\": 2");
            File.ReadAllText(manifestPath).Should().Contain("\"contractName\": \"governance-report-pack\"");
            File.ReadAllText(manifestPath).Should().Contain("\"status\": \"ReviewRequired\"");
            File.ReadAllText(manifestPath).Should().Contain("\"auditPackReadiness\"");

            var trialBalanceCsv = snapshot.Artifacts.Single(artifact =>
                artifact.ArtifactKind == "trial-balance" && artifact.Format == GovernanceReportArtifactFormatDto.Csv);
            var csvLines = await File.ReadAllLinesAsync(ResolveArtifactPath(tempRoot, trialBalanceCsv));
            csvLines.Should().HaveCountGreaterThan(1);
            csvLines[0].Should().Be("accountName,accountType,symbol,currency,assetClass,primaryIdentifierKind,primaryIdentifierValue,subType,assetFamily,issuerType,riskCountry,lookupQuality,displayName,netBalance");
            csvLines.Skip(1).Select(static line => line.Split(',')[0])
                .Should()
                .ContainInOrder("Cash", "Securities", "Capital Account");

            var workbook = snapshot.Artifacts.Single(artifact => artifact.Format == GovernanceReportArtifactFormatDto.Xlsx);
            using var archive = ZipFile.OpenRead(ResolveArtifactPath(tempRoot, workbook));
            archive.GetEntry("xl/workbook.xml").Should().NotBeNull();
            archive.GetEntry("xl/worksheets/sheet1.xml").Should().NotBeNull();
            archive.GetEntry("xl/worksheets/sheet2.xml").Should().NotBeNull();
            archive.GetEntry("xl/worksheets/sheet3.xml").Should().NotBeNull();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task GenerateReportPackAsync_WithBrandingOverride_AppliesThemeToManifestAndDocuments()
    {
        var fundProfileId = $"fund-branded-{Guid.NewGuid():N}";
        var strategyRepository = new StrategyRunStore();
        await strategyRepository.RecordRunAsync(BuildRun(
            runId: "run-brand-001",
            strategyId: "brand-1",
            strategyName: "Brand Strategy",
            fundProfileId: fundProfileId,
            fundDisplayName: "Branded Fund"));
        var tempRoot = CreateTempDirectory();
        try
        {
            var service = CreateReportPackService(
                new InMemoryFundAccountService(),
                strategyRepository,
                CreateReportPackRepository(tempRoot));
            var customTheme = new ReportBrandingThemeDto(
                "LP Custom Theme",
                "LP Custom Theme",
                "Northstar Capital",
                "#123456",
                "#AA5500",
                "#111111",
                "#FAFAFA",
                LogoUri: "https://example.test/northstar.png",
                FooterText: "Northstar investor reporting",
                Disclaimer: "Prepared for authorized allocator review.",
                IsBuiltIn: false);

            var snapshot = await service.GenerateReportPackAsync(new FundReportPackGenerateRequestDto(
                FundProfileId: fundProfileId,
                AuditActor: "unit-test",
                AsOf: new DateTimeOffset(2026, 4, 11, 16, 0, 0, TimeSpan.Zero),
                Formats:
                [
                    GovernanceReportArtifactFormatDto.Html,
                    GovernanceReportArtifactFormatDto.Pdf,
                    GovernanceReportArtifactFormatDto.Xlsx
                ],
                BrandingThemeOverride: customTheme));

            snapshot.BrandingTheme.Should().NotBeNull();
            snapshot.BrandingTheme!.ThemeId.Should().Be("lpcustomtheme");
            snapshot.BrandingTheme.FirmName.Should().Be("Northstar Capital");
            snapshot.BrandingTheme.PrimaryColor.Should().Be("#123456");
            snapshot.BrandingTheme.IsBuiltIn.Should().BeFalse();

            var manifestPath = Directory.EnumerateFiles(
                    Path.Combine(tempRoot, "governance-report-packs"),
                    "manifest.json",
                    SearchOption.AllDirectories)
                .Single();
            var manifest = await File.ReadAllTextAsync(manifestPath);
            manifest.Should().Contain("\"firmName\": \"Northstar Capital\"");
            manifest.Should().Contain("\"themeId\": \"lpcustomtheme\"");

            var htmlArtifact = snapshot.Artifacts.Single(artifact => artifact.Format == GovernanceReportArtifactFormatDto.Html);
            var html = await File.ReadAllTextAsync(ResolveArtifactPath(tempRoot, htmlArtifact));
            html.Should().Contain("Northstar Capital");
            html.Should().Contain("--report-primary:#123456");
            html.Should().Contain("https://example.test/northstar.png");
            html.Should().Contain("Prepared for authorized allocator review.");

            var pdfArtifact = snapshot.Artifacts.Single(artifact => artifact.Format == GovernanceReportArtifactFormatDto.Pdf);
            var pdfText = Encoding.ASCII.GetString(await File.ReadAllBytesAsync(ResolveArtifactPath(tempRoot, pdfArtifact)));
            pdfText.Should().Contain("Northstar Capital");
            pdfText.Should().Contain("LP Custom Theme");

            var workbook = snapshot.Artifacts.Single(artifact => artifact.Format == GovernanceReportArtifactFormatDto.Xlsx);
            using var archive = ZipFile.OpenRead(ResolveArtifactPath(tempRoot, workbook));
            archive.GetEntry("xl/worksheets/sheet3.xml").Should().NotBeNull();
            var sharedStrings = archive.GetEntry("xl/sharedStrings.xml");
            sharedStrings.Should().NotBeNull();
            using var reader = new StreamReader(sharedStrings!.Open());
            var stringsXml = await reader.ReadToEndAsync();
            stringsXml.Should().Contain("Northstar Capital");
            stringsXml.Should().Contain("LP Custom Theme");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task GenerateReportPackAsync_WithInvalidBrandingOverride_ThrowsArgumentException()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var service = CreateReportPackService(
                new InMemoryFundAccountService(),
                new StrategyRunStore(),
                CreateReportPackRepository(tempRoot));
            var invalidTheme = new ReportBrandingThemeDto(
                "bad-theme",
                "Bad Theme",
                "Bad Firm",
                "blue",
                "#AA5500",
                "#111111",
                "#FFFFFF");

            var act = () => service.GenerateReportPackAsync(new FundReportPackGenerateRequestDto(
                FundProfileId: "fund-bad-branding",
                AuditActor: "unit-test",
                BrandingThemeOverride: invalidTheme));

            await act.Should().ThrowAsync<ArgumentException>()
                .WithMessage("*PrimaryColor*#RRGGBB*");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task ExportReportPackEvidenceBundleAsync_WritesManifestProvenanceApprovalsAndSourceLinks()
    {
        var fundProfileId = $"fund-report-{Guid.NewGuid():N}";
        var accountService = new InMemoryFundAccountService();
        var strategyRepository = new StrategyRunStore();
        await strategyRepository.RecordRunAsync(BuildRun(
            runId: "run-bundle-001",
            strategyId: "bundle-1",
            strategyName: "Bundle Strategy",
            fundProfileId: fundProfileId,
            fundDisplayName: "Bundle Fund"));

        var tempRoot = CreateTempDirectory();
        try
        {
            var repository = CreateReportPackRepository(tempRoot);
            var service = CreateReportPackService(accountService, strategyRepository, repository);
            var snapshot = await service.GenerateReportPackAsync(new FundReportPackGenerateRequestDto(
                FundProfileId: fundProfileId,
                AuditActor: "controller",
                AsOf: new DateTimeOffset(2026, 4, 11, 16, 0, 0, TimeSpan.Zero),
                CorrelationId: "corr-bundle-001",
                ExpectedSchemaVersion: GovernanceReportPackContract.CurrentSchemaVersion));

            var bundle = await service.ExportReportPackEvidenceBundleAsync(
                snapshot.ReportId,
                "audit-lead");

            bundle.Should().NotBeNull();
            bundle!.ReportId.Should().Be(snapshot.ReportId);
            bundle.ExportedBy.Should().Be("audit-lead");
            bundle.Manifest.Should().BeEquivalentTo(snapshot);
            bundle.Provenance.SourceSnapshotHash.Should().Be(snapshot.Provenance.SourceSnapshotHash);
            bundle.ManifestPath.Should().EndWith("/manifest.json");
            bundle.ProvenancePath.Should().EndWith("/provenance.json");
            bundle.Approvals.Select(static approval => approval.ToStatus)
                .Should()
                .ContainInOrder(GovernanceReportPackStatusDto.Generated, GovernanceReportPackStatusDto.ReviewRequired);
            bundle.SourceLinks.Should().Contain(link =>
                link.SourceType == "run" &&
                link.SourceId == "run-bundle-001" &&
                link.Route == UiApiRoutes.WithParam(UiApiRoutes.RunsContinuity, "runId", "run-bundle-001"));
            bundle.Artifacts.Should().BeEquivalentTo(snapshot.Artifacts);
            bundle.BundleArtifact.Should().NotBeNull();
            bundle.BundleArtifact!.ArtifactKind.Should().Be("evidence-bundle");
            bundle.BundleArtifact.Format.Should().Be(GovernanceReportArtifactFormatDto.Json);

            var bundlePath = ResolveArtifactPath(tempRoot, bundle.BundleArtifact);
            File.Exists(bundlePath).Should().BeTrue(bundlePath);
            var bundleJson = await File.ReadAllTextAsync(bundlePath);
            bundleJson.Should().Contain("\"manifest\"");
            bundleJson.Should().Contain("\"provenance\"");
            bundleJson.Should().Contain("\"approvals\"");
            bundleJson.Should().Contain("\"sourceLinks\"");
            bundleJson.Should().Contain(snapshot.Provenance.SourceSnapshotHash);
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(bundlePath)))
                .ToLowerInvariant()
                .Should()
                .Be(bundle.BundleArtifact.ChecksumSha256);
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task ReportPackRepository_WithInvalidSnapshot_UsesCanonicalReportPackValidationWording()
    {
        var fundProfileId = $"fund-report-{Guid.NewGuid():N}";
        var accountService = new InMemoryFundAccountService();
        var strategyRepository = new StrategyRunStore();
        await strategyRepository.RecordRunAsync(BuildRun(
            runId: "run-report-invalid",
            strategyId: "report-invalid",
            strategyName: "Report Strategy",
            fundProfileId: fundProfileId,
            fundDisplayName: "Governed Report Fund"));

        var tempRoot = CreateTempDirectory();
        try
        {
            var repository = CreateReportPackRepository(tempRoot);
            var service = CreateReportPackService(accountService, strategyRepository, repository);

            var snapshot = await service.GenerateReportPackAsync(new FundReportPackGenerateRequestDto(
                FundProfileId: fundProfileId,
                AuditActor: "unit-test",
                ExpectedSchemaVersion: GovernanceReportPackContract.CurrentSchemaVersion));

            var act = () => repository.SaveAsync(
                snapshot with { Status = GovernanceReportPackStatusDto.Unknown },
                []);

            var exception = await act.Should().ThrowAsync<ArgumentException>();
            exception.Which.Message.Should().Contain("Report-pack status is required.");
            exception.Which.Message.Should().NotContain("Governance report-pack");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task ReportPackHistory_ListsNewestFirstAndRetrievesById()
    {
        var fundProfileId = $"fund-history-{Guid.NewGuid():N}";
        var accountService = new InMemoryFundAccountService();
        var strategyRepository = new StrategyRunStore();
        await strategyRepository.RecordRunAsync(BuildRun(
            runId: "run-history-001",
            strategyId: "history-1",
            strategyName: "History Strategy",
            fundProfileId: fundProfileId,
            fundDisplayName: "History Fund"));

        var tempRoot = CreateTempDirectory();
        try
        {
            var service = CreateReportPackService(accountService, strategyRepository, CreateReportPackRepository(tempRoot));

            var older = await service.GenerateReportPackAsync(new FundReportPackGenerateRequestDto(
                FundProfileId: fundProfileId,
                AuditActor: "unit-test",
                AsOf: new DateTimeOffset(2026, 4, 10, 16, 0, 0, TimeSpan.Zero),
                Formats: [GovernanceReportArtifactFormatDto.Json]));
            await Task.Delay(10);
            var newer = await service.GenerateReportPackAsync(new FundReportPackGenerateRequestDto(
                FundProfileId: fundProfileId,
                AuditActor: "unit-test",
                AsOf: new DateTimeOffset(2026, 4, 11, 16, 0, 0, TimeSpan.Zero),
                Formats: [GovernanceReportArtifactFormatDto.Json]));

            var history = await service.GetReportPackHistoryAsync(fundProfileId, limit: 10);

            history.Should().HaveCount(2);
            history[0].GeneratedAt.Should().BeOnOrAfter(history[1].GeneratedAt);
            history.Select(item => item.ReportId).Should().ContainInOrder(newer.ReportId, older.ReportId);
            history.Should().OnlyContain(item =>
                item.Status == GovernanceReportPackStatusDto.ReviewRequired &&
                item.ValidationIssueCount > 0 &&
                item.LifecycleEventCount == 2);

            var detail = await service.GetReportPackAsync(newer.ReportId);
            detail.Should().NotBeNull();
            detail!.ReportId.Should().Be(newer.ReportId);
            detail.SchemaVersion.Should().Be(GovernanceReportPackContract.CurrentSchemaVersion);
            detail.Artifacts.Should().NotBeEmpty();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task GenerateReportPackAsync_WithUnsupportedExpectedSchemaVersion_ThrowsArgumentException()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var service = CreateReportPackService(
                new InMemoryFundAccountService(),
                new StrategyRunStore(),
                CreateReportPackRepository(tempRoot));

            var act = () => service.GenerateReportPackAsync(new FundReportPackGenerateRequestDto(
                FundProfileId: "fund-schema-version",
                AuditActor: "unit-test",
                ExpectedSchemaVersion: GovernanceReportPackContract.CurrentSchemaVersion + 1));

            await act.Should()
                .ThrowAsync<ArgumentException>()
                .WithMessage("*Unsupported governed report-pack schema version*");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task ReportPackRepository_WithFutureSchemaVersion_SkipsManifest_AndReadsLegacyV1Manifest()
    {
        var fundProfileId = $"fund-future-schema-{Guid.NewGuid():N}";
        var strategyRepository = new StrategyRunStore();
        await strategyRepository.RecordRunAsync(BuildRun(
            runId: "run-future-schema-001",
            strategyId: "future-1",
            strategyName: "Future Schema Strategy",
            fundProfileId: fundProfileId,
            fundDisplayName: "Future Schema Fund"));

        var tempRoot = CreateTempDirectory();
        try
        {
            var service = CreateReportPackService(
                new InMemoryFundAccountService(),
                strategyRepository,
                CreateReportPackRepository(tempRoot));
            var generated = await service.GenerateReportPackAsync(new FundReportPackGenerateRequestDto(
                FundProfileId: fundProfileId,
                AuditActor: "unit-test",
                Formats: [GovernanceReportArtifactFormatDto.Json]));
            var manifestPath = Directory.EnumerateFiles(
                    Path.Combine(tempRoot, "governance-report-packs"),
                    "manifest.json",
                    SearchOption.AllDirectories)
                .Single();
            var incompatible = generated with
            {
                SchemaVersion = GovernanceReportPackContract.CurrentSchemaVersion + 1
            };

            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(incompatible, ReportPackJsonOptions()));

            var history = await service.GetReportPackHistoryAsync(fundProfileId);
            var detail = await service.GetReportPackAsync(generated.ReportId);

            history.Should().BeEmpty();
            detail.Should().BeNull();

            var legacy = generated with
            {
                SchemaVersion = GovernanceReportPackContract.MinimumReadableSchemaVersion,
                Provenance = generated.Provenance with { SchemaVersion = GovernanceReportPackContract.MinimumReadableSchemaVersion },
                Artifacts = generated.Artifacts
                    .Select(static artifact => artifact with { SchemaVersion = GovernanceReportPackContract.MinimumReadableSchemaVersion })
                    .ToArray(),
                Status = GovernanceReportPackStatusDto.Unknown,
                ValidationIssues = [],
                LifecycleEvents = [],
                AuditPackReadiness = null
            };
            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(legacy, ReportPackJsonOptions()));

            var legacyHistory = await service.GetReportPackHistoryAsync(fundProfileId);
            var legacyDetail = await service.GetReportPackAsync(generated.ReportId);

            legacyHistory.Should().ContainSingle(item =>
                item.ReportId == generated.ReportId &&
                item.SchemaVersion == GovernanceReportPackContract.MinimumReadableSchemaVersion &&
                item.Status == GovernanceReportPackStatusDto.Unknown &&
                item.ValidationIssueCount == 0 &&
                item.LifecycleEventCount == 0 &&
                item.AuditPackReadiness == null);
            legacyDetail.Should().NotBeNull();
            legacyDetail!.SchemaVersion.Should().Be(GovernanceReportPackContract.MinimumReadableSchemaVersion);
            legacyDetail.Status.Should().Be(GovernanceReportPackStatusDto.Unknown);
            legacyDetail.AuditPackReadiness.Should().BeNull();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task GenerateReportPackAsync_WithEmptyFundData_WritesPackageWithWarnings()
    {
        var fundProfileId = $"fund-empty-{Guid.NewGuid():N}";
        var tempRoot = CreateTempDirectory();
        try
        {
            var service = CreateReportPackService(
                new InMemoryFundAccountService(),
                new StrategyRunStore(),
                CreateReportPackRepository(tempRoot));

            var snapshot = await service.GenerateReportPackAsync(new FundReportPackGenerateRequestDto(
                FundProfileId: fundProfileId,
                AuditActor: "unit-test",
                Formats: [GovernanceReportArtifactFormatDto.Json]));

            snapshot.Artifacts.Should().Contain(artifact => artifact.ArtifactKind == "trial-balance");
            snapshot.Artifacts.Should().Contain(artifact => artifact.ArtifactKind == "provenance");
            snapshot.Status.Should().Be(GovernanceReportPackStatusDto.ReviewRequired);
            snapshot.ValidationIssues.Should().Contain(issue =>
                issue.Code == "report-pack.no-contributing-runs" &&
                issue.Severity == GovernanceReportValidationSeverityDto.Warning);
            snapshot.ValidationIssues.Should().Contain(issue =>
                issue.Code == "report-pack.empty-trial-balance" &&
                issue.Severity == GovernanceReportValidationSeverityDto.Critical);
            snapshot.ValidationIssues.Should().Contain(issue =>
                issue.Code == "report-pack.missing-ledger-postings" &&
                issue.Severity == GovernanceReportValidationSeverityDto.Critical);
            snapshot.LifecycleEvents.Should().Contain(lifecycle =>
                lifecycle.FromStatus == GovernanceReportPackStatusDto.Generated &&
                lifecycle.ToStatus == GovernanceReportPackStatusDto.ReviewRequired);
            snapshot.Warnings.Should().Contain(warning => warning.Contains("No recorded fund-scoped runs", StringComparison.Ordinal));
            snapshot.Warnings.Should().Contain(warning => warning.Contains("no trial-balance rows", StringComparison.Ordinal));
            snapshot.Provenance.TrialBalanceLineCount.Should().Be(0);
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task GenerateReportPackAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var service = CreateReportPackService(
                new InMemoryFundAccountService(),
                new StrategyRunStore(),
                CreateReportPackRepository(tempRoot));
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var act = () => service.GenerateReportPackAsync(
                new FundReportPackGenerateRequestDto(
                    FundProfileId: "fund-cancel",
                    AuditActor: "unit-test"),
                cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    private static FundOperationsWorkspaceReadService CreateReportPackService(
        InMemoryFundAccountService accountService,
        StrategyRunStore strategyRepository,
        IGovernanceReportPackRepository reportPackRepository)
    {
        var securityMaster = new NullSecurityMasterQueryService();
        return new FundOperationsWorkspaceReadService(
            accountService,
            strategyRepository,
            new PortfolioReadService(),
            new NavAttributionService(securityMaster),
            new ReportGenerationService(securityMaster),
            reportPackRepository: reportPackRepository);
    }

    private static FileGovernanceReportPackRepository CreateReportPackRepository(string tempRoot) =>
        new(tempRoot, NullLogger<FileGovernanceReportPackRepository>.Instance);

    private static JsonSerializerOptions ReportPackJsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static string CreateTempDirectory()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "meridian-report-pack-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        return tempRoot;
    }

    private static string ResolveArtifactPath(string tempRoot, FundReportPackArtifactDto artifact) =>
        Path.Combine(
            tempRoot,
            "governance-report-packs",
            artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static StrategyRunEntry BuildRun(
        string runId,
        string strategyId,
        string strategyName,
        string fundProfileId,
        string fundDisplayName,
        decimal realizedPnl = 0m,
        decimal unrealizedPnl = 0m,
        IReadOnlyDictionary<string, (decimal RealizedPnl, decimal UnrealizedPnl)>? positionPnl = null,
        DateTimeOffset? startedAtUtc = null)
    {
        var startedAt = startedAtUtc ?? new DateTimeOffset(2026, 4, 11, 14, 0, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddMinutes(30);
        var ledger = CreateLedger();
        positionPnl ??= new Dictionary<string, (decimal RealizedPnl, decimal UnrealizedPnl)>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAPL"] = (realizedPnl, unrealizedPnl)
        };
        var positions = positionPnl.ToDictionary(
            static pair => pair.Key,
            static pair => new Position(pair.Key, 10, 40m, UnrealizedPnl: pair.Value.UnrealizedPnl, RealizedPnl: pair.Value.RealizedPnl),
            StringComparer.OrdinalIgnoreCase);
        var accountSnapshot = new FinancialAccountSnapshot(
            AccountId: BacktestDefaults.DefaultBrokerageAccountId,
            DisplayName: "Primary Brokerage",
            Kind: FinancialAccountKind.Brokerage,
            Institution: "Simulated Broker",
            Cash: 750m,
            MarginBalance: 0m,
            LongMarketValue: 400m,
            ShortMarketValue: 0m,
            Equity: 1_150m,
            Positions: positions,
            Rules: new FinancialAccountRules());
        var snapshot = new PortfolioSnapshot(
            Timestamp: completedAt,
            Date: DateOnly.FromDateTime(completedAt.UtcDateTime),
            Cash: 750m,
            MarginBalance: 0m,
            LongMarketValue: 400m,
            ShortMarketValue: 0m,
            TotalEquity: 1_150m,
            DailyReturn: 0m,
            Positions: positions,
            Accounts: new Dictionary<string, FinancialAccountSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                [accountSnapshot.AccountId] = accountSnapshot
            },
            DayCashFlows: []);

        var request = new BacktestRequest(
            From: new DateOnly(2026, 4, 10),
            To: new DateOnly(2026, 4, 11),
            Symbols: ["AAPL"],
            InitialCash: 1_000m,
            DataRoot: "./data");
        var metrics = new BacktestMetrics(
            InitialCapital: 1_000m,
            FinalEquity: 1_150m,
            GrossPnl: 150m,
            NetPnl: 150m,
            TotalReturn: 0.15m,
            AnnualizedReturn: 0.15m,
            SharpeRatio: 1.2,
            SortinoRatio: 1.2,
            CalmarRatio: 1.2,
            MaxDrawdown: 0m,
            MaxDrawdownPercent: 0m,
            MaxDrawdownRecoveryDays: 0,
            ProfitFactor: 1.0,
            WinRate: 1.0,
            TotalTrades: 1,
            WinningTrades: 1,
            LosingTrades: 0,
            TotalCommissions: 1m,
            TotalMarginInterest: 0m,
            TotalShortRebates: 0m,
            Xirr: 0.15,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>
            {
                ["AAPL"] = new("AAPL", 150m, 0m, 1, 1m, 0m)
            });
        var result = new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AAPL" },
            Snapshots: [snapshot],
            CashFlows: [],
            Fills: [],
            Metrics: metrics,
            Ledger: ledger,
            ElapsedTime: TimeSpan.FromSeconds(5),
            TotalEventsProcessed: 10);

        return StrategyRunEntry.Start(strategyId, strategyName, RunType.Paper) with
        {
            RunId = runId,
            StartedAt = startedAt,
            EndedAt = completedAt,
            Metrics = result,
            PortfolioId = $"{strategyId}-paper-portfolio",
            LedgerReference = $"{strategyId}-paper-ledger",
            AuditReference = $"audit-{runId}",
            FundProfileId = fundProfileId,
            FundDisplayName = fundDisplayName
        };
    }

    private static Meridian.Ledger.Ledger CreateLedger()
    {
        var ledger = new Meridian.Ledger.Ledger();
        PostBalancedEntry(ledger, new DateTimeOffset(2026, 4, 11, 14, 0, 0, TimeSpan.Zero), "Initial capital",
        [
            (LedgerAccounts.Cash, 1_000m, 0m),
            (LedgerAccounts.CapitalAccount, 0m, 1_000m)
        ]);
        PostBalancedEntry(ledger, new DateTimeOffset(2026, 4, 11, 14, 10, 0, TimeSpan.Zero), "Buy AAPL",
        [
            (LedgerAccounts.Securities("AAPL"), 400m, 0m),
            (LedgerAccounts.Cash, 0m, 400m)
        ]);
        return ledger;
    }

    private static void PostBalancedEntry(
        Meridian.Ledger.Ledger ledger,
        DateTimeOffset timestamp,
        string description,
        IReadOnlyList<(LedgerAccount Account, decimal Debit, decimal Credit)> lines)
    {
        var journalId = Guid.NewGuid();
        var ledgerLines = lines
            .Select(line => new LedgerEntry(
                Guid.NewGuid(),
                journalId,
                timestamp,
                line.Account,
                line.Debit,
                line.Credit,
                description))
            .ToArray();
        ledger.Post(new JournalEntry(journalId, timestamp, description, ledgerLines));
    }

    private static bool IsGuidString(string value) => Guid.TryParse(value, out _);

    private static Guid TranslateFundProfileId(string fundProfileId)
        => new(MD5.HashData(Encoding.UTF8.GetBytes(fundProfileId)));
}
