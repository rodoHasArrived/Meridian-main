using Meridian.Contracts.AssetOperations;

namespace Meridian.Contracts.Accounting.Lots;

/// <summary>Tax character of a short sale as attested by a reviewer; never derived from opening-lot age.</summary>
public enum ShortSaleTaxCharacter
{
    ShortTerm = 0,
    LongTerm = 1
}

/// <summary>
/// Explicit, reviewed short-sale tax character carried on the cover instruction for reporting. Holding
/// period rules for short sales depend on property held outside the lot, so the character is an
/// attestation with its own evidence; absence keeps the existing reporting refusal.
/// </summary>
public sealed record ShortSaleCharacterAttestation(
    ShortSaleTaxCharacter Character,
    string ReviewedBy,
    DateTimeOffset ReviewedAtUtc,
    string Justification,
    IReadOnlyList<RetainedEvidenceIdentityDto> Evidence)
{
    public IReadOnlyList<string> Validate()
    {
        var issues = new List<string>();
        if (!Enum.IsDefined(Character))
            issues.Add("Short-sale tax character must be ShortTerm or LongTerm.");
        if (string.IsNullOrWhiteSpace(ReviewedBy))
            issues.Add("The attesting reviewer is required.");
        if (ReviewedAtUtc == default || ReviewedAtUtc.Offset != TimeSpan.Zero)
            issues.Add("ReviewedAtUtc must be an explicit UTC instant.");
        if (string.IsNullOrWhiteSpace(Justification) || Justification.Trim().Length < 20)
            issues.Add("A substantive justification (at least 20 characters) naming the property and dates relied on is required.");
        if (Evidence is null || Evidence.Count == 0 || Evidence.Any(e => !RetainedEvidenceIdentityValidator.IsComplete(e)))
            issues.Add("Short-sale character requires at least one complete retained evidence identity.");
        return issues;
    }

    public bool IsComplete => Validate().Count == 0;
}

public enum ShortCorrectionKind
{
    /// <summary>Exact inverse of the original short opening or cover journal; restores the lot as it was.</summary>
    Reversal = 0,
    /// <summary>A fresh opening or cover posted after a reversal, with corrected inputs, linked to the reversal.</summary>
    Rebook = 1
}

/// <summary>
/// Immutable short correction receipt: a reversal names the mutation and journal it inverts and the lot
/// state it restores; a rebook names the reversal it follows. Posted journals are never edited.
/// </summary>
public sealed record ShortCorrectionReceipt(
    Guid CorrectionId,
    ShortCorrectionKind Kind,
    Guid CorrectsMutationBatchId,
    Guid CorrectsJournalEntryId,
    OpenLotDto RestoresLot,
    string Reason,
    string Actor,
    DateTimeOffset RecordedAt,
    Guid? FollowsReversalId = null)
{
    public IReadOnlyList<string> Validate()
    {
        var issues = new List<string>();
        if (CorrectionId == Guid.Empty || CorrectsMutationBatchId == Guid.Empty || CorrectsJournalEntryId == Guid.Empty)
            issues.Add("A short correction must name its own identity and the mutation and journal it corrects.");
        if (CorrectionId == CorrectsMutationBatchId)
            issues.Add("A correction cannot correct itself.");
        if (string.IsNullOrWhiteSpace(Reason) || string.IsNullOrWhiteSpace(Actor))
            issues.Add("A reviewed reason and attributed actor are required.");
        if (RecordedAt == default)
            issues.Add("RecordedAt is required.");
        if (RestoresLot is null)
            issues.Add("The restored lot state is required.");
        else
        {
            try
            { OpenLotValidation.Validate(RestoresLot); }
            catch (ArgumentException exception)
            { issues.Add("The restored lot is not a valid canonical open lot: " + exception.Message); }
            if (RestoresLot.Acquisition?.Direction != OpenLotDirection.Short)
                issues.Add("Short corrections restore Short lots only.");
        }
        if (Kind == ShortCorrectionKind.Rebook && (FollowsReversalId is null || FollowsReversalId == Guid.Empty))
            issues.Add("A rebook must follow a retained reversal.");
        if (Kind == ShortCorrectionKind.Reversal && FollowsReversalId is not null)
            issues.Add("A reversal follows no other correction.");
        if (!Enum.IsDefined(Kind))
            issues.Add("Unsupported short correction kind.");
        return issues;
    }
}
