using Meridian.Contracts.Api;
using Meridian.Wpf.Services;
using Meridian.Ui.Services.Services;

namespace Meridian.Wpf.Features.Settings.Shell;

public interface ISettingsWorkspaceShellSnapshotService
{
    Task<SettingsWorkspaceShellSnapshot> LoadAsync(CancellationToken cancellationToken = default);
}

public sealed class SettingsWorkspaceShellSnapshotService : ISettingsWorkspaceShellSnapshotService, IWorkspaceScopedService
{
    private readonly SettingsConfigurationService _settingsConfigurationService;

    public SettingsWorkspaceShellSnapshotService()
        : this(SettingsConfigurationService.Instance)
    {
    }

    internal SettingsWorkspaceShellSnapshotService(SettingsConfigurationService settingsConfigurationService)
    {
        _settingsConfigurationService = settingsConfigurationService ?? throw new ArgumentNullException(nameof(settingsConfigurationService));
    }

    public async Task<SettingsWorkspaceShellSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var credentialStatuses = await _settingsConfigurationService.GetProviderCredentialStatusesAsync(cancellationToken).ConfigureAwait(false);
        var configuredCount = credentialStatuses.Count(status => status.State is CredentialState.Configured or CredentialState.NotRequired);
        // A failed or refused status read yields Unavailable rows; that is missing evidence, not an
        // asserted credential gap, so it is counted separately.
        var unavailableCount = credentialStatuses.Count(status => status.State == CredentialState.Unavailable);
        var missingCount = credentialStatuses.Count - configuredCount - unavailableCount;

        return new SettingsWorkspaceShellSnapshot
        {
            ProviderCount = credentialStatuses.Count,
            ConfiguredCredentialCount = configuredCount,
            MissingCredentialCount = missingCount,
            UnavailableCredentialCount = unavailableCount,
            ShellDensityLabel = _settingsConfigurationService.GetShellDensityMode().ToString(),
            AsOfUtc = DateTimeOffset.UtcNow
        };
    }
}
