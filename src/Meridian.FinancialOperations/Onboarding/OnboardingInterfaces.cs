using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.Onboarding;

/// <summary>Durable workspace storage. Save must atomically compare the retained version and replace.</summary>
public interface IOnboardingWorkspaceStore
{
    Task<IReadOnlyList<OnboardingWorkspaceDto>> ListAsync(string tenantId, string companyId, CancellationToken ct = default);
    Task<OnboardingWorkspaceDto?> GetAsync(string tenantId, string companyId, string workspaceId, CancellationToken ct = default);
    Task SaveAsync(OnboardingWorkspaceDto workspace, int? expectedVersion, CancellationToken ct = default);
}

/// <summary>Composes existing reconciliation, ledger/NAV, close and evidence services without writes.</summary>
public interface IOnboardingSourceProvider
{
    Task<OnboardingSourceCaptureDto> CaptureAsync(
        OnboardingWorkspaceDto workspace, CaptureOnboardingComparisonRequestDto request, CancellationToken ct = default);
}

public sealed class OnboardingConcurrencyException(string message) : InvalidOperationException(message);

public sealed class OnboardingValidationException(string message) : ArgumentException(message);
