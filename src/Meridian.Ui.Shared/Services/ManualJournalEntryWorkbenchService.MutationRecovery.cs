using System.Runtime.CompilerServices;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Storage.Ledger;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ManualJournalEntryWorkbenchService
{
    private static readonly ConditionalWeakTable<IManualJournalEntryDraftStore, InMemoryManualJournalMutationRecoveryStore>
        EphemeralRecoveryStores = new();
    private readonly IManualJournalMutationRecoveryStore _mutationRecovery;
    // Only accessed while holding the recovery store's full-command lease.
    private MutationCommand? _mutationCommand;
    private IManualJournalMutationSession? _mutationSession;

    internal static IManualJournalMutationRecoveryStore DefaultMutationRecoveryFor(IManualJournalEntryDraftStore store)
        => store is FileManualJournalEntryDraftStore fileStore
            ? new FileManualJournalMutationRecoveryStore(fileStore.MutationRecoveryDirectory)
            : EphemeralRecoveryStores.GetValue(store, static _ => new InMemoryManualJournalMutationRecoveryStore());

    private sealed record MutationCommand(string Key, string RequestHash, string ScopeKey, Guid JournalEntryId, bool OperationIsLifecycle);

    private static string RecoveryScope(string fund, string? tenant, string? company)
        => JsonSerializer.Serialize(new[]
        {
            NormalizeFundProfileId(fund).ToUpperInvariant(),
            NormalizeOptional(tenant)?.ToUpperInvariant(),
            NormalizeOptional(company)?.ToUpperInvariant()
        });

    private async Task<T> ExecuteMutationAsync<TRequest, T>(
        string operation, TRequest request, string fund, Guid journalEntryId, int version,
        string? tenant, string? company, string? correlationId, Func<Task<T>> execute,
        CancellationToken ct, bool replayThroughValidation = false, string? fingerprintSalt = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = RecoveryScope(fund, tenant, company);
        var requestHash = FingerprintRequest(JsonSerializer.SerializeToElement(request,
            typeof(TRequest), ManualJournalMutationJsonContext.Default), fingerprintSalt);
        var identity = NormalizeOptional(correlationId)?.ToUpperInvariant()
            ?? $"version:{version}:{requestHash}";
        var key = Sha256Digest.ComputeUtf8($"{scope}|{journalEntryId:D}|{operation}|{identity}");
        await using var session = await _mutationRecovery.OpenSessionAsync(ct).ConfigureAwait(false);
        var command = new MutationCommand(key, requestHash, scope, journalEntryId, operation.StartsWith("lifecycle-", StringComparison.Ordinal));

        var retained = await session.GetAsync(key, ct).ConfigureAwait(false);
        if (retained is not null && !string.Equals(retained.RequestHash, requestHash, StringComparison.Ordinal))
            throw new InvalidOperationException("The manual journal command identity was reused with different input or actor.");

        foreach (var pending in await session.ListPendingAsync(ct).ConfigureAwait(false))
        {
            if (pending.ScopeKey != scope ||
                (pending.JournalEntryId != journalEntryId && !pending.After.Any(x => x.JournalEntryId == journalEntryId)))
                continue;
            var applied = await RecoverMutationAsync(pending, session, ct).ConfigureAwait(false);
            if (!applied)
            {
                if (pending.CommandKey != key)
                    throw new InvalidOperationException("An earlier manual journal command requires its original retry before another mutation.");
                // The before-images and absence of any committed journal were verified. Discard
                // only this unapplied plan, then run ALL current authority and version checks.
                await session.DiscardUnappliedAsync(pending, ct).ConfigureAwait(false);
                retained = null;
            }
            else if (pending.CommandKey == key)
                retained = pending with { Completed = true };
        }

        if (retained is not null)
        {
            await VerifyCommittedPostingAsync(retained, requirePresent: true, ct).ConfigureAwait(false);
            foreach (var expected in retained.After)
            {
                var current = await _draftStore.GetAsync(expected.FundProfileId, expected.JournalEntryId, ct,
                    expected.TenantId, expected.CompanyId).ConfigureAwait(false);
                if (current is null || current.Version < expected.Version || current.LedgerBookId != expected.LedgerBookId ||
                    (current.Version == expected.Version && !DraftMatches(current, expected)))
                    throw new InvalidOperationException("The completed manual journal receipt conflicts with retained draft state.");
            }
            foreach (var audit in retained.AuditEvents)
                await _auditStore.AppendAsync(audit, ct).ConfigureAwait(false);
            if (!replayThroughValidation)
                return retained.Result.Deserialize<T>(ManualJournalMutationJsonContext.Default.Options)
                    ?? throw new InvalidOperationException("Manual journal command receipt has no result.");
        }

        _mutationCommand = command;
        _mutationSession = session;
        try
        {
            var result = await execute().ConfigureAwait(false);
            var unresolved = await session.GetAsync(key, ct).ConfigureAwait(false);
            if (unresolved is { Completed: false })
                throw new InvalidOperationException("Manual journal mutation audit recovery remains unresolved.");
            return result;
        }
        finally
        {
            _mutationCommand = null;
            _mutationSession = null;
        }
    }

    private async Task PersistMutationAsync<T>(
        IReadOnlyList<ManualJournalEntryDraftDto> drafts, IReadOnlyList<string> actions,
        string actor, string? correlationId, IReadOnlyList<string>? principals, T result,
        CancellationToken ct, LedgerJournalEntryWrite? posting = null)
    {
        var command = _mutationCommand ?? throw new InvalidOperationException("Manual journal mutation requires a command lease.");
        var session = _mutationSession ?? throw new InvalidOperationException("Manual journal mutation requires a recovery session.");
        if (drafts.Count == 0 || drafts.Count != actions.Count)
            throw new InvalidOperationException("Manual journal mutation has incomplete audit actions.");
        var before = new List<ManualJournalEntryDraftDto?>();
        var audits = new List<AccountingActionAuditEventDto>();
        for (var index = 0; index < drafts.Count; index++)
        {
            var draft = drafts[index];
            var prior = await _draftStore.GetAsync(draft.FundProfileId, draft.JournalEntryId, ct,
                draft.TenantId, draft.CompanyId).ConfigureAwait(false);
            before.Add(prior);
            audits.Add(new AccountingActionAuditEventDto(
                CreateDeterministicGuid("manual-je-mutation-audit", command.Key, index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                draft.UpdatedAtUtc, RequireText(actor, nameof(actor)), actions[index], draft.FundProfileId,
                draft.LedgerBookId, NormalizeOptional(correlationId), prior is null ? Sha256Digest.ComputeUtf8("null") : Hash(prior), Hash(draft),
                draft.ValidationIssues, draft.EvidenceLinks, draft.CompanyId, NormalizePrincipalIds(principals), draft.TenantId));
        }
        var intent = new ManualJournalMutationIntent(command.Key, command.RequestHash, command.ScopeKey,
            command.JournalEntryId, before, drafts, audits, posting,
            JsonSerializer.SerializeToElement(result, result!.GetType(), ManualJournalMutationJsonContext.Default));
        await session.RetainAsync(intent, ct).ConfigureAwait(false);
        if (posting is not null)
        {
            if (_postingTarget is not null)
                await _postingTarget.PostAsync(posting, ct).ConfigureAwait(false);
            else
                await (_journalStore ?? throw new InvalidOperationException("The journal store is unavailable."))
                    .AppendAsync(posting, ct).ConfigureAwait(false);
            await VerifyCommittedPostingAsync(intent, requirePresent: true, ct).ConfigureAwait(false);
        }
        await _draftStore.SaveBatchAsync(drafts, ct).ConfigureAwait(false);
        await CompleteMutationAsync(intent, session, ct).ConfigureAwait(false);
    }

    /// <returns>False only when every original draft is unchanged and no journal was committed.</returns>
    private async Task<bool> RecoverMutationAsync(ManualJournalMutationIntent intent,
        IManualJournalMutationSession session, CancellationToken ct)
    {
        if (intent.After.Count == 0 || intent.Before.Count != intent.After.Count || intent.AuditEvents.Count != intent.After.Count)
            throw new InvalidOperationException("Manual journal recovery receipt is incomplete.");
        var allBefore = true;
        var allAfter = true;
        for (var index = 0; index < intent.After.Count; index++)
        {
            var expected = intent.After[index];
            var actual = await _draftStore.GetAsync(expected.FundProfileId, expected.JournalEntryId, ct,
                expected.TenantId, expected.CompanyId).ConfigureAwait(false);
            allBefore &= DraftMatches(actual, intent.Before[index]);
            allAfter &= DraftMatches(actual, expected);
        }
        var posted = await VerifyCommittedPostingAsync(intent, requirePresent: false, ct).ConfigureAwait(false);
        if (allBefore && !posted) return false;
        if (allBefore && posted)
        {
            // Financial facts already exist. Do not execute the posting target, reconsider the
            // approved command, or edit those facts; finish only the retained workbench outcome.
            await _draftStore.SaveBatchAsync(intent.After, ct).ConfigureAwait(false);
        }
        else if (!allAfter || (intent.Posting is not null && !posted))
            throw new InvalidOperationException("Manual journal recovery conflicts with retained draft or journal state.");
        await CompleteMutationAsync(intent, session, ct).ConfigureAwait(false);
        return true;
    }

    private async Task CompleteMutationAsync(ManualJournalMutationIntent intent,
        IManualJournalMutationSession session, CancellationToken ct)
    {
        foreach (var audit in intent.AuditEvents)
            await _auditStore.AppendAsync(audit, ct).ConfigureAwait(false);
        await session.CompleteAsync(intent, ct).ConfigureAwait(false);
    }

    private async Task<bool> VerifyCommittedPostingAsync(ManualJournalMutationIntent intent, bool requirePresent, CancellationToken ct)
    {
        if (intent.Posting is not { } write) return false;
        var store = _journalStore ?? throw new InvalidOperationException("The journal store is required for manual posting recovery.");
        var collisions = await store.FindPostingIdentityCollisionsAsync(LedgerPostingIdentity.FromWrite(write), ct).ConfigureAwait(false);
        if (collisions.Count == 0)
        {
            if (requirePresent) throw new InvalidOperationException("The manual journal command has no retained journal entry.");
            return false;
        }
        foreach (var entry in collisions)
            DurableLedgerPostingTarget.VerifyRetainedEntry(entry, write);
        return true;
    }

    private static bool DraftMatches(ManualJournalEntryDraftDto? actual, ManualJournalEntryDraftDto? expected)
        => actual is null ? expected is null : expected is not null && Hash(actual) == Hash(expected);

    private static string FingerprintRequest(JsonElement request, string? salt)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(request, writer, null);
        return Sha256Digest.ComputeUtf8(System.Text.Encoding.UTF8.GetString(stream.ToArray()) + salt);
    }

    private static void WriteCanonical(JsonElement value, Utf8JsonWriter writer, string? property)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var item in value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(item.Name);
                    WriteCanonical(item.Value, writer, item.Name);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteCanonical(item, writer, null);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String when property is "FundProfileId" or "TenantId" or "CompanyId" or "Actor" or "CorrelationId":
                writer.WriteStringValue(NormalizeOptional(value.GetString())?.ToUpperInvariant());
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}
