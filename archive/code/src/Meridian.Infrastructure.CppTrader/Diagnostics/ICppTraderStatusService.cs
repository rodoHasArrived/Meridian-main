using Meridian.Infrastructure.CppTrader.Protocol;

namespace Meridian.Infrastructure.CppTrader.Diagnostics;

public interface ICppTraderStatusService
{
    HostHealthSnapshot GetStatus();
}
