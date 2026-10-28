using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Evidence;

namespace Meridian.Ui.Shared.Services;

/// <summary>Resolves immutable posted-line identities without account-name, symbol, or case searches.</summary>
public sealed class PostedLedgerAmountProvenanceService(
    ILedgerJournalStore? journals,
    ILedgerBookService? books,
    IFundProfileTenancyRegistry? tenancy,
    IEvidenceArtifactStore? artifacts)
{
    public static string BuildRetainedSubjectId(string subjectId, LedgerAmountScopeDto scope)
        => $"{Uri.EscapeDataString(scope.FundProfileId)}:{scope.LedgerBookId:D}:{scope.PeriodId:D}:{subjectId}";

    public async Task<EvidencePacketDto?> GetPacketAsync(
        string subjectId,
        LedgerAmountScopeDto scope,
        CancellationToken ct = default)
    {
        if (journals is null || books is null || tenancy is null ||
            !IsComplete(scope) || !TryParseSubject(subjectId, out var journalId, out var entryId, out var debit))
            return null;

        // IsAccessibleAsync intentionally permits unbound legacy funds. Proof requires retained ownership.
        var owner = await tenancy.ResolveAsync(scope.FundProfileId, ct).ConfigureAwait(false);
        if (owner is null || owner.FundProfileId != scope.FundProfileId ||
            owner.TenantId != scope.TenantId || owner.CompanyId != scope.CompanyId)
            return null;

        var book = await books.GetBookAsync(scope.LedgerBookId, ct).ConfigureAwait(false);
        var period = await journals.GetPeriodAsync(scope.PeriodId, ct).ConfigureAwait(false);
        if (book is null || book.FundProfileId != scope.FundProfileId ||
            book.LedgerBookId != scope.LedgerBookId || period?.LedgerBookId != scope.LedgerBookId || period.PeriodId != scope.PeriodId)
            return null;

        var records = await journals.QueryAsync(new LedgerJournalEntryQuery(
            LedgerBookId: scope.LedgerBookId, PeriodId: scope.PeriodId), ct).ConfigureAwait(false);
        var matches = records.Where(record => record.PeriodId == scope.PeriodId &&
            record.Entry.JournalEntryId == journalId).ToArray();
        if (matches.Length == 0)
            return null;
        if (matches.Length != 1)
            return Packet(subjectId, scope, 0m, book.BaseCurrency, EvidenceStatusDto.Blocked, [],
                ["The retained journal identity is ambiguous. Amount proof is blocked."]);

        var record = matches[0];
        return await GetRetainedPacketAsync(subjectId, scope, record, book.BaseCurrency, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies source support for a journal captured inside an integrity-checked report snapshot.
    /// The caller must authenticate the retained report and verify its population digest first;
    /// this path deliberately never substitutes a journal from the current database.
    /// </summary>
    internal async Task<EvidencePacketDto?> GetRetainedPacketAsync(
        string subjectId, LedgerAmountScopeDto scope, LedgerJournalEntryRecord record,
        string baseCurrency, CancellationToken ct = default)
    {
        if (!IsComplete(scope) || !TryParseSubject(subjectId, out var journalId, out var entryId, out var debit)
            || record.PeriodId != scope.PeriodId || record.Entry.JournalEntryId != journalId)
            return null;
        var lines = record.Entry.Lines.Where(line => line.EntryId == entryId).ToArray();
        if (lines.Length != 1 || lines[0].JournalEntryId != journalId ||
            (debit ? lines[0].Debit : lines[0].Credit) <= 0m)
            return null;
        var line = lines[0];
        if ((line.Dimensions?.FundId is { } fund && fund != scope.FundProfileId) ||
            (line.Dimensions?.BookId is { } lineBook && lineBook != scope.LedgerBookId.ToString("D")))
            return null;

        var canonicalId = $"{journalId:D}:{entryId:D}:{(debit ? "debit" : "credit")}";
        var retainedSubject = BuildRetainedSubjectId(canonicalId, scope);
        var siblingSubjects = record.Entry.Lines.Where(other => other.EntryId != entryId)
            .Select(other => BuildRetainedSubjectId($"{journalId:D}:{other.EntryId:D}:{(other.Debit > 0m ? "debit" : "credit")}", scope))
            .ToHashSet(StringComparer.Ordinal);
        var evidence = new List<LedgerAmountProofEvidenceDto>
        {
            new($"journal-line:{canonicalId}", "ledger-record", "Retained ledger record", null,
                "ledger-journal", record.CreatedAt, EvidenceStatusDto.Ready)
        };
        var warnings = new List<string>();
        var status = EvidenceStatusDto.Ready;
        var references = record.Entry.Metadata.EvidenceReferences.Where(reference =>
            reference.SubjectType != EvidenceSubjectResolver.LedgerAmountKind || reference.SubjectId is null ||
            !siblingSubjects.Contains(reference.SubjectId)).ToArray();
        var duplicates = references.GroupBy(reference => reference.EvidenceId, StringComparer.Ordinal)
            .Where(group => group.Count() != 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        if (references.Any(reference => reference.EvidenceId == evidence[0].EvidenceId))
            duplicates.Add(evidence[0].EvidenceId);
        if (duplicates.Count > 0)
        {
            status = EvidenceStatusDto.Blocked;
            warnings.Add("The journal retains ambiguous supporting-evidence identifiers. Conflicting evidence is withheld.");
        }

        foreach (var reference in references.Where(reference => !duplicates.Contains(reference.EvidenceId)))
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(reference.SubjectType) || string.IsNullOrWhiteSpace(reference.SubjectId))
            {
                Review("A supporting-evidence association has no retained amount scope.");
                continue;
            }
            if (reference.SubjectType != EvidenceSubjectResolver.LedgerAmountKind || reference.SubjectId != retainedSubject)
            {
                // A reference for another line must never become this line's proof.
                status = EvidenceStatusDto.Blocked;
                warnings.Add("A supporting-evidence association does not match this amount's complete retained scope and was withheld.");
                continue;
            }

            var vaultId = ParseVaultId(reference.Uri);
            if (vaultId is null || artifacts is null)
            {
                Review("Supporting evidence has no verifiable retained vault identity.");
                continue;
            }
            var identity = await artifacts.TryGetVaultIdentityAsync(vaultId, scope.TenantId, scope.CompanyId, ct).ConfigureAwait(false);
            if (identity is null)
            {
                Review("Supporting evidence is missing or inaccessible in the selected tenant and company.");
                continue;
            }
            if (identity.TenantId != scope.TenantId || identity.Scope != scope.CompanyId ||
                identity.VaultId != vaultId || identity.SubjectKind != EvidenceSubjectResolver.LedgerAmountKind ||
                identity.SubjectId != retainedSubject || identity.Artifacts.Any(artifact =>
                    artifact.CanonicalSubjectKind != EvidenceSubjectResolver.LedgerAmountKind ||
                    artifact.CanonicalSubjectId != retainedSubject))
            {
                status = EvidenceStatusDto.Blocked;
                warnings.Add("Foreign supporting evidence was withheld because its retained identity does not match this amount.");
                continue;
            }
            if (!Sha256Digest.IsWellFormed(reference.ContentHash))
            {
                Review("Supporting evidence has no retained content digest for this posted amount.");
                continue;
            }
            if (!Sha256Digest.FixedEquals(reference.ContentHash, identity.ContentHashSha256))
            {
                status = EvidenceStatusDto.Blocked;
                warnings.Add("Supporting evidence changed after its digest was retained with the journal. Proof is blocked.");
                continue;
            }
            if (!await artifacts.VerifyRetainedContentAsync(vaultId, scope.TenantId, scope.CompanyId, ct).ConfigureAwait(false))
            {
                Review("Retained supporting source bytes are missing or fail integrity verification.");
                continue;
            }
            if (reference.RetainedAtUtc != identity.RetainedAt || identity.RetainedAt > record.CreatedAt ||
                identity.RetainedAt > DateTimeOffset.UtcNow || identity.Artifacts.Count == 0)
            {
                Review("Supporting evidence is stale or its retained source artifact is missing.");
                continue;
            }

            var manifest = await artifacts.TryOpenManifestByVaultIdAsync(vaultId, scope.TenantId, scope.CompanyId, ct).ConfigureAwait(false);
            if (manifest is null)
            {
                Review("The retained supporting-evidence manifest is missing or unverifiable.");
                continue;
            }
            await using var stream = manifest.Content;
            try
            {
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
                if (document.RootElement.TryGetProperty("lifecycle", out var lifecycle) &&
                    lifecycle.ValueKind == JsonValueKind.Object &&
                    lifecycle.TryGetProperty("expiresAt", out var expiration) &&
                    expiration.ValueKind != JsonValueKind.Null &&
                    (expiration.ValueKind != JsonValueKind.String || !expiration.TryGetDateTimeOffset(out var expiresAt) || expiresAt <= DateTimeOffset.UtcNow))
                {
                    Review("Supporting evidence has expired and requires renewed review.");
                    continue;
                }
            }
            catch (JsonException)
            {
                Review("The retained supporting-evidence manifest could not be verified.");
                continue;
            }

            var reviewed = string.Equals(reference.ReviewStatus, "Accepted", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(reference.ReviewStatus, "Approved", StringComparison.OrdinalIgnoreCase);
            if (!reviewed || string.IsNullOrWhiteSpace(reference.ReviewedBy) || reference.ReviewedAtUtc is null ||
                reference.ReviewedAtUtc < identity.RetainedAt || reference.ReviewedAtUtc > record.CreatedAt ||
                identity.Documents.Any(document => document.ReviewerState.Status != EvidenceDocumentReviewStatusDto.Accepted ||
                    document.ReviewerState.ReviewedAt is null || document.ReviewerState.ReviewedAt > record.CreatedAt))
            {
                Review("Supporting evidence has not completed retained review.");
                continue;
            }
            evidence.Add(new LedgerAmountProofEvidenceDto(reference.EvidenceId, reference.Kind,
                reference.Description ?? "Retained supporting evidence", BuildManifestRoute(vaultId, canonicalId, scope, identity.ContentHashSha256),
                reference.SourceSystem, identity.RetainedAt, EvidenceStatusDto.Ready, identity.ContentHashSha256));
        }
        if (evidence.Count == 1)
            Review("No verified retained source evidence supports this amount. Review is required.");

        return Packet(canonicalId, scope, debit ? line.Debit : line.Credit,
            line.Currency?.FunctionalCurrency ?? baseCurrency, status, evidence, warnings.Distinct().ToArray());

        void Review(string warning)
        {
            if (status != EvidenceStatusDto.Blocked)
                status = EvidenceStatusDto.ReviewRequired;
            warnings.Add(warning);
        }
    }

    private static EvidencePacketDto Packet(string subjectId, LedgerAmountScopeDto scope, decimal amount,
        string currency, EvidenceStatusDto status, IReadOnlyList<LedgerAmountProofEvidenceDto> evidence,
        IReadOnlyList<string> warnings)
    {
        var subject = new EvidenceSubjectDto(subjectId, EvidenceSubjectResolver.LedgerAmountKind,
            "Posted ledger amount", "Accounting", null, "AccountingLedgerExplorer", scope.LedgerBookId);
        var required = evidence.Select(item => item.EvidenceId)
            .Concat(status == EvidenceStatusDto.Ready ? [] : new[] { "retained-source-support" }).ToArray();
        var ready = evidence.Select(item => item.EvidenceId).ToArray();
        var completeness = new EvidenceCompletenessDto(status == EvidenceStatusDto.Ready ? 100 : 0, status,
            required, ready,
            status == EvidenceStatusDto.Ready ? [] : ["retained-source-support"], [], [])
        {
            ValidationIssues = warnings.Select(warning => new EvidenceValidationIssueDto("ledger-amount-proof",
                status == EvidenceStatusDto.Blocked ? EvidenceValidationSeverityDto.Critical : EvidenceValidationSeverityDto.Warning,
                warning)).ToArray(),
            BlockingIssueCount = status == EvidenceStatusDto.Blocked ? warnings.Count : 0,
            WarningIssueCount = status == EvidenceStatusDto.ReviewRequired ? warnings.Count : 0
        };
        var nodes = evidence.Select(item => new EvidenceNodeDto(item.EvidenceId, subject, item.Kind, item.Status,
            new EvidenceFreshnessDto(item.RetainedAt, false, item.Reason), item.SourceSystem, item.Label,
            item.Route is null ? [] : [new EvidenceArtifactRefDto(item.EvidenceId, item.Kind, null, item.Route,
                item.RetainedAt ?? DateTimeOffset.MinValue, item.ContentHash, true,
                EvidenceSubjectResolver.LedgerAmountKind, BuildRetainedSubjectId(subjectId, scope))], [])).ToArray();
        var ledgerIds = evidence.Where(item => item.Kind == "ledger-record").Select(item => item.EvidenceId).ToArray();
        var sourceIds = evidence.Where(item => item.Kind != "ledger-record").Select(item => item.EvidenceId).ToArray();
        var edges = ledgerIds.Length != 1 ? [] : sourceIds.Select(id =>
            new EvidenceEdgeDto(id, ledgerIds[0], "supports", "Exact retained scope and source digest support this posted amount.")).ToArray();
        return new EvidencePacketDto(subject, DateTimeOffset.UtcNow, nodes, edges, completeness, [], warnings)
        {
            LedgerAmount = new LedgerAmountProofDto(subjectId, scope, amount, currency, status, evidence, warnings),
            ProofChain = new EvidenceProofChainDto(completeness.Score, status,
                status == EvidenceStatusDto.Ready ? 2 : evidence.Count > 0 ? 1 : 0, 2,
                [new EvidenceProofChainLayerDto(EvidenceProofChainLayerKindDto.Source, "Retained source", status,
                    status == EvidenceStatusDto.Ready ? 100 : 0, sourceIds, sourceIds, sourceIds, [], completeness.MissingIds,
                    ["source-document"], "Only evidence bound to this exact amount can support it."),
                 new EvidenceProofChainLayerDto(EvidenceProofChainLayerKindDto.Ledger, "Posted ledger record",
                    ledgerIds.Length == 1 ? EvidenceStatusDto.Ready : EvidenceStatusDto.Blocked,
                    ledgerIds.Length == 1 ? 100 : 0, ledgerIds, ledgerIds, ledgerIds, [], [], ["ledger-record"],
                    "Immutable journal and line identifiers locate the amount.")],
                status == EvidenceStatusDto.Ready ? "Retained source evidence supports the posted ledger amount." : "Amount proof requires review.")
        };
    }

    private static bool IsComplete(LedgerAmountScopeDto scope)
        => !string.IsNullOrWhiteSpace(scope.TenantId) && !string.IsNullOrWhiteSpace(scope.CompanyId) &&
           !string.IsNullOrWhiteSpace(scope.FundProfileId) && scope.LedgerBookId != Guid.Empty && scope.PeriodId != Guid.Empty;

    private static bool TryParseSubject(string subjectId, out Guid journalId, out Guid entryId, out bool debit)
    {
        journalId = entryId = Guid.Empty;
        var parts = subjectId.Split(':');
        debit = parts.Length == 3 && parts[2] == "debit";
        return parts.Length == 3 && (debit || parts[2] == "credit") &&
            Guid.TryParseExact(parts[0], "D", out journalId) && journalId != Guid.Empty &&
            Guid.TryParseExact(parts[1], "D", out entryId) && entryId != Guid.Empty;
    }

    private static string? ParseVaultId(string uri)
    {
        const string route = "/workstation/evidence/vault/";
        var id = uri.StartsWith("vault:", StringComparison.Ordinal) ? uri[6..] :
            uri.StartsWith(route, StringComparison.Ordinal) ? uri[route.Length..] : null;
        return !string.IsNullOrWhiteSpace(id) && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_') ? id : null;
    }

    private static string BuildManifestRoute(string vaultId, string subjectId, LedgerAmountScopeDto scope, string hash)
        => $"/workstation/evidence/vault/{Uri.EscapeDataString(vaultId)}" +
           $"?ledgerAmountSubjectId={Uri.EscapeDataString(subjectId)}&ledgerBookId={scope.LedgerBookId:D}" +
           $"&periodId={scope.PeriodId:D}&fundProfileId={Uri.EscapeDataString(scope.FundProfileId)}&expectedContentHash={hash}";
}
