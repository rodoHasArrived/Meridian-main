using Meridian.Application.Composition.Startup;

namespace Meridian;

public partial class Program
{
    public static Task<int> Main(string[] args)
        => SharedStartupBootstrapper.RunAsync(args, DashboardServerBridge.Create);
}

// Partial Program class to support WebApplicationFactory in integration tests
// The main Program class remains available for test host discovery.
public partial class Program { }
