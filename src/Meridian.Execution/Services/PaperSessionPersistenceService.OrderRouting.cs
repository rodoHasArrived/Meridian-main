using Meridian.Execution.Sdk;

namespace Meridian.Execution.Services;

/// <summary>Reads the current base currency of an account authorized by the host.</summary>
public delegate Task<string?> ResolvePaperAccountCurrency(Guid accountId, CancellationToken ct);

public sealed partial class PaperSessionPersistenceService
{
    internal bool SupportsNativeOrderRouting => _store is { SupportsJournalBatches: true } && _financing is null;

    internal bool IsNativeDurableSession(string sessionId) =>
        CurrentSessions.TryGetValue(sessionId, out var session) && session.JournalCurrency is not null;

    internal async Task<string> ReserveNativeOrderAsync(OrderRequest request, OrderSubmissionAuthority authority,
        string? selectedSessionId, string orderId, CancellationToken ct)
    {
        using var operation = EnterOperation(ct);
        ct = operation.Token;
        await InitialiseAsync(ct).ConfigureAwait(false);
        if (request.FundAccountId != authority.AccountGrant?.FundAccountId)
            throw new InvalidOperationException("The paper order must carry authority for the selected account.");
        var accountId = authority.AccountGrant?.FundAccountId.ToString("D") ?? PaperTradingPortfolio.DefaultAccountId;

        var sessionId = selectedSessionId;
        if (IsStrategyRunSessionId(sessionId) && (!request.IntentId.HasValue
            || authority.Origin != OrderCommandOrigin.StrategyRun || authority.RunId is not { } runId
            || sessionId != authority.SessionId || sessionId != GetStrategyRunSessionId(runId)))
            throw new InvalidOperationException("The selected paper session requires authority for its strategy run.");

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            var session = await CreateSessionAsync(new CreatePaperSessionDto("paper-account", "Paper account")
            {
                FinancialAccountId = accountId
            }, isAccountDefault: true, ct).ConfigureAwait(false);
            sessionId = session.SessionId;
        }
        else if (authority.AccountGrant is { } grant)
        {
            await RequireNativeAccountCurrencyAsync(grant.FundAccountId, ct).ConfigureAwait(false);
        }

        var gate = GetSessionGate(sessionId);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!CurrentSessions.TryGetValue(sessionId, out var retained))
                throw new InvalidOperationException("The selected paper session is unavailable for this account.");
            lock (retained.SyncRoot)
            {
                if (!retained.IsActive || retained.JournalCurrency is null
                    || !string.Equals(retained.FinancialAccountId ?? PaperTradingPortfolio.DefaultAccountId,
                        accountId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The selected paper session is unavailable for this account.");
                EnsureDurableEffectCanProceed(retained);
                if (!retained.NativeOrderReservations.Add(orderId))
                    throw new InvalidOperationException("The paper session already has a working order with this identity.");
            }
            return sessionId;
        }
        finally { gate.Release(); }
    }

    private async Task RequireNativeAccountCurrencyAsync(Guid accountId, CancellationToken ct)
    {
        var currency = _resolveAccountCurrency is null
            ? null
            : await _resolveAccountCurrency(accountId, ct).ConfigureAwait(false);
        if (!string.Equals(currency, NativeJournalCurrency, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Native paper sessions require an authorized account with {NativeJournalCurrency} base currency.");
    }
}
