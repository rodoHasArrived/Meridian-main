using Meridian.Contracts.DirectLending;
using Meridian.FSharp.DirectLending.Aggregates;
using Meridian.Storage.DirectLending;

namespace Meridian.Application.DirectLending;

/// <summary>
/// Scheduled resets, PIK capitalization and index fixings for the nonproduction in-memory service.
/// The decisions run through the same F# aggregate functions as the durable service.
/// </summary>
public sealed partial class InMemoryDirectLendingService
{
    private readonly IDirectLendingIndexFixingStore _indexFixingStore = new InMemoryDirectLendingIndexFixingStore();

    public async Task<DirectLendingIndexFixingDto?> RecordIndexFixingAsync(RecordIndexFixingRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await DirectLendingServicingScheduleSupport.RecordIndexFixingAsync(_indexFixingStore, request, ct).ConfigureAwait(false);
        return DirectLendingServiceSupport.RequireSuccess(result);
    }

    public Task<IReadOnlyList<DirectLendingIndexFixingDto>> GetIndexFixingsAsync(string? indexName = null, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default)
        => _indexFixingStore.ListAsync(indexName, from, to, ct);

    public async Task<LoanServicingStateDto?> ApplyScheduledRateResetAsync(Guid loanId, ApplyScheduledRateResetRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        DirectLendingTermsDto terms;
        LoanServicingStateDto servicing;
        lock (_gate)
        {
            if (!_loans.TryGetValue(loanId, out var stored))
            {
                return null;
            }

            EnsureActive(stored);
            terms = stored.TermsVersions[^1].Terms;
            servicing = ToServicingState(stored);
        }

        var resolved = await DirectLendingServicingScheduleSupport.ResolveScheduledFixingAsync(terms, servicing, request.ResetDate, _indexFixingStore, ct).ConfigureAwait(false);
        if (resolved.AlreadyApplied)
        {
            return servicing;
        }

        if (resolved.Error is not null)
        {
            throw new DirectLendingCommandException(resolved.Error);
        }

        var fixing = resolved.Fixing!;
        lock (_gate)
        {
            var stored = _loans[loanId];
            var decision = Decide(() => DirectLendingAggregateInterop.ApplyScheduledRateResetFromFixing(ToServicingState(stored), stored.TermsVersions[^1].Terms, request.ResetDate, fixing, DirectLendingServicingScheduleSupport.FixingSourceRef(fixing)));
            var reset = decision.Servicing.CurrentRateReset!;

            stored.Servicing = stored.Servicing with
            {
                CurrentRateReset = reset,
                RateResetHistory = decision.Servicing.RateResetHistory
            };
            AppendRevision(stored, "ScheduledRateReset", request.ResetDate, $"Scheduled rate reset applied at {reset.AllInRate:P4}.");
            AppendEvent(stored, "loan.rate-reset-applied", request.ResetDate, new
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
            }, metadata);
            return ToServicingState(stored);
        }
    }

    public Task<LoanServicingStateDto?> CapitalizePikAsync(Guid loanId, CapitalizePikRequest request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            if (!_loans.TryGetValue(loanId, out var stored))
            {
                return Task.FromResult<LoanServicingStateDto?>(null);
            }

            EnsureActive(stored);
            if (stored.Servicing.LastPikCapitalizationDate == request.CapitalizationDate)
            {
                return Task.FromResult<LoanServicingStateDto?>(ToServicingState(stored));
            }

            var decision = Decide(() => DirectLendingAggregateInterop.CapitalizePik(ToServicingState(stored), stored.TermsVersions[^1].Terms, request));
            var capitalization = decision.Capitalization;
            stored.Servicing = stored.Servicing with
            {
                Balances = decision.Servicing.Balances,
                PikInterestAccruedUnpaid = decision.Servicing.PikInterestAccruedUnpaid,
                LastPikCapitalizationDate = decision.Servicing.LastPikCapitalizationDate,
                PikCapitalizations = decision.Servicing.PikCapitalizations
            };
            AppendRevision(stored, "PikCapitalization", request.CapitalizationDate, $"PIK interest of {capitalization.Amount:0.00} capitalized into principal.");
            AppendEvent(stored, "loan.pik-capitalized", request.CapitalizationDate, new
            {
                loanId,
                capitalization.CapitalizationDate,
                capitalization.PeriodStart,
                capitalization.PeriodEnd,
                capitalization.Amount
            }, metadata);
            return Task.FromResult<LoanServicingStateDto?>(ToServicingState(stored));
        }
    }

    public async Task<LoanServicingSchedulePreviewDto?> GetServicingSchedulePreviewAsync(Guid loanId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        DirectLendingTermsDto terms;
        LoanServicingStateDto servicing;
        lock (_gate)
        {
            if (!_loans.TryGetValue(loanId, out var stored))
            {
                return null;
            }

            terms = stored.TermsVersions[^1].Terms;
            servicing = ToServicingState(stored);
        }

        return await DirectLendingServicingScheduleSupport.BuildPreviewAsync(loanId, terms, servicing, _indexFixingStore, from, to, ct).ConfigureAwait(false);
    }

    private static T Decide<T>(Func<T> decide)
    {
        try
        {
            return decide();
        }
        catch (Exception ex) when (ex is not DirectLendingCommandException and not OperationCanceledException)
        {
            throw new DirectLendingCommandException(new DirectLendingCommandError(DirectLendingErrorCode.Validation, ex.Message));
        }
    }
}
