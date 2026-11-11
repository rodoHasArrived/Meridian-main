using System.Globalization;
using System.Text.Json;
using Meridian.Application.Scheduling;
using Meridian.Application.Backfill;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Storage.SecurityMaster;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private sealed record DataSourceExportRow(string Id, IReadOnlyDictionary<string, object?> Cells, string Search,
        string EvidenceScope, string? TenantId = null, string? CompanyId = null);

    private static string? ValidateDataSourceExport(WorkstationTableExportRequest request, IReadOnlyDictionary<string, string> columns, params string[] filters)
    {
        if (request.ColumnIds is null || request.ColumnIds.Count is < 1 or > 30
            || request.ColumnIds.Distinct(StringComparer.Ordinal).Count() != request.ColumnIds.Count
            || request.ColumnIds.Any(column => !columns.ContainsKey(column)) || request.SearchText?.Length > 1000
            || request.SortDirection is not (null or "asc" or "desc")
            || request.Filters?.Any(filter => !filters.Contains(filter.Key, StringComparer.Ordinal) || (filter.Value is null || filter.Value.Length > 1000)) == true)
            return "Unknown columns or invalid source export filters.";
        return null;
    }

    private static WorkstationTableExportDto BuildDataSourceExport(string tableId, IReadOnlyDictionary<string, string> columns,
        IReadOnlyList<string> selectedColumns, IEnumerable<DataSourceExportRow> rows, string source, DateTimeOffset asOf)
    {
        var materialized = rows.ToArray();
        var metadata = new[] { "Context · Record ID", "Context · Evidence scope", "Context · Tenant", "Context · Company", "Context · Source", "Context · Currency", "Context · Exported at UTC" };
        var lines = new List<string> { string.Join(',', selectedColumns.Select(column => CsvCell(columns[column])).Concat(metadata.Select(CsvCell))) };
        foreach (var row in materialized)
            lines.Add(string.Join(',', selectedColumns.Select(column => CsvCell(row.Cells.GetValueOrDefault(column))).Concat(
                new[] { row.Id, row.EvidenceScope, row.TenantId, row.CompanyId, source, Convert.ToString(row.Cells.GetValueOrDefault("source:currency"), CultureInfo.InvariantCulture), asOf.ToString("O", CultureInfo.InvariantCulture) }.Select(CsvCell))));
        return new($"{tableId}-{asOf:yyyyMMddTHHmmssZ}.csv", string.Join("\r\n", lines), materialized.Length, asOf);
    }

    private static async Task<IResult> ExportDataWorkspaceSnapshotAsync(string tableId, WorkstationTableExportRequest request, HttpContext context, JsonSerializerOptions jsonOptions)
    {
        if (!EndpointAuthorization.HasAnyPermission(context, UserPermission.ViewHistoricalData, UserPermission.ViewDiagnostics, UserPermission.ManageStorage))
            return EndpointHelpers.Forbidden();
        var columns = tableId == "data-backfill-queue"
            ? new Dictionary<string, string> { ["job"] = "Job", ["provider"] = "Provider", ["scope"] = "Scope", ["status"] = "Status", ["progress"] = "Progress", ["updated"] = "Updated" }
            : new Dictionary<string, string> { ["profile"] = "Profile", ["target"] = "Target", ["status"] = "Status", ["rows"] = "Rows", ["updated"] = "Updated" };
        var invalid = ValidateDataSourceExport(request, columns);
        if (invalid is not null || request.SortColumnId is not null)
            return Results.BadRequest(new { error = invalid ?? "This source does not declare a sort column." });
        var source = await BuildDataPayloadAsync(context).ConfigureAwait(false);
        if (source is null)
            return Results.Problem("The authoritative Data workspace snapshot is unavailable.", statusCode: 503);
        var rows = tableId == "data-backfill-queue"
            ? source.Backfills.Select(row => new DataSourceExportRow(row.JobId, new Dictionary<string, object?>
            { ["job"] = row.JobId, ["provider"] = row.Provider, ["scope"] = row.Scope, ["status"] = row.Status, ["progress"] = row.Progress, ["updated"] = row.UpdatedAt },
                $"{row.JobId} {row.Provider} {row.Scope} {row.Status}", "authorized current workspace snapshot"))
            : source.Exports.Select(row => new DataSourceExportRow(row.ExportId, new Dictionary<string, object?>
            { ["profile"] = row.Profile, ["target"] = row.Target, ["status"] = row.Status, ["rows"] = row.Rows, ["updated"] = row.UpdatedAt },
                $"{row.ExportId} {row.Profile} {row.Target} {row.Status}", "authorized current workspace snapshot"));
        var query = request.SearchText?.Trim() ?? string.Empty;
        return Results.Json(BuildDataSourceExport(tableId, columns, request.ColumnIds,
            rows.Where(row => query.Length == 0 || row.Search.Contains(query, StringComparison.OrdinalIgnoreCase)),
            "Complete current Data workspace projection; not the historical execution or package catalog", DateTimeOffset.UtcNow), jsonOptions);
    }

    internal static IResult ExportRetainedBackfillRemediation(WorkstationTableExportRequest request, HttpContext context, JsonSerializerOptions jsonOptions)
    {
        if (!EndpointAuthorization.HasAnyPermission(context, UserPermission.ViewHistoricalData, UserPermission.TriggerBackfill))
            return EndpointHelpers.Forbidden();
        var columns = new Dictionary<string, string> { ["execution"] = "Execution", ["provider"] = "Provider", ["tier"] = "SLA tier", ["status"] = "Status", ["deadline"] = "Deadline UTC", ["ownership"] = "Workflow / owner", ["outcome"] = "Outcome / evidence" };
        var invalid = ValidateDataSourceExport(request, columns);
        if (invalid is not null || request.SortColumnId is not (null or "tier" or "deadline"))
            return Results.BadRequest(new { error = invalid ?? "Unknown remediation sort column." });
        var history = context.RequestServices.GetService<BackfillExecutionHistory>();
        if (history is null)
            return Results.Problem("Retained backfill execution history is unavailable.", statusCode: 503);
        var asOf = DateTimeOffset.UtcNow;
        var source = BackfillExecutionContractProjection.Build(history.GetRecentExecutions(int.MaxValue),
            context.RequestServices.GetService<IOptions<AutoGapRemediationPolicy>>()?.Value.DefaultProvider, asOf);
        var rows = source.Executions.Where(row => row.AutoRemediationSla is not null).ToArray();
        var search = request.SearchText?.Trim() ?? string.Empty;
        var selected = rows.Where(row => search.Length == 0 || $"{row.Id} {string.Join(", ", row.Symbols)} {row.AutoRemediationSla!.Provider} {(row.AutoRemediationSla.Status == BackfillRemediationSlaStatusDto.DueSoon ? "Due Soon" : row.AutoRemediationSla.Status.ToString())} {row.AutoRemediationSla.DownstreamWorkflow}".Contains(search, StringComparison.OrdinalIgnoreCase));
        var sorted = request.SortColumnId == "deadline"
            ? request.SortDirection == "desc" ? selected.OrderByDescending(row => row.AutoRemediationSla!.DueAtUtc) : selected.OrderBy(row => row.AutoRemediationSla!.DueAtUtc)
            : request.SortDirection == "desc" ? selected.OrderByDescending(row => row.AutoRemediationSla!.Tier == BackfillRemediationSlaTierDto.SameBusinessDay ? 0 : 1).ThenByDescending(row => row.AutoRemediationSla!.DueAtUtc)
            : selected.OrderBy(row => row.AutoRemediationSla!.Tier == BackfillRemediationSlaTierDto.SameBusinessDay ? 0 : 1).ThenBy(row => row.AutoRemediationSla!.DueAtUtc);
        var exportRows = sorted.ThenBy(row => row.Id, StringComparer.Ordinal).Select(row => new DataSourceExportRow(row.Id, new Dictionary<string, object?>
        {
            ["execution"] = row.Id,
            ["provider"] = row.AutoRemediationSla!.Provider,
            ["tier"] = row.AutoRemediationSla.Tier.ToString(),
            ["status"] = row.AutoRemediationSla.Status.ToString(),
            ["deadline"] = row.AutoRemediationSla.DueAtUtc.ToString("O", CultureInfo.InvariantCulture),
            ["ownership"] = $"{row.AutoRemediationSla.DownstreamWorkflow}; {(row.AutoRemediationSla.RequiresOwnerAssignment ? "Assignment required" : "Not required")}",
            ["outcome"] = $"{row.AutoRemediationLastOutcome ?? row.Status}; {row.AutoRemediationSla.ReasonCode}; {row.AutoRemediationSla.TriggerSource}"
        }, string.Empty, "global host backfill history"));
        return Results.Json(BuildDataSourceExport("data-remediation-queue", columns, request.ColumnIds, exportRows,
            "Global host backfill history with shared typed SLA projection; all retained executions before filtering; retention applies", asOf), jsonOptions);
    }

    internal static async Task<IResult> ExportCorporateActionInboxAsync(WorkstationTableExportRequest request, HttpContext context, JsonSerializerOptions jsonOptions)
    {
        if (!EndpointAuthorization.HasPermission(context, UserPermission.ViewCorporateActions))
            return EndpointHelpers.Forbidden();
        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        if (string.IsNullOrWhiteSpace(tenant.TenantId) || string.IsNullOrWhiteSpace(tenant.CompanyId))
            return EndpointHelpers.Forbidden();
        var columns = new Dictionary<string, string> { ["event"] = "Event", ["deadline"] = "Ex-date", ["case"] = "Case / status", ["assignment"] = "Assignment", ["control"] = "Conflict / version", ["value"] = "Terms (source denomination)" };
        var invalid = ValidateDataSourceExport(request, columns, "search", "status", "assignment", "conflict");
        if (invalid is not null || request.SortColumnId is not null)
            return Results.BadRequest(new { error = invalid ?? "This source does not declare a sort column." });
        var store = context.RequestServices.GetService<ICorporateActionOperationsStore>();
        if (store is null)
            return Results.Problem("Complete corporate-action export is unavailable.", statusCode: 503);
        try
        {
            var source = await store.ReadInboxExportSourceAsync(tenant.TenantId, tenant.CompanyId, context.RequestAborted).ConfigureAwait(false);
            if (source.Cases.Any(row => row.Scope.TenantId != tenant.TenantId || row.Scope.CompanyId != tenant.CompanyId)
                || source.Cases.GroupBy(row => row.ProposalId).Any(group => group.Count() != 1)
                || source.Proposals.GroupBy(row => row.ProposalId).Any(group => group.Count() != 1))
                return Results.Problem("Corporate-action case ownership could not be established.", statusCode: 503);
            var rows = BuildCorporateActionExportRows(source, tenant.TenantId, tenant.CompanyId);
            var query = request.SearchText?.Trim() ?? string.Empty;
            var sourceQuery = request.Filters?.GetValueOrDefault("search")?.Trim() ?? string.Empty;
            var selected = rows.Where(row => (query.Length == 0 || row.Search.Contains(query, StringComparison.OrdinalIgnoreCase))
                && (sourceQuery.Length == 0 || new[] { "source:ticker", "source:securityId", "source:actionType", "source:caseId", "source:proposalId", "source:winningSource" }.Any(key => Convert.ToString(row.Cells.GetValueOrDefault(key), CultureInfo.InvariantCulture)?.Contains(sourceQuery, StringComparison.OrdinalIgnoreCase) == true))
                && new[] { "status", "assignment", "conflict" }.All(key => request.Filters?.GetValueOrDefault(key) is not { } filter || filter == "All" || Equals(row.Cells.GetValueOrDefault($"filter:{key}"), filter)));
            return Results.Json(BuildDataSourceExport("data-corporate-action-queue", columns, request.ColumnIds, selected,
                "Repeatable-read global provider-economic reference observations plus exact tenant/company cases; namespaces declared per row", DateTimeOffset.UtcNow), jsonOptions);
        }
        catch (Exception exception) when (exception is NotSupportedException or IOException or CorporateActionOperationException)
        { return Results.Problem("The complete authorized corporate-action source is unavailable. No partial export was produced.", statusCode: 503); }
    }

    private static IReadOnlyList<DataSourceExportRow> BuildCorporateActionExportRows(CorporateActionInboxExportSource source, string tenantId, string companyId)
    {
        var cases = source.Cases.ToDictionary(row => row.ProposalId);
        var staged = source.Proposals.Select(row => row.ProposalId).ToHashSet();
        var rows = new List<DataSourceExportRow>();
        foreach (var proposal in source.Proposals)
        {
            cases.TryGetValue(proposal.ProposalId, out var processingCase);
            var action = proposal.ProposedAction;
            Add(proposal.ProposalId, processingCase, action, proposal.DisplayMetadata?.Ticker ?? action.SecurityId.ToString(),
                processingCase?.State ?? proposal.State, proposal.DisplayMetadata?.DissentingSources.Count > 0 ? "Source dissent" : "Not supplied",
                proposal.Version, processingCase is null ? "global source reference" : "global source reference + tenant/company case", proposal.DisplayMetadata?.WinningSource ?? proposal.ProviderIdentity.ProviderId);
        }
        foreach (var processingCase in source.Cases.Where(row => !staged.Contains(row.ProposalId)))
            Add(processingCase.ProposalId, processingCase, processingCase.SourceSnapshot?.ProposedAction,
                processingCase.SourceSnapshot?.DisplayMetadata?.Ticker ?? (processingCase.SourceSnapshot is null ? "Not supplied" : processingCase.SecurityId.ToString()), processingCase.State,
                "Not supplied", processingCase.Version, "tenant/company case", processingCase.SourceSnapshot?.DisplayMetadata?.WinningSource ?? processingCase.SourceSnapshot?.ProviderIdentity.ProviderId ?? "Not supplied");
        return rows.OrderBy(row => row.Cells["deadline"] is null ? 1 : 0)
            .ThenBy(row => Convert.ToString(row.Cells["deadline"], CultureInfo.InvariantCulture), StringComparer.Ordinal)
            .ThenBy(row => Convert.ToString(row.Cells["source:ticker"], CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => Convert.ToString(row.Cells["source:caseId"], CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Id, StringComparer.Ordinal).ToArray();

        void Add(Guid proposalId, CorporateActionProcessingCaseDto? processingCase, CorporateActionDto? action, string ticker,
            string status, string conflict, long version, string evidenceScope, string winningSource)
        {
            var assignment = processingCase?.AssignedTo?.Trim() is { Length: > 0 } owner ? owner : "Unassigned";
            object? terms = action?.SplitRatio is { } ratio ? $"{ratio.ToString(CultureInfo.InvariantCulture)}:1 split" : action?.DividendPerShare is { } amount ? amount : null;
            var actionType = action?.EventType ?? "Not supplied";
            rows.Add(new(proposalId.ToString(), new Dictionary<string, object?>
            {
                ["event"] = $"{actionType} {ticker}",
                ["deadline"] = action?.ExDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["case"] = $"{processingCase?.CaseId.ToString() ?? "Not supplied"}; {status}",
                ["assignment"] = assignment,
                ["control"] = $"{conflict}; {version}",
                ["value"] = terms,
                ["filter:status"] = status,
                ["filter:assignment"] = assignment,
                ["filter:conflict"] = conflict,
                ["source:securityId"] = action?.SecurityId.ToString() ?? processingCase?.SecurityId.ToString(),
                ["source:proposalId"] = proposalId.ToString(),
                ["source:currency"] = action?.Currency,
                ["source:ticker"] = ticker,
                ["source:actionType"] = actionType,
                ["source:caseId"] = processingCase?.CaseId.ToString() ?? "Not supplied",
                ["source:winningSource"] = winningSource
            }, $"{ticker} {actionType} {status} {assignment}", evidenceScope, processingCase is null ? null : tenantId, processingCase is null ? null : companyId));
        }
    }
}
