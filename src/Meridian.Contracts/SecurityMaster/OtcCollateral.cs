using System.Text.Json.Serialization;
using Meridian.Contracts.Operations;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Which way collateral moved: posted by the fund to the counterparty, or received by the fund from it.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OtcCollateralDirection>))]
public enum OtcCollateralDirection
{
    /// <summary>Posted by the fund; the counterparty holds it.</summary>
    Posted,

    /// <summary>Received from the counterparty; the fund holds it.</summary>
    Received
}

/// <summary>What the collateral is.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OtcCollateralKind>))]
public enum OtcCollateralKind
{
    Cash,
    Security
}

/// <summary>Whether a movement adds collateral to what the Secured Party holds or gives some back.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OtcMovementKind>))]
public enum OtcMovementKind
{
    Delivery,
    Return
}

/// <summary>The party that transfers collateral in a movement.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OtcMovementParty>))]
public enum OtcMovementParty
{
    Fund,
    Counterparty
}

/// <summary>Where a margin call is in its life. Settled and cancelled calls are terminal.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OtcMarginCallStatus>))]
public enum OtcMarginCallStatus
{
    Issued,
    Agreed,
    Disputed,
    Settled,
    Cancelled
}

/// <summary>The lifecycle actions an operator takes on a margin call.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OtcMarginCallAction>))]
public enum OtcMarginCallAction
{
    Agree,
    Dispute,
    Settle,
    Cancel
}

/// <summary>
/// Collateral posted or received under a collateral agreement: one eligible-collateral line,
/// its quantity and unit value, the valued amount they multiply to, the haircut copied from the
/// agreement when the balance was written and the date the valuation is good for. A zero quantity
/// is a fully returned line. A snapshot applies the line's haircut as the agreement states it at
/// computation time, so the copy here records what applied when the balance was written.
/// </summary>
public sealed record OtcCollateralBalanceDto(
    Guid BalanceId,
    Guid CollateralAgreementId,
    OtcCollateralDirection Direction,
    OtcCollateralKind Kind,
    string EligibleCollateralDescription,
    Guid? SecurityId,
    string Currency,
    decimal Quantity,
    decimal UnitValue,
    decimal ValuedAmount,
    decimal HaircutPercent,
    DateOnly ValuedAsOf,
    string? Reference,
    string? Notes,
    long Version,
    string UpdatedBy,
    DateTimeOffset UpdatedAt);

/// <summary>Records (<see cref="BalanceId"/> null) or amends a collateral balance. The haircut and the valued amount are never supplied.</summary>
public sealed record UpsertOtcCollateralBalanceRequest(
    Guid? BalanceId,
    Guid CollateralAgreementId,
    OtcCollateralDirection Direction,
    OtcCollateralKind Kind,
    string EligibleCollateralDescription,
    Guid? SecurityId,
    string Currency,
    decimal Quantity,
    decimal UnitValue,
    DateOnly ValuedAsOf,
    string? Reference = null,
    string? Notes = null,
    string? UpdatedBy = null);

/// <summary>
/// A margin call: one movement from an exposure snapshot, taken through its life. The amount is
/// copied from the snapshot's movement, never typed.
/// </summary>
public sealed record OtcMarginCallDto(
    Guid CallId,
    Guid NettingSetId,
    Guid CollateralAgreementId,
    Guid SnapshotId,
    OtcMovementKind Kind,
    OtcMovementParty Mover,
    string Currency,
    decimal AmountCalled,
    decimal? AmountAgreed,
    OtcMarginCallStatus Status,
    DateOnly IssuedOn,
    DateOnly? DueOn,
    string? DisputeReason,
    DateOnly? SettledOn,
    decimal? SettledAmount,
    string? Notes,
    long Version,
    string UpdatedBy,
    DateTimeOffset UpdatedAt);

/// <summary>Issues the call for one movement of a snapshot.</summary>
public sealed record IssueOtcMarginCallRequest(
    Guid SnapshotId,
    OtcMovementKind Kind,
    OtcMovementParty Mover,
    DateOnly IssuedOn,
    DateOnly? DueOn = null,
    string? Notes = null,
    string? UpdatedBy = null);

/// <summary>Moves a call through its life. <see cref="Amount"/> is the agreed or settled amount; <see cref="Reason"/> explains a dispute or a cancellation.</summary>
public sealed record TransitionOtcMarginCallRequest(
    Guid CallId,
    OtcMarginCallAction Action,
    decimal? Amount = null,
    string? Reason = null,
    DateOnly? SettledOn = null,
    string? Notes = null,
    string? UpdatedBy = null);

/// <summary>Persistence for exposure snapshots, collateral balances and margin calls.</summary>
public interface IOtcCollateralStore
{
    /// <summary>Appends the snapshot as the next version for its netting set and date, assigning the version and the digest.</summary>
    Task<OtcExposureSnapshotDto> AppendExposureSnapshotAsync(OtcExposureSnapshotDto snapshot, CancellationToken ct = default);
    Task<OtcExposureSnapshotDto?> GetExposureSnapshotAsync(Guid snapshotId, CancellationToken ct = default);
    Task<IReadOnlyList<OtcExposureSnapshotDto>> ListExposureSnapshotsAsync(Guid nettingSetId, DateOnly? asOfDate = null, bool latestVersionOnly = true, CancellationToken ct = default);

    Task<OtcCollateralBalanceDto> UpsertCollateralBalanceAsync(UpsertOtcCollateralBalanceRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<OtcCollateralBalanceDto>> ListCollateralBalancesAsync(Guid collateralAgreementId, CancellationToken ct = default);

    /// <summary>
    /// The netting sets that reference a collateral agreement, in id order. More than one means the
    /// agreement's balances would be credited to each, which a computation refuses to do.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListNettingSetsUnderCollateralAgreementAsync(Guid collateralAgreementId, CancellationToken ct = default);

    Task<OtcMarginCallDto> IssueMarginCallAsync(IssueOtcMarginCallRequest request, CancellationToken ct = default);
    Task<OtcMarginCallDto> TransitionMarginCallAsync(TransitionOtcMarginCallRequest request, CancellationToken ct = default);
    Task<OtcMarginCallDto?> GetMarginCallAsync(Guid callId, CancellationToken ct = default);
    Task<IReadOnlyList<OtcMarginCallDto>> ListMarginCallsAsync(Guid nettingSetId, CancellationToken ct = default);
}

/// <summary>
/// The rules every collateral balance write satisfies, pure so they are testable without a
/// database and identical in every store. Each returns the normalized request or throws
/// <see cref="ArgumentException"/> naming the rule.
/// </summary>
public static class OtcCollateralRules
{
    public static UpsertOtcCollateralBalanceRequest Normalize(UpsertOtcCollateralBalanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CollateralAgreementId == Guid.Empty)
            throw new ArgumentException("A collateral balance sits under a collateral agreement.", nameof(request));
        if (request.BalanceId == Guid.Empty)
            throw new ArgumentException("A collateral balance to amend must be identified.", nameof(request));
        if (!Enum.IsDefined(request.Direction) || !Enum.IsDefined(request.Kind))
            throw new ArgumentException("A collateral balance states its direction and kind.", nameof(request));
        var description = NormalizeOptional(request.EligibleCollateralDescription)
            ?? throw new ArgumentException("A collateral balance names the eligible collateral it is posted as.", nameof(request));
        var amending = request.BalanceId is not null;
        if (request.Quantity < 0m || (!amending && request.Quantity == 0m))
            throw new ArgumentException(amending
                ? "An amended quantity cannot be negative; zero records a fully returned balance."
                : "A recorded collateral quantity must be positive.", nameof(request));
        if (request.UnitValue <= 0m)
            throw new ArgumentException("A collateral unit value must be positive.", nameof(request));
        if (request.ValuedAsOf == default)
            throw new ArgumentException("A collateral balance states the date its valuation is good for.", nameof(request));
        if (request.SecurityId == Guid.Empty)
            throw new ArgumentException("A collateral security, when named, must be identified.", nameof(request));
        if (request.Kind == OtcCollateralKind.Cash)
        {
            if (request.UnitValue != 1m)
                throw new ArgumentException("Cash collateral is valued at one unit of its currency per unit.", nameof(request));
            if (request.SecurityId is not null)
                throw new ArgumentException("Cash collateral does not name a security.", nameof(request));
        }

        return request with
        {
            EligibleCollateralDescription = description,
            Currency = OtcContractRules.RequireCurrency(request.Currency),
            Reference = NormalizeOptional(request.Reference),
            Notes = NormalizeOptional(request.Notes),
            UpdatedBy = RequireActor(request.UpdatedBy)
        };
    }

    /// <summary>The collateral agreement's eligible-collateral line a balance is posted as, matched by description regardless of case.</summary>
    public static OtcEligibleCollateralDto ResolveEligible(OtcCollateralAgreementDto agreement, string description)
    {
        ArgumentNullException.ThrowIfNull(agreement);
        var wanted = NormalizeOptional(description) ?? throw new ArgumentException("A collateral balance names the eligible collateral it is posted as.", nameof(description));
        return FindEligible(agreement, wanted)
            ?? throw new ArgumentException($"'{wanted}' is not eligible under this collateral agreement.", nameof(description));
    }

    /// <summary>The agreement's eligible-collateral line with this description regardless of case, or null when there is none.</summary>
    public static OtcEligibleCollateralDto? FindEligible(OtcCollateralAgreementDto agreement, string? description)
    {
        ArgumentNullException.ThrowIfNull(agreement);
        var wanted = NormalizeOptional(description);
        return wanted is null
            ? null
            : agreement.EligibleCollateral.FirstOrDefault(line => string.Equals(line.Description, wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The valued amount of a balance: quantity times unit value, computed, never supplied.</summary>
    public static decimal ValuedAmount(decimal quantity, decimal unitValue) => quantity * unitValue;

    /// <summary>Collateral and margin writes are operator actions, so the actor is required rather than defaulted.</summary>
    public static string RequireActor(string? value)
        => NormalizeOptional(value) ?? throw new ArgumentException("A collateral or margin write names the operator who made it.");
}

/// <summary>
/// The margin call lifecycle as pure functions. A call is issued from a complete, real snapshot
/// whose movement has not already been called (a cancelled call aside); the store also requires
/// the snapshot to be the latest version for its date and computed under the netting set's current
/// master agreement version and current, unshared collateral agreement version. It copies the
/// movement's amount and then moves Issued → Agreed | Disputed | Cancelled,
/// Disputed → Agreed | Cancelled, Agreed → Settled | Cancelled, and Settled and Cancelled are
/// terminal. Settlement does not move a balance; the operator records the resulting balance.
/// </summary>
public static class OtcMarginCallRules
{
    /// <summary>Whether a call still awaits agreement or settlement.</summary>
    public static bool IsOpen(OtcMarginCallStatus status)
        => status is OtcMarginCallStatus.Issued or OtcMarginCallStatus.Agreed or OtcMarginCallStatus.Disputed;

    /// <summary>
    /// The call a request issues from a snapshot, given the netting set's existing calls: at least
    /// every call still open and every call already issued from this snapshot.
    /// </summary>
    public static OtcMarginCallDto Issue(OtcExposureSnapshotDto snapshot, IssueOtcMarginCallRequest request, IEnumerable<OtcMarginCallDto> existingCalls, DateTimeOffset issuedAt)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(existingCalls);
        var calls = existingCalls.ToArray();
        var actor = OtcCollateralRules.RequireActor(request.UpdatedBy);
        if (request.SnapshotId != snapshot.SnapshotId)
            throw new ArgumentException("The call names a different snapshot than the one supplied.", nameof(request));
        if (!Enum.IsDefined(request.Kind) || !Enum.IsDefined(request.Mover))
            throw new ArgumentException("A margin call names the movement kind and the party that moves collateral.", nameof(request));
        if (snapshot.Status != OtcExposureSnapshotStatus.Complete)
            throw new ArgumentException($"Snapshot {snapshot.SnapshotId:D} is {snapshot.Status}; a call is issued only from a complete snapshot.", nameof(request));
        if (snapshot.Provenance.IsNonReal())
            throw new ArgumentException($"Snapshot {snapshot.SnapshotId:D} is {snapshot.Provenance.Token()} data; a call is issued only from real valuations.", nameof(request));
        if (snapshot.CollateralAgreementId is not { } collateralAgreementId)
            throw new ArgumentException($"Snapshot {snapshot.SnapshotId:D} has no collateral agreement to call under.", nameof(request));
        var movement = snapshot.Movements.FirstOrDefault(candidate => candidate.Kind == request.Kind && candidate.Mover == request.Mover)
            ?? throw new ArgumentException($"Snapshot {snapshot.SnapshotId:D} computes no {request.Kind} by the {request.Mover}.", nameof(request));
        if (request.IssuedOn < snapshot.AsOfDate)
            throw new ArgumentException("A call cannot be issued before the date its snapshot values.", nameof(request));
        if (request.DueOn is { } dueOn && dueOn < request.IssuedOn)
            throw new ArgumentException("A call cannot fall due before it is issued.", nameof(request));
        // One open call per obligation: a mover's delivery and its return are distinct movements (both
        // arise when an independent amount is in play), so each may be open at once; a second call for
        // the same kind and mover is a duplicate.
        if (calls.Any(call => call.NettingSetId == snapshot.NettingSetId && call.Mover == request.Mover && call.Kind == request.Kind && IsOpen(call.Status)))
            throw new ArgumentException($"Netting set {snapshot.NettingSetId:D} already has an open {request.Kind} call for the {request.Mover}; settle or cancel it first.", nameof(request));
        // A snapshot's movement is one obligation: once called it is not called again from the same
        // snapshot unless that call was cancelled. A settled movement is a new obligation only after a
        // recompute, so a retry cannot raise a duplicate collateral demand.
        if (calls.FirstOrDefault(call => call.SnapshotId == snapshot.SnapshotId && call.Mover == request.Mover && call.Kind == request.Kind && call.Status != OtcMarginCallStatus.Cancelled) is { } prior)
            throw new ArgumentException($"Snapshot {snapshot.SnapshotId:D}'s {request.Kind} by the {request.Mover} was already called (call {prior.CallId:D}, {prior.Status}); recompute the snapshot to call it again.", nameof(request));

        return new OtcMarginCallDto(
            Guid.NewGuid(),
            snapshot.NettingSetId,
            collateralAgreementId,
            snapshot.SnapshotId,
            movement.Kind,
            movement.Mover,
            movement.Currency,
            movement.Amount,
            null,
            OtcMarginCallStatus.Issued,
            request.IssuedOn,
            request.DueOn,
            null,
            null,
            null,
            NormalizeOptional(request.Notes),
            1,
            actor,
            issuedAt);
    }

    /// <summary>The call after one lifecycle action, with its version bumped.</summary>
    public static OtcMarginCallDto Transition(OtcMarginCallDto current, TransitionOtcMarginCallRequest request, DateTimeOffset transitionedAt)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(request);
        var actor = OtcCollateralRules.RequireActor(request.UpdatedBy);
        if (request.CallId != current.CallId)
            throw new ArgumentException("The transition names a different call than the one supplied.", nameof(request));
        if (!IsOpen(current.Status))
            throw new ArgumentException($"Call {current.CallId:D} is {current.Status} and can no longer change.", nameof(request));
        var notes = NormalizeOptional(request.Notes) ?? current.Notes;

        OtcMarginCallDto next;
        switch (request.Action)
        {
            case OtcMarginCallAction.Agree:
                RequireFrom(current, request.Action, OtcMarginCallStatus.Issued, OtcMarginCallStatus.Disputed);
                var agreed = request.Amount ?? current.AmountCalled;
                if (agreed <= 0m)
                    throw new ArgumentException("An agreed amount must be positive.", nameof(request));
                next = current with { Status = OtcMarginCallStatus.Agreed, AmountAgreed = agreed };
                break;

            case OtcMarginCallAction.Dispute:
                RequireFrom(current, request.Action, OtcMarginCallStatus.Issued);
                var disputeReason = NormalizeOptional(request.Reason) ?? throw new ArgumentException("A dispute states its reason.", nameof(request));
                next = current with { Status = OtcMarginCallStatus.Disputed, DisputeReason = disputeReason };
                break;

            case OtcMarginCallAction.Settle:
                RequireFrom(current, request.Action, OtcMarginCallStatus.Agreed);
                var settledOn = request.SettledOn ?? throw new ArgumentException("A settlement states the date it settled.", nameof(request));
                if (settledOn < current.IssuedOn)
                    throw new ArgumentException("A call cannot settle before it was issued.", nameof(request));
                var settledAmount = request.Amount ?? current.AmountAgreed
                    ?? throw new ArgumentException("A settlement states the amount settled.", nameof(request));
                if (settledAmount <= 0m)
                    throw new ArgumentException("A settled amount must be positive.", nameof(request));
                next = current with { Status = OtcMarginCallStatus.Settled, SettledOn = settledOn, SettledAmount = settledAmount };
                break;

            case OtcMarginCallAction.Cancel:
                RequireFrom(current, request.Action, OtcMarginCallStatus.Issued, OtcMarginCallStatus.Disputed, OtcMarginCallStatus.Agreed);
                var cancelReason = NormalizeOptional(request.Reason) ?? throw new ArgumentException("A cancellation states its reason.", nameof(request));
                next = current with { Status = OtcMarginCallStatus.Cancelled, Notes = notes is null ? $"Cancelled: {cancelReason}" : $"{notes}; Cancelled: {cancelReason}" };
                notes = next.Notes;
                break;

            default:
                throw new ArgumentException($"'{request.Action}' is not a margin call action.", nameof(request));
        }

        return next with { Notes = notes, Version = current.Version + 1, UpdatedBy = actor, UpdatedAt = transitionedAt };
    }

    /// <summary>The governance event type a transition records.</summary>
    public static string EventType(OtcMarginCallAction action) => action switch
    {
        OtcMarginCallAction.Agree => "MarginCallAgreed",
        OtcMarginCallAction.Dispute => "MarginCallDisputed",
        OtcMarginCallAction.Settle => "MarginCallSettled",
        OtcMarginCallAction.Cancel => "MarginCallCancelled",
        _ => throw new ArgumentException($"'{action}' is not a margin call action.", nameof(action))
    };

    private static void RequireFrom(OtcMarginCallDto current, OtcMarginCallAction action, params OtcMarginCallStatus[] allowed)
    {
        if (!allowed.Contains(current.Status))
            throw new ArgumentException($"Call {current.CallId:D} is {current.Status}; it cannot be {Describe(action)} from there.", nameof(current));
    }

    private static string Describe(OtcMarginCallAction action) => action switch
    {
        OtcMarginCallAction.Agree => "agreed",
        OtcMarginCallAction.Dispute => "disputed",
        OtcMarginCallAction.Settle => "settled",
        OtcMarginCallAction.Cancel => "cancelled",
        _ => action.ToString()
    };
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(OtcExposureSnapshotDto))]
public sealed partial class OtcCollateralJsonContext : JsonSerializerContext;
