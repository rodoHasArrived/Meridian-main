using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Services;

public interface IFactorPaydownApiClient
{
    Task<FactorPaydownPreparationDto> PrepareAsync(PrepareFactorPaydownRequestDto request, CancellationToken ct);
    Task<AssetAccountingPostingCandidateDto> CreateDraftAsync(CreateFactorPaydownDraftRequestDto request, CancellationToken ct);
    Task<ManualJournalEntryWorkbenchDto> GetRetainedDraftsAsync(string fundProfileId, Guid ledgerBookId, CancellationToken ct);
    Task<ManualJournalEntryDraftDto> SubmitForReviewAsync(SubmitManualJournalEntryApprovalRequest request, CancellationToken ct);
    Task<AssetAccountingDraftCapabilitiesDto?> GetCapabilitiesAsync(string fundProfileId, Guid ledgerBookId, Guid journalEntryId, CancellationToken ct);
    Task<JournalEntryLifecycleActionResultDto> ApplyLifecycleAsync(JournalEntryLifecycleActionRequestDto request, CancellationToken ct);
}

/// <summary>Uses governed factor preparation, draft intake and server-authorized lifecycle routes.</summary>
public sealed class FactorPaydownApiClient(ApiClientService apiClient) : IFactorPaydownApiClient
{
    // This route preserves decimals as strings for browser precision. Keep its reader local rather
    // than changing serialization policy for unrelated desktop routes.
    private static readonly JsonSerializerOptions JsonOptions = new(DesktopJsonOptions.Api)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public Task<FactorPaydownPreparationDto> PrepareAsync(PrepareFactorPaydownRequestDto request, CancellationToken ct)
        => PostAsync<FactorPaydownPreparationDto>(UiApiRoutes.LedgerFactorPaydownPreparation
            .Replace("{ledgerBookId:guid}", request.LedgerBookId.ToString("D")), request, ct);

    public Task<AssetAccountingPostingCandidateDto> CreateDraftAsync(CreateFactorPaydownDraftRequestDto request, CancellationToken ct)
        => PostAsync<AssetAccountingPostingCandidateDto>(UiApiRoutes.LedgerFactorPaydownDrafts
            .Replace("{ledgerBookId:guid}", request.Request.LedgerBookId.ToString("D")), request, ct);

    public async Task<ManualJournalEntryWorkbenchDto> GetRetainedDraftsAsync(string fundProfileId, Guid ledgerBookId, CancellationToken ct)
    {
        var response = await apiClient.GetWithResponseAsync<ManualJournalEntryWorkbenchDto>(
            UiApiRoutes.LedgerManualJournalEntryWorkbench + "?fundProfileId=" + Uri.EscapeDataString(fundProfileId)
            + "&ledgerBookId=" + ledgerBookId.ToString("D"), ct).ConfigureAwait(false);
        return response.Success && response.Data is { } data ? data
            : throw new InvalidOperationException(response.ErrorMessage ?? "Retained server drafts are unavailable.");
    }

    public Task<ManualJournalEntryDraftDto> SubmitForReviewAsync(SubmitManualJournalEntryApprovalRequest request, CancellationToken ct)
        => PostAsync<ManualJournalEntryDraftDto>(UiApiRoutes.LedgerManualJournalEntrySubmitApproval, request, ct);

    public async Task<AssetAccountingDraftCapabilitiesDto?> GetCapabilitiesAsync(string fundProfileId, Guid ledgerBookId, Guid journalEntryId, CancellationToken ct)
    {
        var route = UiApiRoutes.LedgerFactorPaydownDraftCapabilities.Replace("{ledgerBookId:guid}", ledgerBookId.ToString("D"))
            .Replace("{journalEntryId:guid}", journalEntryId.ToString("D")) + "?fundProfileId=" + Uri.EscapeDataString(fundProfileId);
        var response = await apiClient.GetWithResponseAsync<AssetAccountingDraftCapabilitiesDto>(route, ct).ConfigureAwait(false);
        if (response.StatusCode is 404 or 501) return null;
        return response.Success && response.Data is { } data ? data
            : throw new InvalidOperationException(response.ErrorMessage ?? "Approval and posting authorization is unavailable.");
    }

    public Task<JournalEntryLifecycleActionResultDto> ApplyLifecycleAsync(JournalEntryLifecycleActionRequestDto request, CancellationToken ct)
        => PostAsync<JournalEntryLifecycleActionResultDto>(UiApiRoutes.LedgerManualJournalEntryLifecycleAction, request, ct);

    private async Task<T> PostAsync<T>(string route, object request, CancellationToken ct) where T : class
    {
        var response = await apiClient.PostWithResponseAsync<JsonDocument>(route, request, ct).ConfigureAwait(false);
        if (!response.Success || response.Data is not { } document)
            throw new InvalidOperationException(response.ErrorMessage ?? "The governed factor-paydown service is unavailable.");
        using (document)
            return document.RootElement.Deserialize<T>(JsonOptions)
                ?? throw new InvalidOperationException("The governed factor-paydown service returned no result.");
    }
}
