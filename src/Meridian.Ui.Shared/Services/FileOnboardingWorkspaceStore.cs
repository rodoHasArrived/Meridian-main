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
        {
            if (workspace.Comparisons is null || workspace.Packets is null)
                throw new InvalidDataException("Retained onboarding history is missing.");
            string? previousHash = null;
            var sequence = 1;
            foreach (var comparison in workspace.Comparisons)
            {
                if (comparison.Sequence != sequence++ || comparison.PreviousContentHash != previousHash ||
                    !OnboardingWorkspaceService.VerifyComparisonHash(comparison))
                    throw new InvalidDataException("Retained onboarding comparison history failed its integrity check.");
                previousHash = comparison.ContentHash;
            }

            if (workspace.Packets.Any(packet => packet.ContentHash != OnboardingWorkspaceService.HashPacketContent(packet.Content)))
                throw new InvalidDataException("A retained onboarding packet failed its integrity check.");
        }

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

    public async Task SaveAsync(OnboardingWorkspaceDto workspace, int? expectedVersion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        RequireScope(workspace.Scope.TenantId, workspace.Scope.CompanyId);
        // Serialize before awaiting so callers cannot mutate nested lists during persistence.
        var retained = JsonSerializer.Deserialize<OnboardingWorkspaceDto>(JsonSerializer.Serialize(workspace, JsonOptions), JsonOptions)!;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(SnapshotPath))!);
        // Serialize writers across store instances and processes, in addition to the base store's gate.
        await using var lease = await AcquireLeaseAsync(ct).ConfigureAwait(false);
        await UpdateSnapshotAsync(snapshot =>
        {
            var current = snapshot.Workspaces.SingleOrDefault(item => item.WorkspaceId == retained.WorkspaceId);
            if (current is null ? expectedVersion is not null : expectedVersion != current.Version)
            {
                throw new OnboardingConcurrencyException("The onboarding workspace changed. Refresh before saving.");
            }

            if (retained.Version != (current?.Version ?? 0) + 1)
            {
                throw new OnboardingConcurrencyException("Onboarding versions must advance exactly once.");
            }

            if (current is not null)
            {
                if (!Equal(current.Scope, retained.Scope) || current.OwnerId != retained.OwnerId ||
                    current.Name != retained.Name || current.CreatedAtUtc != retained.CreatedAtUtc)
                {
                    throw new OnboardingValidationException("The retained onboarding scope and owner cannot be replaced.");
                }

                RequirePrefix(current.Comparisons, retained.Comparisons);
                RequirePrefix(current.CriteriaHistory, retained.CriteriaHistory);
                RequirePrefix(current.Assignments, retained.Assignments);
                RequirePrefix(current.Reviews, retained.Reviews);
                RequirePrefix(current.Packets, retained.Packets);
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

    private static bool Equal<T>(T left, T right)
        => JsonSerializer.Serialize(left, JsonOptions) == JsonSerializer.Serialize(right, JsonOptions);

    private static void RequirePrefix<T>(IReadOnlyList<T> previous, IReadOnlyList<T> next)
    {
        if (next.Count < previous.Count || previous.Where((item, index) => !Equal(item, next[index])).Any())
        {
            throw new OnboardingValidationException("Retained onboarding history and packets are immutable.");
        }
    }
}
