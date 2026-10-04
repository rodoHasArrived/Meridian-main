using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Banking;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Storage.Store;

namespace Meridian.Ui.Shared.Services;

public sealed class InMemoryAccountingConfigurationStore :
    IAccountingConfigurationStore,
    Meridian.Application.Composition.INonProductionOnlyService
{
    private readonly Dictionary<string, AccountingConfigurationWorkspaceDto> _workspaces = new(StringComparer.OrdinalIgnoreCase);

    public Task<AccountingConfigurationWorkspaceDto?> GetAsync(
        string fundProfileId,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        ct.ThrowIfCancellationRequested();
        _workspaces.TryGetValue(Key(fundProfileId, ledgerBookId, tenantId, companyId), out var workspace);
        return Task.FromResult(workspace);
    }

    public Task SaveAsync(AccountingConfigurationWorkspaceDto workspace, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(workspace);
        _workspaces[Key(workspace.FundProfileId, workspace.LedgerBookId, workspace.TenantId, workspace.CompanyId)] = workspace;
        return Task.CompletedTask;
    }

    private static string Key(string fundProfileId, Guid? ledgerBookId, string? tenantId, string? companyId)
        => $"{NormalizeOptional(tenantId) ?? "all"}|{NormalizeOptional(companyId) ?? "all"}|{NormalizeFundProfileId(fundProfileId)}|{ledgerBookId?.ToString("D") ?? "fund"}";

    private static string NormalizeFundProfileId(string value)
        => string.IsNullOrWhiteSpace(value) ? "default-fund" : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class InMemoryAccountingActionAuditStore :
    IAccountingActionAuditStore,
    Meridian.Application.Composition.INonProductionOnlyService
{
    private readonly List<AccountingActionAuditEventDto> _events = [];

    public Task AppendAsync(AccountingActionAuditEventDto auditEvent, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(auditEvent);

        // Idempotent on the id and content-validating, as IAccountingActionAuditStore requires and
        // as the file and PostgreSQL stores already were. This one appended unconditionally, so
        // recovery -- which replays the append to establish whether the declared event is the one
        // already retained -- duplicated it instead, and then cleared the marker over a history
        // carrying the same event twice (Codex review finding on PR #2871).
        var retained = _events.FirstOrDefault(item => item.AuditEventId == auditEvent.AuditEventId);
        if (retained is not null)
        {
            if (!string.Equals(
                    AccountingAuditChain.ComputePayloadHash(retained),
                    AccountingAuditChain.ComputePayloadHash(auditEvent),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Audit event '{auditEvent.AuditEventId.ToString("D", CultureInfo.InvariantCulture)}' "
                    + "is already retained with different content.");
            }

            return Task.CompletedTask;
        }

        _events.Add(auditEvent);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AccountingActionAuditEventDto>> ListAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedTenantId = NormalizeOptional(tenantId);
        var normalizedCompanyId = NormalizeOptional(companyId);
        var events = _events
            .Where(item => string.IsNullOrWhiteSpace(fundProfileId) || string.Equals(item.FundProfileId, fundProfileId.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(item => !ledgerBookId.HasValue || item.LedgerBookId == ledgerBookId)
            .Where(item => normalizedTenantId is null || string.Equals(NormalizeOptional(item.TenantId), normalizedTenantId, StringComparison.OrdinalIgnoreCase))
            .Where(item => normalizedCompanyId is null || string.Equals(item.CompanyId, normalizedCompanyId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.RecordedAtUtc)
            .ThenBy(item => item.AuditEventId)
            .ToArray();

        return Task.FromResult<IReadOnlyList<AccountingActionAuditEventDto>>(events);
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class FileAccountingConfigurationStore :
    JsonFileSnapshotStore<FileAccountingConfigurationStore.AccountingConfigurationSnapshot>,
    IAccountingConfigurationStore,
    IAccountingActionAuditStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly TimeSpan CrossProcessLockTimeout = TimeSpan.FromSeconds(30);

    private readonly FileAccountingAuditChainAnchor _auditChainAnchor;

    /// <summary>
    /// Serializes whole write cycles against every other process composed over the same snapshot
    /// (W9-GOV-008 criterion 3: cross-process file-append serialization).
    /// </summary>
    /// <remarks>
    /// <para>The base class gate is in-process only, and this store replaces the whole document on
    /// every write — so without this lock, two processes (the browser host and the WPF shell both
    /// compose this store over one data root) could interleave read-modify-write cycles, and the
    /// later snapshot write would silently discard the earlier process's committed audit event or
    /// workspace save. The anchor's monotonic-sequence refusal caught that interleaving after the
    /// fact, but by then the surviving snapshot disagreed with the committed head, and verification
    /// reported the race as tampering (<c>AnchorMismatch</c>) — permanently, on a store nobody
    /// tampered with.</para>
    /// <para>Lock ordering is store lock → base gate → anchor lock, everywhere; the anchor keeps
    /// its own narrower lock (a different file) so its journal stays internally serialized even for
    /// callers that hold no store lock, and no path acquires the two in the reverse order. Plain
    /// reads (<see cref="GetAsync"/>, <see cref="ListAsync"/>) deliberately do not take this lock:
    /// the snapshot is replaced atomically, so a reader always sees a consistent document, at worst
    /// one write old.</para>
    /// </remarks>
    private readonly string _storeLockPath;

    public FileAccountingConfigurationStore(string snapshotPath)
        : this(snapshotPath, anchorPath: null)
    {
    }

    /// <param name="anchorPath">
    /// Where the audit chain's head journal is retained. Defaults to a sidecar beside the snapshot;
    /// a deployment that can offer stronger retention (a WORM mount, a separate volume) points this
    /// at it, which is the whole reason the head is a parameter rather than a fixed sibling.
    /// </param>
    public FileAccountingConfigurationStore(string snapshotPath, string? anchorPath)
        : base(
            string.IsNullOrWhiteSpace(snapshotPath)
                ? throw new ArgumentException("Accounting configuration snapshot path is required.", nameof(snapshotPath))
                : snapshotPath,
            JsonOptions)
    {
        _auditChainAnchor = new FileAccountingAuditChainAnchor(
            string.IsNullOrWhiteSpace(anchorPath)
                ? FileAccountingAuditChainAnchor.AnchorPathFor(snapshotPath)
                : anchorPath);
        _storeLockPath = snapshotPath + ".lock";
    }

    /// <summary>Path of the head journal that anchors this store's audit chain.</summary>
    public string AuditChainAnchorPath => _auditChainAnchor.AnchorPath;

    protected override AccountingConfigurationSnapshot CreateEmptySnapshot()
        => AccountingConfigurationSnapshot.Empty;

    public async Task<AccountingConfigurationWorkspaceDto?> GetAsync(
        string fundProfileId,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        var normalizedFundProfileId = NormalizeFundProfileId(fundProfileId);
        var normalizedTenantId = NormalizeOptional(tenantId);
        var normalizedCompanyId = NormalizeOptional(companyId);
        return await ReadSnapshotAsync(
            snapshot => snapshot.Workspaces.FirstOrDefault(item =>
                string.Equals(item.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase) &&
                item.LedgerBookId == ledgerBookId &&
                string.Equals(NormalizeOptional(item.TenantId), normalizedTenantId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizeOptional(item.CompanyId), normalizedCompanyId, StringComparison.OrdinalIgnoreCase)) is { } workspace
                ? workspace with { FundProfileId = normalizedFundProfileId, AuditTrail = [] }
                : null,
            ct).ConfigureAwait(false);
    }

    /// <remarks>
    /// Holds the cross-process store lock for the whole read-modify-write cycle: a save replaces
    /// the entire document, so one racing another process's append would otherwise write back a
    /// snapshot without the event that append had already committed to the anchor — shortening a
    /// chain whose head says otherwise, which verification must then report as tampering.
    /// </remarks>
    public async Task SaveAsync(AccountingConfigurationWorkspaceDto workspace, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var normalizedFundProfileId = NormalizeFundProfileId(workspace.FundProfileId);
        var normalizedTenantId = NormalizeOptional(workspace.TenantId);
        var normalizedCompanyId = NormalizeOptional(workspace.CompanyId);
        await using var storeLock = await CrossProcessFileLock
            .AcquireAsync(_storeLockPath, CrossProcessLockTimeout, ct).ConfigureAwait(false);
        await UpdateSnapshotAsync(
            snapshot =>
            {
                var workspaces = snapshot.Workspaces
                    .Where(item =>
                        !string.Equals(item.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase) ||
                        item.LedgerBookId != workspace.LedgerBookId ||
                        !string.Equals(NormalizeOptional(item.TenantId), normalizedTenantId, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(NormalizeOptional(item.CompanyId), normalizedCompanyId, StringComparison.OrdinalIgnoreCase))
                    .Append(workspace with
                    {
                        FundProfileId = normalizedFundProfileId,
                        TenantId = normalizedTenantId,
                        CompanyId = normalizedCompanyId,
                        AuditTrail = []
                    })
                    .OrderBy(item => item.FundProfileId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.LedgerBookId?.ToString("D") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.TenantId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.CompanyId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return snapshot with { Workspaces = workspaces };
            },
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Appends an audit event and extends the tamper-evident chain over it.
    /// </summary>
    /// <remarks>
    /// <para>Fails closed: the retained chain is verified before the append, so a mutated, reordered,
    /// truncated, or rolled-back history cannot quietly acquire valid-looking successors. Reads stay
    /// available on a failed chain so an operator can still investigate what was retained —
    /// verification is surfaced through <see cref="VerifyAuditChainAsync"/> rather than by blinding
    /// the store.</para>
    /// <para>The head is advanced write-ahead (declare → write snapshot → commit) against a journal
    /// held outside the snapshot, because this store replaces the whole document on every write: a
    /// head stored inside it would be removed by the same replacement that removed the events, and
    /// what remained would verify perfectly. See <see cref="FileAccountingAuditChainAnchor"/>.</para>
    /// <para>Serialized across processes: the whole cycle runs under a lock file every composition
    /// over this snapshot shares, so concurrent appends from the browser host and the WPF shell
    /// chain one after the other rather than interleaving (W9-GOV-008 criterion 3).</para>
    /// <para>Idempotent on <see cref="AccountingActionAuditEventDto.AuditEventId"/>: an append whose
    /// event is already retained does nothing. This is not a convenience. The chain requires each
    /// link to claim a distinct event, so a second append of one id produces a history that can
    /// never verify again — and a retry is not hypothetical, it is what
    /// <c>RecoverPendingAuditAsync</c> does after a crash between the mutation and its audit. That
    /// recovery has a pre-check of its own, but it asks a filtered read, so a normalization or scope
    /// difference makes it miss; only the store sees the whole history.</para>
    /// </remarks>
    /// <exception cref="AccountingAuditChainIntegrityException">The retained chain does not verify.</exception>
    public async Task AppendAsync(AccountingActionAuditEventDto auditEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        var normalized = auditEvent with { FundProfileId = NormalizeOptional(auditEvent.FundProfileId) };

        // The cross-process lock spans the whole cycle — verify, link, declare, write, commit — so
        // an append from another process serializes behind this one and then chains onto its
        // result, instead of interleaving with it. Without this, both processes could pass the
        // anchor's declare (a pending declaration at one sequence supersedes another, by design,
        // because that is what crash recovery looks like), and the later snapshot write would
        // replace the earlier one — losing its event while the anchor retained its hash, which
        // verification then reports as tampering. The anchor's monotonic refusal stays as the
        // backstop for a writer that bypasses this store.
        await using var storeLock = await CrossProcessFileLock
            .AcquireAsync(_storeLockPath, CrossProcessLockTimeout, ct).ConfigureAwait(false);

        await UpdateSnapshotAsync<AccountingAuditChainLink?>(
            async (snapshot, token) =>
            {
                // Read the head under the store gate: reading it beforehand would race this store's
                // own write.
                var anchor = await _auditChainAnchor.ReadHeadAsync(token).ConfigureAwait(false);
                var verification = AccountingAuditChain.Verify(
                    snapshot.AuditChain,
                    snapshot.AuditEvents,
                    anchor);

                // An interrupted append is resumed here rather than refused. Write-ahead ordering
                // means InterruptedAppend can only mean "sequence N was declared and its snapshot
                // write never landed", so no event occupies N and abandoning that declaration
                // discards nothing -- the mutation it would have audited is the pending marker's
                // business, not the chain's. Refusing instead would be far worse than the crash it
                // reports: this append would throw, and so would every append after it, leaving one
                // power cut to permanently stop the audit log of the posture that runs whenever
                // PostgreSQL is not configured.
                //
                // This is narrow on purpose. InterruptedAppend is only returned when the declared
                // sequence is exactly the slot this append will take, and only once VerifyLinks has
                // already passed, so accepting it relaxes the anchor divergence alone and never
                // chain integrity: a rollback or truncated tail is AnchorMismatch and still throws.
                if (!verification.IsValid
                    && verification.Status != AccountingAuditChainStatus.InterruptedAppend)
                {
                    throw new AccountingAuditChainIntegrityException(verification);
                }

                // Deliberately after the verification above, matching the PostgreSQL posture. A
                // repeat is a no-op, but reporting success for one on a chain that does not verify
                // would answer "appended" about a store whose history is broken -- and this method
                // fails closed, which has to mean before every outcome, not just the writing ones.
                var retained = snapshot.AuditEvents
                    .FirstOrDefault(item => item.AuditEventId == normalized.AuditEventId);
                if (retained is not null)
                {
                    // Same id, same content: a repeat of an append that already landed. Returning
                    // the snapshot unchanged leaves the chain exactly as it was, which is what a
                    // retry of a completed operation should do.
                    if (string.Equals(
                            AccountingAuditChain.ComputePayloadHash(retained),
                            AccountingAuditChain.ComputePayloadHash(normalized),
                            StringComparison.Ordinal))
                    {
                        return (snapshot, null);
                    }

                    // Same id, different content: two distinct events claiming one identity. Both
                    // available answers are bad -- appending breaks verification permanently, and
                    // dropping it loses an audit record -- so neither is taken silently.
                    throw new InvalidOperationException(
                        $"Audit event '{normalized.AuditEventId.ToString("D", CultureInfo.InvariantCulture)}' "
                        + "is already retained with different content. Appending it would leave two links "
                        + "claiming one event and the chain could never verify again.");
                }

                // First chained append on a history that predates chaining: record how many events
                // are outside the chain rather than letting them look protected by it.
                var chain = snapshot.AuditChain
                    ?? AccountingAuditChainState.Begin(snapshot.AuditEvents.Count);

                var link = AccountingAuditChain.CreateLink(chain, normalized);

                var events = snapshot.AuditEvents
                    .Append(normalized)
                    .OrderByDescending(item => item.RecordedAtUtc)
                    .ThenBy(item => item.AuditEventId)
                    .ToArray();

                return (
                    snapshot with
                    {
                        AuditEvents = events,
                        AuditChain = chain with { Links = [.. chain.Links, link] },
                    },
                    link);
            },
            // The genesis boundary rides both anchor writes. It is snapshot-resident everywhere
            // else, and the retained-event count is checked against it, so without a copy outside
            // the snapshot an actor could raise it to cover an injected unlinked event.
            beforeWrite: async (written, link, token) =>
                await _auditChainAnchor.DeclareAsync(
                    link!.Sequence,
                    link.EntryHash,
                    written.AuditChain!.GenesisSequence,
                    written.AuditChain.PreChainEventCount,
                    token).ConfigureAwait(false),
            afterWrite: async (written, link, token) =>
                await _auditChainAnchor.CommitAsync(
                    link!.Sequence,
                    link.EntryHash,
                    written.AuditChain!.GenesisSequence,
                    written.AuditChain.PreChainEventCount,
                    token).ConfigureAwait(false),
            // A repeat produces no link, and must therefore write nothing at all. Returning the
            // unchanged snapshot through the write path would replace the retained file with this
            // cycle's copy of it -- losing any event another process appended in between, while the
            // external anchor stayed ahead of the chain. Skipping the write also skips the anchor
            // hooks, which is right on its own terms: there is no sequence to declare or commit, and
            // declaring one would advance the journal past a slot no event occupies.
            shouldWrite: link => link is not null,
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies the retained audit chain against its events and its external head.
    /// </summary>
    /// <remarks>
    /// This is the verification tooling seam: it reports mutation, reordering, a removed event, a
    /// rollback or truncated tail, and — distinctly from all of those — an append interrupted by a
    /// crash, so an operator is not told "tampering" when the cause was a power cut.
    /// </remarks>
    public async Task<AccountingAuditChainVerification> VerifyAuditChainAsync(CancellationToken ct = default)
    {
        // Verification reads two files, and only the store lock makes that pair atomic against a
        // writer: an append in flight elsewhere holds the same lock from declare through commit, so
        // a verification that read the anchor between those writes would see a head the snapshot
        // has not caught up with and report an interrupted append — or worse, read them in the
        // other order and report a rollback — about a store that is merely busy.
        await using var storeLock = await CrossProcessFileLock
            .AcquireAsync(_storeLockPath, CrossProcessLockTimeout, ct).ConfigureAwait(false);

        var anchor = await _auditChainAnchor.ReadHeadAsync(ct).ConfigureAwait(false);
        return await ReadSnapshotAsync(
            snapshot => AccountingAuditChain.Verify(snapshot.AuditChain, snapshot.AuditEvents, anchor),
            ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AccountingActionAuditEventDto>> ListAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        var normalizedFundProfileId = NormalizeOptional(fundProfileId);
        var normalizedTenantId = NormalizeOptional(tenantId);
        var normalizedCompanyId = NormalizeOptional(companyId);
        return await ReadSnapshotAsync(
            snapshot => snapshot.AuditEvents
                .Where(item => string.IsNullOrWhiteSpace(normalizedFundProfileId) ||
                               string.Equals(item.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase))
                .Where(item => !ledgerBookId.HasValue || item.LedgerBookId == ledgerBookId)
                .Where(item => normalizedTenantId is null ||
                               string.Equals(NormalizeOptional(item.TenantId), normalizedTenantId, StringComparison.OrdinalIgnoreCase))
                .Where(item => normalizedCompanyId is null || string.Equals(item.CompanyId, normalizedCompanyId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.RecordedAtUtc)
                .ThenBy(item => item.AuditEventId)
                .ToArray(),
            ct).ConfigureAwait(false);
    }

    private static string NormalizeFundProfileId(string? value)
        => string.IsNullOrWhiteSpace(value) ? "default-fund" : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <param name="AuditChain">
    /// The tamper-evident chain over <paramref name="AuditEvents"/>, in append order. Null in a
    /// snapshot written before chaining existed — those events are pre-chain and are reported as
    /// such by verification rather than presented as protected.
    /// </param>
    public sealed record AccountingConfigurationSnapshot(
        IReadOnlyList<AccountingConfigurationWorkspaceDto> Workspaces,
        IReadOnlyList<AccountingActionAuditEventDto> AuditEvents,
        AccountingAuditChainState? AuditChain = null)
    {
        public static AccountingConfigurationSnapshot Empty { get; } = new([], []);
    }
}
