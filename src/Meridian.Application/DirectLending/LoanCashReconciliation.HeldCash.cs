using System.Text.Json;
using Meridian.Contracts.DirectLending;

namespace Meridian.Application.DirectLending;

public static partial class LoanCashReconciliation
{
    private static string? ReassignHeldCash(List<Payment> candidates, Guid loanId, DateOnly asOf,
        DateTimeOffset observedAt, IReadOnlyList<LoanEventLineageDto> history, CurrencyCode? loanCurrency)
    {
        var events = history.DistinctBy(static item => item.EventId).OrderBy(static item => item.AggregateVersion).ToArray();
        var movements = events.Where(item => item.EventType is "loan.unapplied-cash-applied" or "loan.unapplied-cash-refunded")
            .Where(item => (item.EffectiveDate is null || item.EffectiveDate <= asOf) && item.RecordedAt <= observedAt).ToArray();
        if (movements.Length == 0)
            return null;

        var versions = events.ToDictionary(static item => item.EventId, static item => item.AggregateVersion);
        var pools = candidates.Where(static payment => payment.IsUnappliedCash)
            .Select(payment => new HeldCash(payment, payment.Amount,
                payment.SourceEventId == payment.Cash.SourceEventId && versions.TryGetValue(payment.Cash.SourceEventId, out var version) ? version : null))
            .OrderBy(static pool => pool.Version).ThenBy(static pool => pool.Payment.Cash.CashTransactionId)
            .ThenBy(static pool => pool.Payment.AllocationId).ToArray();
        var applied = new List<Payment>();
        foreach (var movement in movements)
        {
            if (loanCurrency is null || movement.EventSchemaVersion != 1
                || !TryReadHeldCashMovement(movement, loanId, out var components))
                return Unavailable(movement, "Missing or invalid retained application, currency or breakdown evidence.");

            // A later receipt, another currency or an unlinked allocation cannot fund this movement.
            var eligible = pools.Where(pool => pool.Version < movement.AggregateVersion
                && pool.Payment.Cash.Currency == loanCurrency
                && pool.Payment.Cash.SettlementDate <= movement.EffectiveDate).ToArray();
            if (eligible.Sum(static pool => pool.Remaining) < components.Sum(static component => component.Amount))
                return Unavailable(movement, "The retained movement exceeds its eligible original unapplied-cash funding.");

            foreach (var (target, amount) in components.Where(static component => component.Amount > 0m))
            {
                var needed = amount;
                var funding = new List<Funding>();
                foreach (var pool in eligible)
                {
                    var take = Math.Min(needed, pool.Remaining);
                    if (take <= 0m)
                        continue;
                    pool.Remaining -= take;
                    needed -= take;
                    funding.Add(new(pool.Payment, take, movement.EventId, movement.EffectiveDate!.Value));
                    if (needed == 0m)
                        break;
                }
                applied.Add(new(funding[0].Payment.Cash, amount, target,
                    funding.Count == 1 ? funding[0].Payment.AllocationId : null)
                {
                    ApplicationDate = movement.EffectiveDate,
                    FundingSources = funding
                });
            }
        }

        candidates.RemoveAll(static payment => payment.IsUnappliedCash);
        candidates.AddRange(pools.Where(static pool => pool.Remaining > 0m)
            .Select(static pool => pool.Payment with { Amount = pool.Remaining }));
        candidates.AddRange(applied);
        return null;

        string Unavailable(LoanEventLineageDto movement, string reason)
        {
            // Withhold the whole held-cash reconstruction when its history is incomplete; an
            // uncertain consumed balance must not reappear as an available or explained receipt.
            candidates.RemoveAll(static payment => payment.IsUnappliedCash);
            return $"Unapplied-cash movement {movement.EventId:D} is unavailable. {reason}";
        }
    }

    private static bool TryReadHeldCashMovement(LoanEventLineageDto movement, Guid loanId,
        out (string Target, decimal Amount)[] components)
    {
        components = [];
        try
        {
            using var document = JsonDocument.Parse(movement.PayloadJson);
            var root = document.RootElement;
            if (root.GetProperty("loanId").GetGuid() != loanId || movement.EffectiveDate is not { } effective
                || root.GetProperty("effectiveDate").GetString() != effective.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))
                return false;
            var requested = root.GetProperty("amount").GetDecimal();
            if (requested <= 0m)
                return false;
            if (movement.EventType == "loan.unapplied-cash-refunded")
            {
                components = [("RetainedExcess", requested)];
                return true;
            }
            var breakdown = root.GetProperty("breakdown");
            components =
            [
                ("Interest", breakdown.GetProperty("toInterest").GetDecimal()),
                ("CommitmentFee", breakdown.GetProperty("toCommitmentFee").GetDecimal()),
                ("Fees", breakdown.GetProperty("toFees").GetDecimal()),
                ("Penalty", breakdown.GetProperty("toPenalty").GetDecimal()),
                ("Principal", breakdown.GetProperty("toPrincipal").GetDecimal())
            ];
            var applied = root.GetProperty("appliedAmount").GetDecimal();
            return components.All(static component => component.Amount >= 0m)
                && applied <= requested && components.Sum(static component => component.Amount) == applied;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            return false;
        }
    }

    private sealed class HeldCash(Payment payment, decimal remaining, long? version)
    {
        public Payment Payment { get; } = payment;
        public decimal Remaining { get; set; } = remaining;
        public long? Version { get; } = version;
    }
}
