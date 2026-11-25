using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Services.Services.Accounting;

/// <summary>The retained adjustment draft as the desktop shows it: its identity, review status and book.</summary>
public sealed record IntercompanyConfirmationAdjustmentDraftResult(Guid JournalEntryId, string Status, Guid? LedgerBookId, DateOnly AccountingDate);

public partial interface IWorkstationIntercompanyAgreementApiClient
{
    /// <summary>
    /// Pre-fills an ordinary journal-workbench draft correcting one side after a resolved dispute (DEC-IC-CONFIRM-003).
    /// Nothing is submitted or posted.
    /// </summary>
    Task<ApiResponse<IntercompanyConfirmationAdjustmentDraftResult>> CreateConfirmationAdjustmentDraftAsync(Guid confirmationId,
        CreateIntercompanyConfirmationAdjustmentDraftRequest request, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyConfirmationAdjustmentDraftResult>.Fail("Adjustment drafting is unavailable.", 503));
}

public sealed partial class WorkstationIntercompanyAgreementApiClient
{
    public async Task<ApiResponse<IntercompanyConfirmationAdjustmentDraftResult>> CreateConfirmationAdjustmentDraftAsync(Guid confirmationId,
        CreateIntercompanyConfirmationAdjustmentDraftRequest request, CancellationToken ct = default)
    {
        var transport = await apiClient.PostWithResponseAsync<JsonDocument>($"{ConfirmationRoute}/{confirmationId:D}/adjustment-drafts", request, ct)
            .ConfigureAwait(false);
        if (!transport.Success || transport.Data is null)
            return ApiResponse<IntercompanyConfirmationAdjustmentDraftResult>.Fail(transport.ErrorMessage ?? "Adjustment drafting is unavailable.",
                transport.StatusCode, transport.IsConnectionError);
        using var document = transport.Data;
        var root = document.RootElement;
        // Only the identity, status and scope are read; the draft itself is reviewed in the journal workbench.
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("journalEntryId", out var id) || !id.TryGetGuid(out var journalEntryId)
            || !root.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("accountingDate", out var date) || date.ValueKind != JsonValueKind.String
            || !DateOnly.TryParseExact(date.GetString(), "yyyy-MM-dd", out var accountingDate))
            return ApiResponse<IntercompanyConfirmationAdjustmentDraftResult>.Fail("Adjustment drafting returned an invalid draft.", 502);
        Guid? book = root.TryGetProperty("ledgerBookId", out var ledger) && ledger.ValueKind == JsonValueKind.String && ledger.TryGetGuid(out var parsed)
            ? parsed : null;
        return ApiResponse<IntercompanyConfirmationAdjustmentDraftResult>.Ok(new(journalEntryId, status.GetString()!, book, accountingDate), transport.StatusCode);
    }
}
