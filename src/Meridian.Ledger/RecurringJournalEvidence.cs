using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;

namespace Meridian.Ledger;

/// <summary>The exact inputs and source references used for one retained recurring draft.</summary>
public sealed record RecurringJournalEvidence(
    string OccurrenceKey, Guid JournalEntryId, string ScheduleId, int ScheduleVersion,
    string TemplateId, int TemplateVersion, string ScheduleDefinitionJson, string TemplateDefinitionJson,
    string FundProfileId, Guid LedgerBookId, string EntityId, string TenantId, string CompanyId,
    string Currency, DateOnly EffectiveDate, string PeriodId, long PeriodVersion,
    IReadOnlyList<JournalEvidenceReference> SourceEvidence);

/// <summary>Source provenance is immutable across edits, human approval and posting.</summary>
public static class RecurringJournalEvidenceGuard
{
    public const string EvidenceTag = "recurring.journalEvidence.v1";
    public const string DigestTag = "recurring.journalEvidence.sha256";
    public static bool IsRecurring(string? key) => key?.StartsWith("recurring|", StringComparison.Ordinal) == true;

    public static string Serialize(RecurringJournalEvidence evidence) => JsonSerializer.Serialize(evidence, RecurringJournalEvidenceJsonContext.Default.RecurringJournalEvidence);
    public static RecurringJournalEvidence? Deserialize(string json) => JsonSerializer.Deserialize(json, RecurringJournalEvidenceJsonContext.Default.RecurringJournalEvidence);

    public static string? ValidateSources(IReadOnlyList<JournalEvidenceReference>? evidence)
    {
        if (evidence is null || evidence.Count == 0)
            return "Recurring journals require retained source evidence before generating an approval draft.";
        foreach (var source in evidence)
        {
            if (source is null || string.IsNullOrWhiteSpace(source.EvidenceId) ||
                string.IsNullOrWhiteSpace(source.Uri) || string.IsNullOrWhiteSpace(source.SourceSystem) ||
                string.IsNullOrWhiteSpace(source.Kind) || string.IsNullOrWhiteSpace(source.RetainedBy) ||
                source.RetainedAtUtc == default ||
                (!Sha256Digest.IsCanonical(source.ContentHash) && source.EvidenceVersion is not > 0))
                return "Recurring source evidence requires its identity, retained location, source, custodian, retention time and content hash or source version.";
        }
        return null;
    }

    public static string? Validate(ManualJournalEntryDraftDto draft)
    {
        if (string.IsNullOrWhiteSpace(draft.RecurringJournalEvidenceJson) ||
            !Sha256Digest.IsCanonical(draft.RecurringJournalEvidenceDigest) ||
            !Sha256Digest.FixedEquals(draft.RecurringJournalEvidenceDigest,
                Sha256Digest.ComputeUtf8(draft.RecurringJournalEvidenceJson)))
            return "Recurring journal provenance is missing or differs from the retained server claim.";
        RecurringJournalEvidence? evidence;
        try
        { evidence = Deserialize(draft.RecurringJournalEvidenceJson); }
        catch (JsonException) { return "Recurring journal provenance is invalid."; }
        var correction = evidence is not null && evidence.JournalEntryId != draft.JournalEntryId && HasCorrectionLink(draft);
        if (evidence is null || (evidence.JournalEntryId != draft.JournalEntryId && !correction) ||
            (!correction && evidence.EffectiveDate != draft.AccountingDate) || evidence.LedgerBookId != draft.LedgerBookId ||
            !Equal(evidence.FundProfileId, draft.FundProfileId) || !Equal(evidence.EntityId, draft.EntityId) ||
            !Equal(evidence.TenantId, draft.TenantId) || !Equal(evidence.CompanyId, draft.CompanyId) ||
            !Equal(evidence.Currency, draft.Currency) || (!correction && !Equal(evidence.PeriodId, draft.PeriodId)) ||
            draft.TreasuryContext?.IdempotencyKey != (correction
                ? CorrectionKey(evidence.OccurrenceKey, draft.JournalEntryId) : $"recurring|{evidence.OccurrenceKey}") ||
            evidence.ScheduleVersion < 1 || evidence.TemplateVersion < 1 ||
            string.IsNullOrWhiteSpace(evidence.ScheduleId) || string.IsNullOrWhiteSpace(evidence.TemplateId) ||
            string.IsNullOrWhiteSpace(evidence.ScheduleDefinitionJson) || string.IsNullOrWhiteSpace(evidence.TemplateDefinitionJson))
            return "Recurring journal provenance does not match its occurrence, definition versions or accounting scope.";
        var reason = ValidateSources(evidence.SourceEvidence);
        if (reason is not null)
            return reason;
        if (evidence.SourceEvidence.Any(source => !draft.EvidenceLinks.Contains(source.Uri, StringComparer.Ordinal)))
            return "Recurring source evidence cannot be removed from the approval draft.";
        return null;
    }

    public static string CorrectionKey(string occurrenceKey, Guid journalEntryId)
        => $"recurring|{occurrenceKey}|correction|{journalEntryId:N}";

    private static bool HasCorrectionLink(ManualJournalEntryDraftDto draft)
        => (draft.ReversalOfJournalEntryId is { } reversalSource && draft.RebookedFromJournalEntryId is null &&
            draft.Reversal is { } reversal && reversal.OriginalJournalEntryId == reversalSource &&
            reversal.ReversalJournalEntryId == draft.JournalEntryId && reversalSource != draft.JournalEntryId) ||
           (draft.RebookedFromJournalEntryId is { } rebookSource && draft.ReversalOfJournalEntryId is null &&
            draft.Rebook is { } rebook && rebook.OriginalJournalEntryId == rebookSource &&
            rebook.RebookJournalEntryId == draft.JournalEntryId && rebookSource != draft.JournalEntryId);

    private static bool Equal(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

[JsonSerializable(typeof(RecurringJournalEvidence))]
internal partial class RecurringJournalEvidenceJsonContext : JsonSerializerContext;
