using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;

namespace Meridian.Ui.Services.Services.Accounting;

public partial interface IWorkstationIntercompanyAgreementApiClient
{
    /// <summary>Reviewed fixings of one index over an inclusive date range, as retained by the server.</summary>
    Task<ApiResponse<IntercompanyRateFixingDto[]>> GetRateFixingsAsync(string indexReference, DateOnly from, DateOnly to, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyRateFixingDto[]>.Fail("Intercompany rate fixings are unavailable.", 503));
    /// <summary>Proposes a first value or a correction; it sets no rate until a second person approves it.</summary>
    Task<ApiResponse<IntercompanyRateFixingDto>> ProposeRateFixingAsync(ProposeIntercompanyRateFixingRequest request, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyRateFixingDto>.Fail("Intercompany rate fixings are unavailable.", 503));
    /// <summary>Approves or rejects exactly the pending value the reviewer saw.</summary>
    Task<ApiResponse<IntercompanyRateFixingDto>> DecideRateFixingAsync(Guid fixingId, DecideIntercompanyRateFixingRequest request, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyRateFixingDto>.Fail("Intercompany rate fixings are unavailable.", 503));
}

public sealed partial class WorkstationIntercompanyAgreementApiClient
{
    private const string RateFixingRoute = "/api/ledger/intercompany/rate-fixings";

    public async Task<ApiResponse<IntercompanyRateFixingDto[]>> GetRateFixingsAsync(string indexReference, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var response = await apiClient.GetWithResponseAsync<IntercompanyRateFixingDto[]>(
            $"{RateFixingRoute}?indexReference={Uri.EscapeDataString(indexReference.Trim())}&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", ct).ConfigureAwait(false);
        var index = indexReference.Trim().ToUpperInvariant();
        return response.Success && (response.Data is null || response.Data.Any(item => item.IndexReference != index || item.FixingDate < from || item.FixingDate > to))
            ? ApiResponse<IntercompanyRateFixingDto[]>.Fail("The server returned fixings for a different index or date range.", 502)
            : response;
    }

    public async Task<ApiResponse<IntercompanyRateFixingDto>> ProposeRateFixingAsync(ProposeIntercompanyRateFixingRequest request, CancellationToken ct = default)
        => Checked(await apiClient.PostWithResponseAsync<IntercompanyRateFixingDto>(RateFixingRoute, request, ct).ConfigureAwait(false), request.RequestId,
            item => item.FixingDate == request.FixingDate && item.IndexReference == request.IndexReference.Trim().ToUpperInvariant());

    public async Task<ApiResponse<IntercompanyRateFixingDto>> DecideRateFixingAsync(Guid fixingId, DecideIntercompanyRateFixingRequest request, CancellationToken ct = default)
        => Checked(await apiClient.PostWithResponseAsync<IntercompanyRateFixingDto>($"{RateFixingRoute}/{fixingId:D}/decisions", request, ct).ConfigureAwait(false),
            request.RequestId, item => item.FixingId == fixingId);

    // A response that does not record this request for this fixing is not this request's answer.
    private static ApiResponse<IntercompanyRateFixingDto> Checked(ApiResponse<IntercompanyRateFixingDto> response, Guid requestId, Func<IntercompanyRateFixingDto, bool> matches)
        => response.Success && (response.Data is not { } fixing || !matches(fixing) || fixing.Events.All(item => item.RequestId != requestId))
            ? ApiResponse<IntercompanyRateFixingDto>.Fail("The fixing response does not record this request.", 502)
            : response;
}
