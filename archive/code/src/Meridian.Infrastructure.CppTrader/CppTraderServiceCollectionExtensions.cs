using Meridian.Infrastructure.CppTrader.Diagnostics;
using Meridian.Infrastructure.CppTrader.Execution;
using Meridian.Infrastructure.CppTrader.Host;
using Meridian.Infrastructure.CppTrader.Options;
using Meridian.Infrastructure.CppTrader.Providers;
using Meridian.Infrastructure.CppTrader.Replay;
using Meridian.Infrastructure.CppTrader.Symbols;
using Meridian.Infrastructure.CppTrader.Translation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meridian.Infrastructure.CppTrader;

public static class CppTraderServiceCollectionExtensions
{
    public static IServiceCollection AddCppTraderIntegration(this IServiceCollection services, string? configPath = null)
    {
        services.TryAddSingleton<IOptionsMonitor<CppTraderOptions>>(_ => new FileBackedCppTraderOptionsMonitor(configPath));

        services.TryAddSingleton<ICppTraderHostManager, CppTraderHostManager>();
        services.TryAddSingleton<ICppTraderSymbolMapper, CppTraderSymbolMapper>();
        services.TryAddSingleton<ICppTraderSnapshotTranslator, CppTraderSnapshotTranslator>();
        services.TryAddSingleton<ICppTraderExecutionTranslator, CppTraderExecutionTranslator>();
        services.TryAddSingleton<ICppTraderReplayService, CppTraderReplayService>();
        services.TryAddSingleton<ICppTraderItchIngestionService, CppTraderItchIngestionService>();
        services.TryAddSingleton<ICppTraderStatusService, CppTraderStatusService>();
        services.TryAddSingleton<ICppTraderSessionDiagnosticsService, CppTraderSessionDiagnosticsService>();
        services.TryAddSingleton<CppTraderLiveFeedAdapter>();
        services.TryAddSingleton<ILiveFeedAdapter>(sp => sp.GetRequiredService<CppTraderLiveFeedAdapter>());
        services.TryAddSingleton<CppTraderOrderGateway>();
        services.TryAddSingleton<IOrderGateway>(sp =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<CppTraderOptions>>().CurrentValue;
            if (options.Enabled && options.Features.ExecutionEnabled)
                return sp.GetRequiredService<CppTraderOrderGateway>();

            return ActivatorUtilities.CreateInstance<Meridian.Execution.Adapters.PaperTradingGateway>(sp);
        });
        services.TryAddSingleton<CppTraderMarketDataClient>();

        return services;
    }

    private sealed class FileBackedCppTraderOptionsMonitor(string? configPath) : IOptionsMonitor<CppTraderOptions>
    {
        private readonly string _configPath = ResolveConfigPath(configPath);

        public CppTraderOptions CurrentValue => Load();

        public CppTraderOptions Get(string? name) => Load();

        public IDisposable? OnChange(Action<CppTraderOptions, string> listener) => NullDisposable.Instance;

        private CppTraderOptions Load()
        {
            if (!File.Exists(_configPath))
                return new CppTraderOptions();

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(_configPath));
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return new CppTraderOptions();

                if (!doc.RootElement.TryGetProperty(CppTraderOptions.SectionName, out var section))
                    return new CppTraderOptions();

                return section.Deserialize(CppTraderJsonContext.Default.CppTraderOptions) ?? new CppTraderOptions();
            }
            catch
            {
                return new CppTraderOptions();
            }
        }

        private static string ResolveConfigPath(string? configuredPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
                return Path.GetFullPath(configuredPath);

            if (File.Exists("config/appsettings.json"))
                return Path.GetFullPath("config/appsettings.json");

            return Path.GetFullPath("appsettings.json");
        }

        private sealed class NullDisposable : IDisposable
        {
            public static NullDisposable Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
