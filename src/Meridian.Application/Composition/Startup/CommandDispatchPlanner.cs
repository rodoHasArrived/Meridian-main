using Meridian.Application.Commands;
using Meridian.Application.Config;
using Meridian.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Meridian.Application.Composition.Startup;

internal sealed class CommandDispatchPlan(CommandDispatcher dispatcher, ServiceProvider? ownedServices = null) : IDisposable
{
    public CommandDispatcher Dispatcher { get; } = dispatcher;

    public void Dispose() => ownedServices?.Dispose();
}

internal static class CommandDispatchPlanner
{
    public static CommandDispatchPlan Create(
        AppConfig cfg,
        string cfgPath,
        ILogger log,
        ConfigurationService configService)
    {
        var services = new ServiceCollection();
        services.AddCommandDispatchServices(cfg, cfgPath, log, configService);
        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<CommandDispatchLifetimeProbe>();
        return new CommandDispatchPlan(provider.GetRequiredService<CommandDispatcher>(), provider);
    }
}
