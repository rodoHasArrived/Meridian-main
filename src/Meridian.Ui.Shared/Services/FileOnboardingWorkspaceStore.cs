using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Onboarding;
using Meridian.Storage.Store;

namespace Meridian.Ui.Shared.Services;

/// <summary>Atomic, version-checked retention of onboarding inputs, history and frozen packets.</summary>
public sealed class FileOnboardingWorkspaceStore(string snapshotPath)
    : JsonFileSnapshotStore<FileOnboardingWorkspaceStore.Snapshot>(snapshotPath, JsonOptions), IOnboardingWorkspaceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public sealed record Snapshot(IReadOnlyList<OnboardingWorkspaceDto> Workspaces);

    protected override Snapshot CreateEmptySnapshot()
        => File.Exists(SnapshotPath)
            ? throw new InvalidDataException("Retained onboarding storage must not contain a null document.")
            : new([]);

    protected override Snapshot OnSnapshotLoaded(Snapshot snapshot)
    {
        if (snapshot.Workspaces is null || snapshot.Workspaces.Any(workspace => workspace?.Scope is null ||
                string.IsNullOrWhiteSpace(workspace.Scope.TenantId) || string.IsNullOrWhiteSpace(workspace.Scope.CompanyId)) ||
            snapshot.Workspaces.Select(workspace => workspace.WorkspaceId).Distinct(StringComparer.Ordinal).Count() != snapshot.Workspaces.Count)
        {
            throw new InvalidDataException("Retained onboarding storage contains invalid or ambiguous scope.");
        }

        foreach (var workspace in snapshot.Workspaces)
            OnboardingWorkspaceRetention.VerifyIntegrity(workspace);

        return snapshot;
    }

    public Task<IReadOnlyList<OnboardingWorkspaceDto>> ListAsync(string tenantId, string companyId, CancellationToken ct = default)
    {
        RequireScope(tenantId, companyId);
        return ReadSnapshotAsync<IReadOnlyList<OnboardingWorkspaceDto>>(snapshot => snapshot.Workspaces
            .Where(workspace => Matches(workspace, tenantId, companyId))
            .OrderByDescending(workspace => workspace.UpdatedAtUtc).ToArray(), ct);
    }

    public Task<OnboardingWorkspaceDto?> GetAsync(string tenantId, string companyId, string workspaceId, CancellationToken ct = default)
    {
        RequireScope(tenantId, companyId);
        return ReadSnapshotAsync(snapshot => snapshot.Workspaces.SingleOrDefault(workspace =>
            workspace.WorkspaceId == workspaceId && Matches(workspace, tenantId, companyId)), ct);
    }

    public Task<IReadOnlyList<OnboardingWorkspaceDto>> ListByLedgerBookAsync(
        string tenantId, string? companyId, Guid ledgerBookId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new OnboardingValidationException("A tenant is required for onboarding storage.");
        return ReadSnapshotAsync<IReadOnlyList<OnboardingWorkspaceDto>>(snapshot => snapshot.Workspaces
            .Where(workspace => workspace.Scope.TenantId == tenantId && workspace.Scope.LedgerBookId == ledgerBookId
                && (companyId is null || workspace.Scope.CompanyId == companyId))
            .OrderBy(workspace => workspace.CreatedAtUtc).ToArray(), ct);
    }

    public async Task SaveAsync(OnboardingWorkspaceDto workspace, int? expectedVersion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        RequireScope(workspace.Scope.TenantId, workspace.Scope.CompanyId);
        // Serialize before awaiting so callers cannot mutate nested lists during persistence.
        var retained = OnboardingWorkspaceRetention.Detach(workspace);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(SnapshotPath))!);
        // Serialize writers across store instances and processes, in addition to the base store's gate.
        await using var lease = await AcquireLeaseAsync(ct).ConfigureAwait(false);
        await UpdateSnapshotAsync(snapshot =>
        {
            var current = snapshot.Workspaces.SingleOrDefault(item => item.WorkspaceId == retained.WorkspaceId);
            OnboardingWorkspaceRetention.RequireValidTransition(current, retained, expectedVersion);

            // Book-level exclusivity is checked inside the same writer lease as the save, so two concurrent
            // designations of one ledger book cannot both pass a pre-save read.
            if (retained.ShadowDesignation?.State == OnboardingShadowStates.Shadow && current?.ShadowDesignation is null
                && snapshot.Workspaces.Any(item => item.WorkspaceId != retained.WorkspaceId
                    && item.Scope.TenantId == retained.Scope.TenantId
                    && item.Scope.LedgerBookId == retained.Scope.LedgerBookId
                    && OnboardingWorkspaceRetention.HoldsLedgerBook(item.ShadowDesignation?.State)))
            {
                throw new OnboardingConcurrencyException(OnboardingWorkspaceRetention.ShadowBookHeldMessage);
            }

            return new Snapshot(snapshot.Workspaces.Where(item => item.WorkspaceId != retained.WorkspaceId).Append(retained).ToArray());
        }, ct).ConfigureAwait(false);
    }

    private async Task<FileStream> AcquireLeaseAsync(CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(SnapshotPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow - started < TimeSpan.FromSeconds(10))
            {
                await Task.Delay(25, ct).ConfigureAwait(false);
            }
        }
    }

    private static bool Matches(OnboardingWorkspaceDto workspace, string tenantId, string companyId)
        => workspace.Scope.TenantId == tenantId && workspace.Scope.CompanyId == companyId;

    private static void RequireScope(string tenantId, string companyId)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(companyId))
        {
            throw new OnboardingValidationException("A tenant and company are required for onboarding storage.");
        }
    }
}
