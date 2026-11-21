using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Services.Services.Accounting;

public partial interface IWorkstationIntercompanyAgreementApiClient
{
    Task<ApiResponse<IntercompanyObligationMonitorDto>> GetObligationMonitorAsync(IntercompanyObligationMonitorRequestDto request, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyObligationMonitorDto>.Fail("Intercompany monitoring is unavailable.", 503));
    Task<ApiResponse<IntercompanyStructuredTermsPreviewDto>> PreviewStructuredTermsAsync(Guid agreementId, DateOnly asOf, int? agreementVersion = null, Guid? proposalId = null, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyStructuredTermsPreviewDto>.Fail("Structured terms preview is unavailable.", 503));
}

public sealed partial class WorkstationIntercompanyAgreementApiClient
{
    public async Task<ApiResponse<IntercompanyObligationMonitorDto>> GetObligationMonitorAsync(IntercompanyObligationMonitorRequestDto request, CancellationToken ct = default)
    {
        var query = new List<string> { $"asOf={request.AsOf:yyyy-MM-dd}", $"expiryHorizonDays={request.ExpiryHorizonDays}" };
        if (request.LedgerBookId.HasValue)
            query.Add($"ledgerBookId={request.LedgerBookId:D}");
        if (request.EntityId.HasValue)
            query.Add($"entityId={request.EntityId:D}");
        if (!string.IsNullOrWhiteSpace(request.FundProfileId))
            query.Add("fundProfileId=" + Uri.EscapeDataString(request.FundProfileId));
        var transport = await apiClient.GetWithResponseAsync<JsonDocument>("/api/ledger/intercompany/obligations?" + string.Join("&", query), ct).ConfigureAwait(false);
        if (!transport.Success || transport.Data is null)
            return ApiResponse<IntercompanyObligationMonitorDto>.Fail(transport.ErrorMessage ?? "Obligation monitoring is unavailable.", transport.StatusCode, transport.IsConnectionError);
        using var document = transport.Data;
        IntercompanyObligationMonitorDto? decoded;
        try
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { NumberHandling = JsonNumberHandling.AllowReadingFromString };
            decoded = document.RootElement.Deserialize(new IntercompanyObligationMonitorJsonContext(options).IntercompanyObligationMonitorDto);
        }
        catch (JsonException)
        { return ApiResponse<IntercompanyObligationMonitorDto>.Fail("The monitor returned invalid exact-decimal JSON.", 502); }
        var response = decoded is null ? ApiResponse<IntercompanyObligationMonitorDto>.Fail("The monitor returned no observation.", 502)
            : ApiResponse<IntercompanyObligationMonitorDto>.Ok(decoded, transport.StatusCode);
        if (response.Success && (response.Data is not { } result || result.AsOf != request.AsOf || result.ExpiryHorizonDays != request.ExpiryHorizonDays
            || result.BalanceSchedule is { } schedule && (schedule.AsOf != request.AsOf
                || request.LedgerBookId.HasValue && schedule.LedgerBookId != request.LedgerBookId
                || request.FundProfileId is not null && schedule.FundProfileId != request.FundProfileId)))
            return ApiResponse<IntercompanyObligationMonitorDto>.Fail("The obligation result belongs to a different book, profile or cutoff.", 502);
        return response;
    }

    public Task<ApiResponse<IntercompanyStructuredTermsPreviewDto>> PreviewStructuredTermsAsync(Guid agreementId, DateOnly asOf, int? agreementVersion = null, Guid? proposalId = null, CancellationToken ct = default)
    {
        if (agreementVersion.HasValue == proposalId.HasValue)
            throw new ArgumentException("Select exactly one retained proposal or approved version for date preview.");
        var query = $"asOf={asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
            + (agreementVersion.HasValue ? $"&version={agreementVersion}" : $"&proposalId={proposalId:D}");
        return apiClient.GetWithResponseAsync<IntercompanyStructuredTermsPreviewDto>($"{UiApiRoutes.IntercompanyAgreements}/{agreementId:D}/schedule?{query}", ct);
    }
}
