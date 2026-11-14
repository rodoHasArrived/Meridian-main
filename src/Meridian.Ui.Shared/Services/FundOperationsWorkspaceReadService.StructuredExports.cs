using System.Globalization;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Storage.Export;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

public sealed partial class FundOperationsWorkspaceReadService
{

    private const int StructuredReportingExportSchemaVersion = 1;
    private const string RegulatoryTrialBalanceExportId = "regulatory-trial-balance";
    private const string WarehouseLedgerFactsExportId = "warehouse-ledger-facts";
    private const string InvestmentPortfolioCutsExportId = "investment-portfolio-cuts";
    private const string InvestmentTopNContributionAnalyticsExportId = "investment-topn-contribution-analytics";
    private const string CrossFundConsolidationExportId = "cross-fund-consolidation";

    private static readonly StructuredReportingExportColumnDto[] RegulatoryTrialBalanceColumns =
    [
        new("accountName", "string", "Ledger account display name."),
        new("accountType", "string", "Ledger account type."),
        new("symbol", "string", "Security symbol when the line is security-scoped."),
        new("financialAccountId", "string", "Linked financial account identifier."),
        new("fundId", "string", "Canonical fund dimension retained on the ledger row."),
        new("entityId", "string", "Canonical entity dimension retained on the ledger row."),
        new("sleeveId", "string", "Canonical sleeve dimension retained on the ledger row."),
        new("strategyId", "string", "Canonical strategy dimension retained on the ledger row."),
        new("investorId", "string", "Canonical investor dimension retained on the ledger row."),
        new("capitalAccountId", "string", "Canonical capital-account dimension retained on the ledger row."),
        new("instrumentId", "guid", "Canonical instrument dimension retained on the ledger row."),
        new("taxLotId", "string", "Canonical tax-lot dimension retained on the ledger row."),
        new("costCenterId", "string", "Canonical cost-center dimension retained on the ledger row."),
        new("counterpartyId", "string", "Canonical counterparty dimension retained on the ledger row."),
        new("organizationId", "string", "Canonical organization dimension retained on the ledger row."),
        new("portfolioId", "string", "Canonical portfolio dimension retained on the ledger row."),
        new("bookId", "string", "Canonical ledger-book dimension retained on the ledger row."),
        new("accountId", "string", "Canonical account dimension retained on the ledger row."),
        new("customerId", "string", "Canonical customer dimension retained on the ledger row."),
        new("vendorId", "string", "Canonical vendor dimension retained on the ledger row."),
        new("projectId", "string", "Canonical project dimension retained on the ledger row."),
        new("externalGlDimensionsJson", "json", "Deterministic external-GL dimension map retained on the ledger row."),
        new("currency", "string", "Workspace base currency."),
        new("balance", "decimal", "Signed trial-balance amount."),
        new("entryCount", "integer", "Ledger entry count behind the balance."),
        new("securityId", "guid", "Resolved Security Master identifier when available."),
        new("securityDisplayName", "string", "Resolved Security Master display name."),
        new("sourceAsOfUtc", "datetime", "Workspace as-of timestamp in UTC.")
    ];

    private static readonly StructuredReportingExportColumnDto[] WarehouseLedgerFactColumns =
    [
        new("scope", "string", "Ledger consolidation scope."),
        new("accountName", "string", "Ledger account display name."),
        new("accountType", "string", "Ledger account type."),
        new("symbol", "string", "Security symbol when the balance is security-scoped."),
        new("financialAccountId", "string", "Linked financial account identifier."),
        new("fundId", "string", "Canonical fund dimension retained on the ledger row."),
        new("entityId", "string", "Canonical entity dimension retained on the ledger row."),
        new("sleeveId", "string", "Canonical sleeve dimension retained on the ledger row."),
        new("strategyId", "string", "Canonical strategy dimension retained on the ledger row."),
        new("investorId", "string", "Canonical investor dimension retained on the ledger row."),
        new("capitalAccountId", "string", "Canonical capital-account dimension retained on the ledger row."),
        new("instrumentId", "guid", "Canonical instrument dimension retained on the ledger row."),
        new("taxLotId", "string", "Canonical tax-lot dimension retained on the ledger row."),
        new("costCenterId", "string", "Canonical cost-center dimension retained on the ledger row."),
        new("counterpartyId", "string", "Canonical counterparty dimension retained on the ledger row."),
        new("organizationId", "string", "Canonical organization dimension retained on the ledger row."),
        new("portfolioId", "string", "Canonical portfolio dimension retained on the ledger row."),
        new("bookId", "string", "Canonical ledger-book dimension retained on the ledger row."),
        new("accountId", "string", "Canonical account dimension retained on the ledger row."),
        new("customerId", "string", "Canonical customer dimension retained on the ledger row."),
        new("vendorId", "string", "Canonical vendor dimension retained on the ledger row."),
        new("projectId", "string", "Canonical project dimension retained on the ledger row."),
        new("externalGlDimensionsJson", "json", "Deterministic external-GL dimension map retained on the ledger row."),
        new("balance", "decimal", "Signed ledger snapshot amount."),
        new("journalEntryCount", "integer", "Journal entries in the source snapshot."),
        new("ledgerEntryCount", "integer", "Ledger entries in the source snapshot."),
        new("sourceAsOfUtc", "datetime", "Snapshot as-of timestamp in UTC.")
    ];

    private static readonly StructuredReportingExportColumnDto[] InvestmentPortfolioCutColumns =
    [
        new("cutId", "string", "Stable fund, strategy, or user-tag cut identifier."),
        new("label", "string", "Human-readable cut label."),
        new("kind", "string", "Cut kind: Fund, Strategy, or UserTag."),
        new("currency", "string", "Reporting currency."),
        new("grossExposure", "decimal", "Gross exposure for the cut."),
        new("netExposure", "decimal", "Net exposure for the cut."),
        new("totalCash", "decimal", "Cash balance included in the cut."),
        new("pendingSettlement", "decimal", "Pending settlement amount."),
        new("totalPnl", "decimal", "Realized plus unrealized P&L."),
        new("shadowNav", "decimal", "Shadow NAV for the cut."),
        new("shadowNavVariance", "decimal", "Shadow NAV variance against source equity."),
        new("sourceCount", "integer", "Number of contributing source records."),
        new("versionStamp", "string", "Deterministic export/cut version marker.")
    ];

    private static readonly StructuredReportingExportColumnDto[] CrossFundConsolidationColumns =
    [
        new("consolidationId", "string", "Stable cross-fund consolidation row identifier."),
        new("label", "string", "Human-readable consolidation label."),
        new("scope", "string", "Consolidation scope: Company, Fund, or Entity."),
        new("currency", "string", "Reporting currency."),
        new("isReady", "boolean", "Whether the row has source-backed records."),
        new("fundCount", "integer", "Distinct fund count represented by the row."),
        new("entityCount", "integer", "Distinct legal/entity count represented by the row."),
        new("accountCount", "integer", "Contributing account source count."),
        new("runCount", "integer", "Contributing strategy-run source count."),
        new("grossExposure", "decimal", "Aggregated gross exposure."),
        new("netExposure", "decimal", "Aggregated net exposure."),
        new("totalCash", "decimal", "Aggregated cash balance."),
        new("pendingSettlement", "decimal", "Aggregated pending settlement amount."),
        new("totalPnl", "decimal", "Aggregated realized plus unrealized P&L."),
        new("shadowNav", "decimal", "Aggregated source-backed shadow NAV."),
        new("shadowNavVariance", "decimal", "Shadow NAV variance against aggregate net exposure."),
        new("sourceCount", "integer", "Contributing account plus run source count."),
        new("readinessSummary", "string", "Source-backed readiness description."),
        new("versionStamp", "string", "Deterministic export/consolidation version marker.")
    ];

    private static readonly StructuredReportingExportColumnDto[] InvestmentTopNContributionAnalyticsColumns =
    [
        new("analyticsId", "string", "Stable Top-N or contribution analytics row identifier."),
        new("kind", "string", "Analytics row kind: TopWinner, TopLaggard, or Contribution."),
        new("scope", "string", "Analytics scope: Security, Strategy, or AssetClass."),
        new("rank", "integer", "Rank within the analytics kind and scope."),
        new("label", "string", "Human-readable row label."),
        new("symbol", "string", "Security symbol when the row is security-scoped."),
        new("classification", "string", "Security asset class, strategy, or asset-class classification."),
        new("currency", "string", "Reporting currency."),
        new("realizedPnl", "decimal", "Realized P&L included in the analytics row."),
        new("unrealizedPnl", "decimal", "Unrealized P&L included in the analytics row."),
        new("totalPnl", "decimal", "Realized plus unrealized P&L."),
        new("contributionPercent", "decimal", "Percent contribution to total portfolio P&L."),
        new("heatMapIntensity", "decimal", "Absolute P&L intensity used by reporting heat maps."),
        new("sourceCount", "integer", "Number of contributing source runs."),
        new("asOfUtc", "datetime", "Analytics as-of timestamp in UTC."),
        new("readinessSummary", "string", "Source-backed readiness description."),
        new("versionStamp", "string", "Deterministic analytics version marker."),
        new("tags", "string", "Pipe-delimited analytics tags.")
    ];

    public async Task<StructuredReportingExportPayloadDto> GetStructuredReportingExportAsync(
        StructuredReportingExportRequestDto request,
        CancellationToken ct = default) =>
        await GetStructuredReportingExportAsync(request, accessContext: null, ct).ConfigureAwait(false);

    public async Task<StructuredReportingExportPayloadDto> GetStructuredReportingExportAsync(
        StructuredReportingExportRequestDto request,
        ReportAccessQueryContext? accessContext,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FundProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExportId);
        ct.ThrowIfCancellationRequested();

        var exportId = NormalizeStructuredExportId(request.ExportId);
        var workspace = await GetWorkspaceAsync(
            new FundOperationsWorkspaceQuery(
                request.FundProfileId,
                request.AsOf,
                request.Currency),
            accessContext,
            ct).ConfigureAwait(false);

        var descriptor = workspace.Reporting.StructuredExports?
            .FirstOrDefault(export => string.Equals(export.ExportId, exportId, StringComparison.OrdinalIgnoreCase));
        if (descriptor is null)
        {
            throw new KeyNotFoundException($"Structured reporting export '{request.ExportId}' is not available.");
        }

        var columns = GetStructuredExportColumns(exportId);
        var rows = BuildStructuredExportRows(exportId, workspace, ct);
        var warnings = descriptor.IsReady
            ? Array.Empty<string>()
            : [$"Export '{descriptor.ExportId}' is not ready. {descriptor.ValidationSummary}"];

        return new StructuredReportingExportPayloadDto(
            descriptor,
            columns,
            rows,
            warnings,
            DateTimeOffset.UtcNow,
            BuildStructuredExportDataDictionary(columns),
            BuildStructuredExportValidationChecks(descriptor, columns, rows),
            GeneratedByPrincipalId: accessContext?.ActorPrincipalId,
            GeneratedForCompanyId: accessContext?.CompanyId,
            GeneratedForGroupPrincipalIds: accessContext?.GroupPrincipalIds?.ToArray() ?? [],
            RowLineage: BuildStructuredExportRowLineage(descriptor.ExportId, columns, rows));
    }

    private static IReadOnlyList<StructuredReportingExportDto> BuildStructuredReportingExports(
        string fundProfileId,
        FundLedgerSummary ledger,
        FundLedgerReconciliationSnapshot ledgerReconciliationSnapshot,
        IReadOnlyList<PortfolioReportingCutDto> portfolioCuts,
        IReadOnlyList<PortfolioReportingAnalyticsRowDto> analyticsRows,
        IReadOnlyList<CrossFundReportingConsolidationDto> crossFundConsolidations,
        string currency,
        DateTimeOffset asOf)
    {
        var trialBalanceSourceCount = ledger.JournalEntryCount + ledger.LedgerEntryCount;
        var ledgerFactSourceCount = ledgerReconciliationSnapshot.Consolidated.JournalEntryCount
            + ledgerReconciliationSnapshot.Consolidated.LedgerEntryCount;
        var portfolioSourceCount = portfolioCuts.Sum(static cut => cut.SourceCount);
        var portfolioReady = portfolioCuts.Any(static cut =>
            cut.GrossExposure != 0m
            || cut.NetExposure != 0m
            || cut.TotalCash != 0m
            || cut.TotalPnl != 0m
            || cut.ShadowNav != 0m);
        var crossFundSourceCount = crossFundConsolidations
            .FirstOrDefault(static row => row.Scope == CrossFundReportingConsolidationScopeDto.Company)
            ?.SourceCount
            ?? crossFundConsolidations.Sum(static row => row.SourceCount);
        var crossFundReady = crossFundConsolidations.Any(static row => row.IsReady);
        var analyticsReady = analyticsRows.Any(static row =>
            !string.Equals(row.AnalyticsId, "analytics:blocked", StringComparison.OrdinalIgnoreCase)
            && row.SourceCount > 0);
        var analyticsSourceCount = analyticsRows
            .Where(static row => !string.Equals(row.AnalyticsId, "analytics:blocked", StringComparison.OrdinalIgnoreCase))
            .Sum(static row => row.SourceCount);

        return
        [
            BuildStructuredExportDescriptor(
                fundProfileId,
                RegulatoryTrialBalanceExportId,
                "Regulatory trial balance",
                StructuredReportingExportPurposeDto.Regulatory,
                GovernanceReportArtifactFormatDto.Csv,
                "fund-trial-balance",
                "Regulatory and compliance reporting",
                ledger.TrialBalance.Count,
                RegulatoryTrialBalanceColumns.Length,
                trialBalanceSourceCount,
                currency,
                asOf,
                ledger.TrialBalance.Count > 0 && trialBalanceSourceCount > 0,
                "Exports normalized trial-balance rows with ledger source counts and Security Master identifiers.",
                "No normalized trial-balance rows are available for this as-of date.",
                ["regulatory", "trial-balance", "ledger"]),
            BuildStructuredExportDescriptor(
                fundProfileId,
                WarehouseLedgerFactsExportId,
                "Warehouse ledger facts",
                StructuredReportingExportPurposeDto.DataWarehouse,
                GovernanceReportArtifactFormatDto.Json,
                "ledger-reconciliation-facts",
                "Data warehouse and lakehouse ingestion",
                ledgerReconciliationSnapshot.Consolidated.Balances.Count,
                WarehouseLedgerFactColumns.Length,
                ledgerFactSourceCount,
                currency,
                asOf,
                ledgerReconciliationSnapshot.Consolidated.Balances.Count > 0 && ledgerFactSourceCount > 0,
                "Exports consolidated ledger snapshot facts for downstream warehouse loading.",
                "No consolidated ledger snapshot facts are available for this as-of date.",
                ["warehouse", "ledger", "reconciliation"]),
            BuildStructuredExportDescriptor(
                fundProfileId,
                InvestmentPortfolioCutsExportId,
                "Investment portfolio cuts",
                StructuredReportingExportPurposeDto.InvestmentDecision,
                GovernanceReportArtifactFormatDto.Xlsx,
                "portfolio-reporting-cuts",
                "Investment and risk decision workflows",
                portfolioCuts.Count,
                InvestmentPortfolioCutColumns.Length,
                portfolioSourceCount,
                currency,
                asOf,
                portfolioReady,
                "Exports fund, strategy, and user-tag exposure, cash, P&L, and shadow-NAV cuts.",
                "No source-backed portfolio cut values are available for this as-of date.",
                ["investment", "portfolio-cuts", "shadow-nav"]),
            BuildStructuredExportDescriptor(
                fundProfileId,
                InvestmentTopNContributionAnalyticsExportId,
                "Top-N contribution analytics",
                StructuredReportingExportPurposeDto.InvestmentDecision,
                GovernanceReportArtifactFormatDto.Csv,
                "portfolio-topn-contribution-analytics",
                "Investment and risk decision workflows",
                analyticsRows.Count,
                InvestmentTopNContributionAnalyticsColumns.Length,
                analyticsSourceCount,
                currency,
                asOf,
                analyticsReady,
                "Exports source-backed Top-N winners, laggards, and contribution rows with P&L percentages and heat-map intensities.",
                "No source-backed Top-N or contribution analytics rows are available for this as-of date.",
                ["investment", "top-n", "contribution", "analytics"]),
            BuildStructuredExportDescriptor(
                fundProfileId,
                CrossFundConsolidationExportId,
                "Cross-fund consolidation",
                StructuredReportingExportPurposeDto.InvestmentDecision,
                GovernanceReportArtifactFormatDto.Xlsx,
                "cross-fund-reporting-consolidation",
                "Investment and operating committee reporting",
                crossFundConsolidations.Count,
                CrossFundConsolidationColumns.Length,
                crossFundSourceCount,
                currency,
                asOf,
                crossFundReady,
                "Exports company, fund, and entity-level consolidated exposure and performance rows.",
                "No source-backed cross-fund consolidation rows are available for this as-of date.",
                ["investment", "cross-fund", "consolidation"])
        ];
    }

    private static StructuredReportingExportDto BuildStructuredExportDescriptor(
        string fundProfileId,
        string exportId,
        string label,
        StructuredReportingExportPurposeDto purpose,
        GovernanceReportArtifactFormatDto format,
        string dataset,
        string consumer,
        int rowCount,
        int fieldCount,
        int sourceCount,
        string currency,
        DateTimeOffset asOf,
        bool isReady,
        string readySummary,
        string blockedSummary,
        IReadOnlyList<string> tags)
    {
        var retainedPath = BuildStructuredExportRetainedPath(fundProfileId, exportId, asOf, format);
        var retainedManifestPath = BuildStructuredExportRetainedManifestPath(fundProfileId, exportId, asOf);
        var versionStamp = BuildStructuredExportVersionStamp(asOf, rowCount, sourceCount);
        var integrityHash = BuildStructuredExportIntegrityHash(
            fundProfileId,
            exportId,
            dataset,
            consumer,
            currency,
            asOf,
            rowCount,
            fieldCount,
            sourceCount,
            retainedPath,
            retainedManifestPath,
            versionStamp);

        return new StructuredReportingExportDto(
            exportId,
            label,
            purpose,
            format,
            dataset,
            consumer,
            StructuredReportingExportSchemaVersion,
            rowCount,
            fieldCount,
            sourceCount,
            currency,
            asOf,
            isReady,
            retainedPath,
            BuildStructuredExportRoute(fundProfileId, exportId, asOf, currency, format),
            UiApiRoutes.WorkstationReporting,
            isReady
                ? $"{readySummary} {rowCount} row(s), {fieldCount} field(s), and {sourceCount} source record(s) are ready."
                : blockedSummary,
            UiApiRoutes.FundReportPacks,
            versionStamp,
            tags,
            isReady ? [] : [blockedSummary],
            retainedManifestPath,
            integrityHash,
            BuildStructuredExportIntegritySummary(rowCount, fieldCount, sourceCount, integrityHash),
            rowCount);
    }

    private static IReadOnlyList<StructuredReportingExportColumnDto> GetStructuredExportColumns(string exportId) =>
        NormalizeStructuredExportId(exportId) switch
        {
            RegulatoryTrialBalanceExportId => RegulatoryTrialBalanceColumns,
            WarehouseLedgerFactsExportId => WarehouseLedgerFactColumns,
            InvestmentPortfolioCutsExportId => InvestmentPortfolioCutColumns,
            InvestmentTopNContributionAnalyticsExportId => InvestmentTopNContributionAnalyticsColumns,
            CrossFundConsolidationExportId => CrossFundConsolidationColumns,
            _ => throw new KeyNotFoundException($"Structured reporting export '{exportId}' is not available.")
        };

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> BuildStructuredExportRows(
        string exportId,
        FundOperationsWorkspaceDto workspace,
        CancellationToken ct)
        => NormalizeStructuredExportId(exportId) switch
        {
            RegulatoryTrialBalanceExportId => BuildRegulatoryTrialBalanceRows(workspace, ct),
            WarehouseLedgerFactsExportId => BuildWarehouseLedgerFactRows(workspace, ct),
            InvestmentPortfolioCutsExportId => BuildInvestmentPortfolioCutRows(workspace, ct),
            InvestmentTopNContributionAnalyticsExportId => BuildInvestmentTopNContributionAnalyticsRows(workspace, ct),
            CrossFundConsolidationExportId => BuildCrossFundConsolidationRows(workspace, ct),
            _ => throw new KeyNotFoundException($"Structured reporting export '{exportId}' is not available.")
        };

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> BuildRegulatoryTrialBalanceRows(
        FundOperationsWorkspaceDto workspace,
        CancellationToken ct)
    {
        return workspace.Ledger.TrialBalance
            .OrderBy(static row => row.AccountType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.AccountName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(row =>
            {
                ct.ThrowIfCancellationRequested();
                return BuildStructuredRow(
                    ("accountName", row.AccountName),
                    ("accountType", row.AccountType),
                    ("symbol", row.Symbol),
                    ("financialAccountId", row.FinancialAccountId),
                    ("fundId", row.Dimensions?.FundId),
                    ("entityId", row.Dimensions?.EntityId),
                    ("sleeveId", row.Dimensions?.SleeveId),
                    ("strategyId", row.Dimensions?.StrategyId),
                    ("investorId", row.Dimensions?.InvestorId),
                    ("capitalAccountId", row.Dimensions?.CapitalAccountId),
                    ("instrumentId", FundReportArtifactRenderer.FormatStructuredGuid(row.Dimensions?.InstrumentId)),
                    ("taxLotId", row.Dimensions?.TaxLotId),
                    ("costCenterId", row.Dimensions?.CostCenterId),
                    ("counterpartyId", row.Dimensions?.CounterpartyId),
                    ("organizationId", row.Dimensions?.OrganizationId),
                    ("portfolioId", row.Dimensions?.PortfolioId),
                    ("bookId", row.Dimensions?.BookId),
                    ("accountId", row.Dimensions?.AccountId),
                    ("customerId", row.Dimensions?.CustomerId),
                    ("vendorId", row.Dimensions?.VendorId),
                    ("projectId", row.Dimensions?.ProjectId),
                    ("externalGlDimensionsJson", FundReportArtifactRenderer.FormatExternalGlDimensions(row.Dimensions)),
                    ("currency", workspace.BaseCurrency),
                    ("balance", FormatStructuredDecimal(row.Balance)),
                    ("entryCount", row.EntryCount.ToString(CultureInfo.InvariantCulture)),
                    ("securityId", row.Security?.SecurityId.ToString("D")),
                    ("securityDisplayName", row.Security?.DisplayName),
                    ("sourceAsOfUtc", FormatStructuredDateTime(workspace.AsOf)));
            })
            .ToArray();
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> BuildWarehouseLedgerFactRows(
        FundOperationsWorkspaceDto workspace,
        CancellationToken ct)
    {
        var snapshot = workspace.LedgerReconciliationSnapshot.Consolidated;
        return snapshot.Balances
            .OrderBy(static row => row.AccountType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.AccountName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(row =>
            {
                ct.ThrowIfCancellationRequested();
                return BuildStructuredRow(
                    ("scope", workspace.Ledger.ScopeKind.ToString()),
                    ("accountName", row.AccountName),
                    ("accountType", row.AccountType),
                    ("symbol", row.Symbol),
                    ("financialAccountId", row.FinancialAccountId),
                    ("fundId", row.Dimensions?.FundId),
                    ("entityId", row.Dimensions?.EntityId),
                    ("sleeveId", row.Dimensions?.SleeveId),
                    ("strategyId", row.Dimensions?.StrategyId),
                    ("investorId", row.Dimensions?.InvestorId),
                    ("capitalAccountId", row.Dimensions?.CapitalAccountId),
                    ("instrumentId", FundReportArtifactRenderer.FormatStructuredGuid(row.Dimensions?.InstrumentId)),
                    ("taxLotId", row.Dimensions?.TaxLotId),
                    ("costCenterId", row.Dimensions?.CostCenterId),
                    ("counterpartyId", row.Dimensions?.CounterpartyId),
                    ("organizationId", row.Dimensions?.OrganizationId),
                    ("portfolioId", row.Dimensions?.PortfolioId),
                    ("bookId", row.Dimensions?.BookId),
                    ("accountId", row.Dimensions?.AccountId),
                    ("customerId", row.Dimensions?.CustomerId),
                    ("vendorId", row.Dimensions?.VendorId),
                    ("projectId", row.Dimensions?.ProjectId),
                    ("externalGlDimensionsJson", FundReportArtifactRenderer.FormatExternalGlDimensions(row.Dimensions)),
                    ("balance", FormatStructuredDecimal(row.Balance)),
                    ("journalEntryCount", snapshot.JournalEntryCount.ToString(CultureInfo.InvariantCulture)),
                    ("ledgerEntryCount", snapshot.LedgerEntryCount.ToString(CultureInfo.InvariantCulture)),
                    ("sourceAsOfUtc", FormatStructuredDateTime(snapshot.Timestamp)));
            })
            .ToArray();
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> BuildInvestmentPortfolioCutRows(
        FundOperationsWorkspaceDto workspace,
        CancellationToken ct)
    {
        return (workspace.Reporting.PortfolioCuts ?? [])
            .OrderBy(static cut => cut.Kind)
            .ThenBy(static cut => cut.Label, StringComparer.OrdinalIgnoreCase)
            .Select(cut =>
            {
                ct.ThrowIfCancellationRequested();
                return BuildStructuredRow(
                    ("cutId", cut.CutId),
                    ("label", cut.Label),
                    ("kind", cut.Kind.ToString()),
                    ("currency", cut.Currency),
                    ("grossExposure", FormatStructuredDecimal(cut.GrossExposure)),
                    ("netExposure", FormatStructuredDecimal(cut.NetExposure)),
                    ("totalCash", FormatStructuredDecimal(cut.TotalCash)),
                    ("pendingSettlement", FormatStructuredDecimal(cut.PendingSettlement)),
                    ("totalPnl", FormatStructuredDecimal(cut.TotalPnl)),
                    ("shadowNav", FormatStructuredDecimal(cut.ShadowNav)),
                    ("shadowNavVariance", FormatStructuredDecimal(cut.ShadowNavVariance)),
                    ("sourceCount", cut.SourceCount.ToString(CultureInfo.InvariantCulture)),
                    ("versionStamp", cut.VersionStamp));
            })
            .ToArray();
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> BuildInvestmentTopNContributionAnalyticsRows(
        FundOperationsWorkspaceDto workspace,
        CancellationToken ct)
    {
        return (workspace.Reporting.AnalyticsRows ?? [])
            .OrderBy(static row => row.Kind)
            .ThenBy(static row => row.Scope)
            .ThenBy(static row => row.Rank)
            .ThenBy(static row => row.Label, StringComparer.OrdinalIgnoreCase)
            .Select(row =>
            {
                ct.ThrowIfCancellationRequested();
                return BuildStructuredRow(
                    ("analyticsId", row.AnalyticsId),
                    ("kind", row.Kind.ToString()),
                    ("scope", row.Scope.ToString()),
                    ("rank", row.Rank.ToString(CultureInfo.InvariantCulture)),
                    ("label", row.Label),
                    ("symbol", row.Symbol),
                    ("classification", row.Classification),
                    ("currency", row.Currency),
                    ("realizedPnl", FormatStructuredDecimal(row.RealizedPnl)),
                    ("unrealizedPnl", FormatStructuredDecimal(row.UnrealizedPnl)),
                    ("totalPnl", FormatStructuredDecimal(row.TotalPnl)),
                    ("contributionPercent", FormatStructuredPercent(row.ContributionPercent)),
                    ("heatMapIntensity", FormatStructuredPercent(row.HeatMapIntensity)),
                    ("sourceCount", row.SourceCount.ToString(CultureInfo.InvariantCulture)),
                    ("asOfUtc", FormatStructuredDateTime(row.AsOf)),
                    ("readinessSummary", row.ReadinessSummary),
                    ("versionStamp", row.VersionStamp),
                    ("tags", string.Join("|", row.Tags)));
            })
            .ToArray();
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> BuildCrossFundConsolidationRows(
        FundOperationsWorkspaceDto workspace,
        CancellationToken ct)
    {
        return (workspace.Reporting.CrossFundConsolidations ?? [])
            .OrderBy(static row => row.Scope)
            .ThenBy(static row => row.Label, StringComparer.OrdinalIgnoreCase)
            .Select(row =>
            {
                ct.ThrowIfCancellationRequested();
                return BuildStructuredRow(
                    ("consolidationId", row.ConsolidationId),
                    ("label", row.Label),
                    ("scope", row.Scope.ToString()),
                    ("currency", row.Currency),
                    ("isReady", row.IsReady ? "true" : "false"),
                    ("fundCount", row.FundCount.ToString(CultureInfo.InvariantCulture)),
                    ("entityCount", row.EntityCount.ToString(CultureInfo.InvariantCulture)),
                    ("accountCount", row.AccountCount.ToString(CultureInfo.InvariantCulture)),
                    ("runCount", row.RunCount.ToString(CultureInfo.InvariantCulture)),
                    ("grossExposure", FormatStructuredDecimal(row.GrossExposure)),
                    ("netExposure", FormatStructuredDecimal(row.NetExposure)),
                    ("totalCash", FormatStructuredDecimal(row.TotalCash)),
                    ("pendingSettlement", FormatStructuredDecimal(row.PendingSettlement)),
                    ("totalPnl", FormatStructuredDecimal(row.TotalPnl)),
                    ("shadowNav", FormatStructuredDecimal(row.ShadowNav)),
                    ("shadowNavVariance", FormatStructuredDecimal(row.ShadowNavVariance)),
                    ("sourceCount", row.SourceCount.ToString(CultureInfo.InvariantCulture)),
                    ("readinessSummary", row.ReadinessSummary),
                    ("versionStamp", row.VersionStamp));
            })
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string?> BuildStructuredRow(params (string Key, string? Value)[] values)
    {
        var row = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            row[key] = value;
        }

        return row;
    }

    private static IReadOnlyList<StructuredReportingExportDataDictionaryFieldDto> BuildStructuredExportDataDictionary(
        IReadOnlyList<StructuredReportingExportColumnDto> columns) =>
        columns
            .Select((column, index) => new StructuredReportingExportDataDictionaryFieldDto(
                column.Name,
                column.DataType,
                column.Description,
                index + 1))
            .ToArray();

    private static IReadOnlyList<StructuredReportingExportValidationCheckDto> BuildStructuredExportValidationChecks(
        StructuredReportingExportDto descriptor,
        IReadOnlyList<StructuredReportingExportColumnDto> columns,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows) =>
    [
        new StructuredReportingExportValidationCheckDto(
            "readiness",
            descriptor.IsReady ? "Passed" : "Warning",
            descriptor.ValidationSummary ?? (descriptor.IsReady
                ? "Structured export is ready."
                : "Structured export readiness is blocked.")),
        new StructuredReportingExportValidationCheckDto(
            "column-count",
            descriptor.FieldCount == columns.Count ? "Passed" : "Failed",
            $"Descriptor declares {descriptor.FieldCount.ToString(CultureInfo.InvariantCulture)} field(s); payload contains {columns.Count.ToString(CultureInfo.InvariantCulture)} column(s)."),
        new StructuredReportingExportValidationCheckDto(
            "row-count",
            descriptor.RowCount == rows.Count ? "Passed" : descriptor.IsReady ? "Failed" : "Warning",
            $"Descriptor declares {descriptor.RowCount.ToString(CultureInfo.InvariantCulture)} row(s); payload contains {rows.Count.ToString(CultureInfo.InvariantCulture)} row(s)."),
        new StructuredReportingExportValidationCheckDto(
            "source-count",
            descriptor.SourceCount > 0 ? "Passed" : "Warning",
            descriptor.SourceCount > 0
                ? $"Structured export is backed by {descriptor.SourceCount.ToString(CultureInfo.InvariantCulture)} source record(s)."
                : "No source records are linked to this structured export.")
    ];

    private static IReadOnlyList<StructuredReportingExportRowLineageDto> BuildStructuredExportRowLineage(
        string exportId,
        IReadOnlyList<StructuredReportingExportColumnDto> columns,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows) =>
        rows
            .Select((row, index) => new StructuredReportingExportRowLineageDto(
                index + 1,
                ResolveStructuredExportRowKey(row, index),
                ComputeStructuredExportRowHash(exportId, columns, row)))
            .ToArray();

    private static string ResolveStructuredExportRowKey(
        IReadOnlyDictionary<string, string?> row,
        int index)
    {
        foreach (var key in new[] { "cutId", "analyticsId", "consolidationId", "accountName", "scope", "label" })
        {
            if (row.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return $"row-{(index + 1).ToString(CultureInfo.InvariantCulture)}";
    }

    private static string ComputeStructuredExportRowHash(
        string exportId,
        IReadOnlyList<StructuredReportingExportColumnDto> columns,
        IReadOnlyDictionary<string, string?> row)
    {
        var builder = new StringBuilder();
        builder.Append(exportId);
        foreach (var column in columns)
        {
            builder.Append('|');
            builder.Append(column.Name);
            builder.Append('=');
            builder.Append(row.TryGetValue(column.Name, out var value) ? value : string.Empty);
        }

        return Sha256Digest.ComputeUtf8(builder.ToString());
    }

    private static string BuildStructuredExportRoute(
        string fundProfileId,
        string exportId,
        DateTimeOffset asOf,
        string currency,
        GovernanceReportArtifactFormatDto format)
    {
        var route = UiApiRoutes.WithParam(UiApiRoutes.ReportingStructuredExport, "exportId", exportId);
        var queryParts = new List<string>
        {
            $"fundProfileId={Uri.EscapeDataString(fundProfileId)}",
            $"asOf={Uri.EscapeDataString(FormatStructuredDateTime(asOf))}",
            $"currency={Uri.EscapeDataString(currency)}"
        };
        if (format == GovernanceReportArtifactFormatDto.Csv)
        {
            queryParts.Add("format=csv");
        }
        else if (format == GovernanceReportArtifactFormatDto.Xlsx)
        {
            queryParts.Add("format=xlsx");
        }

        var query = string.Join('&', queryParts);
        return UiApiRoutes.WithQuery(route, query);
    }

    private static string BuildStructuredExportRetainedPath(
        string fundProfileId,
        string exportId,
        DateTimeOffset asOf,
        GovernanceReportArtifactFormatDto format)
    {
        var extension = format switch
        {
            GovernanceReportArtifactFormatDto.Csv => "csv",
            GovernanceReportArtifactFormatDto.Xlsx => "xlsx",
            GovernanceReportArtifactFormatDto.Json => "json",
            GovernanceReportArtifactFormatDto.Html => "html",
            GovernanceReportArtifactFormatDto.Pdf => "pdf",
            _ => "dat"
        };
        return $"exports/reporting/{NormalizeCutId(fundProfileId)}/{asOf.UtcDateTime:yyyyMMddHHmmss}/{exportId}.{extension}";
    }

    private static string BuildStructuredExportRetainedManifestPath(
        string fundProfileId,
        string exportId,
        DateTimeOffset asOf) =>
        $"exports/reporting/{NormalizeCutId(fundProfileId)}/{asOf.UtcDateTime:yyyyMMddHHmmss}/{exportId}.manifest.json";

    private static string BuildStructuredExportIntegrityHash(
        string fundProfileId,
        string exportId,
        string dataset,
        string consumer,
        string currency,
        DateTimeOffset asOf,
        int rowCount,
        int fieldCount,
        int sourceCount,
        string retainedPath,
        string retainedManifestPath,
        string versionStamp)
    {
        var input = string.Join('|',
            NormalizeCutId(fundProfileId),
            exportId,
            dataset,
            consumer,
            currency,
            asOf.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            rowCount.ToString(CultureInfo.InvariantCulture),
            fieldCount.ToString(CultureInfo.InvariantCulture),
            sourceCount.ToString(CultureInfo.InvariantCulture),
            retainedPath,
            retainedManifestPath,
            versionStamp);
        return Sha256Digest.ComputeUtf8(input);
    }

    private static string BuildStructuredExportIntegritySummary(
        int rowCount,
        int fieldCount,
        int sourceCount,
        string integrityHash) =>
        $"{rowCount.ToString(CultureInfo.InvariantCulture)} row(s), {fieldCount.ToString(CultureInfo.InvariantCulture)} field(s), and {sourceCount.ToString(CultureInfo.InvariantCulture)} source record(s) retained with SHA-256 {integrityHash}.";

    private static string BuildStructuredExportVersionStamp(DateTimeOffset asOf, int rowCount, int sourceCount) =>
        $"structured-export:{asOf.UtcDateTime:yyyyMMddHHmmss}:rows-{rowCount}:sources-{sourceCount}:schema-{StructuredReportingExportSchemaVersion}";

    private static string NormalizeStructuredExportId(string value) =>
        value.Trim().ToLowerInvariant();

    private static string FormatStructuredDecimal(decimal value) =>
        value.ToString("0.#############################", CultureInfo.InvariantCulture);

    private static string FormatStructuredPercent(decimal value) =>
        value.ToString("0.0###", CultureInfo.InvariantCulture);

    private static string FormatStructuredDateTime(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}
