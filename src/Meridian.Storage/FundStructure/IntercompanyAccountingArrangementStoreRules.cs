using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;

namespace Meridian.Storage.FundStructure;

internal static class IntercompanyAccountingArrangementStoreRules
{
    public static string Tenant(string tenant) => !string.IsNullOrWhiteSpace(tenant) && !tenant.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)
        ? tenant.Trim().ToLowerInvariant() : throw new ArgumentException("An accounting arrangement tenant scope is required.");
    public static string Serialize(IntercompanyAccountingArrangementHistoryDto value)
        => JsonSerializer.Serialize(value, IntercompanyAccountingArrangementsJsonContext.Default.IntercompanyAccountingArrangementHistoryDto);
    public static IntercompanyAccountingArrangementHistoryDto Deserialize(string value)
    {
        var history = JsonSerializer.Deserialize(value, IntercompanyAccountingArrangementsJsonContext.Default.IntercompanyAccountingArrangementHistoryDto)
            ?? throw new InvalidDataException("Accounting arrangement history is missing.");
        ValidateHistory(history);
        return history;
    }
    public static void ValidateHistory(IntercompanyAccountingArrangementHistoryDto history)
    {
        if (history.Events is null || history.Events.Count == 0)
            throw new InvalidDataException("Accounting arrangement history is empty.");
        var id = history.Events[0].Snapshot.ArrangementId;
        var seen = new HashSet<Guid>();
        var previousHash = "";
        var termsByRevision = new Dictionary<Guid, string>();
        var states = new Dictionary<Guid, IntercompanyAccountingArrangementDto>();
        for (var index = 0; index < history.Events.Count; index++)
        {
            var item = history.Events[index];
            var affected = item.AffectedRevision;
            if (id == Guid.Empty || item.RequestId == Guid.Empty || !seen.Add(item.RequestId) || item.Snapshot.ArrangementId != id
                || item.Snapshot.RevisionId == Guid.Empty || item.Snapshot.RevisionNumber < 1 || item.Snapshot.Version != index + 1
                || string.IsNullOrWhiteSpace(item.Actor) || item.RecordedAt == default || item.RequestDigest.Length != 64
                || !item.RequestDigest.All(char.IsAsciiHexDigit))
                throw new InvalidDataException("Accounting arrangement history is not a contiguous request sequence.");
            if ((item.TargetRevisionId is null) != (item.TargetRevisionSnapshot is null)
                || item.TargetRevisionId is { } target && (target != affected.RevisionId || target == item.Snapshot.RevisionId
                    || affected.ArrangementId != id || affected.Version != item.Snapshot.Version
                    || item.Action != IntercompanyAccountingArrangementActionDto.Retire))
                throw new InvalidDataException("A targeted retirement must retain its exact affected mapping revision.");
            if (index == 0)
            {
                if (item.Action != IntercompanyAccountingArrangementActionDto.Create
                    || item.Snapshot.Status != IntercompanyAccountingArrangementStatusDto.Draft || item.Snapshot.RevisionNumber != 1)
                    throw new InvalidDataException("Accounting arrangement history must begin with a first draft revision.");
            }
            else
            {
                var before = history.Events[index - 1].Snapshot;
                if (item.Snapshot.OrganizationId != before.OrganizationId || item.Snapshot.CanonicalAgreementId != before.CanonicalAgreementId)
                    throw new InvalidDataException("An accounting arrangement cannot change its organization or canonical legal agreement identity.");
                if (item.Action == IntercompanyAccountingArrangementActionDto.Revise)
                {
                    if (states.ContainsKey(item.Snapshot.RevisionId) || item.Snapshot.RevisionNumber != before.RevisionNumber + 1
                        || item.Snapshot.Status != IntercompanyAccountingArrangementStatusDto.Draft)
                        throw new InvalidDataException("Mapping amendments require a new draft revision.");
                }
                else
                {
                    if (!states.TryGetValue(affected.RevisionId, out var prior))
                        throw new InvalidDataException("The affected mapping revision does not exist.");
                    var expected = (prior.Status, item.Action) switch
                    {
                        (IntercompanyAccountingArrangementStatusDto.Draft or IntercompanyAccountingArrangementStatusDto.Rejected, IntercompanyAccountingArrangementActionDto.Submit) => IntercompanyAccountingArrangementStatusDto.Submitted,
                        (IntercompanyAccountingArrangementStatusDto.Submitted, IntercompanyAccountingArrangementActionDto.Approve) => IntercompanyAccountingArrangementStatusDto.Approved,
                        (IntercompanyAccountingArrangementStatusDto.Submitted, IntercompanyAccountingArrangementActionDto.Reject) => IntercompanyAccountingArrangementStatusDto.Rejected,
                        (IntercompanyAccountingArrangementStatusDto.Approved, IntercompanyAccountingArrangementActionDto.Retire) => IntercompanyAccountingArrangementStatusDto.Retired,
                        _ => throw new InvalidDataException("Accounting arrangement history contains an invalid workflow transition.")
                    };
                    if (affected.RevisionNumber != prior.RevisionNumber || affected.Status != expected)
                        throw new InvalidDataException("A workflow transition cannot change mapping revision identity.");
                    if (item.TargetRevisionId is not null)
                    {
                        if (item.Snapshot.RevisionId != before.RevisionId || item.Snapshot.Status != before.Status
                            || Terms(item.Snapshot) != Terms(before) || item.Snapshot.ApprovedBy != before.ApprovedBy
                            || item.Snapshot.ApprovedAt != before.ApprovedAt || item.Snapshot.RetiredAt != before.RetiredAt)
                            throw new InvalidDataException("A prior-revision retirement cannot discard or change the pending mapping amendment.");
                    }
                    else if (item.Snapshot.RevisionId != before.RevisionId)
                        throw new InvalidDataException("A non-targeted action must affect the current mapping revision.");
                }
            }
            if (item.PreviousHash != previousHash || item.ContentHash != Hash(item with { ContentHash = "" }))
                throw new InvalidDataException("Accounting arrangement immutable history hash chain is invalid.");
            previousHash = item.ContentHash;
            foreach (var snapshot in new[] { item.Snapshot, affected })
            {
                var terms = Terms(snapshot);
                if (termsByRevision.TryGetValue(snapshot.RevisionId, out var prior) && prior != terms)
                    throw new InvalidDataException("An immutable mapping revision's configuration was changed.");
                termsByRevision[snapshot.RevisionId] = terms;
                states[snapshot.RevisionId] = snapshot;
            }
        }
    }
    public static (IntercompanyAccountingArrangementHistoryDto History, IntercompanyAccountingArrangementDto Result, bool Changed) Append(
        IntercompanyAccountingArrangementHistoryDto? history, long expectedVersion, IntercompanyAccountingArrangementEventDto item)
    {
        if (history is not null)
            ValidateHistory(history);
        if (history?.Events.SingleOrDefault(x => x.RequestId == item.RequestId) is { } retry)
        {
            if (retry.RequestDigest != item.RequestDigest || retry.Actor != item.Actor || retry.Action != item.Action)
                throw new InvalidOperationException("The accounting arrangement request ID is retained with different content.");
            return (history, retry.Snapshot, false);
        }
        if (expectedVersion != (history?.Current.Version ?? 0) || item.Snapshot.Version != expectedVersion + 1)
            throw new InvalidOperationException("The accounting arrangement version changed; reload before saving.");
        item = item with { PreviousHash = history?.Events[^1].ContentHash ?? "", ContentHash = "" };
        item = item with { ContentHash = Hash(item) };
        var next = Deserialize(Serialize(new([.. history?.Events ?? [], item])));
        return (next, next.Current, true);
    }
    public static IntercompanyAccountingArrangementDto? Retry(IntercompanyAccountingArrangementHistoryDto? history, Guid requestId, string requestDigest)
    {
        if (history is null)
            return null;
        ValidateHistory(history);
        var retained = history.Events.SingleOrDefault(x => x.RequestId == requestId);
        if (retained is null)
            return null;
        if (retained.RequestDigest != requestDigest)
            throw new InvalidOperationException("The accounting arrangement request ID is retained with different content.");
        return retained.Snapshot;
    }
    public static string FileKey(string tenant, Guid id) => Sha256Digest.ComputeUtf8(Tenant(tenant) + ":" + id.ToString("D"));
    private static string Hash(IntercompanyAccountingArrangementEventDto item) => Sha256Digest.ComputeUtf8(
        JsonSerializer.Serialize(item, IntercompanyAccountingArrangementsJsonContext.Default.IntercompanyAccountingArrangementEventDto));
    private static string Terms(IntercompanyAccountingArrangementDto item) => JsonSerializer.Serialize(item with
    {
        Version = 0,
        Status = IntercompanyAccountingArrangementStatusDto.Draft,
        UpdatedBy = "",
        UpdatedAt = default,
        ApprovedBy = null,
        ApprovedAt = null,
        RetiredAt = null,
        LifecycleEvidence = null
    }, IntercompanyAccountingArrangementsJsonContext.Default.IntercompanyAccountingArrangementDto);
}
