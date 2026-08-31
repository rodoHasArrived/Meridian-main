using Meridian.QuantScript.Api;
using Meridian.QuantScript.Compilation;
using Meridian.QuantScript.Documents;
using Meridian.QuantScript.Plotting;
using Meridian.Storage.Store;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Meridian.QuantScript;

/// <summary>
/// Extensions for registering the Meridian QuantScript engine with a host's DI container.
/// </summary>
public static class QuantScriptServiceCollectionExtensions
{
    /// <summary>
    /// Registers the QuantScript engine: compiler, runner, plot queue, data context, and notebook store.
    /// Safe to call multiple times — calls TryAddSingleton so an existing host registration wins.
    /// </summary>
    public static IServiceCollection AddMeridianQuantScript(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return AddMeridianQuantScriptCore(services, services.AddOptions<QuantScriptOptions>());
    }

    /// <summary>
    /// Registers QuantScript and binds its documented <c>QuantScript</c> configuration section.
    /// Invalid isolation or quota settings fail options resolution instead of silently falling back
    /// to defaults.
    /// </summary>
    public static IServiceCollection AddMeridianQuantScript(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = services
            .AddOptions<QuantScriptOptions>()
            .Bind(configuration.GetSection(QuantScriptOptions.SectionName));

        return AddMeridianQuantScriptCore(services, options);
    }

    private static IServiceCollection AddMeridianQuantScriptCore(
        IServiceCollection services,
        OptionsBuilder<QuantScriptOptions> options)
    {
        options.Validate(
            static value => QuantScriptOptions.GetIsolationValidationFailure(value) is null,
            "QuantScript isolation, admission, and host-RPC limits are invalid.");

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<QuantScriptOptions>>().Value;
            return new JsonlMarketDataStore(options.DefaultDataRoot);
        });

        services.AddSingleton<IQuantDataContext, QuantDataContext>();
        services.AddSingleton<PlotQueue>();
        services.AddSingleton<IQuantScriptCompiler, RoslynScriptCompiler>();
        services.AddSingleton<IScriptRunner, ScriptRunner>();
        services.AddSingleton<IQuantScriptNotebookStore>(sp =>
            new QuantScriptNotebookStore(sp.GetRequiredService<IOptions<QuantScriptOptions>>().Value));

        return services;
    }
}
