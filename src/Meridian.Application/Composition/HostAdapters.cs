using Meridian.Application.UI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.Composition;

/// <summary>
/// Adapter interface for host-specific behavior.
/// Each host type (console, web, desktop) can customize endpoint exposure
/// while sharing the same service graph from <see cref="ServiceCompositionRoot"/>
/// and the same startup abstractions used by <see cref="HostStartup"/>.
/// </summary>
[ImplementsAdr("ADR-001", "Host adapters for mode-specific behavior")]
public interface IHostAdapter
{
    /// <summary>
    /// Configures additional services specific to this host type.
    /// Called after the common services are registered.
    /// </summary>
    void ConfigureServices(IServiceCollection services);

    /// <summary>
    /// Configures the application (routes, middleware, etc.) after build.
    /// </summary>
    void ConfigureApplication(WebApplication app);
}

/// <summary>
/// Host adapter for console/headless mode.
/// Minimal HTTP surface - only health endpoints if HTTP is enabled.
/// </summary>
public sealed class ConsoleHostAdapter : IHostAdapter
{
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    private readonly bool _enableHealthEndpoints;

    public ConsoleHostAdapter(bool enableHealthEndpoints = true)
    {
        _enableHealthEndpoints = enableHealthEndpoints;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        // Console mode typically doesn't need additional services
    }

    public void ConfigureApplication(WebApplication app)
    {
        if (_enableHealthEndpoints)
        {
            MapMinimalHealthEndpoints(app);
        }
    }

    private void MapMinimalHealthEndpoints(WebApplication app)
    {
        app.MapGet("/health", () =>
        {
            var uptime = DateTimeOffset.UtcNow - _startTime;
            return Results.Json(new
            {
                status = "healthy",
                timestamp = DateTimeOffset.UtcNow,
                uptime = uptime.ToString()
            });
        });

        app.MapGet("/healthz", () => Results.Ok("healthy"));
        app.MapGet("/ready", () => Results.Ok("ready"));
        app.MapGet("/live", () => Results.Ok("alive"));
    }
}

/// <summary>
/// Host adapter for desktop mode (WPF).
/// Provides both collector functionality and embedded HTTP server.
/// </summary>
public sealed class DesktopHostAdapter : IHostAdapter
{
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    private readonly int _port;

    public DesktopHostAdapter(int port = 8080)
    {
        _port = port;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        // Desktop mode may need additional UI-specific services
    }

    public void ConfigureApplication(WebApplication app)
    {
        // Health endpoints
        app.MapGet("/health", () =>
        {
            var uptime = DateTimeOffset.UtcNow - _startTime;
            return Results.Json(new
            {
                status = "healthy",
                timestamp = DateTimeOffset.UtcNow,
                uptime = uptime.ToString(),
                mode = "desktop"
            });
        });

        app.MapGet("/healthz", () => Results.Ok("healthy"));

    }
}

/// <summary>
/// Host adapter for streaming/data collection mode (CLI headless with active data collection).
/// This adapter is used when the application is collecting streaming market data.
/// </summary>
public sealed class StreamingHostAdapter : IHostAdapter
{
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    private readonly bool _enableHealthEndpoints;
    private readonly int? _httpPort;

    public StreamingHostAdapter(bool enableHealthEndpoints = true, int? httpPort = null)
    {
        _enableHealthEndpoints = enableHealthEndpoints;
        _httpPort = httpPort;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        // Streaming mode uses full service set from composition root
        // No additional services needed beyond what's in CompositionOptions.Streaming
    }

    public void ConfigureApplication(WebApplication app)
    {
        if (_enableHealthEndpoints)
        {
            MapStreamingHealthEndpoints(app);
        }
    }

    private void MapStreamingHealthEndpoints(WebApplication app)
    {
        app.MapGet("/health", () =>
        {
            var uptime = DateTimeOffset.UtcNow - _startTime;
            return Results.Json(new
            {
                status = "healthy",
                mode = "streaming",
                timestamp = DateTimeOffset.UtcNow,
                uptime = uptime.ToString()
            });
        });

        app.MapGet("/healthz", () => Results.Ok("healthy"));
        app.MapGet("/ready", () => Results.Ok("ready"));
        app.MapGet("/live", () => Results.Ok("alive"));
    }
}

/// <summary>
/// Host adapter for backfill-only mode.
/// This adapter is used when running historical data backfill operations.
/// </summary>
public sealed class BackfillHostAdapter : IHostAdapter
{
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;

    public void ConfigureServices(IServiceCollection services)
    {
        // Backfill mode uses BackfillOnly service set from composition root
        // No additional services needed
    }

    public void ConfigureApplication(WebApplication app)
    {
        // Backfill mode typically doesn't need HTTP endpoints
        // But we can add health endpoints if needed
        app.MapGet("/health", () =>
        {
            var uptime = DateTimeOffset.UtcNow - _startTime;
            return Results.Json(new
            {
                status = "healthy",
                mode = "backfill",
                timestamp = DateTimeOffset.UtcNow,
                uptime = uptime.ToString()
            });
        });
    }
}

/// <summary>
/// Builder for creating configured web applications using the composition root and host adapters.
/// </summary>
public sealed class HostBuilder
{
    private readonly WebApplicationBuilder _builder;
    private readonly CompositionOptions _compositionOptions;
    private IHostAdapter? _adapter;

    private HostBuilder(WebApplicationBuilder builder, CompositionOptions compositionOptions)
    {
        _builder = builder;
        _compositionOptions = compositionOptions;
    }

    /// <summary>
    /// Creates a new host builder with the specified composition options.
    /// </summary>
    public static HostBuilder Create(string[]? args = null, CompositionOptions? options = null)
    {
        var builder = WebApplication.CreateBuilder(args ?? Array.Empty<string>());
        return new HostBuilder(builder, options ?? CompositionOptions.Default);
    }

    /// <summary>
    /// Creates a new host builder for streaming/data collection mode.
    /// </summary>
    public static HostBuilder CreateForStreaming(string configPath, int? httpPort = null)
    {
        var args = httpPort.HasValue
            ? new[] { "--urls", $"http://localhost:{httpPort}" }
            : Array.Empty<string>();

        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        var options = CompositionOptions.Streaming with { ConfigPath = configPath };
        return new HostBuilder(builder, options);
    }

    /// <summary>
    /// Creates a new host builder for backfill-only mode.
    /// </summary>
    public static HostBuilder CreateForBackfill(string configPath)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        var options = CompositionOptions.BackfillOnly with { ConfigPath = configPath };
        return new HostBuilder(builder, options);
    }

    /// <summary>
    /// Configures the host with a specific adapter.
    /// </summary>
    public HostBuilder WithAdapter(IHostAdapter adapter)
    {
        _adapter = adapter;
        return this;
    }

    /// <summary>
    /// Configures the host for console/headless mode.
    /// </summary>
    public HostBuilder AsConsole(bool enableHealthEndpoints = true)
    {
        _adapter = new ConsoleHostAdapter(enableHealthEndpoints);
        return this;
    }

    /// <summary>
    /// Configures the host for desktop mode.
    /// </summary>
    public HostBuilder AsDesktop(int port = 8080)
    {
        _adapter = new DesktopHostAdapter(port);
        return this;
    }

    /// <summary>
    /// Configures the host for streaming/data collection mode.
    /// </summary>
    public HostBuilder AsStreaming(bool enableHealthEndpoints = true, int? httpPort = null)
    {
        _adapter = new StreamingHostAdapter(enableHealthEndpoints, httpPort);
        return this;
    }

    /// <summary>
    /// Configures the host for backfill-only mode.
    /// </summary>
    public HostBuilder AsBackfill()
    {
        _adapter = new BackfillHostAdapter();
        return this;
    }

    /// <summary>
    /// Builds the configured web application.
    /// </summary>
    public WebApplication Build()
    {
        // Register core services from composition root
        _builder.Services.AddMarketDataServices(_compositionOptions);

        // Register adapter-specific services
        _adapter?.ConfigureServices(_builder.Services);

        var app = _builder.Build();

        // Configure adapter-specific application
        _adapter?.ConfigureApplication(app);

        return app;
    }
}
