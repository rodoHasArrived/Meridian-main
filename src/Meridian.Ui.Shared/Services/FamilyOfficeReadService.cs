using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Contracts;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Assembles the cross-surface single-family-office read model from existing structure,
/// account, portfolio, ledger, and reconciliation seams.
/// </summary>
public sealed class FamilyOfficeReadService
{
    private const string EmptyStateGuidance = "Configure a FamilyOffice client and link its entities, funds, accounts, ownership percentages, statements, valuations, and evidence before using the family-office workstation.";
    private static readonly DateOnly FallbackAsOfDate = new(1970, 1, 1);

    private readonly IFundStructureService? _fundStructureService;
    private readonly IFundAccountService? _fundAccountService;
    private readonly StrategyRunReadService? _strategyRunReadService;
    private readonly TimeProvider _timeProvider;

    public FamilyOfficeReadService(
        IFundStructureService? fundStructureService,
        IFundAccountService? fundAccountService,
        StrategyRunReadService? strategyRunReadService = null,
        TimeProvider? timeProvider = null)
    {
        _fundStructureService = fundStructureService;
        _fundAccountService = fundAccountService;
        _strategyRunReadService = strategyRunReadService;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<FamilyOfficeOverviewDto> GetOverviewAsync(CancellationToken ct = default)
    {
        var generatedAt = _timeProvider.GetUtcNow();
        if (_fundStructureService is null)
        {
            return BuildEmptyOverview(generatedAt, ["Family-office structure service is not registered."]);
        }

        var graph = await _fundStructureService
            .GetOrganizationStructureAsync(new OrganizationStructureQuery(ActiveOnly: true, AsOf: generatedAt), ct)
            .ConfigureAwait(false);

        var familyClients = graph.Clients
            .Where(static client => client.ClientSegmentKind == ClientSegmentKind.FamilyOffice)
            .OrderBy(static client => client.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (familyClients.Length == 0)
        {
            return BuildEmptyOverview(generatedAt, ["No FamilyOffice client segment is configured in the fund-structure graph."]);
        }

        var warnings = new List<string>();
        var familyClient = familyClients[0];
        if (familyClients.Length > 1)
        {
            warnings.Add("Multiple FamilyOffice clients are configured; this read service currently projects the first active family-office client.");
        }

        var asOfDate = DateOnly.FromDateTime(generatedAt.UtcDateTime);
        var relevant = SelectRelevantGraph(graph, familyClient);
        var entities = BuildEntities(relevant, warnings);
        var ownershipGraph = BuildOwnershipGraph(relevant, asOfDate);
        var evidenceLinks = BuildStructureEvidenceLinks(relevant, asOfDate);
        var accountContexts = await BuildAccountContextsAsync(relevant.Accounts, generatedAt, ct).ConfigureAwait(false);
        if (relevant.Accounts.Count > 0 && accountContexts.Count == 0)
        {
            warnings.Add("Family-office accounts are linked, but the fund-account read service is unavailable, so balances, cash, liabilities, reconciliation, and evidence completeness cannot be assembled.");
        }

        var accounts = accountContexts.Select(static context => context.Summary).ToArray();
        var publicAssets = BuildPublicAssets(accountContexts, warnings);
        var privateAssets = BuildPrivateAssets(relevant, asOfDate, warnings);
        var commitments = BuildCommitments(relevant, asOfDate, warnings);
        AddEvidenceAndReconciliationWarnings(accounts, publicAssets, privateAssets, commitments, warnings);
        var balanceSheet = BuildBalanceSheet(familyClient, accountContexts, privateAssets, commitments, warnings, generatedAt, asOfDate);
        var readiness = BuildReadiness(warnings, evidenceLinks, generatedAt, accounts, entities.Count, balanceSheet);

        return new FamilyOfficeOverviewDto(
            FamilyOfficeId: familyClient.ClientId.ToString("D"),
            DisplayName: familyClient.Name,
            BaseCurrency: familyClient.BaseCurrency,
            AsOfDate: asOfDate,
            BalanceSheet: balanceSheet,
            Entities: entities,
            OwnershipGraph: ownershipGraph,
            Accounts: accounts,
            PublicAssets: publicAssets,
            PrivateAssets: privateAssets,
            CapitalCommitments: commitments,
            RecentCapitalActivity: [],
            Readiness: readiness,
            EvidenceLinks: evidenceLinks);
    }

    public async Task<FamilyOfficeBalanceSheetResponseDto> GetBalanceSheetAsync(CancellationToken ct = default)
    {
        var overview = await GetOverviewAsync(ct).ConfigureAwait(false);
        return new FamilyOfficeBalanceSheetResponseDto(overview.BalanceSheet, BuildEndpointState(overview));
    }

    public async Task<FamilyOfficeEntitiesResponseDto> GetEntitiesAsync(CancellationToken ct = default)
    {
        var overview = await GetOverviewAsync(ct).ConfigureAwait(false);
        return new FamilyOfficeEntitiesResponseDto(overview.Entities, BuildEndpointState(overview));
    }

    public async Task<FamilyOfficeOwnershipGraphResponseDto> GetOwnershipGraphAsync(CancellationToken ct = default)
    {
        var overview = await GetOverviewAsync(ct).ConfigureAwait(false);
        return new FamilyOfficeOwnershipGraphResponseDto(overview.OwnershipGraph, BuildEndpointState(overview));
    }

    private static FamilyOfficeEndpointStateDto BuildEndpointState(FamilyOfficeOverviewDto overview)
    {
        var isEmpty = overview.Entities.Count == 0;
        return new FamilyOfficeEndpointStateDto(
            IsEmpty: isEmpty,
            EmptyStateGuidance: isEmpty ? EmptyStateGuidance : null,
            Warnings: overview.Readiness.Blockers,
            GeneratedAtUtc: overview.Readiness.GeneratedAtUtc);
    }

    private FamilyOfficeOverviewDto BuildEmptyOverview(DateTimeOffset generatedAt, IReadOnlyList<string> warnings)
    {
        var asOfDate = DateOnly.FromDateTime(generatedAt.UtcDateTime);
        var stateEvidence = new FamilyOfficeEvidenceLinkDto(
            EvidenceId: "family-office-empty-state",
            SourceSystem: "fund-structure",
            SourceDocumentId: null,
            Label: "Family-office configuration is missing",
            EvidenceType: "configuration-guidance",
            Route: "/settings/fund-structure",
            CapturedAtUtc: generatedAt,
            AsOfDate: asOfDate,
            ValuationDate: null,
            CompletenessStatus: "Missing");

        var balanceSheet = new FamilyBalanceSheetDto(
            FamilyOfficeId: "family-office-unconfigured",
            BaseCurrency: "USD",
            TotalAssets: 0m,
            TotalLiabilities: 0m,
            NetWorth: 0m,
            LiquidAssets: 0m,
            MarketableSecurities: 0m,
            PrivateAssets: 0m,
            RealAssets: 0m,
            CashAndEquivalents: 0m,
            UnfundedCommitments: 0m,
            SourceSystem: "empty-state",
            SourceDocumentId: null,
            AsOfDate: asOfDate,
            ValuationDate: null,
            EvidenceCompleteness: "Missing",
            ReconciliationStatus: "NotConfigured",
            LastReviewedBy: null,
            LastReviewedAtUtc: null);

        var readiness = new FamilyOfficeReadinessDto(
            Status: "NotConfigured",
            EvidenceCompleteness: "Missing",
            ReconciliationStatus: "NotConfigured",
            OpenExceptionCount: warnings.Count,
            ItemsNeedingReviewCount: warnings.Count,
            GeneratedAtUtc: generatedAt,
            LastReviewedBy: null,
            LastReviewedAtUtc: null,
            Blockers: [EmptyStateGuidance, .. warnings],
            EvidenceLinks: [stateEvidence]);

        return new FamilyOfficeOverviewDto(
            FamilyOfficeId: "family-office-unconfigured",
            DisplayName: "Family office setup required",
            BaseCurrency: "USD",
            AsOfDate: asOfDate,
            BalanceSheet: balanceSheet,
            Entities: [],
            OwnershipGraph: new FamilyOwnershipGraphDto(asOfDate, [], [], [stateEvidence]),
            Accounts: [],
            PublicAssets: [],
            PrivateAssets: [],
            CapitalCommitments: [],
            RecentCapitalActivity: [],
            Readiness: readiness,
            EvidenceLinks: [stateEvidence]);
    }

    private static RelevantFamilyGraph SelectRelevantGraph(OrganizationStructureGraphDto graph, ClientSummaryDto familyClient)
    {
        var clientId = familyClient.ClientId;
        var business = graph.Businesses.FirstOrDefault(b => b.BusinessId == familyClient.BusinessId);
        var portfolios = graph.InvestmentPortfolios.Where(p => p.ClientId == clientId || (business is not null && p.BusinessId == business.BusinessId)).ToArray();
        var funds = graph.Funds.Where(f => business is not null && f.BusinessId == business.BusinessId).ToArray();
        var fundIds = funds.Select(static fund => fund.FundId).ToHashSet();
        var portfolioEntityIds = portfolios.Select(static portfolio => portfolio.EntityId).OfType<Guid>().ToHashSet();
        var fundEntityIds = funds.SelectMany(static fund => fund.EntityIds).ToHashSet();
        var entityIds = portfolioEntityIds.Concat(fundEntityIds).ToHashSet();
        var accounts = graph.Accounts
            .Where(account =>
                (account.EntityId.HasValue && entityIds.Contains(account.EntityId.Value)) ||
                (account.FundId.HasValue && fundIds.Contains(account.FundId.Value)) ||
                portfolios.Any(portfolio => portfolio.AccountIds.Contains(account.AccountId)) ||
                funds.Any(fund => fund.AccountIds.Contains(account.AccountId)))
            .OrderBy(static account => account.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        entityIds.UnionWith(accounts.Select(static account => account.EntityId).OfType<Guid>());
        var entities = graph.Entities.Where(entity => entityIds.Contains(entity.EntityId)).ToArray();
        var nodeIds = new HashSet<Guid>([clientId]);
        if (business is not null)
            nodeIds.Add(business.BusinessId);
        foreach (var fund in funds)
            nodeIds.Add(fund.FundId);
        foreach (var portfolio in portfolios)
            nodeIds.Add(portfolio.InvestmentPortfolioId);
        foreach (var entity in entities)
            nodeIds.Add(entity.EntityId);
        foreach (var account in accounts)
            nodeIds.Add(account.AccountId);

        var links = graph.OwnershipLinks
            .Where(link => nodeIds.Contains(link.ParentNodeId) || nodeIds.Contains(link.ChildNodeId))
            .OrderBy(static link => link.EffectiveFrom)
            .ThenBy(static link => link.OwnershipLinkId)
            .ToArray();
        var assignments = graph.Assignments.Where(assignment => nodeIds.Contains(assignment.NodeId)).ToArray();

        return new RelevantFamilyGraph(familyClient, business, funds, entities, portfolios, accounts, links, assignments);
    }

    private static IReadOnlyList<FamilyEntityDto> BuildEntities(RelevantFamilyGraph graph, List<string> warnings)
    {
        var linksByChild = graph.OwnershipLinks
            .GroupBy(static link => link.ChildNodeId)
            .ToDictionary(static group => group.Key, static group => group.First());
        var entities = new List<FamilyEntityDto>();

        AddEntity(graph.Client.ClientId, graph.Client.Name, "FamilyOfficeClient", null, null, graph.Client.BaseCurrency, graph.Business?.BusinessId, null, false, graph.Client.EffectiveFrom);
        if (graph.Business is not null)
        {
            AddEntity(graph.Business.BusinessId, graph.Business.Name, graph.Business.BusinessKind.ToString(), null, null, graph.Business.BaseCurrency, graph.Business.OrganizationId, null, true, graph.Business.EffectiveFrom);
        }

        foreach (var fund in graph.Funds)
        {
            AddEntity(fund.FundId, fund.Name, "Fund", null, null, fund.BaseCurrency, fund.BusinessId, linksByChild.GetValueOrDefault(fund.FundId)?.OwnershipPercent, true, fund.EffectiveFrom);
        }

        foreach (var entity in graph.LegalEntities)
        {
            AddEntity(entity.EntityId, entity.Name, entity.EntityType.ToString(), entity.Jurisdiction, null, entity.BaseCurrency, linksByChild.GetValueOrDefault(entity.EntityId)?.ParentNodeId, linksByChild.GetValueOrDefault(entity.EntityId)?.OwnershipPercent, false, entity.EffectiveFrom);
        }

        foreach (var account in graph.Accounts)
        {
            AddEntity(account.AccountId, account.DisplayName, $"Account:{account.AccountType}", null, null, account.BaseCurrency, account.EntityId ?? account.FundId, linksByChild.GetValueOrDefault(account.AccountId)?.OwnershipPercent, false, account.EffectiveFrom);
        }

        if (entities.Any(static entity => entity.ParentEntityId is not null && entity.OwnershipPercent is null))
        {
            warnings.Add("One or more entity ownership mappings are missing ownership percentages.");
        }

        return entities
            .OrderBy(static entity => entity.EntityType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entity => entity.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        void AddEntity(Guid id, string name, string type, string? jurisdiction, string? taxResidency, string currency, Guid? parentId, decimal? ownership, bool operating, DateTimeOffset effectiveFrom)
        {
            entities.Add(new FamilyEntityDto(
                EntityId: id.ToString("D"),
                DisplayName: name,
                EntityType: type,
                Jurisdiction: jurisdiction,
                TaxResidency: taxResidency,
                BaseCurrency: currency,
                ParentEntityId: parentId?.ToString("D"),
                OwnershipPercent: ownership,
                IsOperatingEntity: operating,
                EvidenceLinks:
                [
                    new FamilyOfficeEvidenceLinkDto(
                        EvidenceId: $"fund-structure:{id:D}",
                        SourceSystem: "fund-structure",
                        SourceDocumentId: null,
                        Label: $"{type} structure record effective {effectiveFrom:yyyy-MM-dd}",
                        EvidenceType: "structure-record",
                        Route: "/settings/fund-structure",
                        CapturedAtUtc: effectiveFrom,
                        AsOfDate: DateOnly.FromDateTime(effectiveFrom.UtcDateTime),
                        ValuationDate: null,
                        CompletenessStatus: "Complete")
                ]));
        }
    }

    private async Task<IReadOnlyList<AccountContext>> BuildAccountContextsAsync(IReadOnlyList<AccountSummaryDto> accounts, DateTimeOffset generatedAt, CancellationToken ct)
    {
        var contexts = new List<AccountContext>(accounts.Count);
        if (_fundAccountService is null)
        {
            return contexts;
        }

        foreach (var account in accounts)
        {
            ct.ThrowIfCancellationRequested();
            var snapshot = await _fundAccountService.GetLatestBalanceSnapshotAsync(account.AccountId, ct).ConfigureAwait(false);
            var reconciliationRuns = await _fundAccountService.GetReconciliationRunsAsync(account.AccountId, ct).ConfigureAwait(false);
            var openBreaks = await _fundAccountService.GetOpenBreaksAsync(account.AccountId, ct).ConfigureAwait(false);
            var readiness = await _fundAccountService.GetReadinessAsync(account.AccountId, ct).ConfigureAwait(false);
            StrategyRunDetail? runDetail = null;
            if (_strategyRunReadService is not null && !string.IsNullOrWhiteSpace(account.RunId))
            {
                runDetail = await _strategyRunReadService.GetRunDetailAsync(account.RunId, ct).ConfigureAwait(false);
            }

            contexts.Add(BuildAccountContext(account, snapshot, reconciliationRuns, openBreaks, readiness, runDetail, generatedAt));
        }

        return contexts;
    }

    private static AccountContext BuildAccountContext(
        AccountSummaryDto account,
        AccountBalanceSnapshotDto? snapshot,
        IReadOnlyList<AccountReconciliationRunDto> reconciliationRuns,
        IReadOnlyList<AccountReconciliationBreakDto> openBreaks,
        AccountReadinessSnapshotDto? readiness,
        StrategyRunDetail? runDetail,
        DateTimeOffset generatedAt)
    {
        var asOfDate = snapshot?.AsOfDate ?? (runDetail?.Portfolio is not null ? DateOnly.FromDateTime(runDetail.Portfolio.AsOf.UtcDateTime) : DateOnly.FromDateTime(generatedAt.UtcDateTime));
        var cash = snapshot?.CashBalance ?? runDetail?.Portfolio?.Cash ?? 0m;
        var marketValue = snapshot?.SecuritiesMarketValue ?? runDetail?.Portfolio?.LongMarketValue ?? 0m;
        var accrued = snapshot?.AccruedInterest ?? 0m;
        var totalEquity = cash + marketValue + accrued + (snapshot?.PendingSettlement ?? 0m);
        var reconciliationStatus = ResolveReconciliationStatus(reconciliationRuns, openBreaks);
        var evidenceCompleteness = ResolveEvidenceCompleteness(snapshot?.ExternalReference, readiness);
        var source = snapshot?.Source ?? (runDetail?.Portfolio is null ? "fund-account" : "portfolio-read-service");

        var summary = new FamilyAccountSummaryDto(
            AccountId: account.AccountId.ToString("D"),
            EntityId: (account.EntityId ?? account.FundId ?? account.AccountId).ToString("D"),
            DisplayName: account.DisplayName,
            AccountType: account.AccountType.ToString(),
            Custodian: account.Institution,
            ProviderAccountId: account.CustodianDetails?.SubAccountNumber ?? account.BankDetails?.AccountNumber,
            Currency: snapshot?.Currency ?? account.BaseCurrency,
            CashBalance: cash,
            MarketValue: marketValue,
            AccruedIncome: accrued,
            TotalEquity: totalEquity,
            SourceSystem: source,
            SourceDocumentId: snapshot?.ExternalReference,
            AsOfDate: asOfDate,
            ValuationDate: snapshot?.AsOfDate,
            EvidenceCompleteness: evidenceCompleteness,
            ReconciliationStatus: reconciliationStatus,
            LastReviewedBy: null,
            LastReviewedAtUtc: null);

        return new AccountContext(account, summary, snapshot, reconciliationRuns, openBreaks, readiness, runDetail);
    }

    private static IReadOnlyList<FamilyAssetSummaryDto> BuildPublicAssets(IReadOnlyList<AccountContext> accounts, List<string> warnings)
    {
        var assets = new List<FamilyAssetSummaryDto>();
        var netWorth = accounts.Sum(static account => account.Summary.TotalEquity);
        foreach (var account in accounts)
        {
            var portfolio = account.RunDetail?.Portfolio;
            if (portfolio is null)
            {
                if (account.Summary.MarketValue > 0m)
                {
                    assets.Add(new FamilyAssetSummaryDto(
                        AssetId: $"account-securities:{account.Account.AccountId:D}",
                        EntityId: account.Summary.EntityId,
                        AccountId: account.Summary.AccountId,
                        DisplayName: $"{account.Summary.DisplayName} marketable securities",
                        AssetClass: "MarketableSecurities",
                        Symbol: null,
                        Currency: account.Summary.Currency,
                        Quantity: 1m,
                        MarketValue: account.Summary.MarketValue,
                        CostBasis: account.Summary.MarketValue,
                        UnrealizedGainLoss: 0m,
                        PercentOfNetWorth: Percent(account.Summary.MarketValue, netWorth),
                        SourceSystem: account.Summary.SourceSystem,
                        SourceDocumentId: account.Summary.SourceDocumentId,
                        AsOfDate: account.Summary.AsOfDate,
                        ValuationDate: account.Summary.ValuationDate,
                        EvidenceCompleteness: account.Summary.EvidenceCompleteness,
                        ReconciliationStatus: account.Summary.ReconciliationStatus,
                        LastReviewedBy: null,
                        LastReviewedAtUtc: null));
                }

                continue;
            }

            foreach (var position in portfolio.Positions)
            {
                var marketValue = Math.Abs(position.Quantity) * position.AverageCostBasis + position.UnrealizedPnl;
                assets.Add(new FamilyAssetSummaryDto(
                    AssetId: $"position:{portfolio.RunId}:{position.Symbol}",
                    EntityId: account.Summary.EntityId,
                    AccountId: account.Summary.AccountId,
                    DisplayName: position.Security?.DisplayName ?? position.Symbol,
                    AssetClass: position.Security?.AssetClass ?? "PublicSecurity",
                    Symbol: position.Symbol,
                    Currency: account.Summary.Currency,
                    Quantity: position.Quantity,
                    MarketValue: marketValue,
                    CostBasis: Math.Abs(position.Quantity) * position.AverageCostBasis,
                    UnrealizedGainLoss: position.UnrealizedPnl,
                    PercentOfNetWorth: Percent(marketValue, netWorth),
                    SourceSystem: "portfolio-read-service",
                    SourceDocumentId: portfolio.RunId,
                    AsOfDate: DateOnly.FromDateTime(portfolio.AsOf.UtcDateTime),
                    ValuationDate: DateOnly.FromDateTime(portfolio.AsOf.UtcDateTime),
                    EvidenceCompleteness: position.Security is null ? "Partial" : "Complete",
                    ReconciliationStatus: account.Summary.ReconciliationStatus,
                    LastReviewedBy: null,
                    LastReviewedAtUtc: null));
            }
        }

        if (accounts.Any(static account => account.Summary.MarketValue > 0m) && assets.Count == 0)
        {
            warnings.Add("Account market values exist but no portfolio position read model is available for asset-level drilldown.");
        }

        return assets.OrderByDescending(static asset => asset.MarketValue).ToArray();
    }

    private static IReadOnlyList<PrivateAssetSummaryDto> BuildPrivateAssets(RelevantFamilyGraph graph, DateOnly asOfDate, List<string> warnings)
    {
        var privateAssignments = graph.Assignments
            .Where(static assignment => assignment.AssignmentType.Equals("PrivateAssetMark", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (privateAssignments.Length == 0)
        {
            warnings.Add("No private asset marks are configured for the family-office entity graph.");
            return [];
        }

        return privateAssignments.Select(assignment => new PrivateAssetSummaryDto(
            PrivateAssetId: assignment.AssignmentId.ToString("D"),
            EntityId: assignment.NodeId.ToString("D"),
            DisplayName: assignment.AssignmentReference,
            AssetType: "PrivateAsset",
            Currency: graph.Client.BaseCurrency,
            CurrentValue: 0m,
            CommitmentAmount: null,
            CalledCapital: null,
            DistributedCapital: null,
            ValuationMethod: "AssignmentReference",
            SourceSystem: "fund-structure-assignment",
            SourceDocumentId: assignment.AssignmentReference,
            AsOfDate: asOfDate,
            ValuationDate: null,
            EvidenceCompleteness: "Partial",
            ReconciliationStatus: "NotReconciled",
            LastReviewedBy: null,
            LastReviewedAtUtc: null)).ToArray();
    }

    private static IReadOnlyList<CapitalCommitmentDto> BuildCommitments(RelevantFamilyGraph graph, DateOnly asOfDate, List<string> warnings)
    {
        var commitmentAssignments = graph.Assignments
            .Where(static assignment => assignment.AssignmentType.Equals("CapitalCommitment", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (commitmentAssignments.Length == 0)
        {
            warnings.Add("No unfunded commitment source is configured for the family-office entity graph.");
            return [];
        }

        return commitmentAssignments.Select(assignment => new CapitalCommitmentDto(
            CommitmentId: assignment.AssignmentId.ToString("D"),
            EntityId: assignment.NodeId.ToString("D"),
            VehicleName: assignment.AssignmentReference,
            Strategy: "PrivateMarkets",
            Currency: graph.Client.BaseCurrency,
            CommitmentAmount: 0m,
            CalledAmount: 0m,
            UnfundedAmount: 0m,
            DistributedAmount: 0m,
            CurrentNav: 0m,
            VintageYear: null,
            SourceSystem: "fund-structure-assignment",
            SourceDocumentId: assignment.AssignmentReference,
            AsOfDate: asOfDate,
            ValuationDate: null,
            EvidenceCompleteness: "Partial",
            ReconciliationStatus: "NotReconciled",
            LastReviewedBy: null,
            LastReviewedAtUtc: null)).ToArray();
    }

    private static void AddEvidenceAndReconciliationWarnings(
        IReadOnlyList<FamilyAccountSummaryDto> accounts,
        IReadOnlyList<FamilyAssetSummaryDto> publicAssets,
        IReadOnlyList<PrivateAssetSummaryDto> privateAssets,
        IReadOnlyList<CapitalCommitmentDto> commitments,
        List<string> warnings)
    {
        if (accounts.Any(static account => !account.ReconciliationStatus.Equals("Reconciled", StringComparison.OrdinalIgnoreCase))
            || publicAssets.Any(static asset => !asset.ReconciliationStatus.Equals("Reconciled", StringComparison.OrdinalIgnoreCase))
            || privateAssets.Any(static asset => !asset.ReconciliationStatus.Equals("Reconciled", StringComparison.OrdinalIgnoreCase))
            || commitments.Any(static commitment => !commitment.ReconciliationStatus.Equals("Reconciled", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add("One or more family-office values are unreconciled or have open reconciliation breaks.");
        }

        if (accounts.Any(static account => !account.EvidenceCompleteness.Equals("Complete", StringComparison.OrdinalIgnoreCase))
            || publicAssets.Any(static asset => !asset.EvidenceCompleteness.Equals("Complete", StringComparison.OrdinalIgnoreCase))
            || privateAssets.Any(static asset => !asset.EvidenceCompleteness.Equals("Complete", StringComparison.OrdinalIgnoreCase))
            || commitments.Any(static commitment => !commitment.EvidenceCompleteness.Equals("Complete", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add("One or more balances, asset marks, liabilities, commitments, or reconciliations are missing complete evidence.");
        }
    }

    private static FamilyBalanceSheetDto BuildBalanceSheet(
        ClientSummaryDto familyClient,
        IReadOnlyList<AccountContext> accounts,
        IReadOnlyList<PrivateAssetSummaryDto> privateAssets,
        IReadOnlyList<CapitalCommitmentDto> commitments,
        List<string> warnings,
        DateTimeOffset generatedAt,
        DateOnly asOfDate)
    {
        var cash = accounts.Sum(static account => account.Summary.CashBalance);
        var securities = accounts.Sum(static account => account.Summary.MarketValue);
        var privateValue = privateAssets.Sum(static asset => asset.CurrentValue) + commitments.Sum(static commitment => commitment.CurrentNav);
        var liabilities = accounts.Sum(static account => account.RunDetail?.Ledger?.LiabilityBalance ?? 0m);
        var totalAssets = cash + securities + accounts.Sum(static account => account.Summary.AccruedIncome) + privateValue;
        var latestAsOf = accounts.Select(static account => account.Summary.AsOfDate).DefaultIfEmpty(asOfDate).Max();
        var staleCutoff = DateOnly.FromDateTime(generatedAt.UtcDateTime).AddDays(-2);
        if (accounts.Any(account => account.Summary.AsOfDate < staleCutoff))
        {
            warnings.Add("One or more account or asset values are stale compared with the current workstation date.");
        }


        return new FamilyBalanceSheetDto(
            FamilyOfficeId: familyClient.ClientId.ToString("D"),
            BaseCurrency: familyClient.BaseCurrency,
            TotalAssets: totalAssets,
            TotalLiabilities: liabilities,
            NetWorth: totalAssets - liabilities,
            LiquidAssets: cash + securities,
            MarketableSecurities: securities,
            PrivateAssets: privateValue,
            RealAssets: 0m,
            CashAndEquivalents: cash,
            UnfundedCommitments: commitments.Sum(static commitment => commitment.UnfundedAmount),
            SourceSystem: "family-office-read-service",
            SourceDocumentId: null,
            AsOfDate: latestAsOf,
            ValuationDate: latestAsOf == FallbackAsOfDate ? null : latestAsOf,
            EvidenceCompleteness: accounts.All(static account => account.Summary.EvidenceCompleteness == "Complete") ? "Complete" : "Partial",
            ReconciliationStatus: accounts.All(static account => account.Summary.ReconciliationStatus == "Reconciled") ? "Reconciled" : "NeedsReview",
            LastReviewedBy: null,
            LastReviewedAtUtc: null);
    }

    private static FamilyOwnershipGraphDto BuildOwnershipGraph(RelevantFamilyGraph graph, DateOnly asOfDate)
    {
        var nodes = new List<FamilyOwnershipNodeDto>
        {
            new(graph.Client.ClientId.ToString("D"), graph.Client.Name, "FamilyOfficeClient", graph.Client.ClientId.ToString("D"), null, graph.Client.BaseCurrency)
        };
        if (graph.Business is not null)
        {
            nodes.Add(new FamilyOwnershipNodeDto(graph.Business.BusinessId.ToString("D"), graph.Business.Name, graph.Business.BusinessKind.ToString(), graph.Business.BusinessId.ToString("D"), null, graph.Business.BaseCurrency));
        }
        nodes.AddRange(graph.Funds.Select(static fund => new FamilyOwnershipNodeDto(fund.FundId.ToString("D"), fund.Name, "Fund", fund.FundId.ToString("D"), null, fund.BaseCurrency)));
        nodes.AddRange(graph.LegalEntities.Select(static entity => new FamilyOwnershipNodeDto(entity.EntityId.ToString("D"), entity.Name, entity.EntityType.ToString(), entity.EntityId.ToString("D"), entity.Jurisdiction, entity.BaseCurrency)));
        nodes.AddRange(graph.Accounts.Select(static account => new FamilyOwnershipNodeDto(account.AccountId.ToString("D"), account.DisplayName, $"Account:{account.AccountType}", account.AccountId.ToString("D"), null, account.BaseCurrency)));

        var edges = graph.OwnershipLinks.Select(static link => new FamilyOwnershipEdgeDto(
            EdgeId: link.OwnershipLinkId.ToString("D"),
            SourceNodeId: link.ParentNodeId.ToString("D"),
            TargetNodeId: link.ChildNodeId.ToString("D"),
            RelationshipType: link.RelationshipType.ToString(),
            OwnershipPercent: link.OwnershipPercent,
            EffectiveFrom: DateOnly.FromDateTime(link.EffectiveFrom.UtcDateTime),
            EffectiveTo: link.EffectiveTo is null ? null : DateOnly.FromDateTime(link.EffectiveTo.Value.UtcDateTime),
            SourceDocumentId: link.Notes)).ToArray();

        return new FamilyOwnershipGraphDto(asOfDate, nodes.DistinctBy(static node => node.NodeId).ToArray(), edges, BuildStructureEvidenceLinks(graph, asOfDate));
    }

    private static IReadOnlyList<FamilyOfficeEvidenceLinkDto> BuildStructureEvidenceLinks(RelevantFamilyGraph graph, DateOnly asOfDate)
        =>
        [
            new FamilyOfficeEvidenceLinkDto(
                EvidenceId: $"fund-structure-family-office:{graph.Client.ClientId:D}",
                SourceSystem: "fund-structure",
                SourceDocumentId: null,
                Label: "Family-office structure graph",
                EvidenceType: "structure-graph",
                Route: "/settings/fund-structure",
                CapturedAtUtc: graph.Client.EffectiveFrom,
                AsOfDate: asOfDate,
                ValuationDate: null,
                CompletenessStatus: graph.OwnershipLinks.All(static link => link.OwnershipPercent.HasValue) ? "Complete" : "Partial")
        ];

    private static FamilyOfficeReadinessDto BuildReadiness(
        IReadOnlyList<string> warnings,
        IReadOnlyList<FamilyOfficeEvidenceLinkDto> evidenceLinks,
        DateTimeOffset generatedAt,
        IReadOnlyList<FamilyAccountSummaryDto> accounts,
        int entityCount,
        FamilyBalanceSheetDto balanceSheet)
    {
        var distinctWarnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static warning => warning, StringComparer.OrdinalIgnoreCase).ToArray();
        var status = entityCount == 0 ? "NotConfigured" : distinctWarnings.Length == 0 ? "Ready" : "Degraded";
        return new FamilyOfficeReadinessDto(
            Status: status,
            EvidenceCompleteness: balanceSheet.EvidenceCompleteness,
            ReconciliationStatus: balanceSheet.ReconciliationStatus,
            OpenExceptionCount: accounts.Count(static account => account.ReconciliationStatus != "Reconciled"),
            ItemsNeedingReviewCount: distinctWarnings.Length,
            GeneratedAtUtc: generatedAt,
            LastReviewedBy: null,
            LastReviewedAtUtc: null,
            Blockers: distinctWarnings,
            EvidenceLinks: evidenceLinks);
    }

    private static string ResolveReconciliationStatus(IReadOnlyList<AccountReconciliationRunDto> runs, IReadOnlyList<AccountReconciliationBreakDto> breaks)
    {
        if (breaks.Count > 0)
        {
            return "OpenBreaks";
        }

        var latest = runs.OrderByDescending(static run => run.RequestedAt).FirstOrDefault();
        if (latest is null)
        {
            return "NotReconciled";
        }

        return latest.TotalBreaks == 0 && latest.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase)
            ? "Reconciled"
            : latest.Status;
    }

    private static string ResolveEvidenceCompleteness(string? sourceDocumentId, AccountReadinessSnapshotDto? readiness)
    {
        if (readiness is { IsReady: false })
        {
            return "Partial";
        }

        return string.IsNullOrWhiteSpace(sourceDocumentId) ? "Missing" : "Complete";
    }

    private static decimal Percent(decimal value, decimal total) => total == 0m ? 0m : decimal.Round(value / total * 100m, 4);

    private sealed record RelevantFamilyGraph(
        ClientSummaryDto Client,
        BusinessSummaryDto? Business,
        IReadOnlyList<FundSummaryDto> Funds,
        IReadOnlyList<LegalEntitySummaryDto> LegalEntities,
        IReadOnlyList<InvestmentPortfolioSummaryDto> Portfolios,
        IReadOnlyList<AccountSummaryDto> Accounts,
        IReadOnlyList<OwnershipLinkDto> OwnershipLinks,
        IReadOnlyList<FundStructureAssignmentDto> Assignments);

    private sealed record AccountContext(
        AccountSummaryDto Account,
        FamilyAccountSummaryDto Summary,
        AccountBalanceSnapshotDto? Snapshot,
        IReadOnlyList<AccountReconciliationRunDto> ReconciliationRuns,
        IReadOnlyList<AccountReconciliationBreakDto> OpenBreaks,
        AccountReadinessSnapshotDto? Readiness,
        StrategyRunDetail? RunDetail);
}
