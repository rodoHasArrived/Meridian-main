using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Ledger;

namespace Meridian.Reporting;

/// <summary>The exact calculation receipt referenced by a retained journal, copied into report evidence.</summary>
public sealed record ReportingSecurityInputBinding(Guid JournalEntryId, Guid SecurityId,
    string ReceiptSha256, string ReceiptPayloadJson, string ReceiptPayloadSha256);
public sealed record ReportingSecurityInputGap(Guid JournalEntryId, Guid? SecurityId, string Reason);
public sealed record ReportingRetainedSecurityInputs(
    ImmutableArray<ReportingSecurityInputBinding> Bindings,
    ImmutableArray<ReportingSecurityInputGap> Gaps,
    string ContentHashSha256);

/// <summary>Pure verification of frozen report inputs; no current Security Master lookup is permitted.</summary>
public static class ReportingSecurityInputEvidence
{
    public const string ReceiptTag = "cash-flow.receipt.sha256";
    public static string ComputeHash(ReportingRetainedSecurityInputs inputs)
        => Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(inputs with { ContentHashSha256 = "" }));

    public static bool HasInputIdentity(JournalEntry entry)
        => entry.Metadata.SecurityId.HasValue || entry.Metadata.Tags?.ContainsKey(ReceiptTag) == true
            || entry.Metadata.EvidenceReferences.Any(IsReceiptReference);

    public static bool IsReceiptReference(JournalEvidenceReference evidence)
        => evidence.Kind == "cash-flow-calculation-receipt"
            || evidence.EvidenceId.StartsWith("cash-flow-receipt:", StringComparison.Ordinal)
            || evidence.Uri.StartsWith("evidence://security-master/cash-flow-receipts/", StringComparison.Ordinal);

    public static string? ResolveReceiptIdentity(JournalEntry entry)
    {
        var identities = new List<string>();
        if (entry.Metadata.Tags?.TryGetValue(ReceiptTag, out var tag) == true)
            identities.Add(tag);
        foreach (var evidence in entry.Metadata.EvidenceReferences.Where(IsReceiptReference))
        {
            if (evidence.SubjectId is { } subject && subject != entry.Metadata.SecurityId?.ToString("D"))
                return null;
            if (evidence.ContentHash is { } hash)
                identities.Add(hash);
            if (evidence.EvidenceId.StartsWith("cash-flow-receipt:", StringComparison.Ordinal))
                identities.Add(evidence.EvidenceId["cash-flow-receipt:".Length..]);
            if (evidence.Uri.StartsWith("evidence://security-master/cash-flow-receipts/", StringComparison.Ordinal))
                identities.Add(evidence.Uri["evidence://security-master/cash-flow-receipts/".Length..]);
        }
        return identities.Count > 0 && identities.All(Sha256Digest.IsCanonical)
            && identities.Distinct(StringComparer.Ordinal).Count() == 1 ? identities[0] : null;
    }

    public static void Validate(ReportingAuthoritativeSourceCheckpoint source, IReadOnlyList<JournalEntry> journals)
    {
        if (source.SecurityInputs is not { } inputs)
            return; // Exact inputs were not retained by legacy runs.
        if (inputs.Bindings.IsDefault || inputs.Gaps.IsDefault || !Sha256Digest.IsCanonical(inputs.ContentHashSha256)
            || !Sha256Digest.FixedEquals(inputs.ContentHashSha256, ComputeHash(inputs)))
            throw new InvalidDataException("Retained Security Master input evidence has an invalid digest.");
        var expected = journals.Where(HasInputIdentity).ToDictionary(static journal => journal.JournalEntryId);
        var seen = new HashSet<Guid>();
        foreach (var binding in inputs.Bindings)
        {
            if (!seen.Add(binding.JournalEntryId) || !expected.TryGetValue(binding.JournalEntryId, out var journal)
                || journal.Metadata.SecurityId != binding.SecurityId || binding.SecurityId == Guid.Empty
                || ResolveReceiptIdentity(journal) != binding.ReceiptSha256
                || !Sha256Digest.IsCanonical(binding.ReceiptPayloadSha256)
                || !Sha256Digest.FixedEquals(binding.ReceiptPayloadSha256, Sha256Digest.ComputeUtf8(binding.ReceiptPayloadJson)))
                throw new InvalidDataException("Retained Security Master inputs do not match their contributing journals.");
            using var payload = JsonDocument.Parse(binding.ReceiptPayloadJson);
            var root = payload.RootElement;
            if (root.GetProperty("ContentSha256").GetString() != binding.ReceiptSha256
                || root.GetProperty("Security").GetProperty("SecurityId").GetGuid() != binding.SecurityId
                || root.GetProperty("Source").GetProperty("SecurityId").GetGuid() != binding.SecurityId
                || root.GetProperty("Projection").GetProperty("SecurityId").GetGuid() != binding.SecurityId
                || !Sha256Digest.FixedEquals(binding.ReceiptSha256, ReceiptContentHash(root)))
                throw new InvalidDataException("A retained calculation receipt has invalid security identity or content.");
            var context = root.GetProperty("Context");
            var projection = root.GetProperty("Projection");
            if (context.TryGetProperty("SecurityVersion", out var pinnedVersion) && pinnedVersion.ValueKind != JsonValueKind.Null
                    && pinnedVersion.GetInt64() != root.GetProperty("Security").GetProperty("Version").GetInt64()
                || context.GetProperty("AsOf").GetDateTimeOffset() != projection.GetProperty("AsOf").GetDateTimeOffset()
                || root.GetProperty("Scenario").GetRawText() != projection.GetProperty("Scenario").GetRawText()
                || root.GetProperty("Source").GetProperty("SourceKind").GetRawText() != projection.GetProperty("SourceKind").GetRawText())
                throw new InvalidDataException("A retained calculation context contradicts its selected security or projection.");
            if (journal.Metadata.Tags?.TryGetValue("cash-flow.calculationModel", out var model) == true
                && root.GetProperty("ModelVersion").GetString() != model)
                throw new InvalidDataException("A retained calculation model contradicts its journal evidence.");
        }
        foreach (var gap in inputs.Gaps)
            if (!seen.Add(gap.JournalEntryId) || !expected.TryGetValue(gap.JournalEntryId, out var journal)
                || journal.Metadata.SecurityId != gap.SecurityId || string.IsNullOrWhiteSpace(gap.Reason))
                throw new InvalidDataException("Retained Security Master input gaps have an invalid journal binding.");
        if (!seen.SetEquals(expected.Keys))
            throw new InvalidDataException("Retained Security Master inputs omit contributing journals.");
    }

    // The original portable receipt hash blanks only ContentSha256. Writing the retained JSON
    // preserves version-specific schedule rows instead of decoding them into today's row shape.
    public static string ReceiptContentHash(JsonElement root)
    {
        // Preserve exact portable bytes, including historical schedule contracts and the
        // serializer's optimized date/decimal formatting. Re-emitting JsonElements can escape
        // strings differently from the original typed writer and change a legitimate digest.
        var bytes = Encoding.UTF8.GetBytes(root.GetRawText());
        var reader = new Utf8JsonReader(bytes);
        var start = -1;
        var end = -1;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1 && reader.ValueTextEquals("ContentSha256"))
            {
                if (start >= 0 || !reader.Read() || reader.TokenType != JsonTokenType.String)
                    throw new InvalidDataException("The portable receipt digest field is missing or ambiguous.");
                start = checked((int)reader.TokenStartIndex);
                end = checked((int)reader.BytesConsumed);
            }
        }
        if (start < 0)
            throw new InvalidDataException("The portable receipt digest field is missing.");
        var unsigned = new byte[bytes.Length - (end - start) + 2];
        bytes.AsSpan(0, start).CopyTo(unsigned);
        unsigned[start] = (byte)'"';
        unsigned[start + 1] = (byte)'"';
        bytes.AsSpan(end).CopyTo(unsigned.AsSpan(start + 2));
        return Sha256Digest.Compute(unsigned);
    }
}
