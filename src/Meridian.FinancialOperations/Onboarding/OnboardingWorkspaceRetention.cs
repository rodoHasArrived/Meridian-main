using System.Text.Json;
using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.Onboarding;

/// <summary>
/// Retention rules every <see cref="IOnboardingWorkspaceStore"/> applies, whatever its storage medium: retained
/// hash chains must verify on read, and a save may only append history and advance the version once.
/// </summary>
public static class OnboardingWorkspaceRetention
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Refuses a retained workspace whose scope, comparison chain, packets, certificates or audit fail.</summary>
    public static void VerifyIntegrity(OnboardingWorkspaceDto? workspace)
    {
        if (workspace?.Scope is null || string.IsNullOrWhiteSpace(workspace.Scope.TenantId)
            || string.IsNullOrWhiteSpace(workspace.Scope.CompanyId) || string.IsNullOrWhiteSpace(workspace.WorkspaceId))
            throw new InvalidDataException("Retained onboarding storage contains invalid or ambiguous scope.");
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
        if ((workspace.Certificates ?? []).Any(certificate =>
                certificate.ContentHash != OnboardingWorkspaceService.HashCertificateContent(certificate.Content)))
            throw new InvalidDataException("A retained parallel-run certificate failed its integrity check.");
        if (!OnboardingWorkspaceService.VerifyShadowAudit(workspace.ShadowAudit))
            throw new InvalidDataException("The retained shadow-mode audit trail failed its integrity check.");
    }

    /// <summary>
    /// Refuses a save that does not follow the retained copy: the expected version must match, the version must
    /// advance exactly once, scope and owner are fixed, retained history only grows, and final decisions stay final.
    /// </summary>
    public static void RequireValidTransition(OnboardingWorkspaceDto? current, OnboardingWorkspaceDto retained, int? expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(retained);
        if (current is null ? expectedVersion is not null : expectedVersion != current.Version)
            throw new OnboardingConcurrencyException("The onboarding workspace changed. Refresh before saving.");
        if (retained.Version != (current?.Version ?? 0) + 1)
            throw new OnboardingConcurrencyException("Onboarding versions must advance exactly once.");
        if (current is null)
            return;

        if (!Equal(current.Scope, retained.Scope) || current.OwnerId != retained.OwnerId ||
            current.Name != retained.Name || current.CreatedAtUtc != retained.CreatedAtUtc)
            throw new OnboardingValidationException("The retained onboarding scope and owner cannot be replaced.");

        RequirePrefix(current.Comparisons, retained.Comparisons);
        RequirePrefix(current.CriteriaHistory, retained.CriteriaHistory);
        RequirePrefix(current.Assignments, retained.Assignments);
        RequirePrefix(current.Reviews, retained.Reviews);
        RequirePrefix(current.Packets, retained.Packets);
        RequirePrefix(current.Explanations ?? [], retained.Explanations ?? []);
        RequirePrefix(current.Certificates ?? [], retained.Certificates ?? []);
        RequirePrefix(current.ShadowAudit ?? [], retained.ShadowAudit ?? []);
        if (current.Promotion is not null && !Equal(current.Promotion, retained.Promotion))
            throw new OnboardingValidationException("A recorded promotion decision is immutable.");
        if (current.ShadowDesignation?.State is OnboardingShadowStates.Promoted or OnboardingShadowStates.Withdrawn
            && !Equal(current.ShadowDesignation, retained.ShadowDesignation))
            throw new OnboardingValidationException("A promoted or withdrawn shadow designation is final.");
    }

    /// <summary>The message for a designation refused because another workspace holds the book.</summary>
    public const string ShadowBookHeldMessage =
        "Another onboarding workspace already holds this ledger book in shadow mode or promoted it. Refresh before retrying.";

    /// <summary>True when the state keeps a ledger book exclusive to one workspace.</summary>
    public static bool HoldsLedgerBook(string? shadowState)
        => shadowState is OnboardingShadowStates.Shadow or OnboardingShadowStates.Promoted;

    /// <summary>A detached copy, so callers cannot mutate nested lists during persistence.</summary>
    public static OnboardingWorkspaceDto Detach(OnboardingWorkspaceDto workspace)
        => Deserialize(Serialize(workspace));

    public static string Serialize(OnboardingWorkspaceDto workspace) => JsonSerializer.Serialize(workspace, JsonOptions);

    public static OnboardingWorkspaceDto Deserialize(string json)
        => JsonSerializer.Deserialize<OnboardingWorkspaceDto>(json, JsonOptions)
            ?? throw new InvalidDataException("Retained onboarding storage must not contain a null document.");

    private static bool Equal<T>(T left, T right)
        => JsonSerializer.Serialize(left, JsonOptions) == JsonSerializer.Serialize(right, JsonOptions);

    private static void RequirePrefix<T>(IReadOnlyList<T> previous, IReadOnlyList<T> next)
    {
        if (next.Count < previous.Count || previous.Where((item, index) => !Equal(item, next[index])).Any())
            throw new OnboardingValidationException("Retained onboarding history and packets are immutable.");
    }
}
