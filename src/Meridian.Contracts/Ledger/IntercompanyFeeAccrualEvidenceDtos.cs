using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;

namespace Meridian.Contracts.Ledger;

/// <summary>Operator selection only. The fee, basis, schedule and amount are always server-resolved.</summary>
public sealed record IntercompanyFeeAccrualRequestDto(Guid SourceBookId, Guid FeeArrangementId, DateOnly AccrualFrom, DateOnly AccrualTo);

/// <summary>The digest proves the operator is drafting exactly the computation they previewed.</summary>
public sealed record CreateIntercompanyFeeAccrualDraftRequest(IntercompanyFeeAccrualRequestDto Accrual, string ExpectedEvidenceDigest);

/// <summary>
/// Everything that determined one fee amount: the legal version and profile revision of every slice, the schedule
/// periods and calendar, the direction the agreement's declared roles give, and any governed FX conversion.
/// Retained on the draft in the same accrual-evidence fields as interest, under rule <c>intercompany-fee-v1</c>.
/// </summary>
public sealed record IntercompanyFeeAccrualEvidenceDto(string RuleVersion, Guid DraftId, string TenantId,
    Guid SourceBookId, Guid SourceEntityId, Guid CounterpartyEntityId,
    Guid AccountingArrangementId, Guid PostingArrangementRevisionId,
    Guid AgreementId, int PostingAgreementVersion, string PostingAgreementPayloadHash,
    string TransactionId, DateOnly PostingDate, IntercompanyAccountRoleDto SourceRole,
    string MonetaryAccountNodeId, string OperatingAccountNodeId, string FunctionalCurrency,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal FunctionalAmount,
    IntercompanyFeeAccrualPreviewDto Computation,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ConsolidationFxAmountDto? Fx = null);

/// <summary>Preview and draft share one shape so the operator reviews exactly what would be retained.</summary>
public sealed record IntercompanyFeeAccrualProposalDto(IntercompanyFeeAccrualPreviewDto Computation,
    IReadOnlyList<string> Diagnostics,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IntercompanyFeeAccrualEvidenceDto? Evidence = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? EvidenceDigest = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ManualJournalEntryDraftDto? Draft = null)
{
    [JsonIgnore] public bool IsReady => Computation.IsReady && Evidence is not null && Draft is not null;
}

/// <summary>Retained fee provenance stays bound to the financial lines through review and posting.</summary>
public static class IntercompanyFeeAccrualEvidenceGuard
{
    public const string TransactionPrefix = "management-fee:";

    public static string TransactionId(DateOnly accrualFrom, DateOnly accrualTo, int generation = 0)
        => TransactionPrefix + FormattableString.Invariant($"{accrualFrom:yyyyMMdd}-{accrualTo:yyyyMMdd}")
            + (generation == 0 ? string.Empty : FormattableString.Invariant($"#g{generation}"));

    /// <summary>Recovers the half-open fee window a retained transaction identity declares.</summary>
    public static bool TryParseWindow(string? transactionId, out DateOnly accrualFrom, out DateOnly accrualTo)
    {
        accrualFrom = accrualTo = default;
        if (transactionId is null || !transactionId.StartsWith(TransactionPrefix, StringComparison.Ordinal))
            return false;
        var parts = transactionId[TransactionPrefix.Length..].Split('#')[0].Split('-');
        return parts.Length == 2
            && DateOnly.TryParseExact(parts[0], "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out accrualFrom)
            && DateOnly.TryParseExact(parts[1], "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out accrualTo)
            && accrualFrom < accrualTo;
    }

    /// <summary>True when retained accrual evidence was produced by the fee rule rather than an interest rule.</summary>
    public static bool IsFeeEvidence(string? evidenceJson)
    {
        if (string.IsNullOrWhiteSpace(evidenceJson))
            return false;
        try
        {
            using var document = JsonDocument.Parse(evidenceJson);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("ruleVersion", out var rule)
                && rule.ValueKind == JsonValueKind.String && rule.GetString() == IntercompanyFeeAccrualRules.RuleVersion;
        }
        catch (JsonException) { return false; }
    }

    public static IntercompanyFeeAccrualEvidenceDto? Deserialize(string evidenceJson)
        => JsonSerializer.Deserialize(evidenceJson, IntercompanyFeeAccrualJsonContext.Default.IntercompanyFeeAccrualEvidenceDto);
    public static string Serialize(IntercompanyFeeAccrualEvidenceDto evidence)
        => JsonSerializer.Serialize(evidence, IntercompanyFeeAccrualJsonContext.Default.IntercompanyFeeAccrualEvidenceDto);

    /// <summary>Null when the draft still states exactly the retained fee computation; otherwise the refusal.</summary>
    public static string? Validate(ManualJournalEntryDraftDto draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (string.IsNullOrWhiteSpace(draft.IntercompanyAccrualEvidenceJson)
            || !Sha256Digest.IsCanonical(draft.IntercompanyAccrualEvidenceDigest)
            || !Sha256Digest.FixedEquals(draft.IntercompanyAccrualEvidenceDigest, IntercompanyAccrualEvidenceGuard.Digest(draft.IntercompanyAccrualEvidenceJson)))
            return "Fee provenance is missing or differs from the retained server computation.";
        IntercompanyFeeAccrualEvidenceDto? evidence;
        try
        { evidence = Deserialize(draft.IntercompanyAccrualEvidenceJson); }
        catch (JsonException) { return "Fee provenance is invalid."; }
        if (evidence is null || evidence.RuleVersion != IntercompanyFeeAccrualRules.RuleVersion
            || evidence.Computation is not { IsReady: true, Amount: { } amount } || evidence.Computation.RuleVersion != evidence.RuleVersion)
            return "Fee provenance uses an unsupported calculation rule.";
        // A reversal or rebook clones the provenance under a new journal; a rebook must restate it exactly.
        if (evidence.DraftId != draft.JournalEntryId)
            return draft.RebookedFromJournalEntryId is not null && !NetsToZeroPerAccount(draft)
                ? "A fee rebook restates the posted fee exactly (its inverse plus an identical replacement); to change the amount, reverse it and recompute from the source facts."
                : null;
        if (evidence.SourceBookId != draft.LedgerBookId || evidence.PostingDate != draft.AccountingDate
            || !string.Equals(evidence.FunctionalCurrency, draft.Currency, StringComparison.Ordinal)
            || !string.Equals(evidence.TenantId, draft.TenantId, StringComparison.Ordinal))
            return "The fee draft's book, date, currency or tenant differs from the retained computation.";
        if (draft.Lines.Count != 2 || draft.Lines[0].Side == draft.Lines[1].Side)
            return "A fee draft carries exactly the computed debit and credit; edit the source facts and recompute instead.";
        // The provider debits its receivable; the recipient credits its payable. Swapping both sides keeps the
        // journal balanced, so direction is bound explicitly.
        var monetarySide = evidence.SourceRole == IntercompanyAccountRoleDto.Receivable
            ? AccountingTemplateLineSideDto.Debit : AccountingTemplateLineSideDto.Credit;
        var monetary = draft.Lines.Where(line => line.Dimensions?.ChartAccountNodeId == evidence.MonetaryAccountNodeId).ToArray();
        var operating = draft.Lines.Where(line => line.Dimensions?.ChartAccountNodeId == evidence.OperatingAccountNodeId).ToArray();
        if (evidence.MonetaryAccountNodeId == evidence.OperatingAccountNodeId || monetary.Length != 1 || operating.Length != 1
            || monetary[0].Side != monetarySide || operating[0].Side == monetarySide)
            return "Fee lines differ from the retained direction or accounts.";
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
                return "Fee lines differ from the retained amount, parties, agreement version or profile revision.";
        }
        return draft.Lines.Any(line => line.Dimensions?.AccountingArrangementId is not null) ? null
            : "A fee draft requires its reviewed management-fee profile attribution.";
    }

    private static bool NetsToZeroPerAccount(ManualJournalEntryDraftDto draft)
        => draft.Lines.Count > 0 && draft.Lines
            .GroupBy(line => (line.AccountPath, line.Dimensions?.ChartAccountNodeId, line.Currency, TransactionCurrency: line.TransactionCurrency?.Currency))
            .All(group => group.Sum(line => line.Side == AccountingTemplateLineSideDto.Debit ? line.Amount : -line.Amount) == 0m
                && group.Sum(line => line.TransactionCurrency is { } original
                    ? (line.Side == AccountingTemplateLineSideDto.Debit ? original.Amount : -original.Amount) : 0m) == 0m);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(IntercompanyFeeAccrualRequestDto))]
[JsonSerializable(typeof(CreateIntercompanyFeeAccrualDraftRequest))]
[JsonSerializable(typeof(IntercompanyFeeAccrualEvidenceDto))]
[JsonSerializable(typeof(IntercompanyFeeAccrualProposalDto))]
[JsonSerializable(typeof(IntercompanyFeeAccrualPreviewDto))]
public partial class IntercompanyFeeAccrualJsonContext : JsonSerializerContext;
