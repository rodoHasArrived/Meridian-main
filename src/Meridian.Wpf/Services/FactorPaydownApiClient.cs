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
}

/// <summary>Uses governed preparation and candidate intake; never approves or posts.</summary>
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
