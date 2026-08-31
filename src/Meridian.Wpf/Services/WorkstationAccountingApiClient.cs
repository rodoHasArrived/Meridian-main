using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;

namespace Meridian.Wpf.Services;

/// <summary>
/// Marker seam so desktop consumers can depend on the HTTP-backed accounting configuration
/// client specifically, while the in-process <see cref="IAccountingConfigurationService"/>
/// registration continues to serve background workers until they migrate too.
/// </summary>
public interface IWorkstationAccountingApiClient : IAccountingConfigurationService;

/// <summary>
/// HTTP-backed <see cref="IAccountingConfigurationService"/> for the desktop workstation
/// (audit finding P8): operator accounting-configuration reads and mutations go through the
/// server's governed ledger endpoints, so the server remains the single writer for the
/// configuration store and stamps the authenticated session actor and tenant scope over
/// whatever actor the desktop composed locally.
/// Failures surface as <see cref="InvalidOperationException"/> carrying the server's error
/// message; the accounting ViewModels already route service exceptions into operator-visible
/// status text.
/// </summary>
public sealed class WorkstationAccountingApiClient : IWorkstationAccountingApiClient
{
    private readonly Meridian.Ui.Services.ApiClientService _apiClient;

    public WorkstationAccountingApiClient(Meridian.Ui.Services.ApiClientService apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    // tenantId/companyId parameters are intentionally not forwarded on reads: the server
    // resolves tenant scope from the authenticated session, not from client-supplied values.
    public async Task<AccountingConfigurationWorkspaceDto> GetWorkspaceAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        var response = await _apiClient
            .GetWithResponseAsync<AccountingConfigurationWorkspaceDto>(
                UiApiRoutes.LedgerAccountingConfiguration + BuildScopeQuery(fundProfileId, ledgerBookId),
                ct)
            .ConfigureAwait(false);
        return Unwrap(response, "load the accounting configuration workspace");
    }

    public async Task<AccountingConfigurationWorkspaceDto> UpsertChartNodeAsync(
        UpsertChartOfAccountsNodeRequest request,
        CancellationToken ct = default)
    {
        var response = await _apiClient
            .PostWithResponseAsync<AccountingConfigurationWorkspaceDto>(
                UiApiRoutes.LedgerAccountingConfigurationChart, request, ct)
            .ConfigureAwait(false);
        return Unwrap(response, "save the chart of accounts node");
    }

    public async Task<AccountingConfigurationWorkspaceDto> UpsertTemplateAsync(
        UpsertJournalEntryTemplateRequest request,
        CancellationToken ct = default)
    {
        var response = await _apiClient
            .PostWithResponseAsync<AccountingConfigurationWorkspaceDto>(
                UiApiRoutes.LedgerAccountingConfigurationTemplates, request, ct)
            .ConfigureAwait(false);
        return Unwrap(response, "save the journal entry template");
    }

    public async Task<AccountingConfigurationWorkspaceDto> UpsertPostingRuleAsync(
        UpsertPostingRuleRequest request,
        CancellationToken ct = default)
    {
        var response = await _apiClient
            .PostWithResponseAsync<AccountingConfigurationWorkspaceDto>(
                UiApiRoutes.LedgerAccountingConfigurationPostingRules, request, ct)
            .ConfigureAwait(false);
        return Unwrap(response, "save the posting rule");
    }

    public async Task<AccountingConfigurationWorkspaceDto> ApprovePostingRulePromotionAsync(
        ApprovePostingRulePromotionRequest request,
        CancellationToken ct = default)
    {
        var response = await _apiClient
            .PostWithResponseAsync<AccountingConfigurationWorkspaceDto>(
                UiApiRoutes.LedgerAccountingConfigurationPostingRulePromotionApprovals, request, ct)
            .ConfigureAwait(false);
        return Unwrap(response, "approve the posting-rule promotion");
    }

    public async Task<AccountingConfigurationWorkspaceDto> UpsertRuleTestCaseAsync(
        UpsertAccountingRuleTestCaseRequest request,
        CancellationToken ct = default)
    {
        var response = await _apiClient
            .PostWithResponseAsync<AccountingConfigurationWorkspaceDto>(
                UiApiRoutes.LedgerAccountingConfigurationPostingRuleTestCases, request, ct)
            .ConfigureAwait(false);
        return Unwrap(response, "save the posting-rule test case");
    }

    public async Task<AccountingJournalTemplatePreviewDto> PreviewTemplateAsync(
        PreviewJournalTemplateRequest request,
        CancellationToken ct = default)
    {
        var response = await _apiClient
            .PostWithResponseAsync<AccountingJournalTemplatePreviewDto>(
                UiApiRoutes.LedgerAccountingConfigurationPreview, request, ct)
            .ConfigureAwait(false);
        return Unwrap(response, "preview the journal template");
    }

    public async Task<RuleDryRunResultDto> DryRunPostingRuleAsync(
        RuleDryRunRequestDto request,
        CancellationToken ct = default)
    {
        var response = await _apiClient
            .PostWithResponseAsync<RuleDryRunResultDto>(
                UiApiRoutes.LedgerAccountingConfigurationPostingRuleDryRun, request, ct)
            .ConfigureAwait(false);
        return Unwrap(response, "dry-run the posting rule");
    }

    public async Task<AccountingRuleTestSuiteResultDto> ExecuteRuleTestCasesAsync(
        ExecuteAccountingRuleTestCasesRequestDto request,
        CancellationToken ct = default)
    {
        var response = await _apiClient
            .PostWithResponseAsync<AccountingRuleTestSuiteResultDto>(
                UiApiRoutes.LedgerAccountingConfigurationPostingRuleTests, request, ct)
            .ConfigureAwait(false);
        return Unwrap(response, "execute the posting-rule test cases");
    }

    public async Task<AccountingConfigurationWorkspaceDto> ActivateAsync(
        ActivateAccountingConfigurationRequest request,
        CancellationToken ct = default)
    {
        var response = await _apiClient
            .PostWithResponseAsync<AccountingConfigurationWorkspaceDto>(
                UiApiRoutes.LedgerAccountingConfigurationActivate, request, ct)
            .ConfigureAwait(false);
        return Unwrap(response, "activate the accounting configuration");
    }

    public async Task<IReadOnlyList<AccountingActionAuditEventDto>> ListAuditAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        var response = await _apiClient
            .GetWithResponseAsync<List<AccountingActionAuditEventDto>>(
                UiApiRoutes.LedgerAccountingConfigurationAudit + BuildScopeQuery(fundProfileId, ledgerBookId),
                ct)
            .ConfigureAwait(false);
        return Unwrap(response, "load the accounting configuration audit trail");
    }

    private static string BuildScopeQuery(string? fundProfileId, Guid? ledgerBookId)
    {
        var parameters = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(fundProfileId))
        {
            parameters.Add($"fundProfileId={Uri.EscapeDataString(fundProfileId)}");
        }

        if (ledgerBookId.HasValue)
        {
            parameters.Add($"ledgerBookId={ledgerBookId.Value}");
        }

        return parameters.Count == 0 ? string.Empty : "?" + string.Join("&", parameters);
    }

    private static T Unwrap<T>(ApiResponse<T> response, string operation) where T : class
    {
        if (response.Success && response.Data is not null)
        {
            return response.Data;
        }

        var reason = !string.IsNullOrWhiteSpace(response.ErrorMessage)
            ? response.ErrorMessage
            : response.IsConnectionError
                ? "the workstation service is unreachable"
                : $"HTTP {response.StatusCode}";
        throw new InvalidOperationException($"Could not {operation}: {reason}");
    }
}
