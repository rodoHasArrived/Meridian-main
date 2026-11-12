using System.Runtime.CompilerServices;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    private const string ControlDecisionTag = "controls.eligibility";
    private static readonly string[] RequiredControlNames = ["entity-lifecycle", "intercompany"];
    private readonly ConditionalWeakTable<NpgsqlTransaction, PostingAuthorityScope> _postingAuthorityScopes = new();

    public async Task<IAsyncDisposable> AcquirePostingAuthorityAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, IReadOnlyList<LedgerJournalEntryWrite> entries, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0) return EmptyLedgerAuthorityLease.Instance;
        if (_postingAuthorityScopes.TryGetValue(transaction, out _))
            throw new LedgerValidationException("A posting authority scope already exists for this transaction.");
        // A batch may mix existing receipts and new writes. Lock every period first and classify
        // receipts without taking the audit head; new authority leases must precede audit locks.
        var normalized = entries.Select(original => AccountingPostingCommandValidator.NormalizeAndValidate(original,
            _options.RequireGovernedPostingCommand, _options.RequireExpectedVersion)).ToArray();
        foreach (var periodId in normalized.Select(write => write.PeriodId).Distinct().Order())
        {
            _ = await LoadPeriodAsync(connection, transaction, periodId, forUpdate: _options.EnablePeriodLocking,
                callerTenantId: _tenantScope.IsFailClosed ? ResolveCallerTenant() : null, ct: ct).ConfigureAwait(false)
                ?? throw new LedgerValidationException("The posting period does not exist.");
        }
        var contexts = new List<LedgerPostingAuthorityContext>();
        var resolved = new List<(LedgerJournalEntryWrite Write, LedgerAccountingPeriod Period)>();
        var hasCommittedReplay = false;
        foreach (var write in normalized)
        {
            if (await TryResolveCommittedReplayAsync(connection, transaction, write, ct, verifyAudit: false).ConfigureAwait(false))
            {
                hasCommittedReplay = true;
                continue;
            }
            if (write.Entry.Metadata.Tags?.ContainsKey(ControlDecisionTag) == true)
                throw new LedgerValidationException("Posting eligibility evidence is reserved for the authoritative append boundary.");
            var period = await LoadPeriodAsync(connection, transaction, write.PeriodId, forUpdate: false,
                callerTenantId: _tenantScope.IsFailClosed ? ResolveCallerTenant() : null, ct: ct).ConfigureAwait(false)
                ?? throw new LedgerValidationException("The posting period does not exist.");
            resolved.Add((write, period));
        }
        // The period owns the book mapping; caller-supplied/null book claims cannot dictate lock order.
        foreach (var item in resolved.OrderBy(item => item.Period.LedgerBookId))
        {
            var (write, period) = item;
            var bookId = period.LedgerBookId;
            if (!bookId.HasValue)
            {
                if ((write.LedgerBookId ?? write.PostingCommand?.LedgerBookId).HasValue)
                    throw new LedgerValidationException("The posting claims a book but the authoritative period has no book mapping.");
                var legacy = new LedgerPostingAuthorityContext(write,
                    new(Guid.Empty, "legacy-unmapped", Guid.Empty, Meridian.Contracts.FundStructure.FundStructureNodeKindDto.Fund,
                        "Legacy unmapped period", "", DateTimeOffset.MinValue, DateTimeOffset.MinValue),
                    new(Guid.Empty, LedgerBookControlModeDto.LegacyWarning, 1, "entity-intercompany-v1", DateTimeOffset.MinValue), ResolveCallerTenant());
                legacy.Decisions.Add(new("entity-lifecycle", "legacy-book-unmapped", false,
                    "Legacy period has no authoritative book mapping; migrate the book before activating controls."));
                contexts.Add(legacy);
                continue;
            }
            if ((write.LedgerBookId ?? write.PostingCommand?.LedgerBookId) is { } claimed && claimed != bookId.Value)
                throw new LedgerValidationException("The posting book conflicts with the authoritative period book.");
            await EnsureTenantRowAsync(connection, transaction, "ledger_books", "ledger_book_id", bookId.Value, false, ct).ConfigureAwait(false);
            var book = await LoadLedgerBookAsync(connection, transaction, bookId.Value, ct).ConfigureAwait(false)
                ?? throw new LedgerValidationException("The posting ledger book does not exist.");
            var controls = await LoadBookControlsAsync(connection, transaction, bookId.Value, false, ct).ConfigureAwait(false)
                ?? throw new LedgerValidationException("The ledger book has no retained control profile; migration is required.");
            contexts.Add(new LedgerPostingAuthorityContext(write, book, controls, ResolveCallerTenant()));
        }

        var leases = new List<IAsyncDisposable>();
        try
        {
            if (contexts.Count == 0)
            {
                if (hasCommittedReplay)
                    _ = await LockAndVerifyLedgerAuditAsync(connection, transaction, ct).ConfigureAwait(false);
                var replayScope = new PostingAuthorityScope(this, transaction, contexts, leases);
                _postingAuthorityScopes.Add(transaction, replayScope);
                return replayScope;
            }
            var authorities = _postingAuthorities?.Invoke().ToArray() ?? [];
            var missingAuthorities = RequiredControlNames.Where(name => !authorities.Any(item => item.ControlName == name)).ToArray();
            if (missingAuthorities.Length > 0 && contexts.Any(item => item.Controls.Mode == LedgerBookControlModeDto.Enforced))
                throw new LedgerValidationException("Authoritative entity and intercompany posting controls are unavailable.");
            foreach (var authority in authorities)
                leases.Add(await authority.AcquireAsync(contexts.Where(item => item.Book.LedgerBookId != Guid.Empty).ToArray(), ct).ConfigureAwait(false));
            foreach (var context in contexts)
            {
                foreach (var missing in RequiredControlNames.Where(name => !context.Decisions.Any(item => item.Control == name && item.Allowed)))
                    context.Decisions.Add(new(missing, "required-control-unverified", false,
                        $"The required {missing} control did not produce an affirmative authoritative decision."));
                if (authorities.Length == 0)
                    context.Decisions.Add(new("entity-lifecycle", "legacy-authority-unavailable", false,
                        "Legacy book: lifecycle authority is unavailable; migrate before activating controls."));
                if (context.Controls.Mode == LedgerBookControlModeDto.Enforced && context.Decisions.Any(item => !item.Allowed))
                    throw new LedgerValidationException(string.Join(" ", context.Decisions.Where(item => !item.Allowed).Select(item => item.Message)));
            }
            if (hasCommittedReplay)
                _ = await LockAndVerifyLedgerAuditAsync(connection, transaction, ct).ConfigureAwait(false);
            var scope = new PostingAuthorityScope(this, transaction, contexts, leases);
            _postingAuthorityScopes.Add(transaction, scope);
            return scope;
        }
        catch
        {
            for (var index = leases.Count - 1; index >= 0; index--)
                await leases[index].DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private LedgerJournalEntryWrite RetainPostingAuthorityDecision(NpgsqlTransaction transaction, LedgerJournalEntryWrite write, LedgerAccountingPeriod period)
    {
        if (!_postingAuthorityScopes.TryGetValue(transaction, out var scope) || scope.Disposed)
        {
            // Truly unbooked compatibility periods have no external entity authority to retain.
            // Typed or book-claiming commands cannot use this exception.
            if (period.LedgerBookId is not null || write.LedgerBookId is not null || write.PostingCommand?.LedgerBookId is not null)
                throw new LedgerValidationException("Caller-owned ledger transactions require a posting-authority scope held through commit.");
            return WithControlDecision(write, LedgerBookControlModeDto.LegacyWarning, 1,
                [new("entity-lifecycle", "legacy-book-unmapped", false, "Legacy unmapped period requires migration before entity controls can be activated.")]);
        }
        var fingerprint = PostingAuthorityFingerprint(write);
        var context = scope.Contexts.SingleOrDefault(item => item.Write.Entry.JournalEntryId == write.Entry.JournalEntryId
            && PostingAuthorityFingerprint(item.Write) == fingerprint
            && (period.LedgerBookId ?? Guid.Empty) == item.Book.LedgerBookId)
            ?? throw new LedgerValidationException("The journal changed after posting authority was acquired.");
        return WithControlDecision(write, context.Controls.Mode, context.Controls.Version, context.Decisions.ToArray());
    }

    private static LedgerJournalEntryWrite WithControlDecision(LedgerJournalEntryWrite write, LedgerBookControlModeDto mode,
        long version, IReadOnlyList<LedgerControlDecisionDto> decisions)
    {
        var tags = new Dictionary<string, string>(write.Entry.Metadata.Tags ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)
        {
            [ControlDecisionTag] = JsonSerializer.Serialize(new { Mode = mode, PolicyVersion = "entity-intercompany-v1",
                Version = version, Decisions = decisions }, JsonOptions)
        };
        return write with { Entry = new JournalEntry(write.Entry.JournalEntryId, write.Entry.Timestamp,
            write.Entry.Description, write.Entry.Lines, write.Entry.Metadata with { Tags = tags }) };
    }

    private static string PostingAuthorityFingerprint(LedgerJournalEntryWrite write)
        => Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(write, JsonOptions));

    private sealed class PostingAuthorityScope(PostgresLedgerJournalStore owner, NpgsqlTransaction transaction,
        IReadOnlyList<LedgerPostingAuthorityContext> contexts, IReadOnlyList<IAsyncDisposable> leases) : IAsyncDisposable
    {
        public IReadOnlyList<LedgerPostingAuthorityContext> Contexts { get; } = contexts;
        public bool Disposed { get; private set; }
        public async ValueTask DisposeAsync()
        {
            if (Disposed) return;
            Disposed = true;
            owner._postingAuthorityScopes.Remove(transaction);
            for (var index = leases.Count - 1; index >= 0; index--)
                await leases[index].DisposeAsync().ConfigureAwait(false);
        }
    }

    public async Task<LedgerBookControlProfileDto?> GetBookControlsAsync(Guid bookId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await EnsureTenantRowAsync(connection, null, "ledger_books", "ledger_book_id", bookId, false, ct).ConfigureAwait(false);
        return await LoadBookControlsAsync(connection, null, bookId, false, ct).ConfigureAwait(false);
    }

    private async Task<LedgerBookControlProfileDto?> LoadBookControlsAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        Guid bookId, bool forUpdate, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select ledger_book_id,mode,version,policy_version,created_at_utc,activated_at_utc,activated_by,migration_receipt_id from {Qualified("ledger_book_controls")} where ledger_book_id=@book {(forUpdate ? "for update" : transaction is null ? "" : "for share")};";
        command.Parameters.AddWithValue("book", bookId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new(reader.GetGuid(0), Enum.Parse<LedgerBookControlModeDto>(reader.GetString(1)), reader.GetInt64(2),
            reader.GetString(3), ReadUtcDateTimeOffset(reader, 4), reader.IsDBNull(5) ? null : ReadUtcDateTimeOffset(reader, 5),
            reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7));
    }

    public async Task<LedgerBookControlProfileDto> ActivateBookControlsAsync(Guid bookId, long expectedVersion,
        string preflightDigest, string actor, string reason, string evidenceReference, CancellationToken ct = default)
    {
        RequireWriteTenant();
        if (string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(reason) || string.IsNullOrWhiteSpace(evidenceReference)
            || preflightDigest.Length != 64 || preflightDigest.Any(ch => !Uri.IsHexDigit(ch)))
            throw new LedgerValidationException("Activation requires a preflight digest, actor, reason and retained evidence.");
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct).ConfigureAwait(false);
        await EnsureBookWriteAuthorityAsync(connection, transaction, bookId, ct).ConfigureAwait(false);
        var profile = await LoadBookControlsAsync(connection, transaction, bookId, true, ct).ConfigureAwait(false)
            ?? throw new LedgerValidationException("Book control profile is missing.");
        if (profile.Version != expectedVersion) throw new LedgerValidationException("Book controls changed; repeat migration preflight.");
        if (profile.Mode == LedgerBookControlModeDto.Enforced) return profile;
        var book = await LoadLedgerBookAsync(connection, transaction, bookId, ct).ConfigureAwait(false)
            ?? throw new LedgerValidationException("Book was not found.");
        await using var preflight = await AcquireControlPreflightAsync(book, profile, ct).ConfigureAwait(false);
        if (!preflight.Result.CanActivate || !Sha256Digest.FixedEquals(preflight.Result.Digest, preflightDigest))
            throw new LedgerValidationException("Book control preflight changed or contains blockers; prepare and review it again.");
        var receiptId = Guid.NewGuid();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            insert into {Qualified("ledger_book_control_activation")}
                (receipt_id,ledger_book_id,prior_version,preflight_digest,actor,reason,evidence_reference)
            values (@receipt,@book,@version,@digest,@actor,@reason,@evidence);
            update {Qualified("ledger_book_controls")} set mode='Enforced',version=version+1,
                activated_at_utc=now(),activated_by=@actor,migration_receipt_id=@receipt_text where ledger_book_id=@book;
            """;
        command.Parameters.AddWithValue("receipt", receiptId);
        command.Parameters.AddWithValue("receipt_text", receiptId.ToString("D"));
        command.Parameters.AddWithValue("book", bookId);
        command.Parameters.AddWithValue("version", expectedVersion);
        command.Parameters.AddWithValue("digest", preflightDigest);
        command.Parameters.AddWithValue("actor", actor.Trim());
        command.Parameters.AddWithValue("reason", reason.Trim());
        command.Parameters.AddWithValue("evidence", evidenceReference.Trim());
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        var updated = await LoadBookControlsAsync(connection, transaction, bookId, false, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return updated!;
    }

    public async Task<LedgerBookControlPreflightDto> PrepareBookControlsAsync(Guid bookId, CancellationToken ct = default)
    {
        RequireWriteTenant();
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct).ConfigureAwait(false);
        await EnsureBookWriteAuthorityAsync(connection, transaction, bookId, ct).ConfigureAwait(false);
        var book = await LoadLedgerBookAsync(connection, transaction, bookId, ct).ConfigureAwait(false)
            ?? throw new LedgerValidationException("Book was not found.");
        var profile = await LoadBookControlsAsync(connection, transaction, bookId, false, ct).ConfigureAwait(false)
            ?? throw new LedgerValidationException("Book control profile is missing.");
        await using var preflight = await AcquireControlPreflightAsync(book, profile, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return preflight.Result;
    }

    private async Task<ControlPreflightScope> AcquireControlPreflightAsync(LedgerBookRecord book, LedgerBookControlProfileDto profile, CancellationToken ct)
    {
        var checks = _controlPreflights?.Invoke().ToArray() ?? [];
        var leases = new List<LedgerBookControlPreflightLease>();
        try
        {
            foreach (var check in checks)
                leases.Add(await check.CheckAsync(book, ResolveCallerTenant(), ct).ConfigureAwait(false));
            var decisions = leases.SelectMany(item => item.Decisions).ToList();
            foreach (var required in RequiredControlNames)
                if (!checks.Any(item => item.ControlName == required) || !decisions.Any(item => item.Control == required && item.Allowed))
                    decisions.Add(new(required, "preflight-unavailable", false, $"Authoritative {required} migration preflight is unavailable or unverified."));
            var ordered = decisions.OrderBy(item => item.Control, StringComparer.Ordinal).ThenBy(item => item.Code, StringComparer.Ordinal).ToArray();
            var digest = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new { book, profile.Version, profile.PolicyVersion, Decisions = ordered }, JsonOptions));
            return new(new(book.LedgerBookId, profile.Version, profile.PolicyVersion, ordered, digest, DateTimeOffset.UtcNow), leases);
        }
        catch
        {
            for (var index = leases.Count - 1; index >= 0; index--) await leases[index].Lease.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class ControlPreflightScope(LedgerBookControlPreflightDto result, IReadOnlyList<LedgerBookControlPreflightLease> leases) : IAsyncDisposable
    {
        public LedgerBookControlPreflightDto Result { get; } = result;
        public async ValueTask DisposeAsync()
        {
            for (var index = leases.Count - 1; index >= 0; index--) await leases[index].Lease.DisposeAsync().ConfigureAwait(false);
        }
    }
}
