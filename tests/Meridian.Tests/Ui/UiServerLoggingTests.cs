using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Storage.Calendars;
using Meridian.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog.Events;

namespace Meridian.Tests.Ui;

/// <summary>
/// Pins UiServer's logging (ADR-025 rollout): every category still stops below Warning through the
/// default providers, and the host-start inventory lines alone (the Security Master terms capability
/// line and each loaded calendar's line) also reach the process's Serilog logger, from Information,
/// so a workstation-mode host writes them to its log file. Nothing else reaches that logger, even
/// when the configuration lowers the default level. Sequential because the host test swaps the
/// process's Serilog logger and the host environment variables.
/// </summary>
[Trait("Category", "Unit")]
[Collection("Sequential")]
public sealed class UiServerLoggingTests : IDisposable
{
    private const string HostedServiceName = "SecurityMasterTermsCapabilityHostedService";
    private const string CapabilityCategory = "Meridian.Application.Composition.SecurityMasterTermsCapabilityHostedService";
    private const string CalendarCategory = "Meridian.Storage.Calendars.FileBusinessDayCalendarSource";
    private const string ProbeCategory = "Meridian.Tests.Ui.UiServerLoggingProbe";

    private readonly TestArtifactDirectory _artifacts = TestArtifactDirectory.Create(nameof(UiServerLoggingTests));

    public void Dispose() => _artifacts.Dispose();

    [Fact]
    public void TheInventoryCategories_AreTheLoggerCategoriesOfTheCapabilityServiceAndTheCalendarSource()
    {
        UiServer.StartupInventoryLogCategories.Should().Equal(CapabilityCategory, CalendarCategory);
    }

    [Fact]
    public async Task HostStart_WritesTheCapabilityAndCalendarLinesToSerilog_AndOtherCategoriesStillStopBelowWarning()
    {
        WriteZzTestCalendar();
        var serilog = new CollectingSink();
        var defaults = new CapturingLoggerProvider();
        await using var provider = BuildHost(serilog, defaults, configuration: null);

        await StartCapabilityServiceAsync(provider);
        var factory = provider.GetRequiredService<ILoggerFactory>();
        LogFromOtherCategories(factory);

        var events = serilog.Events.ToArray();
        events.Select(static e => (SourceContext(e), e.Level)).Should().Equal(
            (CalendarCategory, LogEventLevel.Information),
            (CapabilityCategory, LogEventLevel.Information));
        events[0].MessageTemplate.Text.Should().StartWith("Loaded business-day calendar {CalendarId} version {CalendarVersion}");
        events[0].Properties["CalendarId"].Should().Be(new ScalarValue("ZZTEST"));
        events[1].MessageTemplate.Text.Should().StartWith("Security Master terms capability: declared mapping {DeclaredMappingVersion}");

        // The default providers keep the Warning floor for every category, these two included.
        defaults.Entries.Should().Equal(
            new CapturedLogEntry("Meridian.UiServer", LogLevel.Warning, "UiServer warning"),
            new CapturedLogEntry("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning, "ASP.NET Core warning"));

        factory.CreateLogger(CapabilityCategory).IsEnabled(LogLevel.Information).Should().BeTrue();
        factory.CreateLogger(CalendarCategory).IsEnabled(LogLevel.Information).Should().BeTrue();
        factory.CreateLogger(CalendarCategory).IsEnabled(LogLevel.Debug).Should().BeFalse();
        factory.CreateLogger("Meridian.UiServer").IsEnabled(LogLevel.Information).Should().BeFalse();
        factory.CreateLogger("Meridian.UiServer").IsEnabled(LogLevel.Warning).Should().BeTrue();
    }

    [Fact]
    public async Task AConfiguredDefaultLevel_ReachesTheDefaultProviders_ButNotSerilog()
    {
        // Logging__LogLevel__Default=Information (as the container deployments set it) lowers the
        // default providers' floor as before; the Serilog provider's own rules still admit only the
        // inventory categories.
        WriteZzTestCalendar();
        var serilog = new CollectingSink();
        var defaults = new CapturingLoggerProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Logging:LogLevel:Default"] = "Information" })
            .Build();
        await using var provider = BuildHost(serilog, defaults, configuration);

        await StartCapabilityServiceAsync(provider);
        LogFromOtherCategories(provider.GetRequiredService<ILoggerFactory>());

        serilog.Events.Select(SourceContext).Should().Equal(CalendarCategory, CapabilityCategory);
        defaults.Entries.Select(static entry => entry.Category).Should().Contain(["Meridian.UiServer", "Microsoft.AspNetCore.Hosting.Diagnostics"]);
        defaults.Entries.Should().Contain(static entry => entry.Category == "Meridian.UiServer" && entry.Level == LogLevel.Information);
    }

    [Fact]
    public async Task TheUiServerConstructor_SendsTheInventoryLinesToTheProcessSerilogLogger()
    {
        // The real host, not ConfigureLogging alone: the constructor must hand the UiServer graph the
        // process's Serilog logger, the one LoggingSetup gives the log file sink.
        var dataRoot = Path.Combine(_artifacts.RootPath, "data");
        WriteZzTestCalendar(dataRoot);
        var configPath = WriteMinimalConfig(dataRoot);
        var serilog = new CollectingSink();
        var previous = Serilog.Log.Logger;
        using var processLogger = new Serilog.LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(serilog).CreateLogger();
        using var environment = DevelopmentEnvironmentScope.Enable();
        Serilog.Log.Logger = processLogger;
        try
        {
            await using var server = new UiServer(configPath, port: 0);
            var services = GetServerApp(server).Services;

            await StartCapabilityServiceAsync(services);
            var other = services.GetRequiredService<ILoggerFactory>().CreateLogger(ProbeCategory);
            other.LogInformation("Probe information");
            other.LogWarning("Probe warning");

            // UiServer also writes some lines to Serilog directly, so only these categories are pinned.
            var inventory = serilog.Events.Where(static e => SourceContext(e) is CapabilityCategory or CalendarCategory).ToArray();
            inventory.Select(static e => (SourceContext(e), e.Level)).Should().Equal(
                (CalendarCategory, LogEventLevel.Information),
                (CapabilityCategory, LogEventLevel.Information));
            inventory[0].Properties["CalendarId"].Should().Be(new ScalarValue("ZZTEST"));
            inventory[1].MessageTemplate.Text.Should().StartWith("Security Master terms capability: declared mapping {DeclaredMappingVersion}");
            serilog.Events.Should().NotContain(static e => SourceContext(e) == ProbeCategory);
        }
        finally
        {
            Serilog.Log.Logger = previous;
        }
    }

    private ServiceProvider BuildHost(CollectingSink serilog, CapturingLoggerProvider defaults, IConfiguration? configuration)
    {
        var logger = new Serilog.LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(serilog).CreateLogger();
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            if (configuration is not null)
            {
                logging.AddConfiguration(configuration.GetSection("Logging"));
            }

            // Stands in for the console, debug and event-source providers WebApplication adds.
            logging.AddProvider(defaults);
            UiServer.ConfigureLogging(logging, logger);
        });
        services.AddSecurityMasterTermsCapability(_ => _artifacts.RootPath);
        return services.BuildServiceProvider();
    }

    private static void LogFromOtherCategories(ILoggerFactory factory)
    {
        var uiServer = factory.CreateLogger("Meridian.UiServer");
        var aspNetCore = factory.CreateLogger("Microsoft.AspNetCore.Hosting.Diagnostics");
        uiServer.LogInformation("UiServer information");
        uiServer.LogWarning("UiServer warning");
        aspNetCore.LogInformation("ASP.NET Core information");
        aspNetCore.LogWarning("ASP.NET Core warning");
    }

    private static async Task StartCapabilityServiceAsync(IServiceProvider provider)
    {
        var hosted = provider.GetServices<IHostedService>()
            .Should().ContainSingle(static service => service.GetType().Name == HostedServiceName).Subject;
        await hosted.StartAsync(CancellationToken.None);
        await hosted.StopAsync(CancellationToken.None);
    }

    private void WriteZzTestCalendar() => WriteZzTestCalendar(_artifacts.RootPath);

    private static void WriteZzTestCalendar(string dataRoot)
    {
        var directory = Path.Combine(dataRoot, FileBusinessDayCalendarSource.DirectoryName);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "ZZTEST.json"), """
            { "calendarId": "ZZTEST", "version": 1, "weekendDays": ["Saturday","Sunday"],
              "coverage": { "from": "2026-01-01", "to": "2026-12-31" }, "holidays": ["2026-01-02"] }
            """);
    }

    private string WriteMinimalConfig(string dataRoot)
    {
        var configPath = Path.Combine(_artifacts.RootPath, "appsettings.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(new
        {
            DataRoot = dataRoot,
            Compress = false,
            DataSource = "Synthetic",
            ApiHost = new { Urls = new[] { "http://127.0.0.1:0" }, ServeWorkstationAssets = false },
            Storage = new { NamingConvention = "BySymbol", DatePartition = "Daily", IncludeProvider = false },
            Backfill = new { Enabled = false, Provider = "stooq", Symbols = new[] { "SPY" } }
        }));
        return configPath;
    }

    private static WebApplication GetServerApp(UiServer server)
    {
        var field = typeof(UiServer).GetField("_app", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(server).Should().BeOfType<WebApplication>().Subject;
    }

    private static string? SourceContext(LogEvent logEvent)
        => logEvent.Properties.TryGetValue("SourceContext", out var value) && value is ScalarValue { Value: string category }
            ? category
            : null;

    // The environment the other UiServer composition tests run under: in-memory governance, Development.
    private sealed class DevelopmentEnvironmentScope : IDisposable
    {
        private static readonly string[] Names = ["MERIDIAN_USE_INMEMORY_GOVERNANCE", "ASPNETCORE_ENVIRONMENT", "DOTNET_ENVIRONMENT"];
        private readonly string?[] _original = Names.Select(Environment.GetEnvironmentVariable).ToArray();

        private DevelopmentEnvironmentScope()
        {
            Environment.SetEnvironmentVariable("MERIDIAN_USE_INMEMORY_GOVERNANCE", "true");
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", Environments.Development);
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", Environments.Development);
        }

        public static DevelopmentEnvironmentScope Enable() => new();

        public void Dispose()
        {
            for (var i = 0; i < Names.Length; i++)
            {
                Environment.SetEnvironmentVariable(Names[i], _original[i]);
            }
        }
    }

    private sealed class CollectingSink : Serilog.Core.ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _events = new();

        public IReadOnlyCollection<LogEvent> Events => _events;

        public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
    }

    private sealed record CapturedLogEntry(string Category, LogLevel Level, string Message);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();

        public IReadOnlyCollection<CapturedLogEntry> Entries => _entries;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => entries.Enqueue(new CapturedLogEntry(category, logLevel, formatter(state, exception)));
        }
    }
}
