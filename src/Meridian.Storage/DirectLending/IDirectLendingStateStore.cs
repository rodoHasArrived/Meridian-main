using System.Text.Json;
using Meridian.Contracts.DirectLending;

namespace Meridian.Storage.DirectLending;

public sealed record DirectLendingEventWriteMetadata(
    Guid? CausationId,
    Guid? CorrelationId,
    Guid? CommandId,
    string? SourceSystem,
    bool ReplayFlag);

public interface IDirectLendingStateStore
{
    Task<LoanContractDetailDto?> LoadContractProjectionAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<LoanTermsVersionDto>> LoadTermsVersionProjectionsAsync(Guid loanId, CancellationToken ct = default);

    Task<LoanServicingStateDto?> LoadServicingProjectionAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<DrawdownLotDto>> LoadDrawdownLotProjectionsAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<ServicingRevisionDto>> LoadServicingRevisionProjectionsAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<DailyAccrualEntryDto>> LoadAccrualEntryProjectionsAsync(Guid loanId, CancellationToken ct = default);

    Task<PersistedDirectLendingState?> LoadAsync(Guid loanId, CancellationToken ct = default);

    Task<IReadOnlyList<LoanEventLineageDto>> GetHistoryAsync(Guid loanId, CancellationToken ct = default);

    Task SaveStateAsync(
        Guid loanId,
        long aggregateVersion,
        LoanContractDetailDto contract,
        LoanServicingStateDto servicing,
        CancellationToken ct = default);

    Task SaveAsync(
        Guid loanId,
        long expectedVersion,
        long nextVersion,
        LoanContractDetailDto contract,
        LoanServicingStateDto servicing,
        string eventType,
        int eventSchemaVersion,
        DateOnly? effectiveDate,
        JsonDocument payload,
        DirectLendingEventWriteMetadata metadata,
        DirectLendingPersistenceBatch? persistenceBatch,
        Guid eventId,
        CancellationToken ct = default);
}

public sealed record PersistedDirectLendingState(
    Guid LoanId,
    long AggregateVersion,
    LoanContractDetailDto Contract,
    LoanServicingStateDto Servicing);
