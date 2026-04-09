using System;
using System.Diagnostics;
using Meridian.Ui.Services.Services;

namespace Meridian.Wpf.Services;

/// <summary>
/// WPF platform-specific logging service.
/// Extends <see cref="LoggingServiceBase"/> writing to Debug output.
/// Part of Phase 2 service extraction.
/// </summary>
public sealed class LoggingService : LoggingServiceBase
{
    private static readonly Lazy<LoggingService> _instance = new(() => new LoggingService());

    public static LoggingService Instance => _instance.Value;

    private LoggingService()
    {
    }

    protected override void WriteOutput(string formattedMessage)
    {
        Debug.WriteLine(formattedMessage);
        Console.Error.WriteLine(formattedMessage);
    }
}
