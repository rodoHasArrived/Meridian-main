using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.FundStructure;

namespace Meridian.Storage.FundStructure;

internal static class IntercompanyAgreementStoreRules
{
    public static string Tenant(string tenant) => !string.IsNullOrWhiteSpace(tenant) && !tenant.Trim().Equals("all", StringComparison.OrdinalIgnoreCase) ? tenant.Trim().ToLowerInvariant()
        : throw new ArgumentException("An intercompany tenant scope is required.");

    public static string Serialize(IntercompanyAgreementHistoryDto value)
        => JsonSerializer.Serialize(value, IntercompanyContractsJsonContext.Default.IntercompanyAgreementHistoryDto);

    public static IntercompanyAgreementHistoryDto Deserialize(string value)
    {
        var history = JsonSerializer.Deserialize(value, IntercompanyContractsJsonContext.Default.IntercompanyAgreementHistoryDto)
            ?? throw new InvalidDataException("Agreement history is missing.");
        ValidateHistory(history);
        return history;
    }

    public static void ValidateHistory(IntercompanyAgreementHistoryDto history)
    {
        if (history.Events is null || history.Events.Count == 0) throw new InvalidDataException("Agreement history is empty.");
        var id = history.Events[0].Snapshot.AgreementId;
        var seen = new HashSet<Guid>();
        var previousHash = "";
        var revisionTerms = new Dictionary<Guid, string>();
        var revisionStates = new Dictionary<Guid, IntercompanyAgreementDto>();
        for (var index = 0; index < history.Events.Count; index++)
        {
            var item = history.Events[index];
            var affected = item.AffectedRevision;
            if (id == Guid.Empty || item.RequestId == Guid.Empty || !seen.Add(item.RequestId) || item.Snapshot.AgreementId != id ||
                item.Snapshot.RevisionId == Guid.Empty || item.Snapshot.RevisionNumber < 1 ||
                item.Snapshot.Version != index + 1 || string.IsNullOrWhiteSpace(item.Actor) || item.RecordedAt == default ||
                item.RequestDigest.Length != 64 || !item.RequestDigest.All(char.IsAsciiHexDigit))
                throw new InvalidDataException("Agreement history is not a contiguous, scoped request sequence.");
            if ((item.TargetRevisionId is null) != (item.TargetRevisionSnapshot is null) ||
                item.TargetRevisionId is { } target && (target != affected.RevisionId ||
                    target == item.Snapshot.RevisionId ||
                    affected.AgreementId != id || affected.Version != item.Snapshot.Version ||
                    item.Action is not (IntercompanyAgreementActionDto.Terminate or IntercompanyAgreementActionDto.Novate)))
                throw new InvalidDataException("A targeted lifecycle event must retain the exact affected revision.");
            if (index == 0)
            {
                if (item.Action != IntercompanyAgreementActionDto.Create || item.Snapshot.Status != IntercompanyAgreementStatusDto.Draft || item.Snapshot.RevisionNumber != 1)
                    throw new InvalidDataException("Agreement history must begin with a draft first revision.");
            }
            else
            {
                var before = history.Events[index - 1].Snapshot;
                if (item.Snapshot.OrganizationId != before.OrganizationId || item.Snapshot.Kind != before.Kind ||
                    item.Snapshot.PartyOneEntityId != before.PartyOneEntityId || item.Snapshot.PartyTwoEntityId != before.PartyTwoEntityId)
                    throw new InvalidDataException("An agreement's legal party identity cannot change; retain an explicit successor agreement.");
                if (item.Action == IntercompanyAgreementActionDto.Revise)
                {
                    if (revisionStates.ContainsKey(item.Snapshot.RevisionId) || item.Snapshot.RevisionNumber != before.RevisionNumber + 1 ||
                        item.Snapshot.Status != IntercompanyAgreementStatusDto.Draft)
                        throw new InvalidDataException("Term amendments must create a new draft revision.");
                }
                else
                {
                    if (!revisionStates.TryGetValue(affected.RevisionId, out var affectedBefore))
                        throw new InvalidDataException("The affected agreement revision does not exist.");
                    var expectedStatus = (affectedBefore.Status, item.Action) switch
                    {
                        (IntercompanyAgreementStatusDto.Draft or IntercompanyAgreementStatusDto.Rejected, IntercompanyAgreementActionDto.Submit) => IntercompanyAgreementStatusDto.Submitted,
                        (IntercompanyAgreementStatusDto.Submitted, IntercompanyAgreementActionDto.Approve) => IntercompanyAgreementStatusDto.Approved,
                        (IntercompanyAgreementStatusDto.Submitted, IntercompanyAgreementActionDto.Reject) => IntercompanyAgreementStatusDto.Rejected,
                        (IntercompanyAgreementStatusDto.Approved, IntercompanyAgreementActionDto.Terminate) => IntercompanyAgreementStatusDto.Terminated,
                        (IntercompanyAgreementStatusDto.Approved, IntercompanyAgreementActionDto.Novate) => IntercompanyAgreementStatusDto.Novated,
                        _ => throw new InvalidDataException("Agreement history contains an invalid workflow transition.")
                    };
                    if (affected.RevisionNumber != affectedBefore.RevisionNumber || affected.Status != expectedStatus)
                        throw new InvalidDataException("A workflow transition cannot change the agreement revision identity.");
                    if (item.TargetRevisionId is not null)
                    {
                        if (item.Snapshot.RevisionId != before.RevisionId || item.Snapshot.RevisionNumber != before.RevisionNumber ||
                            item.Snapshot.Status != before.Status || Terms(item.Snapshot) != Terms(before) ||
                            item.Snapshot.ApprovedBy != before.ApprovedBy || item.Snapshot.ApprovedAt != before.ApprovedAt ||
                            item.Snapshot.TerminatedAt != before.TerminatedAt || item.Snapshot.SuccessorAgreementId != before.SuccessorAgreementId)
                            throw new InvalidDataException("A prior-revision lifecycle event cannot discard or change the current amendment.");
                    }
                    else if (item.Snapshot.RevisionId != before.RevisionId)
                        throw new InvalidDataException("A non-targeted action must affect the current authoring revision.");
                }
            }
            if (item.PreviousHash != previousHash || item.ContentHash != Hash(item with { ContentHash = "" }))
                throw new InvalidDataException("Agreement immutable history hash chain is invalid.");
            previousHash = item.ContentHash;
            foreach (var snapshot in new[] { item.Snapshot, affected })
            {
                var terms = Terms(snapshot);
                if (revisionTerms.TryGetValue(snapshot.RevisionId, out var retainedTerms) && retainedTerms != terms)
                    throw new InvalidDataException("An immutable agreement revision's terms were changed.");
                revisionTerms[snapshot.RevisionId] = terms;
            }
            revisionStates[item.Snapshot.RevisionId] = item.Snapshot;
            revisionStates[affected.RevisionId] = affected;
        }
    }

    public static (IntercompanyAgreementHistoryDto History, IntercompanyAgreementDto Result, bool Changed) Append(
        IntercompanyAgreementHistoryDto? history, long expectedVersion, IntercompanyAgreementEventDto item)
    {
        if (history is not null) ValidateHistory(history);
        var retry = history?.Events.SingleOrDefault(x => x.RequestId == item.RequestId);
        if (retry is not null)
        {
            if (retry.RequestDigest != item.RequestDigest || retry.Actor != item.Actor || retry.Action != item.Action)
                throw new InvalidOperationException("The agreement request ID is retained with different content.");
            return (history!, retry.Snapshot, false);
        }
        if (expectedVersion != (history?.Current.Version ?? 0) || item.Snapshot.Version != expectedVersion + 1)
            throw new InvalidOperationException("The agreement version changed; reload before saving.");
        item = item with { PreviousHash = history?.Events[^1].ContentHash ?? "", ContentHash = "" };
        item = item with { ContentHash = Hash(item) };
        var next = new IntercompanyAgreementHistoryDto([.. history?.Events ?? [], item]);
        ValidateHistory(next);
        // Freeze caller-owned collections and enforce the same retained JSON shape across stores.
        next = Deserialize(Serialize(next));
        return (next, next.Current, true);
    }

    public static IntercompanyAgreementDto? Retry(IntercompanyAgreementHistoryDto? history, Guid requestId, string requestDigest)
    {
        if (history is null) return null;
        ValidateHistory(history);
        var retained = history.Events.SingleOrDefault(x => x.RequestId == requestId);
        if (retained is null) return null;
        if (retained.RequestDigest != requestDigest)
            throw new InvalidOperationException("The agreement request ID is retained with different content.");
        return retained.Snapshot;
    }

    public static string FileKey(string tenant, Guid agreementId) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(Tenant(tenant) + ":" + agreementId.ToString("D")))).ToLowerInvariant();

    private static string Hash(IntercompanyAgreementEventDto item) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(item, IntercompanyContractsJsonContext.Default.IntercompanyAgreementEventDto)))).ToLowerInvariant();

    private static string Terms(IntercompanyAgreementDto item) => JsonSerializer.Serialize(item with
    {
        Version = 0, Status = IntercompanyAgreementStatusDto.Draft, UpdatedBy = "", UpdatedAt = default,
        ApprovedBy = null, ApprovedAt = null, TerminatedAt = null, SuccessorAgreementId = null, LifecycleEvidence = null,
        NovationSuccessorRevisionId = null, NovationSuccessorVersion = null, NovationSuccessorLifecycleEvidence = null
    }, IntercompanyContractsJsonContext.Default.IntercompanyAgreementDto);
}
