using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Meridian.Application.Logging;

/// <summary>
/// Configures Serilog logging infrastructure for the Meridian.
/// Supports structured logging with console and file sinks, and runtime log level toggling (QW-53).
/// </summary>
public static class LoggingSetup
{
    private static ILogger? _logger;
    private static LoggingLevelSwitch? _levelSwitch;

    /// <summary>
    /// Gets the configured logger instance.
    /// </summary>
    public static ILogger Logger => _logger ?? Log.Logger;

    /// <summary>
    /// Gets the current minimum log level.
    /// </summary>
    public static LogEventLevel CurrentLevel => _levelSwitch?.MinimumLevel ?? LogEventLevel.Information;

    /// <summary>
    /// Sets the minimum log level at runtime (QW-53: Log Level Runtime Toggle).
    /// </summary>
    /// <param name="level">The new minimum log level.</param>
    public static void SetLogLevel(LogEventLevel level)
    {
        if (_levelSwitch != null)
        {
            var previousLevel = _levelSwitch.MinimumLevel;
            _levelSwitch.MinimumLevel = level;
            Log.Information("Log level changed from {PreviousLevel} to {NewLevel}", previousLevel, level);
        }
    }

    /// <summary>
    /// Sets the minimum log level at runtime using a string (case-insensitive).
    /// Valid values: Verbose, Debug, Information, Warning, Error, Fatal
    /// </summary>
    /// <param name="levelName">The log level name.</param>
    /// <returns>True if the level was set successfully.</returns>
    public static bool SetLogLevel(string levelName)
    {
        if (Enum.TryParse<LogEventLevel>(levelName, ignoreCase: true, out var level))
        {
            SetLogLevel(level);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Toggles between Information and Debug log levels.
    /// </summary>
    public static void ToggleDebug()
    {
        if (_levelSwitch == null)
            return;

        var newLevel = _levelSwitch.MinimumLevel == LogEventLevel.Debug
            ? LogEventLevel.Information
            : LogEventLevel.Debug;

        SetLogLevel(newLevel);
    }

    /// <summary>
    /// Gets whether debug logging is currently enabled.
    /// </summary>
    public static bool IsDebugEnabled => _levelSwitch?.MinimumLevel <= LogEventLevel.Debug;

    /// <summary>
    /// Initializes the logging infrastructure with default settings.
    /// Call this early in application startup.
    /// </summary>
    /// <param name="configuration">Optional configuration for customizing log settings.</param>
    /// <param name="dataRoot">Root directory for log files.</param>
    public static void Initialize(IConfiguration? configuration = null, string dataRoot = "data")
    {
        var logPath = Path.Combine(dataRoot, "_logs", "meridian-.log");

        // Create the level switch for runtime control (QW-53)
        _levelSwitch = new LoggingLevelSwitch(LogEventLevel.Information);

        // Check for debug mode from environment
        var debugEnv = Environment.GetEnvironmentVariable("MDC_DEBUG");
        if (debugEnv?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
        {
            _levelSwitch.MinimumLevel = LogEventLevel.Debug;
        }

        // Check for specific log level from environment
        var levelEnv = Environment.GetEnvironmentVariable("MDC_LOG_LEVEL");
        if (!string.IsNullOrWhiteSpace(levelEnv) && Enum.TryParse<LogEventLevel>(levelEnv, ignoreCase: true, out var envLevel))
        {
            _levelSwitch.MinimumLevel = envLevel;
        }

        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(_levelSwitch)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("ThreadId", Environment.CurrentManagedThreadId)
            .Enrich.WithProperty("Application", "Meridian")
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                levelSwitch: _levelSwitch)
            .WriteTo.File(
                path: logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{ThreadId}] {SourceContext} {Message:lj}{NewLine}{Exception}",
                shared: true,
                flushToDiskInterval: TimeSpan.FromSeconds(1));

        // Allow configuration override if provided
        if (configuration != null)
        {
            loggerConfig = loggerConfig.ReadFrom.Configuration(configuration);
        }

        _logger = loggerConfig.CreateLogger();
        Log.Logger = _logger;
    }

    /// <summary>
    /// Creates a contextual logger for a specific component.
    /// </summary>
    public static ILogger ForContext<T>() => Logger.ForContext<T>();

    /// <summary>
    /// Creates a contextual logger with a custom source context.
    /// </summary>
    public static ILogger ForContext(string sourceContext) => Logger.ForContext("SourceContext", sourceContext);

    /// <summary>
    /// Creates a contextual logger for a specific type.
    /// </summary>
    public static ILogger ForContext(Type sourceType) => Logger.ForContext(sourceType);

    /// <summary>
    /// Flushes any buffered log entries and closes the logger.
    /// Call this during application shutdown.
    /// </summary>
    public static void CloseAndFlush()
    {
        Log.CloseAndFlush();
    }
}
