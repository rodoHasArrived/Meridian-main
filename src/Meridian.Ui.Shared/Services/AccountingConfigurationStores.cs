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

    private readonly FileAccountingAuditChainAnchor _auditChainAnchor;

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

    public async Task SaveAsync(AccountingConfigurationWorkspaceDto workspace, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var normalizedFundProfileId = NormalizeFundProfileId(workspace.FundProfileId);
        var normalizedTenantId = NormalizeOptional(workspace.TenantId);
        var normalizedCompanyId = NormalizeOptional(workspace.CompanyId);
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
    /// </remarks>
    /// <exception cref="AccountingAuditChainIntegrityException">The retained chain does not verify.</exception>
    public async Task AppendAsync(AccountingActionAuditEventDto auditEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        var normalized = auditEvent with { FundProfileId = NormalizeOptional(auditEvent.FundProfileId) };

        await UpdateSnapshotAsync<AccountingAuditChainLink>(
            async (snapshot, token) =>
            {
                // Read the head under the store gate: reading it beforehand would race this store's
                // own write. A concurrent writer in another process is caught by the anchor itself,
                // which refuses a sequence that does not advance the journal.
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
            beforeWrite: async (_, link, token) =>
                await _auditChainAnchor.DeclareAsync(link.Sequence, link.EntryHash, token).ConfigureAwait(false),
            afterWrite: async (_, link, token) =>
                await _auditChainAnchor.CommitAsync(link.Sequence, link.EntryHash, token).ConfigureAwait(false),
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
