using Meridian.Contracts.Ledger;
using Meridian.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Read-only verification tooling over every hash-chained accounting and ledger audit log the
/// composition retains (W9-GOV-008 criterion three). Shared by the browser endpoint and available
/// to the desktop shell so neither client derives its own integrity answer.
/// </summary>
/// <remarks>
/// <para>Nothing is appended and no chain is repaired: a break is reported with its classification
/// and position so an operator can investigate, not hidden by a retry.</para>
/// <para>Each chain is reported independently. A chain the composition does not keep is
/// <see cref="AuditChainIntegrityStatus.NotConfigured"/> rather than verified, and a store that
/// cannot be read is <see cref="AuditChainIntegrityStatus.Unavailable"/> rather than broken, so the
/// report never claims integrity it did not check nor tampering it did not find.</para>
/// </remarks>
public sealed class AccountingAuditIntegrityService(
    IAccountingActionAuditStore? accountingAuditStore,
    ILedgerEventAuditVerifier? ledgerEventAuditVerifier,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<AccountingAuditIntegrityReportDto> VerifyAsync(CancellationToken ct = default)
    {
        var chains = new[]
        {
            await VerifyAccountingActionsAsync(ct).ConfigureAwait(false),
            await VerifyLedgerEventsAsync(ct).ConfigureAwait(false)
        };

        var configured = chains
            .Where(chain => chain.Status != AuditChainIntegrityStatus.NotConfigured)
            .ToArray();

        return new AccountingAuditIntegrityReportDto(
            _timeProvider.GetUtcNow(),
            configured.Length > 0 && configured.All(chain => chain.IsVerified),
            chains);
    }

    private async Task<AuditChainIntegrityResultDto> VerifyAccountingActionsAsync(CancellationToken ct)
    {
        const string chain = AuditChainIdentifiers.AccountingActions;
        if (accountingAuditStore is null)
        {
            return new(chain, AuditChainIntegrityStatus.NotConfigured, 0, 0,
                Detail: "No accounting audit store is composed.");
        }

        if (accountingAuditStore is not IAccountingAuditChainVerifier verifier)
        {
            return new(chain, AuditChainIntegrityStatus.NotConfigured, 0, 0,
                Detail: "The composed accounting audit store keeps no hash chain.");
        }

        try
        {
            var verification = await verifier.VerifyAuditChainAsync(ct).ConfigureAwait(false);
            if (verification.IsValid)
            {
                return new(chain, AuditChainIntegrityStatus.Verified, verification.LinksChecked, verification.PreChainEventCount);
            }

            // A chain written under hashing rules this build does not implement could not be
            // checked at all -- the expected state during a rolling upgrade or downgrade -- so it is
            // reported as unverified, never as evidence of tampering.
            var status = verification.Status == AccountingAuditChainStatus.UnsupportedSchemaVersion
                ? AuditChainIntegrityStatus.Unavailable
                : AuditChainIntegrityStatus.Broken;
            return new(chain,
                status,
                verification.LinksChecked,
                verification.PreChainEventCount,
                verification.Status.ToString(),
                verification.Detail,
                verification.FailedSequence);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Unavailable(chain, ex);
        }
    }

    private async Task<AuditChainIntegrityResultDto> VerifyLedgerEventsAsync(CancellationToken ct)
    {
        const string chain = AuditChainIdentifiers.LedgerEvents;
        if (ledgerEventAuditVerifier is null)
        {
            return new(chain, AuditChainIntegrityStatus.NotConfigured, 0, 0,
                Detail: "No durable ledger journal store is composed; the ledger-event chain is kept by the PostgreSQL ledger.");
        }

        try
        {
            var verification = await ledgerEventAuditVerifier.VerifyLedgerEventAuditAsync(ct).ConfigureAwait(false);
            return new(chain, AuditChainIntegrityStatus.Verified, verification.ChainedEvents, verification.UnprotectedGenesisFacts);
        }
        catch (LedgerValidationException ex)
        {
            // The PostgreSQL store raises its integrity failures as ledger validation errors whose
            // message names the break (it carries no ledger content) and whose data records how far
            // the scan got and where it stopped, when the failure site knows.
            return new(chain,
                AuditChainIntegrityStatus.Broken,
                ReadLong(ex, LedgerEventAuditFailureData.ChainedEventsVerified) ?? 0,
                0,
                "IntegrityFailure",
                ex.Message,
                ReadLong(ex, LedgerEventAuditFailureData.FailedSequence));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Unavailable(chain, ex);
        }
    }

    private static long? ReadLong(Exception ex, string key)
        => ex.Data.Contains(key) && ex.Data[key] is long value ? value : null;

    // The exception message is deliberately not echoed: a connection failure can carry host and
    // credential fragments, and the type is enough to route the operator to the store's logs.
    private static AuditChainIntegrityResultDto Unavailable(string chain, Exception ex)
        => new(chain, AuditChainIntegrityStatus.Unavailable, 0, 0, ex.GetType().Name,
            "The audit store could not be read; its chain was not verified.");
}
