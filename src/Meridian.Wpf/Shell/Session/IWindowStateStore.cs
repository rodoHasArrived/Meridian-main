using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Wpf.Shell.Session;

/// <summary>
/// Loads and asynchronously saves retained desktop window state.
/// </summary>
public interface IWindowStateStore
{
    DesktopWindowState? Load();

    Task SaveAsync(DesktopWindowState state, CancellationToken ct = default);
}
