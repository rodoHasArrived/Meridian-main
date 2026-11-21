using System.Globalization;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

public sealed partial class FinancialRecordExplorerReadService
{
    private async Task<FinancialRecordExplorerDto> BuildLedgerExplorerAsync(
        string tenantId,
        FundOwnershipScope fundScope,
        FinancialRecordExplorerQueryDto? query,
        CancellationToken ct)
    {
        var readService = _runReadService;
        if (readService is null)
        {
            return CreateBlockedExplorer(
                LedgerExplorerId,
                "Strategy Run Ledger Explorer",
                "Explore the selected strategy run's simulated trial-balance records and their proof links. This is a run artifact, not the posted journal.",
                "Strategy run read service is not registered.");
        }

        // The run the caller asked for, carried on the explorer's own filter channel. Without it
        // this surface answered for the newest run whatever the screen was showing, so an operator
        // reading an older run's trial balance saw that run's rows under the newest run's header,
        // proof links and scope.
        var requestedRunId = ReadRequestedRunId(query);
        var candidateSet = await LoadLedgerRunCandidatesAsync(readService, fundScope, requestedRunId, ct)
            .ConfigureAwait(false);
        var candidates = candidateSet.Candidates;
        var source = await TryLoadLedgerRunDetailAsync(
            readService,
            fundScope,
            candidates,
            candidateSet.Requested,
            ct).ConfigureAwait(false);
        if (source is null)
        {
            return await CreateEmptyExplorerAsync(
                LedgerExplorerId,
                "Strategy Run Ledger Explorer",
                "Explore the selected strategy run's simulated trial-balance records and their proof links. This is a run artifact, not the posted journal.",
                "No source-backed ledger projection is available.",
                tenantId,
                ct).ConfigureAwait(false);
        }

        var run = source.Summary;
        var ledger = source.Ledger!;
        var rows = ledger.TrialBalance
            .Select((line, index) => BuildLedgerRow(run, ledger, line, index))
            .ToArray();

        return await CreateExplorerAsync(
            LedgerExplorerId,
            "Strategy Run Ledger Explorer",
            "Explore the selected strategy run's simulated trial-balance records and their proof links. This is a run artifact, not the posted journal.",
            $"Source-backed ledger projection from run {run.RunId}.",
            [
                new("Ledger source", "Strategy run (simulation) — not the posted journal"),
                .. BuildScope(run, ledger.AsOf, ledger.LedgerReference)
            ],
            BuildLedgerSummary(run, ledger, rows.Length),
            BuildLedgerRunViews(IncludeResolvedRun(candidates, run), run.RunId),
            BuildLedgerFilters(run, ledger),
            [
                new("accountName", "Account", Width: 220),
                new("accountType", "Type", Width: 110),
                new("symbol", "Symbol", Width: 90),
                new("balance", "Balance", "money", 120, IsRightAligned: true),
                new("entryCount", "Entries", "number", 90, IsRightAligned: true),
                new("fundId", "Fund", Width: 130),
                new("entityId", "Entity", Width: 130),
                new("bookId", "Book", Width: 130),
                new("accountId", "Account Scope", Width: 150),
                new("source", "Source", Width: 140)
            ],
            rows,
            BuildExplorerProofActions(run, UiApiRoutes.WithParam(UiApiRoutes.RunsLedgerTrialBalance, "runId", run.RunId)),
            tenantId,
            query,
            ct).ConfigureAwait(false);
    }

    private async Task<FinancialRecordExplorerDto> BuildPortfolioExplorerAsync(
        string tenantId,
        FundOwnershipScope fundScope,
        FinancialRecordExplorerQueryDto? query,
        CancellationToken ct)
    {
        var readService = _runReadService;
        if (readService is null)
        {
            return CreateBlockedExplorer(
                PortfolioExplorerId,
                "Portfolio Explorer",
                "Explore retained account and aggregate position records.",
                "Strategy run read service is not registered.");
        }

        var source = await TryLoadLatestRunDetailAsync(
            readService,
            fundScope,
            detail => detail.Portfolio?.Positions.Count > 0,
            ct).ConfigureAwait(false);
        if (source is null)
        {
            return await CreateEmptyExplorerAsync(
                PortfolioExplorerId,
                "Portfolio Explorer",
                "Explore retained account and aggregate position records.",
                "No source-backed portfolio projection is available.",
                tenantId,
                ct).ConfigureAwait(false);
        }

        var run = source.Summary;
        var portfolio = source.Portfolio!;
        var rows = portfolio.Positions
            .Select((position, index) => BuildPortfolioRow(run, portfolio, position, index))
            .ToArray();

        return await CreateExplorerAsync(
            PortfolioExplorerId,
            "Portfolio Explorer",
            "Explore retained account and aggregate position records.",
            $"Source-backed portfolio projection from run {run.RunId}.",
            BuildScope(run, portfolio.AsOf, portfolio.PortfolioId),
            BuildPortfolioSummary(run, portfolio, rows.Length),
            BuildSystemViews(PortfolioExplorerId, "Positions", "Open retained positions for the selected run."),
            BuildPortfolioFilters(run, portfolio),
            [
                new("symbol", "Symbol", Width: 100),
                new("quantity", "Quantity", "number", 100, IsRightAligned: true),
                new("averageCost", "Average Cost", "money", 120, IsRightAligned: true),
                new("realizedPnl", "Realized P&L", "money", 120, IsRightAligned: true),
                new("unrealizedPnl", "Unrealized P&L", "money", 120, IsRightAligned: true),
                new("security", "Security", Width: 180),
                new("coverage", "Coverage", Width: 110)
            ],
            rows,
            BuildExplorerProofActions(run, UiApiRoutes.WithParam(UiApiRoutes.WorkstationPortfolio, "runId", run.RunId)),
            tenantId,
            query,
            ct).ConfigureAwait(false);
    }

    // includeRunIdentity is true for the run-backed explorers, whose callers hold a strategy permission
    // by construction. The security-instrument explorer also admits Security Master callers, and the
    // run's id, strategy name and mode are not part of that basis -- the run routes serving them admit
    // only the strategy permissions.
    private static IReadOnlyList<FinancialRecordExplorerScopeItemDto> BuildScope(
        StrategyRunSummary run,
        DateTimeOffset asOf,
        string source,
        bool includeRunIdentity = true)
    {
        var items = new List<FinancialRecordExplorerScopeItemDto>(5);
        if (includeRunIdentity)
        {
            items.Add(new("Run", run.RunId));
            items.Add(new("Strategy", run.StrategyName));
            items.Add(new("Mode", run.Mode.ToString()));
        }

        items.Add(new("As of", asOf.ToString("u", CultureInfo.InvariantCulture)));
        items.Add(new("Source", source));
        return items;
    }

    private static IReadOnlyList<FinancialRecordExplorerSummaryItemDto> BuildLedgerSummary(
        StrategyRunSummary run,
        LedgerSummary ledger,
        int rowCount)
        =>
        [
            new("Accounts", rowCount.ToString(CultureInfo.InvariantCulture), "Retained trial-balance rows."),
            new("Assets", FormatCurrency(ledger.AssetBalance), "Source-backed asset balance."),
            new("Liabilities", FormatCurrency(ledger.LiabilityBalance), "Source-backed liability balance."),
            new("Equity", FormatCurrency(ledger.EquityBalance), "Source-backed equity balance."),
            new("Run", run.Status.ToString(), $"Last updated {run.LastUpdatedAt:u}.", ToneFromRunStatus(run.Status))
        ];

    private static IReadOnlyList<FinancialRecordExplorerSummaryItemDto> BuildPortfolioSummary(
        StrategyRunSummary run,
        PortfolioSummary portfolio,
        int rowCount)
        =>
        [
            new("Positions", rowCount.ToString(CultureInfo.InvariantCulture), "Retained position rows."),
            new("Equity", FormatCurrency(portfolio.TotalEquity), "Source-backed total equity."),
            new("Cash", FormatCurrency(portfolio.Cash), "Source-backed cash."),
            new("Net Exposure", FormatCurrency(portfolio.NetExposure), "Source-backed net exposure."),
            new("Run", run.Status.ToString(), $"Last updated {run.LastUpdatedAt:u}.", ToneFromRunStatus(run.Status))
        ];

    private static IReadOnlyList<FinancialRecordExplorerFilterDto> BuildLedgerFilters(
        StrategyRunSummary run,
        LedgerSummary ledger)
    {
        var filters = new List<FinancialRecordExplorerFilterDto>
        {
            new("run", "Run", run.RunId, Tone: FinancialRecordExplorerTone.Info),
            new("ledger", "Ledger", ledger.LedgerReference, Tone: FinancialRecordExplorerTone.Info)
        };

        var dimensions = ledger.TrialBalance
            .Select(static line => line.Dimensions)
            .Append(ledger.Dimensions);

        AddDimensionFilters(filters, dimensions);
        return filters;
    }

    private static IReadOnlyList<FinancialRecordExplorerFilterDto> BuildPortfolioFilters(
        StrategyRunSummary run,
        PortfolioSummary portfolio)
        =>
        [
            new("run", "Run", run.RunId, Tone: FinancialRecordExplorerTone.Info),
            new("portfolio", "Portfolio", portfolio.PortfolioId, Tone: FinancialRecordExplorerTone.Info)
        ];

    private static FinancialRecordExplorerRowDto BuildLedgerRow(
        StrategyRunSummary run,
        LedgerSummary ledger,
        LedgerTrialBalanceLine line,
        int index)
    {
        var recordId = $"ledger:{run.RunId}:{index}";
        var dimensionFields = BuildDimensionFields(line.Dimensions);
        var detail = new FinancialRecordExplorerSelectedRecordDto(
            recordId,
            "Ledger account",
            line.AccountName,
            $"{line.AccountType} - {ledger.LedgerReference}",
            "Source-backed trial-balance line retained for run accounting review.",
            FinancialRecordExplorerTone.Default,
            Fields:
            [
                new("Account Type", line.AccountType),
                new("Balance", FormatCurrency(line.Balance)),
                new("Entries", line.EntryCount.ToString(CultureInfo.InvariantCulture)),
                new("Symbol", line.Symbol ?? "None"),
                new("Financial Account", line.FinancialAccountId ?? "None"),
                .. dimensionFields
            ],
            ProofActions: BuildExplorerProofActions(run, UiApiRoutes.WithParam(UiApiRoutes.RunsLedgerTrialBalance, "runId", run.RunId)),
            UsedIn:
            [
                new("ledger-run", "Run ledger", $"Trial balance belongs to run {run.RunId}.", UiApiRoutes.WithParam(UiApiRoutes.RunsLedger, "runId", run.RunId)),
                new("accounting", "Accounting workspace", "Available to Accounting close, reconciliation, and audit review.", UiApiRoutes.WorkstationAccounting)
            ],
            Impacts:
            [
                new("balance-sheet", "Balance sheet", $"{line.AccountType} balance contributes {FormatCurrency(line.Balance)}.", Tone: ToneFromBalance(line.Balance))
            ],
            FullRecordHref: UiApiRoutes.WithParam(UiApiRoutes.RunsLedgerTrialBalance, "runId", run.RunId));

        return new FinancialRecordExplorerRowDto(
            recordId,
            "ledger",
            line.AccountName,
            ledger.LedgerReference,
            line.AccountType,
            ToneFromBalance(line.Balance),
            Cells:
            [
                new("accountName", line.AccountName),
                new("accountType", line.AccountType),
                new("symbol", line.Symbol ?? "-"),
                new("balance", FormatCurrency(line.Balance), line.Balance.ToString(CultureInfo.InvariantCulture), ToneFromBalance(line.Balance)),
                new("entryCount", line.EntryCount.ToString(CultureInfo.InvariantCulture)),
                new("fundId", DisplayDimension(line.Dimensions?.FundId), line.Dimensions?.FundId ?? string.Empty),
                new("entityId", DisplayDimension(line.Dimensions?.EntityId), line.Dimensions?.EntityId ?? string.Empty),
                new("bookId", DisplayDimension(line.Dimensions?.BookId), line.Dimensions?.BookId ?? string.Empty),
                new("accountId", DisplayDimension(line.Dimensions?.AccountId), line.Dimensions?.AccountId ?? string.Empty),
                new("source", ledger.LedgerReference)
            ],
            detail,
            SourceRunId: run.RunId);
    }

    internal static void AddDimensionFilters(
        ICollection<FinancialRecordExplorerFilterDto> filters,
        IEnumerable<LedgerDimensionSetDto?> dimensionSets)
    {
        var dimensions = dimensionSets
            .Where(static dimensions => dimensions is not null)
            .Cast<LedgerDimensionSetDto>()
            .ToArray();

        AddDimensionFilter(filters, "fund", "Fund", dimensions.Select(static value => value.FundId));
        AddDimensionFilter(filters, "entity", "Entity", dimensions.Select(static value => value.EntityId));
        AddDimensionFilter(filters, "sleeve", "Sleeve", dimensions.Select(static value => value.SleeveId));
        AddDimensionFilter(filters, "strategy", "Strategy", dimensions.Select(static value => value.StrategyId));
        AddDimensionFilter(filters, "portfolio", "Portfolio", dimensions.Select(static value => value.PortfolioId));
        AddDimensionFilter(filters, "book", "Book", dimensions.Select(static value => value.BookId));
        AddDimensionFilter(filters, "account", "Account Scope", dimensions.Select(static value => value.AccountId));
        AddDimensionFilter(filters, "investor", "Investor", dimensions.Select(static value => value.InvestorId));
        AddDimensionFilter(filters, "capital-account", "Capital Account", dimensions.Select(static value => value.CapitalAccountId));
        AddDimensionFilter(filters, "instrument", "Instrument", dimensions.Select(static value => value.InstrumentId?.ToString("D")));
        AddDimensionFilter(filters, "position", "Position", dimensions.Select(static value => value.PositionId?.ToString("D")));
        AddDimensionFilter(filters, "tax-lot", "Tax Lot", dimensions.Select(static value => value.TaxLotId));
        AddDimensionFilter(filters, "cost-center", "Cost Center", dimensions.Select(static value => value.CostCenterId));
        AddDimensionFilter(filters, "counterparty", "Counterparty", dimensions.Select(static value => value.CounterpartyId));
        AddDimensionFilter(filters, "accounting-arrangement", "Accounting arrangement", dimensions.Select(static value => value.AccountingArrangementId));
        AddDimensionFilter(filters, "accounting-arrangement-revision", "Accounting arrangement revision", dimensions.Select(static value => value.AccountingArrangementRevisionId));
        AddDimensionFilter(filters, "chart-account-node", "Chart account node", dimensions.Select(static value => value.ChartAccountNodeId));
        AddDimensionFilter(filters, "account-binding-revision", "Account binding revision", dimensions.Select(static value => value.AccountBindingRevisionId));
        AddDimensionFilter(filters, "intercompany-transaction", "Intercompany transaction", dimensions.Select(static value => value.IntercompanyTransactionId));
        AddDimensionFilter(filters, "intercompany-agreement", "Agreement", dimensions.Select(static value => value.IntercompanyAgreementId?.ToString("D")));
        AddDimensionFilter(filters, "intercompany-agreement-version", "Agreement version", dimensions.Select(static value => value.IntercompanyAgreementVersion?.ToString(CultureInfo.InvariantCulture)));
        AddDimensionFilter(filters, "organization", "Organization", dimensions.Select(static value => value.OrganizationId));
        AddDimensionFilter(filters, "customer", "Customer", dimensions.Select(static value => value.CustomerId));
        AddDimensionFilter(filters, "vendor", "Vendor", dimensions.Select(static value => value.VendorId));
        AddDimensionFilter(filters, "project", "Project", dimensions.Select(static value => value.ProjectId));

        var externalGlDimensions = dimensions
            .SelectMany(static value => value.ExternalGlDimensions)
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .GroupBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in externalGlDimensions)
        {
            AddDimensionFilter(filters, $"external-gl-{Slugify(group.Key)}", $"External GL: {group.Key}", group.Select(static pair => pair.Value));
        }
    }

    private static void AddDimensionFilter(
        ICollection<FinancialRecordExplorerFilterDto> filters,
        string filterPrefix,
        string label,
        IEnumerable<string?> values)
    {
        foreach (var value in values
            .Select(static value => value?.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))
        {
            filters.Add(new FinancialRecordExplorerFilterDto(
                $"dimension-{filterPrefix}-{Slugify(value!)}",
                label,
                value!,
                Tone: FinancialRecordExplorerTone.Info));
        }
    }

    private static IReadOnlyList<FinancialRecordExplorerSummaryItemDto> BuildDimensionFields(LedgerDimensionSetDto? dimensions)
    {
        if (dimensions is null)
        {
            return [];
        }

        var fields = new List<FinancialRecordExplorerSummaryItemDto>();
        AddDimensionField(fields, "Fund", dimensions.FundId);
        AddDimensionField(fields, "Entity", dimensions.EntityId);
        AddDimensionField(fields, "Sleeve", dimensions.SleeveId);
        AddDimensionField(fields, "Strategy", dimensions.StrategyId);
        AddDimensionField(fields, "Portfolio", dimensions.PortfolioId);
        AddDimensionField(fields, "Book", dimensions.BookId);
        AddDimensionField(fields, "Account Scope", dimensions.AccountId);
        AddDimensionField(fields, "Investor", dimensions.InvestorId);
        AddDimensionField(fields, "Capital Account", dimensions.CapitalAccountId);
        AddDimensionField(fields, "Instrument", dimensions.InstrumentId?.ToString("D"));
        AddDimensionField(fields, "Position", dimensions.PositionId?.ToString("D"));
        AddDimensionField(fields, "Tax Lot", dimensions.TaxLotId);
        AddDimensionField(fields, "Cost Center", dimensions.CostCenterId);
        AddDimensionField(fields, "Counterparty", dimensions.CounterpartyId);
        AddDimensionField(fields, "Agreement", dimensions.IntercompanyAgreementId?.ToString("D"));
        AddDimensionField(fields, "Agreement version", dimensions.IntercompanyAgreementVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddDimensionField(fields, "Accounting arrangement", dimensions.AccountingArrangementId);
        AddDimensionField(fields, "Accounting arrangement revision", dimensions.AccountingArrangementRevisionId);
        AddDimensionField(fields, "Chart account node", dimensions.ChartAccountNodeId);
        AddDimensionField(fields, "Account binding revision", dimensions.AccountBindingRevisionId);
        AddDimensionField(fields, "Intercompany transaction", dimensions.IntercompanyTransactionId);
        AddDimensionField(fields, "Organization", dimensions.OrganizationId);
        AddDimensionField(fields, "Customer", dimensions.CustomerId);
        AddDimensionField(fields, "Vendor", dimensions.VendorId);
        AddDimensionField(fields, "Project", dimensions.ProjectId);

        foreach (var pair in dimensions.ExternalGlDimensions
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            AddDimensionField(fields, $"External GL: {pair.Key}", pair.Value);
        }

        return fields;
    }

    private static void AddDimensionField(
        ICollection<FinancialRecordExplorerSummaryItemDto> fields,
        string label,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            fields.Add(new FinancialRecordExplorerSummaryItemDto(label, value, Tone: FinancialRecordExplorerTone.Info));
        }
    }

    private static string DisplayDimension(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value;

    private static FinancialRecordExplorerRowDto BuildPortfolioRow(
        StrategyRunSummary run,
        PortfolioSummary portfolio,
        PortfolioPositionSummary position,
        int index)
    {
        var recordId = $"portfolio:{run.RunId}:{index}:{position.Symbol}";
        var security = position.Security;
        var securityHref = security is null || security.SecurityId == Guid.Empty
            ? string.Empty
            : UiApiRoutes.WithParam(UiApiRoutes.WorkstationSecurityMasterById, "securityId", security.SecurityId.ToString("D"));
        var detail = new FinancialRecordExplorerSelectedRecordDto(
            recordId,
            "Portfolio position",
            position.Symbol,
            $"{portfolio.PortfolioId} - {portfolio.AsOf:u}",
            "Source-backed portfolio position retained for account and aggregate review.",
            position.IsShort ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Default,
            Fields:
            [
                new("Quantity", position.Quantity.ToString(CultureInfo.InvariantCulture)),
                new("Average Cost", FormatCurrency(position.AverageCostBasis)),
                new("Realized P&L", FormatCurrency(position.RealizedPnl), Tone: ToneFromBalance(position.RealizedPnl)),
                new("Unrealized P&L", FormatCurrency(position.UnrealizedPnl), Tone: ToneFromBalance(position.UnrealizedPnl)),
                new("Security", security?.DisplayName ?? "Unresolved", Tone: security is null ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Success)
            ],
            ProofActions: BuildExplorerProofActions(run, UiApiRoutes.WorkstationPortfolio),
            UsedIn:
            [
                new("portfolio-run", "Run portfolio", $"Position belongs to portfolio {portfolio.PortfolioId}.", UiApiRoutes.WorkstationPortfolio),
                new("accounting", "Accounting handoff", "Position can be reconciled against ledger and close records.", UiApiRoutes.WorkstationAccounting)
            ],
            Impacts:
            [
                new("portfolio-equity", "Portfolio equity", $"Unrealized P&L impact is {FormatCurrency(position.UnrealizedPnl)}.", Tone: ToneFromBalance(position.UnrealizedPnl)),
                new("security-master", "Security Master", security is null ? "Security reference is unresolved." : $"Resolved to {security.DisplayName}.", securityHref, security is null ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Success)
            ],
            FullRecordHref: securityHref);

        return new FinancialRecordExplorerRowDto(
            recordId,
            "portfolio",
            position.Symbol,
            portfolio.PortfolioId,
            position.IsShort ? "Short" : "Long",
            position.IsShort ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Default,
            Cells:
            [
                new("symbol", position.Symbol),
                new("quantity", position.Quantity.ToString(CultureInfo.InvariantCulture)),
                new("averageCost", FormatCurrency(position.AverageCostBasis), position.AverageCostBasis.ToString(CultureInfo.InvariantCulture)),
                new("realizedPnl", FormatCurrency(position.RealizedPnl), position.RealizedPnl.ToString(CultureInfo.InvariantCulture), ToneFromBalance(position.RealizedPnl)),
                new("unrealizedPnl", FormatCurrency(position.UnrealizedPnl), position.UnrealizedPnl.ToString(CultureInfo.InvariantCulture), ToneFromBalance(position.UnrealizedPnl)),
                new("security", security?.DisplayName ?? "Unresolved", Tone: security is null ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Success, LinkHref: securityHref),
                new("coverage", security?.CoverageStatus.ToString() ?? "Missing", Tone: ToneFromCoverage(security?.CoverageStatus))
            ],
            detail);
    }


    // The evidence action addresses the packet by run id, so it is itself run identity and is withheld
    // from a caller admitted on the Security Master basis alone rather than offered and refused later.
    private static IReadOnlyList<FinancialRecordExplorerProofActionDto> BuildExplorerProofActions(
        StrategyRunSummary run,
        string primaryHref,
        bool includeRunIdentity = true)
    {
        var actions = new List<FinancialRecordExplorerProofActionDto>(2)
        {
            new("open-source", "Open source record", "Open the source-backed projection used by this explorer.", primaryHref)
        };

        if (includeRunIdentity)
        {
            actions.Add(new("open-evidence", "Evidence packet", "Open the retained evidence packet for the source run.", UiApiRoutes.WithParam(UiApiRoutes.WithParam(UiApiRoutes.WorkstationEvidenceSubjectPacket, "subjectKind", "run"), "subjectId", run.RunId), !string.IsNullOrWhiteSpace(run.AuditReference), "Run does not expose an audit reference.", !string.IsNullOrWhiteSpace(run.AuditReference) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning));
        }

        return actions;
    }

}
