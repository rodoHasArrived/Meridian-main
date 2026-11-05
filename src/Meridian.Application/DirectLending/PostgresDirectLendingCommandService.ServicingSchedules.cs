using Meridian.Contracts.DirectLending;
using Meridian.FSharp.DirectLending.Aggregates;

namespace Meridian.Application.DirectLending;

/// <summary>
/// Scheduled floating-rate resets, PIK capitalization and index fixings. See
/// <c>docs/engineering/blueprints/direct-lending-pik-capitalization-and-rate-resets.md</c>.
/// </summary>
public sealed partial class PostgresDirectLendingCommandService
{
    public async Task<DirectLendingCommandResult<DirectLendingIndexFixingDto>> RecordIndexFixingAsync(RecordIndexFixingRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_indexFixingStore is null)
        {
            return DirectLendingCommandResult<DirectLendingIndexFixingDto>.Failure(DirectLendingErrorCode.Validation, "No direct-lending index fixing store is configured.");
        }

        return await DirectLendingServicingScheduleSupport.RecordIndexFixingAsync(_indexFixingStore, request, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DirectLendingIndexFixingDto>> GetIndexFixingsAsync(string? indexName, DateOnly? from, DateOnly? to, CancellationToken ct = default)
        => _indexFixingStore is null
            ? []
            : await _indexFixingStore.ListAsync(indexName, from, to, ct).ConfigureAwait(false);

    public async Task<DirectLendingCommandResult<LoanServicingStateDto>> ApplyScheduledRateResetAsync(Guid loanId, ApplyScheduledRateResetRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stored = await _queryService.LoadAggregateAsync(loanId, ct).ConfigureAwait(false);
        if (stored is null)
        {
            return DirectLendingCommandResult<LoanServicingStateDto>.Failure(DirectLendingErrorCode.NotFound, $"Loan '{loanId}' was not found.");
        }

        var activeError = DirectLendingServiceSupport.EnsureActive(stored.Contract);
        if (activeError is not null)
        {
            return new DirectLendingCommandResult<LoanServicingStateDto>(null, activeError);
        }

        var resolved = await DirectLendingServicingScheduleSupport.ResolveScheduledFixingAsync(
            stored.Contract.CurrentTerms,
            stored.Servicing,
            request.ResetDate,
            _indexFixingStore,
            ct).ConfigureAwait(false);
        if (resolved.AlreadyApplied)
        {
            return DirectLendingCommandResult<LoanServicingStateDto>.Success(stored.Servicing);
        }

        if (resolved.Error is not null)
        {
            return new DirectLendingCommandResult<LoanServicingStateDto>(null, resolved.Error);
        }

        var fixing = resolved.Fixing!;
        RateResetDecision decision;
        try
        {
            decision = DirectLendingAggregateInterop.ApplyScheduledRateResetFromFixing(stored.Servicing, stored.Contract.CurrentTerms, request.ResetDate, fixing, DirectLendingServicingScheduleSupport.FixingSourceRef(fixing));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return DirectLendingCommandResult<LoanServicingStateDto>.Failure(DirectLendingErrorCode.Validation, ex.Message);
        }

        var servicing = decision.Servicing;
        var reset = servicing.CurrentRateReset!;

        using var payload = DirectLendingServiceSupport.CreatePayloadDocument(new
        {
            loanId,
            EffectiveDate = request.ResetDate,
            reset.ObservedRate,
            reset.SpreadBps,
            reset.AllInRate,
            reset.SourceRef,
            ResetKind = nameof(RateResetKind.Scheduled),
            FixingObservationDate = fixing.ObservationDate,
            fixing.FixingId,
            FixingRevision = fixing.Revision
        });

        await SaveAsync(
            loanId,
            stored.AggregateVersion,
            stored.AggregateVersion + 1,
            stored.Contract,
            servicing,
            eventType: "loan.rate-reset-applied",
            effectiveDate: request.ResetDate,
            payload,
            metadata,
            ct: ct,
            persistenceBatch: null).ConfigureAwait(false);

        return DirectLendingCommandResult<LoanServicingStateDto>.Success(servicing);
    }

    public async Task<DirectLendingCommandResult<LoanServicingStateDto>> CapitalizePikAsync(Guid loanId, CapitalizePikRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stored = await _queryService.LoadAggregateAsync(loanId, ct).ConfigureAwait(false);
        if (stored is null)
        {
            return DirectLendingCommandResult<LoanServicingStateDto>.Failure(DirectLendingErrorCode.NotFound, $"Loan '{loanId}' was not found.");
        }

        var activeError = DirectLendingServiceSupport.EnsureActive(stored.Contract);
        if (activeError is not null)
        {
            return new DirectLendingCommandResult<LoanServicingStateDto>(null, activeError);
        }

        if (stored.Servicing.LastPikCapitalizationDate is { } lastCapitalization && lastCapitalization == request.CapitalizationDate)
        {
            return DirectLendingCommandResult<LoanServicingStateDto>.Success(stored.Servicing);
        }

        PikCapitalizationDecision decision;
        try
        {
            decision = DirectLendingAggregateInterop.CapitalizePik(stored.Servicing, stored.Contract.CurrentTerms, request);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return DirectLendingCommandResult<LoanServicingStateDto>.Failure(DirectLendingErrorCode.Validation, ex.Message);
        }

        var capitalization = decision.Capitalization;
        using var payload = DirectLendingServiceSupport.CreatePayloadDocument(new
        {
            loanId,
            capitalization.CapitalizationDate,
            capitalization.PeriodStart,
            capitalization.PeriodEnd,
            capitalization.Amount
        });

        await SaveAsync(
            loanId,
            stored.AggregateVersion,
            stored.AggregateVersion + 1,
            stored.Contract,
            decision.Servicing,
            eventType: "loan.pik-capitalized",
            effectiveDate: request.CapitalizationDate,
            payload,
            metadata,
            ct: ct,
            persistenceBatch: null).ConfigureAwait(false);

        return DirectLendingCommandResult<LoanServicingStateDto>.Success(decision.Servicing);
    }

    public async Task<LoanServicingSchedulePreviewDto?> GetServicingSchedulePreviewAsync(Guid loanId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var stored = await _queryService.LoadAggregateAsync(loanId, ct).ConfigureAwait(false);
        return stored is null
            ? null
            : await DirectLendingServicingScheduleSupport.BuildPreviewAsync(
                loanId,
                stored.Contract.CurrentTerms,
                stored.Servicing,
                _indexFixingStore,
                from,
                to,
                ct).ConfigureAwait(false);
    }
}
