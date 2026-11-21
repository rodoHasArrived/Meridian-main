using System.Globalization;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;

namespace Meridian.Ui.Services.Services.Accounting;

/// <summary>Agreement commands use the authenticated server identity and authority.</summary>
public partial interface IWorkstationIntercompanyAgreementApiClient
{
    Task<ApiResponse<IntercompanyAgreementListDto>> ListAsync(IntercompanyAgreementQueryDto query, CancellationToken ct = default);
    Task<ApiResponse<IntercompanyAgreementDetailDto>> GetAsync(Guid agreementId, DateOnly asOf, CancellationToken ct = default);
    Task<ApiResponse<IntercompanyAgreementJournalLinesDto>> GetJournalLinesAsync(Guid agreementId, Guid ledgerBookId, DateOnly asOf, string? cursor = null, CancellationToken ct = default);
    Task<ApiResponse<IntercompanyAgreementCommandResultDto>> ExecuteAsync(string action, IntercompanyAgreementCommandDto command, CancellationToken ct = default);
}

public sealed partial class WorkstationIntercompanyAgreementApiClient(ApiClientService apiClient) : IWorkstationIntercompanyAgreementApiClient
{
    public Task<ApiResponse<IntercompanyAgreementListDto>> ListAsync(IntercompanyAgreementQueryDto query, CancellationToken ct = default)
    {
        var parameters = new List<string> { $"pageSize={query.PageSize}" };
        if (query.EntityId is { } entity)
            parameters.Add($"entityId={entity:D}");
        if (query.AgreementType is { } type)
            parameters.Add($"agreementType={type}");
        if (query.Status is { } status)
            parameters.Add($"status={status}");
        if (query.AsOf is { } date)
            parameters.Add($"asOf={date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        if (query.KnownAtUtc is { } known)
            parameters.Add($"knownAtUtc={Uri.EscapeDataString(known.ToString("O", CultureInfo.InvariantCulture))}");
        if (query.Cursor is { Length: > 0 } cursor)
            parameters.Add($"cursor={Uri.EscapeDataString(cursor)}");
        return apiClient.GetWithResponseAsync<IntercompanyAgreementListDto>(UiApiRoutes.IntercompanyAgreements + "?" + string.Join("&", parameters), ct);
    }

    public async Task<ApiResponse<IntercompanyAgreementDetailDto>> GetAsync(Guid agreementId, DateOnly asOf, CancellationToken ct = default)
    {
        var response = await apiClient.GetWithResponseAsync<IntercompanyAgreementDetailDto>(
            $"{UiApiRoutes.IntercompanyAgreements}/{agreementId:D}?asOf={asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}", ct).ConfigureAwait(false);
        return response.Success && (response.Data is not { } detail || detail.AgreementId != agreementId || detail.AsOf != asOf)
            ? ApiResponse<IntercompanyAgreementDetailDto>.Fail("The server returned a different agreement or date.", 502) : response;
    }

    public async Task<ApiResponse<IntercompanyAgreementJournalLinesDto>> GetJournalLinesAsync(Guid agreementId, Guid ledgerBookId, DateOnly asOf, string? cursor = null, CancellationToken ct = default)
    {
        var response = await apiClient.GetWithResponseAsync<IntercompanyAgreementJournalLinesDto>(
            $"{UiApiRoutes.IntercompanyAgreements}/{agreementId:D}/journal-lines?ledgerBookId={ledgerBookId:D}&asOf={asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
            + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)), ct).ConfigureAwait(false);
        return response.Success && (response.Data is not { } journal || journal.AgreementId != agreementId || journal.LedgerBookId != ledgerBookId || journal.AsOf != asOf)
            ? ApiResponse<IntercompanyAgreementJournalLinesDto>.Fail("The server returned journal lines for a different agreement, book or date.", 502) : response;
    }

    public async Task<ApiResponse<IntercompanyAgreementCommandResultDto>> ExecuteAsync(string action, IntercompanyAgreementCommandDto command, CancellationToken ct = default)
    {
        var route = UiApiRoutes.IntercompanyAgreements;
        ApiResponse<IntercompanyAgreementCommandResultDto> response;
        if (action == "create")
            response = await apiClient.PostWithResponseAsync<IntercompanyAgreementCommandResultDto>(route, command, ct).ConfigureAwait(false);
        else if (action == "propose")
            response = await apiClient.PostWithResponseAsync<IntercompanyAgreementCommandResultDto>($"{route}/{command.AgreementId:D}/proposals", command, ct).ConfigureAwait(false);
        else if (action == "save")
            response = await apiClient.PutWithResponseAsync<IntercompanyAgreementCommandResultDto>($"{route}/{command.AgreementId:D}/proposals/{command.ProposalId:D}", command, ct).ConfigureAwait(false);
        else if (action is "submit" or "approve" or "reject" or "withdraw")
            response = await apiClient.PostWithResponseAsync<IntercompanyAgreementCommandResultDto>($"{route}/{command.AgreementId:D}/proposals/{command.ProposalId:D}/{action}", command, ct).ConfigureAwait(false);
        else
            throw new ArgumentOutOfRangeException(nameof(action), "Unsupported agreement command.");
        return response.Success && (response.Data is not { } result || result.Detail.AgreementId != command.AgreementId || result.Receipt.CommandId != command.CommandId
            || result.Receipt.Detail.AgreementId != command.AgreementId)
            ? ApiResponse<IntercompanyAgreementCommandResultDto>.Fail("The server returned a receipt for a different agreement or command.", 502) : response;
    }
}
