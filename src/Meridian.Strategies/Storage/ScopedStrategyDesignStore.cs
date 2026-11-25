using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Serialization;
using Meridian.Strategies.Services;

namespace Meridian.Strategies.Storage;

/// <summary>Retains scoped Designer revisions using the case-history store's cross-process compare-and-append.</summary>
public sealed class ScopedStrategyDesignStore(IOperationalCaseHistoryStore history)
{
    private const string CaseType = "strategy-design";
    private const string DocumentKey = "document";

    public async Task<StrategyDesignDocument?> SaveAsync(
        StrategyRunRepositoryScope scope,
        string actor,
        StrategyDesignDocument document,
        string? expectedRevision,
        CancellationToken ct)
    {
        var caseId = BuildCaseId(scope, document.DocumentId);
        var previous = (await ReadAsync(caseId, ct).ConfigureAwait(false)).LastOrDefault();
        if (!string.Equals(expectedRevision, previous?.RecordHashSha256, StringComparison.Ordinal))
            return null;

        var now = DateTimeOffset.UtcNow;
        var snapshot = document with
        {
            Revision = null,
            CreatedAt = previous is null ? now : ReadDocument(previous).CreatedAt,
            UpdatedAt = now
        };
        var json = JsonSerializer.Serialize(snapshot, StrategyDesignJsonContext.Default.StrategyDesignDocument);
        var data = new Dictionary<string, string>
        {
            [DocumentKey] = json,
            ["scope"] = ScopeKey(scope),
            ["expectedPreviousCaseSequence"] = (previous?.Sequence ?? 0).ToString(CultureInfo.InvariantCulture)
        };
        if (previous is not null)
            data["expectedPreviousCaseRecordHashSha256"] = previous.RecordHashSha256;

        try
        {
            var record = await history.AppendAsync(new OperationalCaseHistoryAppendRequest
            {
                CaseId = caseId,
                CaseType = CaseType,
                HistoryEventId = Guid.NewGuid().ToString("N"),
                EventType = "DraftSaved",
                OccurredAtUtc = now,
                ActorId = actor,
                Reason = "Saved normalized strategy draft.",
                CorrelationId = caseId,
                InputHashSha256 = Sha256Digest.ComputeUtf8(json),
                Data = data
            }, ct).ConfigureAwait(false);
            return snapshot with { Revision = record.RecordHashSha256 };
        }
        catch (InvalidOperationException)
        {
            // A different process can append between the read and the claim.
            var current = (await ReadAsync(caseId, ct).ConfigureAwait(false)).LastOrDefault();
            if (!string.Equals(current?.RecordHashSha256, previous?.RecordHashSha256, StringComparison.Ordinal))
                return null;
            throw;
        }
    }

    public async Task<StrategyDesignDocument?> GetAsync(
        StrategyRunRepositoryScope scope, string documentId, string? revision, CancellationToken ct)
    {
        var records = await ReadAsync(BuildCaseId(scope, documentId), ct).ConfigureAwait(false);
        var record = revision is null
            ? records.LastOrDefault()
            : records.LastOrDefault(item => string.Equals(item.RecordHashSha256, revision, StringComparison.Ordinal));
        return record is null ? null : ReadDocument(record);
    }

    public async Task<IReadOnlyList<StrategyDesignDraftSummary>> ListAsync(
        StrategyRunRepositoryScope scope, CancellationToken ct)
    {
        var scopeKey = ScopeKey(scope);
        var records = await history.ReadAsync(new OperationalCaseHistoryQuery { CaseType = CaseType }, ct).ConfigureAwait(false);
        return records.Where(item => item.Data.TryGetValue("scope", out var key) && key == scopeKey)
            .GroupBy(static item => item.CaseId, StringComparer.Ordinal)
            .Select(static group => StrategyDesignService.CreateDraftSummary(ReadDocument(group.Last())))
            .OrderByDescending(static draft => draft.UpdatedAt)
            .ToArray();
    }

    private ValueTask<IReadOnlyList<OperationalCaseHistoryRecord>> ReadAsync(string caseId, CancellationToken ct) =>
        history.ReadAsync(new OperationalCaseHistoryQuery { CaseType = CaseType, CaseId = caseId }, ct);

    private static StrategyDesignDocument ReadDocument(OperationalCaseHistoryRecord record)
    {
        var document = JsonSerializer.Deserialize(record.Data[DocumentKey], StrategyDesignJsonContext.Default.StrategyDesignDocument)
            ?? throw new InvalidDataException("Retained strategy revision has no document.");
        return document with { Revision = record.RecordHashSha256 };
    }

    private static string BuildCaseId(StrategyRunRepositoryScope scope, string documentId) =>
        $"strategy-design:{ScopeKey(scope)}:{Sha256Digest.ComputeUtf8(documentId)}";

    private static string ScopeKey(StrategyRunRepositoryScope scope)
    {
        if (!StrategyRunRepositoryVisibility.TryCreateScopeKey(scope, out var key))
            throw new ArgumentException("An authenticated tenant and company are required.", nameof(scope));
        return Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new[] { key.TenantId, key.CompanyId }));
    }
}
