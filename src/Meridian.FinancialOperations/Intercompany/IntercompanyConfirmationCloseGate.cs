using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Intercompany;

/// <summary>The confirmation position of one book at a date, as the balance confirmation service reports it.</summary>
public interface IIntercompanyBalanceConfirmationSummarySource
{
    Task<IntercompanyBalanceConfirmationSummaryDto> SummaryAsync(string tenant, string? company, Guid ledgerBookId,
        DateOnly asOf, CancellationToken ct = default);
}

/// <summary>
/// Opt-in close blocking from reciprocal balance confirmation (ADR-032). With no active policy for the scope's book it
/// returns null and close readiness is unchanged. With one, it evaluates confirmation at the selected period end and
/// fails closed: a missing period, an unreadable confirmation or a conflicting policy history is <c>Unavailable</c>.
/// </summary>
/// <remarks>
/// The policy store is always required. When the confirmation or ledger services are not composed, an active policy
/// reports <c>Unavailable</c> instead of silently dropping its control.
/// </remarks>
public sealed class IntercompanyConfirmationCloseGate(IIntercompanyConfirmationClosePolicyStore policies,
    IIntercompanyBalanceConfirmationSummarySource? confirmations, ILedgerJournalStore? journals) : IIntercompanyConfirmationCloseGate
{
    public async Task<IntercompanyConfirmationCloseGateResultDto?> EvaluateAsync(CloseReadinessScopeDto scope,
        string? tenantId, string? companyId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.LedgerBookId is not { } bookId || string.IsNullOrWhiteSpace(tenantId))
            return null;
        var active = (await policies.ListAsync(tenantId, bookId, ct).ConfigureAwait(false))
            .Where(item => item.Status == IntercompanyConfirmationClosePolicyStatusDto.Active
                && (item.CompanyId is null || string.Equals(item.CompanyId, companyId, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (active.Length == 0)
            return null;
        if (active.Length > 1)
            return new("Unavailable", active.Select(item => item.PolicyId.ToString("D")).ToArray(),
                "More than one confirmation close policy is active for this book. Disable all but one.", active[0].PolicyId, active[0].Version);
        var policy = active[0];
        IntercompanyConfirmationCloseGateResultDto Result((string Status, IReadOnlyList<string> Records, string Message) value)
            => new(value.Status, value.Records, value.Message, policy.PolicyId, policy.Version);

        if (confirmations is null || journals is null)
            return Result(("Unavailable", [], $"Close policy {policy.PolicyId:D} v{policy.Version} is active, but confirmation status cannot be read on this host."));
        if (!Guid.TryParse(scope.PeriodId, out var periodId))
            return Result(("Unavailable", [], $"Close policy {policy.PolicyId:D} v{policy.Version} evaluates confirmation at the selected period end; select a ledger period."));
        try
        {
            var period = await journals.GetPeriodAsync(periodId, ct).ConfigureAwait(false);
            if (period is null || period.LedgerBookId != bookId)
                return Result(("Unavailable", [], $"Close policy {policy.PolicyId:D} v{policy.Version} cannot resolve the selected period for this book."));
            var summary = await confirmations.SummaryAsync(tenantId, companyId, bookId, period.EndDate, ct).ConfigureAwait(false);
            return Result(IntercompanyConfirmationClosePolicyRules.Evaluate(policy, summary.Findings, period.EndDate));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            return Result(("Unavailable", [], $"Close policy {policy.PolicyId:D} v{policy.Version}: confirmation status could not be read ({error.Message})."));
        }
    }
}
