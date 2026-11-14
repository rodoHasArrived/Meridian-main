using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Services;

/// <summary>
/// Where a set of workstation scope options came from.
/// </summary>
public enum WorkstationScopeOptionsSource : byte
{
    /// <summary>Nothing could be loaded; the lists are empty and nothing is narrowed.</summary>
    Unavailable,

    /// <summary>The shared, grant-filtered <c>GET /api/workstation/scope-options</c> read.</summary>
    ScopeOptionsRoute,

    /// <summary>
    /// The host predates the scope-options route (404), so the desktop read its own local
    /// fund-structure graph instead. That graph is not filtered by the caller's grants.
    /// </summary>
    LocalFundStructureGraph
}

/// <summary>
/// The outcome of one scope-options load: the options (empty when unavailable), where they came
/// from, and an operator-facing status message that is never blank.
/// </summary>
public sealed record WorkstationScopeOptionsLoadResult(
    WorkstationScopeOptionsSource Source,
    WorkstationScopeOptionsDto Options,
    string StatusMessage)
{
    /// <summary>True when the options carry relationships the picker may narrow by.</summary>
    public bool IsAvailable => Source != WorkstationScopeOptionsSource.Unavailable;
}

/// <summary>
/// Loads the entities, funds and fund accounts the operator may scope the workstation to.
/// </summary>
public interface IWorkstationScopeOptionsApiClient
{
    /// <summary>
    /// Reads the shared scope-options seam. Never throws for a failed read (only for
    /// cancellation); a failure comes back as <see cref="WorkstationScopeOptionsSource.Unavailable"/>
    /// with a status message saying why.
    /// </summary>
    Task<WorkstationScopeOptionsLoadResult> GetScopeOptionsAsync(CancellationToken ct = default);
}

/// <summary>
/// Desktop consumer of <see cref="UiApiRoutes.WorkstationScopeOptions"/>, mirroring the browser
/// workstation's scope picker: the shared, grant-filtered read first; on a 404 (a host older than
/// that route) the fund-structure graph this desktop already reads in-process; on any other
/// failure, a 401/403 included, nothing — the graph is not tried, because the caller could not
/// read it on the host either.
/// </summary>
public sealed class WorkstationScopeOptionsApiClient : IWorkstationScopeOptionsApiClient
{
    private static readonly WorkstationScopeOptionsDto EmptyOptions = new(
        Array.Empty<WorkstationScopeEntityOptionDto>(),
        Array.Empty<WorkstationScopeFundOptionDto>(),
        Array.Empty<WorkstationScopeAccountOptionDto>());

    private readonly ApiClientService _apiClient;
    private readonly IFundStructureService? _fundStructureService;

    public WorkstationScopeOptionsApiClient(
        ApiClientService apiClient,
        IFundStructureService? fundStructureService = null)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _fundStructureService = fundStructureService;
    }

    public async Task<WorkstationScopeOptionsLoadResult> GetScopeOptionsAsync(CancellationToken ct = default)
    {
        var response = await _apiClient
            .GetWithResponseAsync<WorkstationScopeOptionsDto>(UiApiRoutes.WorkstationScopeOptions, ct)
            .ConfigureAwait(false);

        if (response.Success && response.Data is not null)
        {
            var options = Normalize(response.Data);
            return new WorkstationScopeOptionsLoadResult(
                WorkstationScopeOptionsSource.ScopeOptionsRoute,
                options,
                DescribeLoaded(options, "Scope options loaded"));
        }

        if (response.StatusCode == 404)
        {
            return await LoadFallbackGraphAsync(ct).ConfigureAwait(false);
        }

        LoggingService.Instance.LogWarning(
            "Workstation scope options could not be loaded.",
            ("route", UiApiRoutes.WorkstationScopeOptions),
            ("statusCode", response.StatusCode.ToString(CultureInfo.InvariantCulture)),
            ("connectionError", response.IsConnectionError ? "true" : "false"));

        var reason = response.IsConnectionError
            ? "the workstation host could not be reached"
            : response.StatusCode is 401 or 403
                ? $"the host refused the read (HTTP {response.StatusCode})"
                : response.StatusCode == 0
                    ? "the response could not be read"
                    : $"the host answered HTTP {response.StatusCode}";
        return Unavailable($"Entity, fund and account lists could not be loaded because {reason}. Accounts are not narrowed by entity or fund.");
    }

    private async Task<WorkstationScopeOptionsLoadResult> LoadFallbackGraphAsync(CancellationToken ct)
    {
        if (_fundStructureService is null)
        {
            LoggingService.Instance.LogInfo(
                "Workstation host has no scope-options route and no local fund-structure graph is registered.",
                ("route", UiApiRoutes.WorkstationScopeOptions));
            return Unavailable("This host does not serve scope options and no fund-structure graph is available on this desktop, so entity, fund and account lists are unavailable.");
        }

        try
        {
            var graph = await _fundStructureService
                .GetOrganizationStructureAsync(new OrganizationStructureQuery(ActiveOnly: true), ct)
                .ConfigureAwait(false);
            var options = FromGraph(graph);

            LoggingService.Instance.LogInfo(
                "Workstation scope options fell back to the local fund-structure graph.",
                ("route", UiApiRoutes.WorkstationScopeOptions),
                ("entities", options.Entities.Count.ToString(CultureInfo.InvariantCulture)),
                ("funds", options.Funds.Count.ToString(CultureInfo.InvariantCulture)),
                ("accounts", options.Accounts.Count.ToString(CultureInfo.InvariantCulture)));

            return new WorkstationScopeOptionsLoadResult(
                WorkstationScopeOptionsSource.LocalFundStructureGraph,
                options,
                DescribeLoaded(options, "This host does not serve scope options yet; showing this desktop's local fund-structure graph, which is not filtered by your grants"));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogWarning(
                "Local fund-structure graph fallback for workstation scope options failed.",
                ("exception", ex.GetType().Name),
                ("message", ex.Message));
            return Unavailable("This host does not serve scope options and the local fund-structure graph could not be read, so entity, fund and account lists are unavailable.");
        }
    }

    /// <summary>
    /// Projects the organization graph onto the scope-options shape, using the same field mapping
    /// the host uses (and the browser's shared parser relies on). Labels fall back to the id.
    /// </summary>
    internal static WorkstationScopeOptionsDto FromGraph(OrganizationStructureGraphDto graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var entities = graph.Entities
            .Where(static entity => entity.EntityId != Guid.Empty)
            .GroupBy(static entity => entity.EntityId)
            .Select(static group => group.First())
            .Select(static entity => new WorkstationScopeEntityOptionDto(entity.EntityId, Label(entity.Name, entity.EntityId)))
            .OrderBy(static entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entity => entity.EntityId)
            .ToArray();

        var funds = graph.Funds
            .Where(static fund => fund.FundId != Guid.Empty)
            .GroupBy(static fund => fund.FundId)
            .Select(static group => group.First())
            .Select(static fund => new WorkstationScopeFundOptionDto(
                fund.FundId,
                Label(fund.Name, fund.FundId),
                (fund.EntityIds ?? Array.Empty<Guid>()).Where(static id => id != Guid.Empty).Distinct().OrderBy(static id => id).ToArray()))
            .OrderBy(static fund => fund.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static fund => fund.FundId)
            .ToArray();

        var accounts = graph.Accounts
            .Where(static account => account.AccountId != Guid.Empty)
            .GroupBy(static account => account.AccountId)
            .Select(static group => group.First())
            .Select(static account => new WorkstationScopeAccountOptionDto(
                account.AccountId,
                Label(account.DisplayName, account.AccountId),
                account.FundId,
                account.EntityId))
            .OrderBy(static account => account.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static account => account.AccountId)
            .ToArray();

        return new WorkstationScopeOptionsDto(entities, funds, accounts);
    }

    /// <summary>Guards against a payload whose lists arrived as null: an empty list means "none visible".</summary>
    private static WorkstationScopeOptionsDto Normalize(WorkstationScopeOptionsDto options)
        => new(
            options.Entities ?? Array.Empty<WorkstationScopeEntityOptionDto>(),
            (options.Funds ?? Array.Empty<WorkstationScopeFundOptionDto>())
                .Select(static fund => fund.EntityIds is null ? fund with { EntityIds = Array.Empty<Guid>() } : fund)
                .ToArray(),
            options.Accounts ?? Array.Empty<WorkstationScopeAccountOptionDto>());

    private static string DescribeLoaded(WorkstationScopeOptionsDto options, string prefix)
    {
        if (options.Entities.Count == 0 && options.Funds.Count == 0 && options.Accounts.Count == 0)
        {
            return $"{prefix}: no entities, funds or accounts are visible to you.";
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}: {1} entit{2}, {3} fund{4}, {5} account{6}.",
            prefix,
            options.Entities.Count,
            options.Entities.Count == 1 ? "y" : "ies",
            options.Funds.Count,
            options.Funds.Count == 1 ? string.Empty : "s",
            options.Accounts.Count,
            options.Accounts.Count == 1 ? string.Empty : "s");
    }

    private static WorkstationScopeOptionsLoadResult Unavailable(string message)
        => new(WorkstationScopeOptionsSource.Unavailable, EmptyOptions, message);

    private static string Label(string? name, Guid id)
        => string.IsNullOrWhiteSpace(name) ? id.ToString("D") : name.Trim();
}
