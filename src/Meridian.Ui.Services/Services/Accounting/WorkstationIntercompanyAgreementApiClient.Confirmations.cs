using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Services.Services.Accounting;

public partial interface IWorkstationIntercompanyAgreementApiClient
{
    /// <summary>Confirmation position for one book at a date. Advisory only; it never changes close eligibility.</summary>
    Task<ApiResponse<IntercompanyBalanceConfirmationSummaryDto>> GetBalanceConfirmationsAsync(Guid ledgerBookId, DateOnly asOf, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyBalanceConfirmationSummaryDto>.Fail("Balance confirmation is unavailable.", 503));
    /// <summary>Pins both sides' current posted balances for one approved accounting profile at the date.</summary>
    Task<ApiResponse<IntercompanyBalanceConfirmationDto>> ProposeBalanceConfirmationAsync(ProposeIntercompanyBalanceConfirmationRequest request, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyBalanceConfirmationDto>.Fail("Balance confirmation is unavailable.", 503));
    /// <summary>Confirms, disputes or resolves the exact retained version the operator saw.</summary>
    Task<ApiResponse<IntercompanyBalanceConfirmationDto>> ActOnBalanceConfirmationAsync(Guid confirmationId, IntercompanyBalanceConfirmationActionRequest request, CancellationToken ct = default)
        => Task.FromResult(ApiResponse<IntercompanyBalanceConfirmationDto>.Fail("Balance confirmation is unavailable.", 503));
}

public sealed partial class WorkstationIntercompanyAgreementApiClient
{
    private const string ConfirmationRoute = "/api/ledger/intercompany/confirmations";

    public async Task<ApiResponse<IntercompanyBalanceConfirmationSummaryDto>> GetBalanceConfirmationsAsync(Guid ledgerBookId, DateOnly asOf, CancellationToken ct = default)
    {
        var transport = await apiClient.GetWithResponseAsync<JsonDocument>($"{ConfirmationRoute}?ledgerBookId={ledgerBookId:D}&asOf={asOf:yyyy-MM-dd}", ct).ConfigureAwait(false);
        var response = Decode(transport, context => context.IntercompanyBalanceConfirmationSummaryDto);
        return response.Success && (response.Data!.LedgerBookId != ledgerBookId || response.Data.AsOf != asOf
            || response.Data.Confirmations.Any(item => item.AsOf != asOf
                || (item.PartyOne.LedgerBookId != ledgerBookId && item.PartyTwo.LedgerBookId != ledgerBookId)))
            ? ApiResponse<IntercompanyBalanceConfirmationSummaryDto>.Fail("The confirmation summary belongs to a different book or date.", 502)
            : response;
    }

    public async Task<ApiResponse<IntercompanyBalanceConfirmationDto>> ProposeBalanceConfirmationAsync(ProposeIntercompanyBalanceConfirmationRequest request, CancellationToken ct = default)
    {
        var transport = await apiClient.PostWithResponseAsync<JsonDocument>(ConfirmationRoute, request, ct).ConfigureAwait(false);
        var response = Decode(transport, context => context.IntercompanyBalanceConfirmationDto);
        return response.Success && (response.Data!.AccountingArrangementId != request.AccountingArrangementId || response.Data.AsOf != request.AsOf)
            ? ApiResponse<IntercompanyBalanceConfirmationDto>.Fail("The proposal response belongs to a different profile or date.", 502)
            : response;
    }

    public async Task<ApiResponse<IntercompanyBalanceConfirmationDto>> ActOnBalanceConfirmationAsync(Guid confirmationId, IntercompanyBalanceConfirmationActionRequest request, CancellationToken ct = default)
    {
        var transport = await apiClient.PostWithResponseAsync<JsonDocument>($"{ConfirmationRoute}/{confirmationId:D}/actions", request, ct).ConfigureAwait(false);
        var response = Decode(transport, context => context.IntercompanyBalanceConfirmationDto);
        return response.Success && (response.Data!.ConfirmationId != confirmationId || response.Data.Events.All(item => item.RequestId != request.RequestId))
            ? ApiResponse<IntercompanyBalanceConfirmationDto>.Fail("The confirmation response does not record this request.", 502)
            : response;
    }

    // The host sends balances as exact decimal strings; they are read back without a binary floating-point round trip.
    private static ApiResponse<T> Decode<T>(ApiResponse<JsonDocument> transport,
        Func<IntercompanyBalanceConfirmationJsonContext, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>> type) where T : class
    {
        if (!transport.Success || transport.Data is null)
            return ApiResponse<T>.Fail(transport.ErrorMessage ?? "Balance confirmation is unavailable.", transport.StatusCode, transport.IsConnectionError);
        using var document = transport.Data;
        try
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { NumberHandling = JsonNumberHandling.AllowReadingFromString };
            return document.RootElement.Deserialize(type(new IntercompanyBalanceConfirmationJsonContext(options))) is { } decoded
                ? ApiResponse<T>.Ok(decoded, transport.StatusCode)
                : ApiResponse<T>.Fail("Balance confirmation returned no record.", 502);
        }
        catch (JsonException)
        { return ApiResponse<T>.Fail("Balance confirmation returned invalid exact-decimal JSON.", 502); }
    }
}
