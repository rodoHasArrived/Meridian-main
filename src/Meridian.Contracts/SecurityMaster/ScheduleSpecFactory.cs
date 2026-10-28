using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>What <see cref="ScheduleSpecFactory"/> made of a security's declared schedule conventions.</summary>
public enum DeclaredScheduleStatus
{
    /// <summary>The record declares no conventions this node decodes, so the legacy projection applies unchanged.</summary>
    Undeclared,

    /// <summary>The record declares conventions, and they map to a schedule spec.</summary>
    Declared,

    /// <summary>The record declares conventions, or cannot be read, and no schedule may be generated from it.</summary>
    Refused
}

/// <summary>Why <see cref="ScheduleSpecFactory"/> refused a record's declared schedule conventions.</summary>
public enum DeclaredScheduleRefusalCode
{
    /// <summary>The record is stamped at an asset-class terms version this node does not read.</summary>
    TermsVersionUnreadable,

    /// <summary>The conventions are declared on, or beside, leg-based terms, which declared mode does not follow yet.</summary>
    LegConventionsNotFollowed,

    /// <summary>The contractual issue date that anchors the schedule is not declared by the class or not carried by the record.</summary>
    EffectiveDateMissing,

    /// <summary>The issue date is not before the maturity, or the record carries no maturity.</summary>
    EffectiveDateNotBeforeTermination,

    /// <summary>The payment frequency is missing or cannot drive a schedule (monthly or longer, or at maturity).</summary>
    CadenceUnavailable,

    /// <summary>A schedule anchor read at the payload root disagrees with what the cash-flow terms resolve.</summary>
    TermsAmbiguous,

    /// <summary>A declared member's spelling is not a recognized value.</summary>
    ConventionUnrecognized,

    /// <summary>Declared members conflict with each other, or one would have no effect.</summary>
    ConventionConflict,

    /// <summary>A business-day convention or payment lag is declared without payment calendars.</summary>
    CalendarRequired,

    /// <summary>Payment calendars are declared and no business-day calendar source is registered.</summary>
    CalendarSourceUnavailable,

    /// <summary>A declared payment calendar is not loaded on this node.</summary>
    CalendarUnresolvable,

    /// <summary>The schedule kernel refused the spec the conventions map to.</summary>
    KernelRefused
}

/// <summary>A refusal from <see cref="ScheduleSpecFactory"/>.</summary>
/// <param name="Code">The refusal code.</param>
/// <param name="Message">The operator-facing reason.</param>
public sealed record DeclaredScheduleRefusal(DeclaredScheduleRefusalCode Code, string Message);

/// <summary>The result of <see cref="ScheduleSpecFactory.Resolve"/> or <see cref="ScheduleSpecFactory.ResolveShape"/>.</summary>
/// <param name="Status">Whether the record declares nothing, declares a schedule, or is refused.</param>
/// <param name="Conventions">The decoded root block; <see cref="ScheduleConventionTerms.Empty"/> when nothing was decoded or the conventions sit in legs.</param>
/// <param name="Spec">The schedule spec when <paramref name="Status"/> is Declared; otherwise null.</param>
/// <param name="Refusal">The reason when <paramref name="Status"/> is Refused; otherwise null.</param>
public sealed record DeclaredScheduleResolution(
    DeclaredScheduleStatus Status,
    ScheduleConventionTerms Conventions,
    ScheduleSpec? Spec,
    DeclaredScheduleRefusal? Refusal)
{
    /// <summary>
    /// Whether the record leaves the legacy path: it declares a schedule or is refused. Only an
    /// <see cref="DeclaredScheduleStatus.Undeclared"/> record keeps the legacy projection.
    /// </summary>
    public bool IsDeclared => Status != DeclaredScheduleStatus.Undeclared;
}

/// <summary>
/// Maps a security's declared <c>scheduleConventions</c> block (ADR-025) to a
/// <see cref="ScheduleSpec"/> for the contractual schedule kernel, or says why it cannot. The block is
/// read only where the mapping decodes it (<see cref="SecurityVersionedTerms"/>: the same predicate,
/// the same read guard and the same leg selection), and the schedule anchors are read at the payload
/// root by exact key, as the mapping reads them, so the factory cannot follow conventions the record
/// does not declare.
/// </summary>
/// <remarks>
/// <para>
/// Both entry points are pure, deterministic and total: they read no clock, perform no I/O beyond the
/// calendar source they are handed, and never throw for any terms shape (only for a null argument).
/// They dispatch on <see cref="SecurityAssetClassDescriptor.CashFlowBehavior"/> and on schema facts,
/// never on an asset-class name.
/// </para>
/// <para>
/// Nothing is guessed: there is no fallback to the record's effective-from date, an annual cadence,
/// the common terms' holiday calendar or the weekends-only calendar. The member combinations ADR-025
/// lists as conflicting or without effect are refused: a roll that contradicts endOfMonthRule,
/// accrualAdjusted true without a convention other than Unadjusted, a convention other than
/// Unadjusted with accrualAdjusted absent, or with accrualAdjusted false and a payment lag above 0,
/// and a negative lag. Not every member without effect is refused: a Term tenor ignores the roll.
/// </para>
/// </remarks>
public static class ScheduleSpecFactory
{
    /// <summary>
    /// The version of this mapping from a declared block to a schedule spec. It is
    /// <see cref="SecurityVersionedTerms.DeclaredScheduleMappingVersion"/> itself, so the startup
    /// capability line and the schedule evidence cannot name different mappings.
    /// </summary>
    public const string DeclaredMappingVersion = SecurityVersionedTerms.DeclaredScheduleMappingVersion;

    // The schedule anchors, read at the payload root by exact key where the class schema declares them.
    private const string IssueDateKey = "issueDate";
    private const string MaturityKey = "maturity";
    private const string PaymentFrequencyKey = "paymentFrequency";

    // What each anchor's exact-kind getter reads, as an ambiguity refusal names it.
    private const string DateKind = "date";
    private const string StringKind = "string";

    // The block members named in refusals; the closed vocabularies are checked against the block's schema.
    private const string BusinessDayConventionMember = "businessDayConvention";
    private const string RollConventionMember = "rollConvention";
    private const string DirectionMember = "direction";
    private const string StubPolicyMember = "stubPolicy";
    private const string CouponRateUnitMember = "couponRateUnit";
    private const string CouponCashBasisMember = "couponCashBasis";

    private const string LegConventionsNotFollowedReason =
        "Declared schedule conventions are not yet followed on leg-based projections; the projection is blocked until per-leg declared schedules are supported.";

    private const string CadenceReason =
        "Declared schedule conventions need a paymentFrequency that is monthly or longer, or AtMaturity";

    private const string CalendarSourceUnavailableReason =
        "Declared paymentCalendars cannot be resolved: no business-day calendar source is registered on this node.";

    private static readonly DeclaredScheduleResolution NotDeclared =
        new(DeclaredScheduleStatus.Undeclared, ScheduleConventionTerms.Empty, null, null);

    /// <summary>
    /// Resolves the schedule <paramref name="security"/> declares, with its payment calendars from
    /// <paramref name="calendars"/>. <see cref="DeclaredScheduleStatus.Undeclared"/> means the record
    /// declares nothing this node decodes and its projection stays on the legacy path;
    /// <see cref="DeclaredScheduleStatus.Refused"/> carries the reason the projection is blocked.
    /// </summary>
    /// <param name="security">The security, whose class and stored asset-specific terms are read.</param>
    /// <param name="terms">The cash-flow terms <see cref="StructuredCashFlowTermsResolver"/> resolved for <paramref name="security"/>.</param>
    /// <param name="calendars">The node's business-day calendars, or null when none is registered.</param>
    /// <exception cref="ArgumentNullException"><paramref name="security"/> or <paramref name="terms"/> is null.</exception>
    public static DeclaredScheduleResolution Resolve(
        SecurityDetailDto security,
        StructuredCashFlowTerms terms,
        IBusinessDayCalendarSource? calendars)
    {
        ArgumentNullException.ThrowIfNull(security);
        ArgumentNullException.ThrowIfNull(terms);
        if (!TryReadShape(security, terms, out var shape, out var resolution))
        {
            return resolution;
        }

        var block = shape.Block;

        // 8. Calendar resolution, whenever calendars are declared.
        IBusinessDayCalendar? calendar = null;
        if (block.PaymentCalendars.Count > 0)
        {
            if (calendars is null)
            {
                return Refuse(DeclaredScheduleRefusalCode.CalendarSourceUnavailable, CalendarSourceUnavailableReason, block);
            }

            if (!calendars.TryResolve(block.PaymentCalendars, out calendar))
            {
                return Refuse(DeclaredScheduleRefusalCode.CalendarUnresolvable, DescribeUnresolvable(calendars, block.PaymentCalendars), block);
            }
        }

        // 9. Adjustments. Accrual is adjusted only when stated; payment dates follow the convention on
        // the resolved calendar, or stay unadjusted without one. Effective and termination dates take
        // the accrual adjustment.
        var accrual = block.AccrualAdjusted == true
            ? new BusinessDayAdjustment(shape.Convention.GetValueOrDefault(), calendar)
            : null;
        var payment = calendar is null
            ? BusinessDayAdjustment.Unadjusted
            : new BusinessDayAdjustment(shape.Convention ?? BusinessDayConvention.Unadjusted, calendar);

        // 10. Declared.
        return Declared(
            shape.Spec with
            {
                AccrualAdjustment = accrual,
                PaymentAdjustment = payment,
                PaymentLagBusinessDays = shape.PaymentLagBusinessDays
            },
            block);
    }

    /// <summary>
    /// The calendar-free check of what <paramref name="security"/> declares: every rule of
    /// <see cref="Resolve"/> except calendar resolution, then the schedule kernel over the spec with no
    /// business-day adjustments and no payment lag. A kernel failure is
    /// <see cref="DeclaredScheduleRefusalCode.KernelRefused"/>, so a write rule that calls this refuses
    /// every block a factory rule or a calendar-free kernel failure would block. That is all it
    /// guarantees: every check that runs after the factory can still block a Declared result, among
    /// them calendar availability, the calendar-dependent kernel failures and the declared walk's
    /// refusals (a contractual principal schedule, a missing or unrecognized dayCount, Level on a Term
    /// period, Act/Act ICMA on adjusted accrual dates, Business/252 without a calendar).
    /// A Declared result carries that calendar-free spec.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="security"/> or <paramref name="terms"/> is null.</exception>
    public static DeclaredScheduleResolution ResolveShape(SecurityDetailDto security, StructuredCashFlowTerms terms)
    {
        ArgumentNullException.ThrowIfNull(security);
        ArgumentNullException.ThrowIfNull(terms);
        if (!TryReadShape(security, terms, out var shape, out var resolution))
        {
            return resolution;
        }

        var generated = ScheduleGenerator.Generate(shape.Spec);
        return generated.Succeeded
            ? Declared(shape.Spec, shape.Block)
            : Refuse(DeclaredScheduleRefusalCode.KernelRefused, $"Declared schedule could not be generated: {generated.Failure.Message}", shape.Block);
    }

    // Rules 1 to 7, shared by both entry points. False with the resolution to return (Undeclared or a
    // refusal); true with the calendar-free shape: the spec without adjustments or lag, the parsed
    // convention and the declared lag.
    private static bool TryReadShape(
        SecurityDetailDto security,
        StructuredCashFlowTerms terms,
        out DeclaredShape shape,
        [NotNullWhen(false)] out DeclaredScheduleResolution? resolution)
    {
        shape = default;

        // 1. The descriptor decides where the class declares conventions.
        var descriptor = SecurityAssetClassCatalog.GetOrDefault(security.AssetClass);
        var assetClass = descriptor.AssetClass;
        var stored = security.AssetSpecificTerms;
        switch (descriptor.CashFlowBehavior)
        {
            case SecurityCashFlowBehavior.SwapLegs:
                var legs = SecurityVersionedTerms.ReadDeclaredLegScheduleConventions(assetClass, stored);
                resolution = legs.Status switch
                {
                    SecurityDeclaredConventionsStatus.Unreadable => Unreadable(legs),
                    SecurityDeclaredConventionsStatus.Declared =>
                        Refuse(DeclaredScheduleRefusalCode.LegConventionsNotFollowed, LegConventionsNotFollowedReason, ScheduleConventionTerms.Empty),
                    _ => NotDeclared
                };
                return false;

            case SecurityCashFlowBehavior.ContractualSchedule:
            case SecurityCashFlowBehavior.FactorAmortizing:
                break;

            default:
                resolution = NotDeclared;
                return false;
        }

        var read = SecurityVersionedTerms.ReadDeclaredScheduleConventions(assetClass, stored);
        if (read.Status == SecurityDeclaredConventionsStatus.Unreadable)
        {
            resolution = Unreadable(read);
            return false;
        }

        if (read.Status != SecurityDeclaredConventionsStatus.Declared)
        {
            resolution = NotDeclared;
            return false;
        }

        var block = read.Block;

        // 2. Leg-based terms on a class whose conventions sit at the root are not followed.
        if (terms.HasLegs)
        {
            resolution = Refuse(DeclaredScheduleRefusalCode.LegConventionsNotFollowed, LegConventionsNotFollowedReason, block);
            return false;
        }

        // 3. Anchors, by schema fact, read at the root exactly as the mapping reads them.
        if (!TryReadAnchors(assetClass, SecurityAssetClassTermsUpgrader.UpgradeForRead(assetClass, stored), terms, block, out var anchors, out resolution))
        {
            return false;
        }

        // 4. Spellings.
        if (!TryReadSpellings(assetClass, block, out var spellings, out resolution))
        {
            return false;
        }

        // 5. Conflicts, and members that would have no effect.
        if (FindConflict(block, spellings) is { } conflict)
        {
            resolution = Refuse(DeclaredScheduleRefusalCode.ConventionConflict, conflict, block);
            return false;
        }

        // 6. Defaults.
        var direction = spellings.Direction ?? ScheduleDirection.Forward;
        var roll = anchors.Tenor.Unit == ScheduleTenorUnit.Term
            ? ScheduleRoll.AnchorDay
            : spellings.Roll
              ?? (block.EndOfMonthRule == true
                  ? ScheduleRoll.ForAnchor(
                      direction == ScheduleDirection.Forward
                          ? block.FirstRegularDate ?? anchors.Effective
                          : block.LastRegularDate ?? anchors.Termination,
                      endOfMonthRule: true)
                  : ScheduleRoll.AnchorDay);
        var lag = block.PaymentLagBusinessDays ?? 0;

        // 7. A convention or a lag needs the block's own calendars: the common terms' holiday calendar
        // is never assumed in their place.
        if (block.PaymentCalendars.Count == 0)
        {
            if (spellings.Convention is { } convention && convention != BusinessDayConvention.Unadjusted)
            {
                resolution = Refuse(
                    DeclaredScheduleRefusalCode.CalendarRequired,
                    $"Declared {BusinessDayConventionMember} '{block.BusinessDayConvention}' needs paymentCalendars; neither the common terms' holiday calendar nor weekends only is assumed.",
                    block);
                return false;
            }

            if (lag > 0)
            {
                resolution = Refuse(
                    DeclaredScheduleRefusalCode.CalendarRequired,
                    string.Create(CultureInfo.InvariantCulture, $"Declared paymentLagBusinessDays {lag} needs paymentCalendars; neither the common terms' holiday calendar nor weekends only is assumed."),
                    block);
                return false;
            }
        }

        shape = new DeclaredShape(
            block,
            new ScheduleSpec(
                anchors.Effective,
                anchors.Termination,
                anchors.Tenor,
                direction,
                ScheduleAnchorMode.Anchored,
                roll,
                spellings.StubPolicy ?? ScheduleStubPolicy.Short,
                block.FirstRegularDate,
                block.LastRegularDate,
                MaxPeriods: ScheduleSpec.DefaultMaxPeriods),
            spellings.Convention,
            lag);
        resolution = null;
        return true;
    }

    // Rule 3: the class schema must declare a top-level issueDate (a date) and paymentFrequency; the
    // root values must agree with what the cash-flow terms resolve; the issue date must precede the
    // maturity; and the frequency must map to a tenor.
    private static bool TryReadAnchors(
        string assetClass,
        JsonElement root,
        StructuredCashFlowTerms terms,
        ScheduleConventionTerms block,
        out DeclaredAnchors anchors,
        [NotNullWhen(false)] out DeclaredScheduleResolution? resolution)
    {
        anchors = default;
        resolution = null;
        if (AnchorField(assetClass, IssueDateKey, SecurityAssetTermFieldType.Date) is not { } issueField)
        {
            resolution = Refuse(
                DeclaredScheduleRefusalCode.EffectiveDateMissing,
                $"Declared schedule conventions need the contractual issue date, and '{assetClass}' terms declare no {IssueDateKey}; the record's effective-from date is not used as a schedule anchor.",
                block);
            return false;
        }

        if (SecurityVersionedTerms.ReadOptionalDate(root, issueField.Key) is not { } issueDate)
        {
            // A root issueDate that is present but not a date the anchors read is not absent: it is
            // ambiguous, as the other anchors are, when the cash-flow terms still resolve a date, and
            // is otherwise refused with its value named.
            resolution = (TryGetPresentRootValue(root, issueField.Key, out var raw), terms.IssueDate) switch
            {
                (true, not null) => Refuse(
                    DeclaredScheduleRefusalCode.TermsAmbiguous,
                    DescribeAmbiguity(root, "issue date", issueField.Key, DateKind, rootValue: null, Format(terms.IssueDate)),
                    block),
                (true, null) => Refuse(
                    DeclaredScheduleRefusalCode.EffectiveDateMissing,
                    $"Declared schedule conventions need the contractual issue date; the record's '{issueField.Key}' is {DescribeRootValue(raw)}, which is not a {DateKind} the schedule anchors read, and its effective-from date is not used as a schedule anchor.",
                    block),
                _ => Refuse(
                    DeclaredScheduleRefusalCode.EffectiveDateMissing,
                    $"Declared schedule conventions need the contractual issue date; the record carries no {IssueDateKey}, and its effective-from date is not used as a schedule anchor.",
                    block)
            };
            return false;
        }

        var maturityField = AnchorField(assetClass, MaturityKey, SecurityAssetTermFieldType.Date);
        var maturity = maturityField is null ? null : SecurityVersionedTerms.ReadOptionalDate(root, maturityField.Key);
        var frequencyField = AnchorField(assetClass, PaymentFrequencyKey, SecurityAssetTermFieldType.String);
        var frequency = frequencyField is null ? null : SecurityVersionedTerms.ReadOptionalString(root, frequencyField.Key);

        string? ambiguity = null;
        if (issueDate != terms.IssueDate)
        {
            ambiguity = DescribeAmbiguity(root, "issue date", issueField.Key, DateKind, Format(issueDate), Format(terms.IssueDate));
        }
        else if (maturity != terms.MaturityDate)
        {
            ambiguity = DescribeAmbiguity(root, "maturity", maturityField?.Key ?? MaturityKey, DateKind, Format(maturity), Format(terms.MaturityDate));
        }
        else if (frequencyField is not null && !SameFrequency(frequency, terms.PaymentFrequency))
        {
            ambiguity = DescribeAmbiguity(root, "payment frequency", frequencyField.Key, StringKind, frequency, terms.PaymentFrequency);
        }

        if (ambiguity is not null)
        {
            resolution = Refuse(DeclaredScheduleRefusalCode.TermsAmbiguous, ambiguity, block);
            return false;
        }

        if (maturity is not { } termination)
        {
            resolution = Refuse(
                DeclaredScheduleRefusalCode.EffectiveDateNotBeforeTermination,
                $"Declared schedule conventions need an {IssueDateKey} before the {MaturityKey}; the record carries no {MaturityKey}.",
                block);
            return false;
        }

        if (issueDate >= termination)
        {
            resolution = Refuse(
                DeclaredScheduleRefusalCode.EffectiveDateNotBeforeTermination,
                $"Declared schedule conventions need an {IssueDateKey} before the {MaturityKey}; the record's {IssueDateKey} {Format(issueDate)} is not before its {MaturityKey} {Format(termination)}.",
                block);
            return false;
        }

        if (frequencyField is null)
        {
            resolution = Refuse(DeclaredScheduleRefusalCode.CadenceUnavailable, $"{CadenceReason}, and '{assetClass}' terms declare no {PaymentFrequencyKey}.", block);
            return false;
        }

        if (frequency is null)
        {
            resolution = Refuse(DeclaredScheduleRefusalCode.CadenceUnavailable, $"{CadenceReason}; the record carries none.", block);
            return false;
        }

        // AtMaturity maps to the single Term period; sub-monthly and irregular cadences have no tenor here.
        if (!SecurityTermConventions.TryParsePaymentFrequency(frequency, out var parsed)
            || !SecurityTermConventions.IsSchedulableCoupon(parsed)
            || !ScheduleTenor.TryFromPaymentFrequency(parsed, out var tenor))
        {
            resolution = Refuse(DeclaredScheduleRefusalCode.CadenceUnavailable, $"{CadenceReason}; '{frequency}' is not one.", block);
            return false;
        }

        anchors = new DeclaredAnchors(issueDate, termination, tenor);
        return true;
    }

    // Rule 4: the convention spellings parse through SecurityTermConventions (blank strings included);
    // the closed vocabularies must match the block's schema exactly. Only the schedule members are
    // interpreted here; the coupon members' meaning stays with the engine.
    private static bool TryReadSpellings(
        string assetClass,
        ScheduleConventionTerms block,
        out DeclaredSpellings spellings,
        [NotNullWhen(false)] out DeclaredScheduleResolution? resolution)
    {
        spellings = default;
        resolution = null;

        BusinessDayConvention? convention = null;
        if (block.BusinessDayConvention is { } rawConvention)
        {
            if (!SecurityTermConventions.TryParseBusinessDayConvention(rawConvention, out var parsedConvention))
            {
                resolution = Unrecognized(BusinessDayConventionMember, rawConvention, block);
                return false;
            }

            convention = parsedConvention;
        }

        ScheduleRoll? roll = null;
        if (block.RollConvention is { } rawRoll)
        {
            if (!SecurityTermConventions.TryParseRollConvention(rawRoll, out var parsedRoll))
            {
                resolution = Unrecognized(RollConventionMember, rawRoll, block);
                return false;
            }

            roll = parsedRoll;
        }

        ScheduleDirection? direction = null;
        if (block.Direction is { } rawDirection)
        {
            if (!AllowedByBlockSchema(assetClass, DirectionMember, rawDirection)
                || !Enum.TryParse<ScheduleDirection>(rawDirection, ignoreCase: false, out var parsedDirection)
                || !Enum.IsDefined(parsedDirection))
            {
                resolution = Unrecognized(DirectionMember, rawDirection, block);
                return false;
            }

            direction = parsedDirection;
        }

        ScheduleStubPolicy? stubPolicy = null;
        if (block.StubPolicy is { } rawStub)
        {
            if (!SecurityTermConventions.TryParseStubPolicy(rawStub, out var parsedStub))
            {
                resolution = Unrecognized(StubPolicyMember, rawStub, block);
                return false;
            }

            stubPolicy = parsedStub;
        }

        if (block.CouponRateUnit is { } rawUnit && !AllowedByBlockSchema(assetClass, CouponRateUnitMember, rawUnit))
        {
            resolution = Unrecognized(CouponRateUnitMember, rawUnit, block);
            return false;
        }

        if (block.CouponCashBasis is { } rawBasis && !AllowedByBlockSchema(assetClass, CouponCashBasisMember, rawBasis))
        {
            resolution = Unrecognized(CouponCashBasisMember, rawBasis, block);
            return false;
        }

        spellings = new DeclaredSpellings(convention, roll, direction, stubPolicy);
        return true;
    }

    // Rule 5: members that contradict each other, a convention whose accrual reach is unstated, and a
    // convention a payment lag would silently ignore (the kernel counts a lag from the accrual end and
    // applies no payment convention with it).
    private static string? FindConflict(ScheduleConventionTerms block, DeclaredSpellings spellings)
    {
        if (spellings.Roll is { } roll && block.EndOfMonthRule is { } endOfMonthRule)
        {
            if (endOfMonthRule && roll.Kind != ScheduleRollKind.EndOfMonth)
            {
                return $"Declared {RollConventionMember} '{block.RollConvention}' conflicts with endOfMonthRule true.";
            }

            if (!endOfMonthRule && roll.Kind == ScheduleRollKind.EndOfMonth)
            {
                return $"Declared {RollConventionMember} '{block.RollConvention}' conflicts with endOfMonthRule false.";
            }
        }

        if (block.AccrualAdjusted == true && spellings.Convention is null or BusinessDayConvention.Unadjusted)
        {
            return $"Declared accrualAdjusted true needs a {BusinessDayConventionMember} other than Unadjusted.";
        }

        if (spellings.Convention is { } convention && convention != BusinessDayConvention.Unadjusted)
        {
            if (block.AccrualAdjusted is null)
            {
                return $"Declared {BusinessDayConventionMember} '{block.BusinessDayConvention}' needs accrualAdjusted stated: true adjusts accrual and payment dates, false adjusts payment dates only.";
            }

            if (block.AccrualAdjusted == false && block.PaymentLagBusinessDays is > 0 and var lag)
            {
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"Declared {BusinessDayConventionMember} '{block.BusinessDayConvention}' has no effect: with paymentLagBusinessDays {lag} and accrualAdjusted false, the payment date counts business days from an unadjusted accrual end.");
            }
        }

        if (block.PaymentLagBusinessDays is < 0 and var negativeLag)
        {
            return string.Create(CultureInfo.InvariantCulture, $"Declared paymentLagBusinessDays {negativeLag} must be zero or more.");
        }

        return null;
    }

    // The top-level anchor field a class schema declares under exactly this key and type, or null.
    private static SecurityAssetTermField? AnchorField(string assetClass, string key, SecurityAssetTermFieldType type)
        => SecurityAssetTermsSchema.Field(assetClass, key) is { } field && field.Key == key && field.Type == type
            ? field
            : null;

    // A member of the block's closed vocabulary, checked exactly; a member the schema does not declare fails closed.
    private static bool AllowedByBlockSchema(string assetClass, string member, string raw)
        => SecurityAssetTermsSchema.ElementFields(assetClass, SecurityVersionedTerms.ScheduleConventionsKey)
               .FirstOrDefault(field => field.Key == member) is { } field
           && field.Allows(raw);

    // The resolver skips blank strings, so a blank root frequency agrees only with one it resolves nowhere.
    private static bool SameFrequency(string? root, string? resolved)
        => string.IsNullOrWhiteSpace(root)
            ? resolved is null
            : string.Equals(root, resolved, StringComparison.Ordinal);

    // A root anchor the exact-kind getter could not read is either absent, so the cash-flow terms
    // resolved it from another key or source, or present as a value the getter does not read, which the
    // cash-flow terms may have read from the same key (they read a number or boolean as a string and a
    // timestamp as a date).
    private static string DescribeAmbiguity(JsonElement root, string term, string key, string kind, string? rootValue, string? resolved)
        => (rootValue, resolved) switch
        {
            (null, _) when TryGetPresentRootValue(root, key, out var raw)
                => $"Declared schedule conventions need one unambiguous {term}; the record's '{key}' is {DescribeRootValue(raw)}, which is not a {kind} the schedule anchors read, but the cash-flow terms resolve '{resolved}'.",
            (null, _) => $"Declared schedule conventions need one unambiguous {term}; the record carries no '{key}' but the cash-flow terms resolve '{resolved}' from another key or source.",
            (_, null) => $"Declared schedule conventions need one unambiguous {term}; the record's '{key}' is '{rootValue}' but the cash-flow terms resolve none.",
            _ => $"Declared schedule conventions need one unambiguous {term}; the record's '{key}' is '{rootValue}' but the cash-flow terms resolve '{resolved}' from another key or source."
        };

    // The root value at key when the key is present and not null, which an anchor getter that read
    // nothing failed to read rather than found absent.
    private static bool TryGetPresentRootValue(JsonElement root, string key, out JsonElement value)
    {
        value = default;
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(key, out value)
            && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
    }

    // A root value as an anchor refusal quotes it: a string in quotes, a number or boolean as
    // written, and only the kind of an object or array.
    private static string DescribeRootValue(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => $"'{value.GetString()}'",
            JsonValueKind.Object => "an object",
            JsonValueKind.Array => "an array",
            _ => value.GetRawText()
        };

    // The declared ids the source does not hold, trimmed and de-duplicated ignoring case in declared
    // order; every declared id when each resolves alone and only their joint failed.
    private static string DescribeUnresolvable(IBusinessDayCalendarSource calendars, IReadOnlyList<string> calendarIds)
    {
        var missing = new List<string>();
        foreach (var calendarId in calendarIds)
        {
            var trimmed = calendarId.Trim();
            if (!calendars.TryGet(calendarId, out _) && !missing.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                missing.Add(trimmed);
            }
        }

        var named = missing.Count > 0
            ? missing
            : calendarIds.Select(static id => id.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return $"Declared paymentCalendars name calendars this node has not loaded: {string.Join(", ", named.Select(static id => $"'{id}'"))}; a missing calendar is never replaced by weekends only.";
    }

    private static string? Format(DateOnly? date)
        => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DeclaredScheduleResolution Declared(ScheduleSpec spec, ScheduleConventionTerms block)
        => new(DeclaredScheduleStatus.Declared, block, spec, null);

    private static DeclaredScheduleResolution Refuse(DeclaredScheduleRefusalCode code, string message, ScheduleConventionTerms block)
        => new(DeclaredScheduleStatus.Refused, block, null, new DeclaredScheduleRefusal(code, message));

    private static DeclaredScheduleResolution Unreadable(SecurityDeclaredConventionsRead read)
        => Refuse(
            DeclaredScheduleRefusalCode.TermsVersionUnreadable,
            $"Declared schedule conventions cannot be read: {read.UnreadableReason}",
            ScheduleConventionTerms.Empty);

    private static DeclaredScheduleResolution Unrecognized(string member, string raw, ScheduleConventionTerms block)
        => Refuse(DeclaredScheduleRefusalCode.ConventionUnrecognized, $"Declared {member} '{raw}' is not a recognized value.", block);

    private readonly record struct DeclaredAnchors(DateOnly Effective, DateOnly Termination, ScheduleTenor Tenor);

    private readonly record struct DeclaredSpellings(
        BusinessDayConvention? Convention,
        ScheduleRoll? Roll,
        ScheduleDirection? Direction,
        ScheduleStubPolicy? StubPolicy);

    private readonly record struct DeclaredShape(
        ScheduleConventionTerms Block,
        ScheduleSpec Spec,
        BusinessDayConvention? Convention,
        int PaymentLagBusinessDays);
}
