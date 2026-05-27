using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Wpf.Shell.Session;

public interface IWindowStateStore
{
    DesktopWindowState? Load();

    Task SaveAsync(DesktopWindowState state, CancellationToken ct = default);
}
