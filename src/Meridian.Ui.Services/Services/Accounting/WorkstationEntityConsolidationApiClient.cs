using System.Globalization;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;

namespace Meridian.Ui.Services.Services.Accounting;

/// <summary>Shared governed entity and consolidation projections; no workstation calculations.</summary>
public interface IWorkstationEntityConsolidationApiClient
{
    Task<OrganizationStructureGraphDto> GetStructureAsync(CancellationToken ct = default);
    Task<EntityTimelineDto> GetTimelineAsync(Guid organizationId, Guid entityId, DateTimeOffset asOf, CancellationToken ct = default);
    Task<IReadOnlyList<LedgerPeriodDto>> GetPeriodsAsync(Guid ledgerBookId, CancellationToken ct = default);
    Task<ConsolidationViewDto> PreviewAsync(ConsolidationRequestDto request, CancellationToken ct = default);
    Task<ConsolidationViewDto> CreateDraftsAsync(ConsolidationRequestDto request, CancellationToken ct = default);
}

public sealed class WorkstationEntityConsolidationApiClient(ApiClientService api) : IWorkstationEntityConsolidationApiClient
{
    public async Task<OrganizationStructureGraphDto> GetStructureAsync(CancellationToken ct = default)
        => Unwrap(await api.GetWithResponseAsync<OrganizationStructureGraphDto>("/api/fund-structure/graph?activeOnly=false", ct).ConfigureAwait(false));

    public async Task<EntityTimelineDto> GetTimelineAsync(Guid organizationId, Guid entityId, DateTimeOffset asOf, CancellationToken ct = default)
    {
        var route = $"/api/fund-structure/entities/{entityId:D}/timeline?organizationId={organizationId:D}&asOf={Uri.EscapeDataString(asOf.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))}";
        var result = Unwrap(await api.GetWithResponseAsync<EntityTimelineDto>(route, ct).ConfigureAwait(false));
        if (result.OrganizationId != organizationId || result.Entity.EntityId != entityId || result.AsOf != asOf)
            throw new InvalidOperationException("The entity timeline does not match the selected organization, entity and as-of time.");
        return result;
    }

    public async Task<IReadOnlyList<LedgerPeriodDto>> GetPeriodsAsync(Guid ledgerBookId, CancellationToken ct = default)
    {
        var result = Unwrap(await api.GetWithResponseAsync<LedgerPeriodDto[]>(UiApiRoutes.LedgerPeriods + $"?ledgerBookId={ledgerBookId:D}", ct).ConfigureAwait(false));
        if (result.Any(period => period.LedgerBookId != ledgerBookId))
            throw new InvalidOperationException("The period response belongs to another ledger book.");
        return result;
    }

    public async Task<ConsolidationViewDto> PreviewAsync(ConsolidationRequestDto request, CancellationToken ct = default)
    {
        var route = $"/api/ledger/consolidation/preview?organizationId={request.OrganizationId:D}&ownershipRootId={request.OwnershipRootId:D}&eliminationBookId={request.EliminationBookId:D}&periodId={request.PeriodId:D}&asOf={request.AsOf:yyyy-MM-dd}";
        return Validate(request, Unwrap(await api.GetWithResponseAsync<ConsolidationViewDto>(route, ct).ConfigureAwait(false)));
    }

    public async Task<ConsolidationViewDto> CreateDraftsAsync(ConsolidationRequestDto request, CancellationToken ct = default)
        => Validate(request, Unwrap(await api.PostWithResponseAsync<ConsolidationViewDto>("/api/ledger/consolidation/drafts", request, ct).ConfigureAwait(false)));

    private static ConsolidationViewDto Validate(ConsolidationRequestDto request, ConsolidationViewDto result)
        => result.Request == request ? result : throw new InvalidOperationException("The consolidation response does not match the selected scope.");

    private static T Unwrap<T>(ApiResponse<T> response) where T : class
        => response.Success && response.Data is not null ? response.Data
            : throw new InvalidOperationException(response.StatusCode switch
            {
                403 => "The authenticated session cannot access this entity or consolidation scope.",
                409 => response.ErrorMessage ?? "The selected source evidence changed or the scope is unavailable. Refresh and review again.",
                429 => "The service is busy. Retry this request shortly.",
                _ => response.ErrorMessage ?? "The governed workstation service is unavailable."
            });
}
