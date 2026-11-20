using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Identity.Auth;
using Meridian.PortfolioRecords.Accounts;
using Meridian.Reporting;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static void MapRecordSearchEndpoint(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationRecordSearch), async (string? q, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var query = q?.Trim() ?? string.Empty;
            if (query.Length > 256)
            {
                return Results.BadRequest();
            }

            if (!TryResolveReconciliationBreakQueueScope(context, out var scope))
            {
                return EndpointHelpers.Forbidden();
            }

            var result = query.Length == 0
                ? new WorkstationRecordSearchResult([], false)
                : await new RecordSearch(context, scope, query).SearchAsync().ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("SearchWorkstationRecords")
        .RequireAuthenticatedSessionOrScopedLocalOperatorRead()
        .RequireAnyPermission(
            UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster, UserPermission.ViewTrades,
            UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports,
            UserPermission.ViewReporting, UserPermission.ManageReporting, UserPermission.ApproveReporting,
            UserPermission.DeliverReporting, UserPermission.ViewDirectLending, UserPermission.ManageDirectLending,
            UserPermission.AdminMaintenance)
        .RequireWorkstationTenantCompanyScope()
        .Produces<WorkstationRecordSearchResult>(200)
        .Produces(400)
        .Produces(401)
        .Produces(403);
    }

    // This read composes the same authorities as the destination workbenches. A session's
    // workspace role admits a family; ownership and effective grants admit individual records.
    private sealed class RecordSearch(HttpContext context, ReconciliationBreakQueueScope scope, string query)
    {
        private const int ResultLimit = 12;
        private static readonly UserPermission[] OperationsPermissions =
            [UserPermission.ViewDirectLending, UserPermission.ManageDirectLending,
             UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster];
        private static readonly UserPermission[] LedgerPermissions =
            [UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports, UserPermission.ManageDirectLending];
        private static readonly UserPermission[] DraftPermissions =
            [UserPermission.ManageLedgerReports, UserPermission.ManageDirectLending];

        private readonly CancellationToken _ct = context.RequestAborted;
        private readonly Guid? _exactGuid = Guid.TryParse(query, out var id) ? id : null;
        private readonly List<RankedHit> _hits = [];
        private readonly Dictionary<string, bool> _fundOwnership = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Guid, LedgerBookRecord?> _operationsBooks = [];
        private IReadOnlyList<AccountSummaryDto>? _ownedAccounts;
        private bool _partial;

        private bool IsAdmin => Has(UserPermission.AdminMaintenance);

        public async Task<WorkstationRecordSearchResult> SearchAsync()
        {
            if (Has(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster))
            {
                await CollectAsync("instruments", SearchInstrumentsAsync).ConfigureAwait(false);
            }

            if (Has(UserPermission.ViewTrades))
            {
                await CollectAsync("positions", SearchPositionsAsync).ConfigureAwait(false);
            }

            if (IsAdmin || Has(LedgerPermissions))
            {
                await CollectAsync("journals", SearchJournalsAsync).ConfigureAwait(false);
            }

            if (IsAdmin || Has(UserPermission.ViewReporting, UserPermission.ManageReporting,
                    UserPermission.ApproveReporting, UserPermission.DeliverReporting))
            {
                await CollectAsync("report runs", SearchReportRunsAsync).ConfigureAwait(false);
            }

            if (IsAdmin || Has(OperationsPermissions))
            {
                IReadOnlyDictionary<Guid, AccountSummaryDto> accounts = new Dictionary<Guid, AccountSummaryDto>();
                await CollectAsync("operations scope", async _ =>
                    accounts = await ReadAccountsAsync(OperationsPermissions).ConfigureAwait(false)).ConfigureAwait(false);
                await CollectAsync("reconciliation cases", hits => SearchCasesAsync(accounts, hits)).ConfigureAwait(false);
                if (accounts.Count > 0)
                {
                    await CollectAsync("statement runs", hits => SearchStatementRunsAsync(accounts, hits)).ConfigureAwait(false);
                    await CollectAsync("workflows", hits => SearchWorkflowsAsync(accounts, hits)).ConfigureAwait(false);
                }
            }

            return new WorkstationRecordSearchResult(
                _hits.OrderBy(static hit => hit.Rank)
                    .ThenBy(static hit => hit.Item.Label, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static hit => hit.Item.RecordType, StringComparer.Ordinal)
                    .ThenBy(static hit => hit.Item.RecordId, StringComparer.Ordinal)
                    .ThenBy(static hit => hit.Item.Href, StringComparer.Ordinal)
                    .DistinctBy(static hit => hit.Item.Href, StringComparer.Ordinal)
                    .Take(ResultLimit)
                    .Select(static hit => hit.Item)
                    .ToArray(),
                _partial);
        }

        private async Task CollectAsync(string source, Func<List<RankedHit>, Task> read)
        {
            var hits = new List<RankedHit>();
            try
            {
                _ct.ThrowIfCancellationRequested();
                await read(hits).ConfigureAwait(false);
                _hits.AddRange(hits);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                _partial = true;
                context.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("WorkstationRecordSearch")
                    .LogWarning("Workstation record search source {Source} unavailable ({ErrorType}).", source, ex.GetType().Name);
            }
        }

        private async Task SearchInstrumentsAsync(List<RankedHit> hits)
        {
            // Security Master is the deployment reference catalog. Account holdings and their
            // instrument attachments are read independently under account authority.
            var service = context.RequestServices.GetRequiredService<ISecurityMasterQueryService>();
            var instruments = await service.SearchAsync(
                new SecuritySearchRequest(query, Take: ResultLimit) { PrefixOnly = true }, _ct).ConfigureAwait(false);
            foreach (var item in instruments)
            {
                var id = item.SecurityId.ToString("D");
                Add(hits, MatchGuid(item.SecurityId, item.PrimaryIdentifier, item.DisplayName), "Instrument", id, item.DisplayName,
                    "Security Master catalog", "Current reference", item.Status.ToString(),
                    Route("/accounting/security-master/detail", ("securityId", id)));
            }
        }

        private async Task SearchPositionsAsync(List<RankedHit> hits)
        {
            var accounts = await ReadAccountsAsync([UserPermission.ViewTrades]).ConfigureAwait(false);
            if (accounts.Count == 0)
            {
                return;
            }

            var service = context.RequestServices.GetRequiredService<BrokeragePortfolioSyncService>();
            var portfolio = await service.GetHouseholdAsync(null,
                (candidates, _) => Task.FromResult<IReadOnlySet<Guid>>(candidates.Where(accounts.ContainsKey).ToHashSet()),
                _ct).ConfigureAwait(false);
            var snapshots = portfolio.Accounts.ToDictionary(static account => account.FundAccountId);
            foreach (var position in portfolio.Positions)
            {
                if (!accounts.TryGetValue(position.FundAccountId, out var account) ||
                    !snapshots.TryGetValue(position.FundAccountId, out var snapshot))
                {
                    continue;
                }

                var id = position.PositionId ?? position.Symbol;
                var selection = $"{position.FundAccountId:D}-{position.Symbol}-{position.PositionId ?? "position"}";
                Add(hits, Match(id, position.Symbol), "Position", id, position.Symbol, account.DisplayName,
                    $"Synced {Date(snapshot.SyncedAt)}", snapshot.Health.ToString(),
                    Route("/portfolio/brokerage-sync", ("fundAccountId", position.FundAccountId.ToString("D")),
                        ("brokeragePosition", selection), ("positionProvider", position.ProviderId)));
            }
        }

        private async Task SearchJournalsAsync(List<RankedHit> hits)
        {
            // Journal ids are UUIDs. Non-id search terms never trigger a ledger scan.
            if (_exactGuid is null &&
                (query.Length < 2 || !query.All(static character => char.IsAsciiHexDigit(character) || character == '-')))
            {
                return;
            }

            var store = context.RequestServices.GetRequiredService<ILedgerJournalStore>();
            var drafts = IsAdmin || Has(DraftPermissions)
                ? context.RequestServices.GetService<IManualJournalEntryDraftStore>() : null;
            var books = await store.ListLedgerBooksAsync(ct: _ct).ConfigureAwait(false);
            var ownedBooks = new List<LedgerBookRecord>();
            foreach (var book in books)
            {
                if (await OwnsFundAsync(book.FundProfileId).ConfigureAwait(false))
                {
                    ownedBooks.Add(book);
                }
            }

            foreach (var group in ownedBooks.GroupBy(static book => BookScope(book.FundStructureNodeKind)))
            {
                if (group.Key is not { } kind)
                {
                    continue;
                }

                var nodes = group.Select(static book => book.FundStructureNodeId).Distinct().ToArray();
                var allowed = await AuthorizeAsync(kind, nodes, LedgerPermissions).ConfigureAwait(false);
                var allowedDrafts = drafts is null
                    ? null : await AuthorizeAsync(kind, nodes, DraftPermissions).ConfigureAwait(false);
                foreach (var book in group.Where(book => allowed.Contains(book.FundStructureNodeId)))
                {
                    var entries = await store.QueryAsync(new LedgerJournalEntryQuery(
                        LedgerBookId: book.LedgerBookId, JournalEntryId: _exactGuid)
                    {
                        JournalEntryIdPrefix = _exactGuid is null ? query : null
                    }, _ct).ConfigureAwait(false);

                    var postedIds = new HashSet<Guid>();
                    foreach (var item in entries)
                    {
                        var id = item.Entry.JournalEntryId.ToString("D");
                        postedIds.Add(item.Entry.JournalEntryId);
                        var asOf = item.Entry.Metadata.EffectiveDate is { } effective
                            ? $"Effective {Date(effective)}" : $"Posted {Date(item.Entry.Timestamp)}";
                        Add(hits, MatchGuid(item.Entry.JournalEntryId), "Journal", id, "Posted journal", book.DisplayName,
                            asOf, "Posted",
                            Route("/accounting/journal-entries/detail", ("journalEntryId", id), ("periodId", item.PeriodId.ToString("D"))));
                    }

                    if (drafts is null || allowedDrafts?.Contains(book.FundStructureNodeId) != true)
                    {
                        continue;
                    }

                    var items = _exactGuid is { } exact
                        ? (await drafts.GetAsync(book.FundProfileId, exact, _ct, scope.TenantId, scope.CompanyId).ConfigureAwait(false)
                            is { } match ? [match] : Array.Empty<ManualJournalEntryDraftDto>())
                        : await drafts.ListAsync(book.FundProfileId, book.LedgerBookId, _ct, scope.TenantId, scope.CompanyId)
                            .ConfigureAwait(false);
                    foreach (var draft in items.Where(draft => !postedIds.Contains(draft.JournalEntryId)
                        && draft.LedgerBookId == book.LedgerBookId
                        && SameId(draft.FundProfileId, book.FundProfileId)
                        && SameId(draft.TenantId, scope.TenantId) && SameId(draft.CompanyId, scope.CompanyId)))
                    {
                        var id = draft.JournalEntryId.ToString("D");
                        Add(hits, MatchGuid(draft.JournalEntryId), "Journal", id, "Manual journal", book.DisplayName,
                            $"Accounting date {Date(draft.AccountingDate)}", draft.Status.ToString(),
                            Route("/accounting/journal-entries/detail", ("journalEntryId", id),
                                ("fundProfileId", book.FundProfileId), ("ledgerBookId", book.LedgerBookId.ToString("D"))));
                    }
                }
            }
        }

        private async Task SearchReportRunsAsync(List<RankedHit> hits)
        {
            if (!FundStructureEndpoints.TryResolveReportingGovernanceCaller(context, out var caller, out _))
            {
                return;
            }

            var coordinator = context.RequestServices.GetRequiredService<IReportingGovernanceEndpointCoordinator>();
            var runs = await coordinator.SearchRunsAsync(query, ResultLimit, caller!, _ct).ConfigureAwait(false);
            foreach (var run in runs)
            {
                if (!SameId(run.Scope.TenantId, scope.TenantId) || !SameId(run.Scope.CompanyId, scope.CompanyId))
                {
                    continue;
                }

                var scopeLabel = run.Scope.FundId is { Length: > 0 } fund
                    ? $"Fund {fund} · Book {run.Scope.BookId}"
                    : $"Book {run.Scope.BookId}";
                var asOf = ReportingGovernanceApiProjector.DeserializeParameters(run)?.AsOfDate;
                Add(hits, Match(run.RunId, run.TemplateId), "Report run", run.RunId, run.TemplateId, scopeLabel,
                    asOf is { } date ? $"As of {Date(date)}" : $"Snapshot captured {Date(run.Snapshot.CapturedAtUtc)}",
                    run.GovernanceState.ToString(),
                    Route("/reporting/runs/detail", ("runId", run.RunId)));
            }
        }

        private async Task SearchCasesAsync(IReadOnlyDictionary<Guid, AccountSummaryDto> accounts, List<RankedHit> hits)
        {
            var repository = context.RequestServices.GetRequiredService<IReconciliationBreakQueueRepository>();
            var cases = await repository.GetAllAsync(scope, ct: _ct).ConfigureAwait(false);
            foreach (var item in cases)
            {
                if (!scope.Owns(item) ||
                    (item.FundProfileId is { Length: > 0 } fund && !await OwnsFundAsync(fund).ConfigureAwait(false)))
                {
                    continue;
                }

                var category = item.Category.ToString();
                var rank = Match(item.BreakId, $"case:{item.BreakId}", category);
                if (rank is null)
                {
                    continue;
                }

                AccountSummaryDto? account = null;
                if (!string.IsNullOrWhiteSpace(item.FundAccountId) &&
                    (!Guid.TryParse(item.FundAccountId, out var accountId) || !accounts.TryGetValue(accountId, out account)))
                {
                    continue;
                }

                var book = item.LedgerBookId is { } bookId ? await ReadOperationsBookAsync(bookId).ConfigureAwait(false) : null;
                if ((item.LedgerBookId is not null && book is null) || (account is null && book is null))
                {
                    continue;
                }

                Add(hits, rank, "Reconciliation case", item.BreakId, category, account?.DisplayName ?? book!.DisplayName,
                    item.AsOfDate is { } asOf ? $"As of {Date(asOf)}" : $"Detected {Date(item.DetectedAt)}",
                    item.Status.ToString(),
                    Route("/accounting/reconciliation", ("fundAccountId", account?.AccountId.ToString("D")),
                        ("reconciliationRun", item.RunId), ("reconciliationBreak", item.BreakId)));
            }
        }

        private async Task SearchStatementRunsAsync(IReadOnlyDictionary<Guid, AccountSummaryDto> accounts, List<RankedHit> hits)
        {
            var service = context.RequestServices.GetRequiredService<IStatementRunWorkflowService>();
            var imports = await service.ListImportsAsync(_ct).ConfigureAwait(false);
            var matches = imports.Where(static item => !string.IsNullOrWhiteSpace(item.ImportId))
                .GroupBy(static item => item.ImportId.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(static group => group.Count() == 1)
                .Select(static group => group.Single())
                .Where(item => Guid.TryParse(item.FundAccountId, out var id) && accounts.ContainsKey(id))
                .Select(item => (Item: item, Rank: Match(item.ImportId, item.Broker)))
                .Where(static match => match.Rank is not null)
                .ToArray();
            if (matches.Length == 0)
            {
                return;
            }

            var openRuns = (await service.ListOpenBreaksAsync(_ct).ConfigureAwait(false))
                .Where(static item => SameId(item.ImportId, item.RunId))
                .Select(static item => item.ImportId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var (item, rank) in matches)
            {
                var account = accounts[Guid.Parse(item.FundAccountId)];
                if (item.AccountingScope is { } accounting &&
                    (!SameId(accounting.FundProfileId, account.FundId!.Value.ToString("D")) ||
                     accounting.AsOfDate != item.StatementPeriodEnd ||
                     await ReadOperationsBookAsync(accounting.LedgerBookId).ConfigureAwait(false) is not { } book ||
                     !SameId(book.FundProfileId, accounting.FundProfileId)))
                {
                    continue;
                }

                Add(hits, rank, "Statement run", item.ImportId, item.Broker, account.DisplayName,
                    $"Statement through {Date(item.StatementPeriodEnd)}",
                    openRuns.Contains(item.ImportId) ? "ReviewRequired" : "Completed",
                    Route("/accounting/reconciliation", ("fundAccountId", account.AccountId.ToString("D")),
                        ("reconciliationRun", item.ImportId)) + "#statement-runs");
            }
        }

        private async Task SearchWorkflowsAsync(IReadOnlyDictionary<Guid, AccountSummaryDto> accounts, List<RankedHit> hits)
        {
            var service = context.RequestServices.GetRequiredService<IOperationsContinuityWorkflowService>();
            var workflows = await service.ListAsync(ct: _ct).ConfigureAwait(false);
            foreach (var item in workflows)
            {
                if (!accounts.TryGetValue(item.FundAccountId, out var account))
                {
                    continue;
                }

                var id = item.WorkflowId.ToString("D");
                var rank = MatchGuid(item.WorkflowId, item.PeriodId);
                if (rank is null || (item.LedgerBookId is { } bookId && await ReadOperationsBookAsync(bookId).ConfigureAwait(false) is null))
                {
                    continue;
                }

                Add(hits, rank, "Workflow", id, $"Close workflow {item.PeriodId}", account.DisplayName,
                    $"Updated {Date(item.UpdatedAtUtc)}", item.Status.ToString(),
                    Route("/accounting/operations-continuity", ("fundAccountId", item.FundAccountId.ToString("D")),
                        ("ledgerBookId", item.LedgerBookId?.ToString("D")), ("workflowId", id)));
            }
        }

        private async Task<LedgerBookRecord?> ReadOperationsBookAsync(Guid id)
        {
            if (!_operationsBooks.TryGetValue(id, out var allowed))
            {
                var store = context.RequestServices.GetService<ILedgerJournalStore>();
                var book = store is null ? null : await store.GetLedgerBookAsync(id, _ct).ConfigureAwait(false);
                if (book?.LedgerBookId == id && BookScope(book.FundStructureNodeKind) is { } kind &&
                    await OwnsFundAsync(book.FundProfileId).ConfigureAwait(false) &&
                    (await AuthorizeAsync(kind, [book.FundStructureNodeId], OperationsPermissions).ConfigureAwait(false))
                    .Contains(book.FundStructureNodeId))
                {
                    allowed = book;
                }
                _operationsBooks.Add(id, allowed);
            }

            return allowed;
        }

        private async Task<IReadOnlyDictionary<Guid, AccountSummaryDto>> ReadAccountsAsync(UserPermission[] permissions)
        {
            if (_ownedAccounts is null)
            {
                var queryService = context.RequestServices.GetRequiredService<IAccountQueryService>();
                var accounts = await queryService.ListAccountsAsync(null, null, null, _ct).ConfigureAwait(false);
                var owned = new List<AccountSummaryDto>();
                foreach (var account in accounts)
                {
                    if (account.IsActive && account.AccountId != Guid.Empty && account.FundId is { } fund &&
                        await OwnsFundAsync(fund.ToString("D")).ConfigureAwait(false))
                    {
                        owned.Add(account);
                    }
                }

                _ownedAccounts = owned;
            }

            var allowed = await AuthorizeAsync(AccessScopeKindDto.Account,
                _ownedAccounts.Select(static account => account.AccountId).ToArray(), permissions).ConfigureAwait(false);
            return _ownedAccounts.Where(account => allowed.Contains(account.AccountId))
                .ToDictionary(static account => account.AccountId);
        }

        private async Task<bool> OwnsFundAsync(string fundId)
        {
            if (!_fundOwnership.TryGetValue(fundId, out var owned))
            {
                var registry = context.RequestServices.GetRequiredService<IFundProfileTenancyRegistry>();
                var owner = await registry.ResolveAsync(fundId, _ct).ConfigureAwait(false);
                owned = owner is not null && SameId(owner.FundProfileId, fundId)
                    && owner.IsHeldBy(scope.TenantId) && SameId(owner.CompanyId, scope.CompanyId);
                _fundOwnership.Add(fundId, owned);
            }

            return owned;
        }

        private async Task<IReadOnlySet<Guid>> AuthorizeAsync(
            AccessScopeKindDto kind, IReadOnlyCollection<Guid> ids, UserPermission[] permissions)
        {
            var allowed = new HashSet<Guid>();
            if (IsAdmin)
            {
                allowed.UnionWith(ids);
                return allowed;
            }

            foreach (var permission in permissions.Where(permission => Has(permission)))
            {
                allowed.UnionWith(await EndpointAuthorization.AuthorizeScopedManyAsync(context, permission, kind, ids, _ct)
                    .ConfigureAwait(false));
            }

            return allowed;
        }

        private bool Has(params UserPermission[] permissions) =>
            EndpointAuthorization.HasAnyPermission(context, permissions);

        private int? Match(string id, string? identifier = null, string? label = null)
        {
            if (SameId(id, query)) return 0;
            if (SameId(identifier, query) || SameId(label, query)) return 1;
            if (query.Length < 2) return null;
            if (StartsWith(id) || StartsWith(identifier)) return 2;
            return StartsWith(label) ? 3 : null;
        }

        private int? MatchGuid(Guid id, string? identifier = null, string? label = null) =>
            id == _exactGuid ? 0 : Match(id.ToString("D"), identifier, label);

        private bool StartsWith(string? value) =>
            value?.StartsWith(query, StringComparison.OrdinalIgnoreCase) == true;

        private static bool SameId(string? value, string query) =>
            string.Equals(value?.Trim(), query, StringComparison.OrdinalIgnoreCase);

        private static void Add(List<RankedHit> hits, int? rank, string type, string id, string label,
            string scopeLabel, string asOf, string status, string href)
        {
            if (rank is { } score)
            {
                hits.Add(new RankedHit(score, new WorkstationRecordSearchHit(type, id, label, scopeLabel, asOf, status, href)));
            }
        }

        private static string Route(string path, params (string Key, string? Value)[] query) =>
            QueryHelpers.AddQueryString(QueryHelpers.AddQueryString(path, "recordScope", "1"),
                query.Select(static item => KeyValuePair.Create(item.Key, item.Value)));

        private static string Date(DateTimeOffset value) => Date(DateOnly.FromDateTime(value.UtcDateTime));

        private static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static AccessScopeKindDto? BookScope(FundStructureNodeKindDto kind) => kind switch
        {
            FundStructureNodeKindDto.Organization => AccessScopeKindDto.Organization,
            FundStructureNodeKindDto.Business => AccessScopeKindDto.Business,
            FundStructureNodeKindDto.Client => AccessScopeKindDto.Client,
            FundStructureNodeKindDto.Fund => AccessScopeKindDto.Fund,
            FundStructureNodeKindDto.Sleeve => AccessScopeKindDto.Sleeve,
            FundStructureNodeKindDto.Vehicle => AccessScopeKindDto.Vehicle,
            FundStructureNodeKindDto.InvestmentPortfolio => AccessScopeKindDto.InvestmentPortfolio,
            FundStructureNodeKindDto.Entity => AccessScopeKindDto.LegalEntity,
            FundStructureNodeKindDto.Account => AccessScopeKindDto.Account,
            _ => null
        };

        private sealed record RankedHit(int Rank, WorkstationRecordSearchHit Item);
    }
}
