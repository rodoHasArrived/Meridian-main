using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;

namespace Meridian.Contracts.Ledger;

/// <summary>Operator selection only. Rates, day counts, principal and amounts are always server-resolved.</summary>
public sealed record IntercompanyAccrualRequestDto(Guid SourceBookId, Guid InterestArrangementId,
    Guid PrincipalArrangementId, DateOnly AccrualFrom, DateOnly AccrualTo);

/// <summary>The digest proves the operator is drafting exactly the computation they previewed.</summary>
public sealed record CreateIntercompanyAccrualDraftRequest(IntercompanyAccrualRequestDto Accrual, string ExpectedEvidenceDigest);

/// <summary>A posted principal journal dated inside the accrual window, signed toward the outstanding balance.</summary>
public sealed record IntercompanyAccrualPrincipalMovementDto(Guid JournalEntryId, DateOnly EffectiveDate,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal SignedAmount);

/// <summary>Principal is read from posted journals of the reviewed loan profile; it is never typed in.</summary>
public sealed record IntercompanyAccrualPrincipalEvidenceDto(Guid PrincipalArrangementId, IntercompanyAccountRoleDto Role,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal OpeningPrincipal,
    int OpeningJournalCount, string OpeningJournalDigest, IReadOnlyList<IntercompanyAccrualPrincipalMovementDto> Movements);

/// <summary>
/// Everything that determined one accrual amount: the legal version and profile revision of every slice, the
/// principal journals, the calendars and kernel, and any governed FX conversion. Retained on the draft and
/// carried onto the posted journal; a client resave cannot replace it.
/// </summary>
public sealed record IntercompanyAccrualEvidenceDto(string RuleVersion, Guid DraftId, string TenantId,
    Guid SourceBookId, Guid SourceEntityId, Guid CounterpartyEntityId,
    Guid AccountingArrangementId, Guid PostingArrangementRevisionId,
    Guid AgreementId, int PostingAgreementVersion, string PostingAgreementPayloadHash,
    string TransactionId, DateOnly PostingDate, IntercompanyAccountRoleDto SourceRole, string FunctionalCurrency,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal FunctionalAmount,
    IntercompanyAccrualPreviewDto Computation, IntercompanyAccrualPrincipalEvidenceDto Principal,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ConsolidationFxAmountDto? Fx = null);

/// <summary>Preview and draft share one shape so the operator reviews exactly what would be retained.</summary>
public sealed record IntercompanyAccrualProposalDto(IntercompanyAccrualPreviewDto Computation,
    IReadOnlyList<string> Diagnostics,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IntercompanyAccrualEvidenceDto? Evidence = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? EvidenceDigest = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ManualJournalEntryDraftDto? Draft = null)
{
    [JsonIgnore] public bool IsReady => Computation.IsReady && Evidence is not null && Draft is not null;
}

/// <summary>Retained accrual provenance stays bound to the financial lines through review and posting.</summary>
public static class IntercompanyAccrualEvidenceGuard
{
    public const string EvidenceTag = "intercompany.accrualEvidence.v1";
    public const string DigestTag = "intercompany.accrualEvidence.sha256";
    public const string TransactionPrefix = "loan-interest:";

    public static string TransactionId(DateOnly accrualFrom, DateOnly accrualTo)
        => $"{TransactionPrefix}{accrualFrom:yyyyMMdd}-{accrualTo:yyyyMMdd}";

    /// <summary>Recovers the half-open accrual window a retained transaction identity declares.</summary>
    public static bool TryParseWindow(string? transactionId, out DateOnly accrualFrom, out DateOnly accrualTo)
    {
        accrualFrom = accrualTo = default;
        if (transactionId is null || !transactionId.StartsWith(TransactionPrefix, StringComparison.Ordinal))
            return false;
        var parts = transactionId[TransactionPrefix.Length..].Split('-');
        return parts.Length == 2
            && DateOnly.TryParseExact(parts[0], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out accrualFrom)
            && DateOnly.TryParseExact(parts[1], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out accrualTo)
            && accrualFrom < accrualTo;
    }

    public static string Serialize(IntercompanyAccrualEvidenceDto evidence)
        => JsonSerializer.Serialize(evidence, IntercompanyAccrualJsonContext.Default.IntercompanyAccrualEvidenceDto);
    public static string Digest(string evidenceJson) => Sha256Digest.ComputeUtf8(evidenceJson);

    public static bool IsAccrualDraft(ManualJournalEntryDraftDto draft)
        => draft.IntercompanyAccrualEvidenceJson is not null || draft.IntercompanyAccrualEvidenceDigest is not null;

    /// <summary>Null when the draft still states exactly the retained computation; otherwise the refusal.</summary>
    public static string? Validate(ManualJournalEntryDraftDto draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (string.IsNullOrWhiteSpace(draft.IntercompanyAccrualEvidenceJson)
            || !Sha256Digest.IsCanonical(draft.IntercompanyAccrualEvidenceDigest)
            || !Sha256Digest.FixedEquals(draft.IntercompanyAccrualEvidenceDigest, Digest(draft.IntercompanyAccrualEvidenceJson)))
            return "Accrual provenance is missing or differs from the retained server computation.";
        IntercompanyAccrualEvidenceDto? evidence;
        try
        { evidence = JsonSerializer.Deserialize(draft.IntercompanyAccrualEvidenceJson, IntercompanyAccrualJsonContext.Default.IntercompanyAccrualEvidenceDto); }
        catch (JsonException) { return "Accrual provenance is invalid."; }
        if (evidence is null || evidence.RuleVersion != IntercompanyAccrualRules.RuleVersion
            || evidence.Computation is not { IsReady: true, Amount: { } amount } || evidence.Principal is null)
            return "Accrual provenance uses an unsupported calculation rule.";
        // A reversal or rebook of a posted accrual is a different journal; its inverse is proved by the
        // correction workflow against the immutable original, not by restating the original computation.
        if (evidence.DraftId != draft.JournalEntryId)
            return null;
        if (evidence.SourceBookId != draft.LedgerBookId || evidence.PostingDate != draft.AccountingDate
            || !string.Equals(evidence.FunctionalCurrency, draft.Currency, StringComparison.Ordinal)
            || !string.Equals(evidence.TenantId, draft.TenantId, StringComparison.Ordinal))
            return "The accrual draft's book, date, currency or tenant differs from the retained computation.";
        if (draft.Lines.Count != 2 || draft.Lines[0].Side == draft.Lines[1].Side)
            return "An accrual draft carries exactly the computed debit and credit; edit the source facts and recompute instead.";
        foreach (var line in draft.Lines)
        {
            var dimensions = line.Dimensions;
            var original = line.TransactionCurrency;
            if (line.Amount != evidence.FunctionalAmount || !string.Equals(line.Currency, evidence.FunctionalCurrency, StringComparison.Ordinal)
                || (original?.Amount ?? line.Amount) != amount
                || !string.Equals(original?.Currency ?? line.Currency, evidence.Computation.Currency, StringComparison.Ordinal)
                || dimensions is null || dimensions.IntercompanyAgreementId != evidence.AgreementId
                || dimensions.IntercompanyAgreementVersion != evidence.PostingAgreementVersion
                || dimensions.EntityId != evidence.SourceEntityId.ToString("D")
                || dimensions.CounterpartyId != evidence.CounterpartyEntityId.ToString("D")
                || (dimensions.AccountingArrangementId is { } arrangement
                    && (arrangement != evidence.AccountingArrangementId.ToString("D")
                        || dimensions.AccountingArrangementRevisionId != evidence.PostingArrangementRevisionId.ToString("D")
                        || dimensions.IntercompanyTransactionId != evidence.TransactionId)))
                return "Accrual lines differ from the retained amount, parties, agreement version or profile revision.";
        }
        return draft.Lines.Any(line => line.Dimensions?.AccountingArrangementId is not null) ? null
            : "An accrual draft requires its reviewed loan-interest profile attribution.";
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(IntercompanyAccrualRequestDto))]
[JsonSerializable(typeof(CreateIntercompanyAccrualDraftRequest))]
[JsonSerializable(typeof(IntercompanyAccrualEvidenceDto))]
[JsonSerializable(typeof(IntercompanyAccrualProposalDto))]
[JsonSerializable(typeof(IntercompanyAccrualPreviewDto))]
public partial class IntercompanyAccrualJsonContext : JsonSerializerContext;
