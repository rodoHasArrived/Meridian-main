namespace Meridian.Contracts.SecurityMaster;

/// <summary>The ownership of a schedule after selecting its authoritative source.</summary>
public enum SecuritySchedulePresence
{
    Absent,
    Empty,
    Populated,
    Invalid
}

/// <summary>The three dated economic schedules shared by cash-flow and relational readers.</summary>
public enum SecurityEconomicScheduleKind
{
    Factor,
    Principal,
    StepCoupon
}

/// <summary>A stable refusal code and the actual source/member path, including the original row index.</summary>
public sealed record SecurityScheduleDiagnostic(string Code, string Path, string Message);

/// <summary>
/// A complete typed schedule or diagnostics. Invalid schedules expose no partial entries, and
/// cannot be confused with an absent value or an explicitly empty authoritative array.
/// </summary>
public sealed record SecurityScheduleParseResult<T>(
    SecuritySchedulePresence Presence,
    IReadOnlyList<T> Entries,
    IReadOnlyList<SecurityScheduleDiagnostic> Diagnostics,
    string? SourcePath = null)
{
    public bool IsValid => Presence != SecuritySchedulePresence.Invalid;
    public bool IsPresent => Presence != SecuritySchedulePresence.Absent;

    /// <summary>
    /// Valid typed rows in their original order, before principal aggregation. Codecs use this
    /// view to retain historical row structure; calculations use <see cref="Entries"/>.
    /// Invalid input exposes neither view.
    /// </summary>
    public IReadOnlyList<T> SourceEntries { get; init; } = Entries;
}

/// <summary>The shared result used by schema admission, cash-flow resolution and relational projection.</summary>
public sealed record SecurityEconomicScheduleValidation(
    SecurityScheduleParseResult<StructuredFactorScheduleEntry> FactorSchedule,
    SecurityScheduleParseResult<StructuredPrincipalScheduleEntry> PrincipalSchedule,
    SecurityScheduleParseResult<StructuredStepCouponEntry> StepCouponSchedule)
{
    public bool IsValid => FactorSchedule.IsValid && PrincipalSchedule.IsValid && StepCouponSchedule.IsValid;

    public IReadOnlyList<SecurityScheduleDiagnostic> Diagnostics
        => FactorSchedule.Diagnostics.Concat(PrincipalSchedule.Diagnostics)
            .Concat(StepCouponSchedule.Diagnostics).Distinct().ToArray();

    /// <summary>Refuses invalid economics before any partial calculation or projection can be published.</summary>
    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new SecurityScheduleValidationException(Diagnostics);
        }
    }
}

/// <summary>
/// An invalid retained or submitted economic schedule. Readers must surface the diagnostic rather
/// than substitute an empty schedule, a lower-priority source or a stale relational projection.
/// </summary>
public sealed class SecurityScheduleValidationException : InvalidOperationException
{
    public SecurityScheduleValidationException(IReadOnlyList<SecurityScheduleDiagnostic> diagnostics)
        : base("Invalid economic schedule: " + string.Join("; ", diagnostics.Select(
            static diagnostic => $"{diagnostic.Path}: {diagnostic.Message} ({diagnostic.Code})")))
    {
        Diagnostics = diagnostics.ToArray();
    }

    public IReadOnlyList<SecurityScheduleDiagnostic> Diagnostics { get; }
}
