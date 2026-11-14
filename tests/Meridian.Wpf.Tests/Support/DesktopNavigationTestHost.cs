using Meridian.Wpf.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Tests.Support;

/// <summary>
/// Owns a smoke test's root provider and the singleton navigation/workspace state that uses it.
/// Construct and dispose on the test's WPF dispatcher after closing its windows.
/// </summary>
internal sealed class DesktopNavigationTestHost : IDisposable
{
    private bool _disposed;

    public DesktopNavigationTestHost(IServiceCollection services)
    {
        NavigationService.Instance.ResetForTests();
        WorkspaceService.Instance.ResetForTests();
        Services = services.BuildServiceProvider();
        NavigationService.Instance.SetServiceProvider(Services);
    }

    public ServiceProvider Services { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            NavigationService.Instance.ResetForTests();
            WorkspaceService.Instance.ResetForTests();
        }
        finally
        {
            Services.Dispose();
        }
    }
}
