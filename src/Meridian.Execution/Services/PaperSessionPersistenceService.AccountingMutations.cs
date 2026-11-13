using Meridian.Execution.Events;

namespace Meridian.Execution.Services;

public sealed partial class PaperSessionPersistenceService
{
    /// <summary>Retains and posts an assessed financing charge before changing session cash.
    /// A failed posting remains pending in the durable outbox and is retried on recovery.</summary>
    public async Task<bool> RecordFinancingAsync(PaperFinancingCharge charge, CancellationToken ct = default)
    {
        charge.Validate();
        if (_store is null || _financing is null)
            throw new InvalidOperationException("Financing requires durable paper sessions and a configured governed ledger posting scope.");
        using var operation = EnterOperation(ct);
        ct = operation.Token;
        await InitialiseAsync(ct).ConfigureAwait(false);
        var gate = GetSessionGate(charge.SessionId);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!CurrentSessions.TryGetValue(charge.SessionId, out var session))
                throw new InvalidOperationException($"Paper session '{charge.SessionId}' is not loaded.");
            lock (session.SyncRoot)
            {
                if (!session.IsActive)
                    throw new InvalidOperationException("A closed paper session cannot accept financing charges.");
                if (charge.AccountId != PaperTradingPortfolio.DefaultAccountId)
                    throw new InvalidDataException("The single-account paper session requires its default account.");
                if (session.AccountingContext != _financing.PostingContext)
                    throw new InvalidDataException("The session is not bound to the configured accounting scope; legacy sessions require explicit migration.");
                if (session.PendingFinancingSourceEventId is { } pending && pending != charge.SourceEventId)
                    throw new InvalidOperationException($"Financing source '{pending}' requires recovery before another assessment.");
                var retained = session.FinancingHistory.Find(item => item.SourceEventId == charge.SourceEventId);
                if (retained is not null)
                    return retained == charge ? false : throw new InvalidDataException("Financing identity has conflicting content.");
            }
            // Validate the complete candidate off-side before accepting financial evidence.
            // The session gate prevents fills changing the source book while posting awaits.
            var replacement = BuildPortfolioProjection(session, candidateFinancing: charge);
            // Past this boundary, cancellation cannot discard a retained financial event.
            try
            { await _financing.PostAsync(charge, ct).ConfigureAwait(false); }
            catch (PaperFinancingAdmissionException refusal)
            {
                // This call accepted no new source. Preserve an existing retry latch, but do
                // not create a recovery obligation for a rejected/cancelled assessment.
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(refusal.InnerException!).Throw();
            }
            catch
            {
                // The source may already be durable. Refuse subsequent changes until this
                // exact assessment is confirmed, or restart recovers the outbox.
                lock (session.SyncRoot)
                    session.PendingFinancingSourceEventId = charge.SourceEventId;
                throw;
            }
            lock (session.SyncRoot)
            {
                session.Portfolio = replacement;
                session.FinancingHistory.Add(charge);
                session.PendingFinancingSourceEventId = null;
            }
            await PersistSessionLedgerAsync(session, CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        finally { gate.Release(); }
    }

    /// <summary>
    /// The latest trading date a session has settled for <paramref name="symbol"/> in
    /// <paramref name="accountId"/>, or <see langword="null"/> when it has never been settled.
    /// </summary>
    public DateOnly? GetLastSettlementDate(string sessionId, string accountId, string symbol)
    {
        ThrowIfDisposed();
        if (!CurrentSessions.TryGetValue(sessionId, out var session))
            return null;

        var normalizedSymbol = symbol.Trim();
        lock (session.SyncRoot)
        {
            DateOnly? latest = null;
            foreach (var settlement in session.SettlementHistory)
            {
                if (string.Equals(settlement.AccountId, accountId, StringComparison.Ordinal)
                    && string.Equals(settlement.Symbol.Trim(), normalizedSymbol, StringComparison.OrdinalIgnoreCase)
                    && (latest is null || settlement.TradingDate > latest))
                {
                    latest = settlement.TradingDate;
                }
            }

            return latest;
        }
    }

    /// <summary>
    /// Durably records one daily variation-margin settlement for an active session, then applies
    /// it to the session's book. The record is appended before the book changes (the same
    /// persist-then-apply order fills use), and its key (account, symbol, trading date) makes the
    /// call idempotent: settling the same day again at the same price changes nothing and returns
    /// <see langword="false"/>; at a different price it fails closed.
    /// </summary>
    /// <returns><see langword="true"/> when this call applied a new settlement.</returns>
    public async Task<bool> RecordSettlementAsync(
        string sessionId,
        PaperSessionSettlementRecord settlement,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        settlement.Validate();

        using var operation = EnterOperation(ct);
        ct = operation.Token;
        await InitialiseAsync(ct).ConfigureAwait(false);
        ThrowIfDisposed();
        var sessions = CurrentSessions;
        var gate = GetSessionGate(sessionId);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!sessions.TryGetValue(sessionId, out var session))
            {
                throw new InvalidOperationException(
                    $"Cannot record settlement '{settlement.Key}' because session '{sessionId}' is not loaded.");
            }

            lock (session.SyncRoot)
            {
                if (!session.IsActive)
                    return false;
                EnsureFinancingRecovered(session);
                if (session.SettlementHistory.Find(existing => string.Equals(existing.Key, settlement.Key, StringComparison.Ordinal)) is { } applied)
                {
                    return applied.SettlementPrice == settlement.SettlementPrice
                        ? false
                        : throw new InvalidDataException(
                            $"Settlement '{settlement.Key}' was already applied at {applied.SettlementPrice}, not {settlement.SettlementPrice}.");
                }
            }

            if (_store is not null)
            {
                var status = await _store.TryAppendSettlementAsync(sessionId, settlement, ct).ConfigureAwait(false);
                if (status == PaperSessionSettlementAppendStatus.Conflict)
                {
                    throw new InvalidDataException(
                        $"Settlement '{settlement.Key}' is already recorded at a different price.");
                }
            }

            var replacementPortfolio = BuildPortfolioProjection(session, candidateSettlement: settlement);
            ct.ThrowIfCancellationRequested();
            lock (session.SyncRoot)
            {
                session.Portfolio = replacementPortfolio;
                session.SettlementHistory.Add(settlement);
            }

            if (_store is not null)
                await PersistSessionLedgerAsync(session, ct).ConfigureAwait(false);

            return true;
        }
        finally
        {
            gate.Release();
        }
    }

}
