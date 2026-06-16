using Meridian.Contracts.Api;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Services;

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

    public Task<SettingsWorkspaceShellSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var credentialStatuses = _settingsConfigurationService.GetProviderCredentialStatuses();
        var configuredCount = credentialStatuses.Count(status => status.State is CredentialState.Configured or CredentialState.NotRequired);
        var missingCount = credentialStatuses.Count - configuredCount;

        return Task.FromResult(new SettingsWorkspaceShellSnapshot
        {
            ProviderCount = credentialStatuses.Count,
            ConfiguredCredentialCount = configuredCount,
            MissingCredentialCount = missingCount,
            ShellDensityLabel = _settingsConfigurationService.GetShellDensityMode().ToString(),
            AsOfUtc = DateTimeOffset.UtcNow
        });
    }
}
