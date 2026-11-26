using Meridian.Application.Composition.Startup;
using Meridian.Application.UI;
using Meridian.Core.Config;
using Meridian.DataIntegration.Monitoring;
using Meridian.Infrastructure.Adapters.Failover;
using Serilog;

namespace Meridian.ProcessTestHelper;

internal static partial class Program
{
    private static async Task<int> WatchCollectorConfigurationAsync(IReadOnlyList<string> args)
    {
        RequireArgumentCount(args, 3);
        var store = new ConfigStore(args[1]);
        var startup = store.LoadRequired();
        var dataSources = startup.DataSources ?? throw new InvalidOperationException("Failover settings required.");
        using var monitor = new ConnectionHealthMonitor();
        using var failover = new StreamingFailoverService(monitor);
        var providers = dataSources.Sources?.Select(source => source.Id).ToArray() ?? [];
        foreach (var id in providers)
            failover.RegisterProvider(id);
        failover.Start(dataSources);
        using var reconciler = new ProviderConfigReconciler(failover, providers, startup, Log.Logger);
        await using var runtime = new CollectorConfigurationRuntime(store, reconciler, startup, Log.Logger, CancellationToken.None);
        await using var watcher = new ConfigWatcher(store.ConfigPath, TimeSpan.FromMilliseconds(35));
        watcher.ConfigChanged += _ => runtime.SignalReload();
        watcher.Start();
        runtime.SignalReload();
        await File.WriteAllTextAsync(args[2], "watching");
        await Task.Delay(Timeout.InfiniteTimeSpan);
        return 0;
    }
}
