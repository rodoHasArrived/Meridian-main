namespace Meridian.Contracts.Workstation;

/// <summary>
/// The signed-in operator's visits to one workstation surface, such as the reconciliation break
/// queue (C3). <see cref="PreviousVisitAt"/> is the baseline for "since your last visit" markers.
/// Recording a visit returns the visit it replaced and the time it recorded. Reading returns the
/// last recorded visit as <see cref="PreviousVisitAt"/> and leaves <see cref="VisitedAt"/> null,
/// because a read records nothing.
/// </summary>
public sealed record OperatorSurfaceVisitDto(
    string Surface,
    DateTimeOffset? PreviousVisitAt,
    DateTimeOffset? VisitedAt);
