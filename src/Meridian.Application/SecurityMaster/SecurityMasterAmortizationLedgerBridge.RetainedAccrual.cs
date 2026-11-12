using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using DomainLedger = Meridian.Ledger.Ledger;

namespace Meridian.Application.SecurityMaster;

public sealed partial class SecurityMasterAmortizationLedgerBridge
{
    // Typed holdings are historical evidence, not an alternate reading of LedgerTaxLot.UnitCost.
    // FaceValueLot owns both the source quote basis and the acquisition/booked-factor relationship.
    private static int PostRetainedAccrual(
        StructuredCashFlowProjectionDto projection,
        IReadOnlyList<StructuredCashFlowScheduleEntry> schedule,
        AmortizationLedgerPostingContext context,
        DayCountConvention convention,
        DateOnly maturity,
        Guid securityId,
        string ticker,
        DomainLedger ledger,
        HashSet<Guid> existingIds,
        LedgerAccount carrying,
        string? financialAccountId,
        CancellationToken ct)
    {
        if (context.OpenLots is not null || context.AccrualWindow is not { } window)
            throw new InvalidOperationException("Retained accrual holdings require an accrual window and cannot be combined with raw open lots.");
        if (projection.SourceKind is not (StructuredCashFlowSourceKind.CalculatedBullet or StructuredCashFlowSourceKind.CalculatedSinker)
            || projection.TermsUsed?.HasLegs == true)
            throw new InvalidOperationException("Retained pool accrual requires a calculated fixed-rate schedule.");
        if (projection.TermsUsed?.PrincipalFace is not > 0m)
            throw new InvalidOperationException("Retained accrual requires the source's original principal face.");

        var holdings = context.AccrualHoldings!;
        if (holdings.Any(holding => holding.Lot.SecurityId != securityId || holding.Date > window.End
                || holding.Date < holding.Lot.AcquiredDate || holding.OpenShare is <= 0m or > 1m
                || holding.AccrualFactor is < 0m or > 1m)
            || holdings.GroupBy(holding => (holding.Date, holding.Lot.LotId)).Any(group => group.Count() != 1))
            throw new InvalidOperationException("Retained accrual holdings have invalid dates, identities, open shares or factors.");

        foreach (var row in schedule)
        {
            if (row.InterestSegments is null)
            {
                if (row.InterestAmount != 0m)
                    throw new InvalidOperationException("Interest principal segment evidence is missing; pool accrual is blocked.");
                continue;
            }
            if (row.RowVersion != SecurityCashFlowScheduleRowCodec.CurrentVersion
                || !SecurityCashFlowScheduleRowCodec.TryValidateInterestSegments(row, out _))
                throw new InvalidOperationException("Interest principal segment evidence is inconsistent; pool accrual is blocked.");
        }

        var segments = schedule.SelectMany(row => row.InterestSegments ?? []).ToArray();
        if (segments.Length == 0)
            throw new InvalidOperationException("Retained accrual requires interest principal segment evidence, including zero-coupon intervals.");
        if (segments.Any(segment => segment.Factor != segment.ReferencePrincipal / projection.TermsUsed.PrincipalFace))
            throw new InvalidOperationException("Interest principal segments disagree with the source's original face and factor.");
        var byDay = holdings.ToLookup(holding => holding.Date);
        // Validate the complete history before posting anything, including zero-interest periods.
        foreach (var holding in holdings.Where(holding => holding.Date > segments.Min(segment => segment.PeriodStart)))
        {
            ct.ThrowIfCancellationRequested();
            var applicable = segments.Where(segment => segment.Start < holding.Date && holding.Date <= segment.End).ToArray();
            if (applicable.Length != 1 || applicable[0].Factor != holding.AccrualFactor)
                throw new InvalidOperationException($"Lot '{holding.Lot.LotId}' has no matching retained interest factor on {holding.Date:yyyy-MM-dd}.");
            var segment = applicable[0];
            var closingFactor = segment.End == holding.Date ? segment.FactorAfter : segment.Factor;
            if (closingFactor is null || holding.Lot.CurrentFactor != closingFactor)
                throw new InvalidOperationException($"Lot '{holding.Lot.LotId}' has a closing factor inconsistent with the source on {holding.Date:yyyy-MM-dd}.");
        }

        var coupon = 0m;
        foreach (var row in schedule.Where(row => row.InterestSegments is not null))
        {
            var throughEnd = 0m;
            var beforeWindow = 0m;
            foreach (var segment in row.InterestSegments!)
            {
                if (segment.ReferencePrincipal == 0m)
                    continue;
                var fullMeasure = Measure(segment.End) - Measure(segment.Start);
                if (fullMeasure <= 0m)
                {
                    if (segment.UnroundedInterestAmount != 0m)
                        throw new InvalidOperationException("Interest segment has no positive day-count measure.");
                    continue;
                }
                var last = segment.End < window.End ? segment.End : window.End;
                for (var date = segment.Start.AddDays(1); date <= last; date = date.AddDays(1))
                {
                    ct.ThrowIfCancellationRequested();
                    // Acquisition day earns nothing; disposal day uses the closing open share.
                    // A factor paydown takes effect only after this day's interest.
                    var face = byDay[date].Where(holding => holding.Lot.AcquiredDate < date)
                        .Sum(holding => holding.AccrualFace);
                    var measured = Measure(date) - Measure(date.AddDays(-1));
                    var owned = segment.UnroundedInterestAmount * measured / fullMeasure
                        * face / segment.ReferencePrincipal;
                    throughEnd += owned;
                    if (date < window.Start)
                        beforeWindow += owned;
                }

                decimal Measure(DateOnly date) => DayCountConventions.Fraction(convention, segment.PeriodStart, date);
            }
            // Round cumulative holder targets once per coupon, so window boundaries cannot create
            // cents even when holdings changed before the current window.
            coupon += RoundCash(throughEnd) - RoundCash(beforeWindow);
        }

        var premium = 0m;
        foreach (var date in holdings.Select(holding => holding.Date).Distinct().Where(date => date >= window.Start
            && date <= window.End && (context.AmortizedThrough is null || date > context.AmortizedThrough)))
        {
            var through = 0m;
            var before = 0m;
            foreach (var holding in byDay[date])
            {
                var lot = holding.Lot;
                if (lot.AcquiredDate >= date || lot.AcquiredDate >= maturity)
                    continue;
                var life = DayCountConventions.Fraction(convention, lot.AcquiredDate, maturity);
                if (life <= 0m)
                    continue;
                var signedPremium = holding.AccrualFace * (lot.PricePercentOfPar / lot.ParBasis - 1m);
                through += signedPremium * Weight(date);
                before += signedPremium * Weight(date.AddDays(-1));

                decimal Weight(DateOnly day) => Math.Clamp(DayCountConventions.Fraction(convention,
                    lot.AcquiredDate, day < maturity ? day : maturity) / life, 0m, 1m);
            }
            // Both targets use the same day's retained face. Selling a lot never recognizes its
            // prior lifetime amortization again, and a fully paid-down lot earns its final day.
            premium += RoundCash(through) - RoundCash(before);
        }
        if (coupon < 0m)
            throw new InvalidOperationException("Signed interest is unsupported for retained pool accrual.");
        if (coupon == 0m && premium == 0m)
            return 0;
        return PostAmortizationEntry(ledger, existingIds, securityId, ticker, carrying, financialAccountId,
            window.End, ToUtcStartOfDay(window.End), coupon,
            premium < 0m ? -premium : 0m, premium > 0m ? premium : 0m) ? 1 : 0;
    }
}
