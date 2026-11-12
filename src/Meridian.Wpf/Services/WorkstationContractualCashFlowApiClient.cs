using Meridian.Contracts.Api;
using Meridian.Contracts.CashFlows;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Services;

/// <summary>
/// Contractual cash-flow scenario preview and retained runs for securities and loans over the shared
/// routes. Previews are hypothetical and never post; retained runs are complete replay packets.
/// </summary>
public interface IWorkstationContractualCashFlowApiClient
{
    Task<ApiResponse<ContractualCashFlowResult>> PreviewAsync(string subjectKind, Guid subjectId, ContractualCashFlowRequest request, CancellationToken ct = default);
    /// <summary>A blocked projection comes back with <c>RunId == Guid.Empty</c> and no result; the caller previews again.</summary>
    Task<ApiResponse<ContractualCashFlowRun>> RetainRunAsync(string subjectKind, Guid subjectId, CreateContractualCashFlowRunRequest request, CancellationToken ct = default);
    Task<ApiResponse<ContractualCashFlowRun>> GetRunAsync(string subjectKind, Guid subjectId, Guid runId, CancellationToken ct = default);
    /// <summary>Pure read over two retained runs of the same subject; nothing is recalculated against current terms.</summary>
    Task<ApiResponse<ContractualCashFlowRunComparison>> CompareRunsAsync(string subjectKind, Guid subjectId, Guid runId, Guid otherRunId, CancellationToken ct = default);
}

public sealed class WorkstationContractualCashFlowApiClient : IWorkstationContractualCashFlowApiClient
{
    private readonly ApiClientService _apiClient;

    public WorkstationContractualCashFlowApiClient(ApiClientService apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    public Task<ApiResponse<ContractualCashFlowResult>> PreviewAsync(string subjectKind, Guid subjectId, ContractualCashFlowRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var template = Kind(subjectKind) == ContractualCashFlowSubjectKinds.Security ? UiApiRoutes.SecurityMasterCashFlowProjectionPreview : UiApiRoutes.DirectLendingProjectionPreview;
        return _apiClient.PostWithResponseAsync<ContractualCashFlowResult>(Fill(template, subjectId), request, ct);
    }

    public Task<ApiResponse<ContractualCashFlowRun>> RetainRunAsync(string subjectKind, Guid subjectId, CreateContractualCashFlowRunRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var template = Kind(subjectKind) == ContractualCashFlowSubjectKinds.Security ? UiApiRoutes.SecurityMasterCashFlowProjectionRuns : UiApiRoutes.DirectLendingContractualRuns;
        return _apiClient.PostWithResponseAsync<ContractualCashFlowRun>(Fill(template, subjectId), request, ct);
    }

    public Task<ApiResponse<ContractualCashFlowRun>> GetRunAsync(string subjectKind, Guid subjectId, Guid runId, CancellationToken ct = default)
    {
        var template = Kind(subjectKind) == ContractualCashFlowSubjectKinds.Security ? UiApiRoutes.SecurityMasterCashFlowProjectionRun : UiApiRoutes.DirectLendingContractualRun;
        return _apiClient.GetWithResponseAsync<ContractualCashFlowRun>(Fill(template, subjectId, runId), ct);
    }

    public Task<ApiResponse<ContractualCashFlowRunComparison>> CompareRunsAsync(string subjectKind, Guid subjectId, Guid runId, Guid otherRunId, CancellationToken ct = default)
    {
        var template = Kind(subjectKind) == ContractualCashFlowSubjectKinds.Security ? UiApiRoutes.SecurityMasterCashFlowProjectionRunCompare : UiApiRoutes.DirectLendingContractualRunCompare;
        return _apiClient.GetWithResponseAsync<ContractualCashFlowRunComparison>(Fill(template, subjectId, runId).Replace("{otherRunId:guid}", otherRunId.ToString("D"), StringComparison.Ordinal), ct);
    }

    private static string Kind(string subjectKind)
        => ContractualCashFlowSubjectKinds.IsSupported(subjectKind) ? subjectKind : throw new ArgumentException("Unsupported contractual cash-flow subject kind.", nameof(subjectKind));

    private static string Fill(string template, Guid subjectId, Guid? runId = null)
        => template.Replace("{securityId:guid}", subjectId.ToString("D"), StringComparison.Ordinal)
            .Replace("{loanId:guid}", subjectId.ToString("D"), StringComparison.Ordinal)
            .Replace("{subjectId:guid}", subjectId.ToString("D"), StringComparison.Ordinal)
            .Replace("{runId:guid}", (runId ?? Guid.Empty).ToString("D"), StringComparison.Ordinal);
}
