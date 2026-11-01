using System.Text.Json.Serialization;

namespace Meridian.Contracts.CashFlows;

[JsonConverter(typeof(JsonStringEnumConverter<ExerciseNoticeApprovalState>))]
public enum ExerciseNoticeApprovalState
{
    Recorded = 0,
    Approved = 1,
    Rejected = 2
}

/// <summary>
/// Governed evidence that an issuer call or holder put was actually noticed: the source document's
/// hash, when it was received, and the exercise economics it states. Recording retains the claim;
/// a separate approver makes it usable. Actual redemption posting is a different treatment; this record
/// only lets a scenario carry a retained notice instead of a hypothetical one.
/// </summary>
public sealed record BondExerciseNoticeDto(
    Guid NoticeId,
    Guid SecurityId,
    string StreamId,
    [property: JsonConverter(typeof(JsonStringEnumConverter<CashFlowExerciseSide>))] CashFlowExerciseSide Side,
    DateOnly NoticeDate,
    DateOnly ExerciseDate,
    decimal Price,
    decimal? Amount,
    string SourceDocumentSha256,
    DateOnly ReceivedDate,
    string SourceReference,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc,
    ExerciseNoticeApprovalState ApprovalState = ExerciseNoticeApprovalState.Recorded,
    string? DecidedBy = null,
    DateTimeOffset? DecidedAtUtc = null,
    string? DecisionReason = null)
{
    public IReadOnlyList<string> Validate()
    {
        var issues = new List<string>();
        if (NoticeId == Guid.Empty || SecurityId == Guid.Empty) issues.Add("A notice needs its own identity and the security it concerns.");
        if (string.IsNullOrWhiteSpace(StreamId)) issues.Add("The exercised stream is required.");
        if (!Enum.IsDefined(Side)) issues.Add("The exercise side must be Call or Put.");
        if (NoticeDate > ExerciseDate) issues.Add("A notice cannot follow the exercise date it announces.");
        if (ReceivedDate < NoticeDate) issues.Add("A notice cannot be received before its notice date.");
        if (Price <= 0m) issues.Add("The noticed redemption price must be positive.");
        if (Amount is <= 0m) issues.Add("A noticed amount must be positive when stated.");
        if (SourceDocumentSha256 is not { Length: 64 } || !SourceDocumentSha256.All(Uri.IsHexDigit)) issues.Add("The source document SHA-256 is required.");
        if (string.IsNullOrWhiteSpace(SourceReference)) issues.Add("The source document reference is required.");
        if (string.IsNullOrWhiteSpace(RecordedBy)) issues.Add("The recording actor is required.");
        if (RecordedAtUtc == default || RecordedAtUtc.Offset != TimeSpan.Zero) issues.Add("RecordedAtUtc must be an explicit UTC instant.");
        if (ApprovalState != ExerciseNoticeApprovalState.Recorded && (string.IsNullOrWhiteSpace(DecidedBy) || DecidedAtUtc is null)) issues.Add("A decided notice names its approver and decision instant.");
        if (ApprovalState != ExerciseNoticeApprovalState.Recorded && string.Equals(DecidedBy, RecordedBy, StringComparison.OrdinalIgnoreCase)) issues.Add("The recording actor cannot decide their own notice.");
        if (!Enum.IsDefined(ApprovalState)) issues.Add("Unsupported approval state.");
        return issues;
    }
}

public sealed record RecordBondExerciseNoticeRequest(
    Guid NoticeId,
    string StreamId,
    [property: JsonConverter(typeof(JsonStringEnumConverter<CashFlowExerciseSide>))] CashFlowExerciseSide Side,
    DateOnly NoticeDate,
    DateOnly ExerciseDate,
    decimal Price,
    decimal? Amount,
    string SourceDocumentSha256,
    DateOnly ReceivedDate,
    string SourceReference);

public sealed record ExerciseNoticeDecisionRequest(bool Approve, string? Reason = null);

public sealed record ExerciseNoticeVerification(bool Verified, string Reason, BondExerciseNoticeDto? Notice = null);

/// <summary>Source verification a scenario needs before a selected exercise may carry a retained (non-hypothetical) notice.</summary>
public interface IExerciseNoticeVerifier
{
    Task<ExerciseNoticeVerification> VerifyAsync(Guid securityId, SelectedCashFlowExercise selection, CancellationToken ct = default);
}

public interface IBondExerciseNoticeStore
{
    /// <summary>Write-once; an identical retry returns the retained notice, a different payload under the same identity is refused.</summary>
    Task<BondExerciseNoticeDto> RecordAsync(BondExerciseNoticeDto notice, CancellationToken ct = default);
    Task<BondExerciseNoticeDto?> GetAsync(Guid securityId, Guid noticeId, CancellationToken ct = default);
    Task<IReadOnlyList<BondExerciseNoticeDto>> ListAsync(Guid securityId, CancellationToken ct = default);
    /// <summary>Approves or rejects a recorded notice once; the deciding actor must differ from the recorder.</summary>
    Task<BondExerciseNoticeDto> DecideAsync(Guid securityId, Guid noticeId, bool approve, string actor, string? reason, CancellationToken ct = default);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(BondExerciseNoticeDto))]
[JsonSerializable(typeof(BondExerciseNoticeDto[]))]
public sealed partial class BondExerciseNoticeJsonContext : JsonSerializerContext;
