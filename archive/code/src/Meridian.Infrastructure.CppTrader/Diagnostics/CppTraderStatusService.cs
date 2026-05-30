using Meridian.Infrastructure.CppTrader.Host;

namespace Meridian.Infrastructure.CppTrader.Diagnostics;

public sealed class CppTraderStatusService(ICppTraderHostManager hostManager) : ICppTraderStatusService
{
    private readonly ICppTraderHostManager _hostManager = hostManager;

    public HostHealthSnapshot GetStatus() => _hostManager.GetHealthSnapshot();
}
