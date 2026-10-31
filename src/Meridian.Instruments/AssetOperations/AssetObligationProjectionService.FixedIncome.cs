using System.Text.Json;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Instruments.AssetOperations;

public sealed partial class AssetObligationProjectionService
{
    private static BondReferenceDto? BuildFixedIncomeReference(SecurityDetailDto security, DateOnly projectionAsOf)
    {
        // Fixed-income cash-flow terms (maturity, issue, par, coupon, day-count, frequency, factor)
        // are resolved once through the shared cash-flow terms resolver instead of re-probing here.
        var terms = StructuredCashFlowTermsResolver.Resolve(security);
        if (terms.MaturityDate is not DateOnly maturity)
        {
            return null;
        }

        var issueDate = terms.IssueDate ?? DateOnly.FromDateTime(security.EffectiveFrom.UtcDateTime.Date);

        // Obligation-specific lifecycle/identity terms the cash-flow resolver does not model.
        var payload = BuildSecurityMasterTermsPayload(security);
        _ = TryReadString(payload, out var issuerName, "issuerName", "issuer", "gpSponsor");

        // The applied factor entry is resolved ONCE and shared: it seeds the principal basis AND
        // bounds the sinking-fund schedule. A dated factor already reflects every principal event
        // up to its as-of, so passing those entries to the projector as well would subtract them a
        // second time (a 0.8 factor reflecting a prior 20 payment must not project 60 at maturity
        // on a 100 face). An undated scalar factor is assumed current, so completed payments before
        // the projection as-of are likewise excluded; with no factor at all, completed payments
        // still reduce the opening balance (via a synthesized full-face factor) and only current or
        // future instalments project.
        var appliedFactorEntry = ResolveAppliedFactorEntry(terms, projectionAsOf);
        // A scalar currentFactor with a retained factorDate is DATED evidence: it reflects
        // principal only through that date, exactly like a schedule entry, so treating it as
        // current would leave a January balance standing across February's completed payment.
        // Only a genuinely undated scalar is assumed current (reflects everything before today).
        // The date resolves NESTED-FIRST (governed profileFields before the envelope root),
        // matching StructuredCashFlowTermsResolver: for profile-backed records the governed
        // nested factorDate is authoritative, and a contradictory outer pass-through date must
        // not shift which completed payments the factor is deemed to reflect.
        DateOnly? scalarFactorDate = SecurityTermReader.ReadDate(
            EnumerateNestedFirstTermSources(payload), "factorDate", "currentFactorDate");
        // A FUTURE-dated scalar does not describe today's balance at all: a 0.8 factor effective
        // next month must not reduce today's face by 20% (understating projected interest and
        // maturity principal on ledger-support outputs). It is excluded here and below — the
        // latest eligible schedule point or the unfactored balance governs instead.
        var scalarFactorApplies = terms.CurrentFactor is not null
            && (scalarFactorDate is not DateOnly scalarAsOf || scalarAsOf <= projectionAsOf);
        DateOnly? factorReflectsThrough = appliedFactorEntry?.AsOfDate
            ?? (scalarFactorApplies
                ? (scalarFactorDate ?? projectionAsOf.AddDays(-1))
                : null);

        // Contractual payments before the projection as-of have already OCCURRED — the run is as of
        // today, so projecting them again would report a completed instalment as newly due (and
        // open a MissingEvidence variance against it). Completed payments the factor does not
        // already reflect (all of them when no factor exists, those after its as-of otherwise) are
        // excluded from the projected schedule below and reduce the opening principal instead.
        // Contractual schedules also need a RESOLVABLE principal basis here: without par/face the
        // 100-unit default in ResolveBondPrincipalBasis would cap real instalments, so basis-less
        // records do not attach a sinking fund at all.
        var hasPrincipalBasis = terms.PrincipalFace is > 0m;
        var completedPostFactorPrincipal = 0m;
        if (terms.HasPrincipalSchedule && hasPrincipalBasis)
        {
            completedPostFactorPrincipal = terms.PrincipalSchedule!
                .Where(entry => entry.PaymentDate >= issueDate
                    && entry.PaymentDate <= maturity
                    && entry.PaymentDate < projectionAsOf
                    && (factorReflectsThrough is not DateOnly reflectedCutoff
                        || entry.PaymentDate > reflectedCutoff))
                .Sum(static entry => entry.Amount);
        }

        return new BondReferenceDto(
            security.SecurityId,
            security.DisplayName,
            security.Currency,
            issuerName,
            null,
            ResolvePrimaryIdentifier(security.Identifiers) ?? security.SecurityId.ToString("D"),
            new BondLifecycleDto(
                security.SecurityId,
                security.Status == SecurityStatusDto.Active ? BondLifecycleStat.Active : BondLifecycleStat.Retired,
                issueDate,
                TryReadDate(payload, out var callDate, "callDate") ? callDate : null,
                maturity,
                TryReadDate(payload, out _, "callDate"),
                security.Version,
                terms.PrincipalFace,
                PaymentFrequency: terms.PaymentFrequency),
            NormalizeCouponRatePercent(security.AssetClass, terms.CouponRate) is not { } couponRatePercent
                ? null
                : new BondAccrualConventionDto(
                    security.SecurityId,
                    terms.DayCountConvention,
                    null,
                    null,
                    "Fixed",
                    couponRatePercent,
                    null,
                    null,
                    security.Version),
            security.Version,
            SinkingFund: terms.HasPrincipalSchedule && hasPrincipalBasis
                ? new BondSinkingFundDto(
                    security.SecurityId,
                    terms.PrincipalSchedule!
                        .Where(entry => entry.PaymentDate >= issueDate
                            && entry.PaymentDate <= maturity
                            && entry.PaymentDate >= projectionAsOf
                            && (factorReflectsThrough is not DateOnly reflectedThrough
                                || entry.PaymentDate > reflectedThrough))
                        .Select(static entry => new BondSinkingFundEntryDto(entry.PaymentDate, entry.Amount))
                        .ToArray(),
                    SinkFrequency: null,
                    IsProRata: false,
                    Version: security.Version)
                : null,
            PrincipalFactor: BuildFactorReference(security, terms, payload, appliedFactorEntry, completedPostFactorPrincipal, scalarFactorApplies));
    }

    /// <summary>
    /// The typed factor entry in effect on <paramref name="projectionAsOf"/> — the latest schedule
    /// point dated on or before that day — or <see langword="null"/> when no dated point applies.
    /// </summary>
    private static StructuredFactorScheduleEntry? ResolveAppliedFactorEntry(
        StructuredCashFlowTerms terms, DateOnly projectionAsOf)
    {
        for (var i = terms.FactorSchedule.Count - 1; i >= 0; i--)
        {
            if (terms.FactorSchedule[i].AsOfDate <= projectionAsOf)
            {
                return terms.FactorSchedule[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the pool/index factor for the reference from the typed factor schedule first — the
    /// applied entry resolved by <see cref="ResolveAppliedFactorEntry"/> — falling back to the
    /// scalar <c>currentFactor</c>. Without this, a record whose newest factor lives only in
    /// <c>factorScheduleEntries</c> reaches <c>ResolveBondPrincipalBasis</c> with no factor at all
    /// and is projected at full (factor 1) or stale principal.
    /// </summary>
    private static BondPrincipalFactorDto? BuildFactorReference(
        SecurityDetailDto security,
        StructuredCashFlowTerms terms,
        JsonElement payload,
        StructuredFactorScheduleEntry? scheduled,
        decimal completedPostFactorPrincipal,
        bool scalarFactorApplies)
    {
        var effectiveFactor = scheduled?.Factor ?? (scalarFactorApplies ? terms.CurrentFactor : null);
        if (effectiveFactor is null
            && completedPostFactorPrincipal > 0m
            && terms.PrincipalFace is decimal fullFace
            && fullFace > 0m)
        {
            // No factor asserts the balance, but completed contractual payments have still
            // occurred: seed the full-face factor so the reduction below absorbs them — otherwise
            // a factor-less sinker projects completed instalments as newly due from full face.
            effectiveFactor = 1m;
        }

        if (effectiveFactor is null)
        {
            return null;
        }

        // The applied factor reflects principal only THROUGH its as-of date (full face when no
        // factor exists). Contractual payments completed before the projection as-of and not yet
        // inside the factor are excluded from the projected schedule, so the opening principal
        // must absorb them here — otherwise the projection starts from a balance those payments
        // already reduced.
        if (completedPostFactorPrincipal > 0m
            && terms.PrincipalFace is decimal face
            && face > 0m)
        {
            effectiveFactor = Math.Max(0m, effectiveFactor.Value - (completedPostFactorPrincipal / face));
        }

        // The retained factor date must be the SAME governed nested-first date the opening-balance
        // calculation resolved: re-reading outer-first here would stamp Asset Operations lineage
        // with a pass-through date inconsistent with the factor actually applied.
        var effectiveFactorDate = scheduled?.AsOfDate
            ?? SecurityTermReader.ReadDate(
                EnumerateNestedFirstTermSources(payload), "factorDate", "currentFactorDate");
        return new BondPrincipalFactorDto(effectiveFactor, effectiveFactorDate,
            JsonSerializer.SerializeToElement(terms.FactorSchedule));
    }

}
