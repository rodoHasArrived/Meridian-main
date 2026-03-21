using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Application.Services;

/// <summary>
/// Service for tracking and querying application errors.
/// Implements QW-58: Last N Errors Endpoint.
/// </summary>
public sealed class ErrorTracker
{
    private readonly ILogger _log = LoggingSetup.ForContext<ErrorTracker>();
    private readonly ConcurrentQueue<TrackedError> _errors = new();
    private readonly int _maxErrors;
    private readonly string _dataRoot;
    private int _totalErrorCount;

    public ErrorTracker(string dataRoot, int maxErrors = 1000)
    {
        _dataRoot = dataRoot;
        _maxErrors = maxErrors;
    }

    /// <summary>
    /// Records an error for tracking.
    /// </summary>
    public void RecordError(Exception exception, string? context = null)
    {
        var error = new TrackedError
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 12),
            Timestamp = DateTimeOffset.UtcNow,
            Message = exception.Message,
            ExceptionType = exception.GetType().FullName,
            StackTrace = exception.StackTrace,
            Context = context,
            InnerException = exception.InnerException?.Message
        };

        RecordError(error);
    }

    /// <summary>
    /// Records a tracked error.
    /// </summary>
    public void RecordError(TrackedError error)
    {
        _errors.Enqueue(error);
        Interlocked.Increment(ref _totalErrorCount);

        // Trim if over limit
        while (_errors.Count > _maxErrors)
        {
            _errors.TryDequeue(out _);
        }
    }

    /// <summary>
    /// Gets the last N errors.
    /// </summary>
    public ErrorQueryResult GetLastErrors(int count = 10, string? filterType = null, string? filterContext = null)
    {
        IEnumerable<TrackedError> errors = _errors.ToArray();

        if (!string.IsNullOrWhiteSpace(filterType))
        {
            errors = errors.Where(e =>
                e.ExceptionType?.Contains(filterType, StringComparison.OrdinalIgnoreCase) == true);
        }

        if (!string.IsNullOrWhiteSpace(filterContext))
        {
            errors = errors.Where(e =>
                e.Context?.Contains(filterContext, StringComparison.OrdinalIgnoreCase) == true);
        }

        return new ErrorQueryResult
        {
            Errors = errors.OrderByDescending(e => e.Timestamp).Take(count).ToList(),
            TotalErrors = _totalErrorCount,
            QueuedErrors = _errors.Count,
            QueryTime = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Gets error statistics.
    /// </summary>
    public ErrorStatistics GetStatistics(TimeSpan? window = null)
    {
        var windowStart = window.HasValue
            ? DateTimeOffset.UtcNow - window.Value
            : DateTimeOffset.MinValue;

        var relevantErrors = _errors.Where(e => e.Timestamp >= windowStart).ToList();

        var byType = relevantErrors
            .GroupBy(e => e.ExceptionType ?? "Unknown")
            .ToDictionary(g => g.Key, g => g.Count());

        var byContext = relevantErrors
            .Where(e => e.Context != null)
            .GroupBy(e => e.Context!)
            .ToDictionary(g => g.Key, g => g.Count());

        var byHour = relevantErrors
            .GroupBy(e => e.Timestamp.ToString("yyyy-MM-dd HH:00"))
            .ToDictionary(g => g.Key, g => g.Count());

        return new ErrorStatistics
        {
            TotalErrors = _totalErrorCount,
            ErrorsInWindow = relevantErrors.Count,
            WindowStart = windowStart,
            WindowEnd = DateTimeOffset.UtcNow,
            ByExceptionType = byType,
            ByContext = byContext,
            ByHour = byHour,
            MostRecentError = relevantErrors.OrderByDescending(e => e.Timestamp).FirstOrDefault()
        };
    }

    /// <summary>
    /// Clears all tracked errors.
    /// </summary>
    public void Clear()
    {
        while (_errors.TryDequeue(out _))
        { }
        Interlocked.Exchange(ref _totalErrorCount, 0);
    }

    /// <summary>
    /// Parses errors from log files.
    /// </summary>
    public async Task<ErrorQueryResult> ParseErrorsFromLogsAsync(
        int maxErrors = 100,
        int daysBack = 1,
        CancellationToken ct = default)
    {
        var errors = new List<TrackedError>();
        var logsDir = Path.Combine(_dataRoot, "_logs");

        if (!Directory.Exists(logsDir))
        {
            return new ErrorQueryResult
            {
                Errors = errors,
                Message = "Logs directory not found"
            };
        }

        var cutoff = DateTime.UtcNow.AddDays(-daysBack);
        var logFiles = Directory.GetFiles(logsDir, "*.log")
            .Where(f => new FileInfo(f).LastWriteTimeUtc >= cutoff)
            .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc);

        var errorPattern = new Regex(
            @"^\[?(\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:\s+[\+\-]\d{2}:\d{2})?)\]?\s*\[?(ERR|ERROR|FTL|FATAL)\]?\s*(.+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        foreach (var logFile in logFiles)
        {
            ct.ThrowIfCancellationRequested();

            if (errors.Count >= maxErrors)
                break;

            try
            {
                await using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);

                string? line;
                var lineNumber = 0;
                var currentError = new List<string>();
                DateTimeOffset? errorTimestamp = null;
                var errorLevel = "ERROR";

                while ((line = await reader.ReadLineAsync(ct)) != null)
                {
                    lineNumber++;

                    var match = errorPattern.Match(line);
                    if (match.Success)
                    {
                        // Save previous error if exists
                        if (currentError.Count > 0 && errors.Count < maxErrors)
                        {
                            errors.Add(CreateTrackedError(
                                errorTimestamp ?? DateTimeOffset.UtcNow,
                                errorLevel,
                                string.Join(Environment.NewLine, currentError),
                                Path.GetFileName(logFile),
                                lineNumber - currentError.Count));
                        }

                        currentError.Clear();
                        if (DateTimeOffset.TryParse(match.Groups[1].Value, out var ts))
                            errorTimestamp = ts;
                        errorLevel = match.Groups[2].Value.ToUpperInvariant();
                        currentError.Add(match.Groups[3].Value);
                    }
                    else if (currentError.Count > 0)
                    {
                        // Continuation of stack trace
                        if (line.TrimStart().StartsWith("at ") ||
                            line.TrimStart().StartsWith("---") ||
                            currentError.Count < 20)
                        {
                            currentError.Add(line);
                        }
                    }
                }

                // Don't forget the last error
                if (currentError.Count > 0 && errors.Count < maxErrors)
                {
                    errors.Add(CreateTrackedError(
                        errorTimestamp ?? DateTimeOffset.UtcNow,
                        errorLevel,
                        string.Join(Environment.NewLine, currentError),
                        Path.GetFileName(logFile),
                        lineNumber - currentError.Count));
                }
            }
            catch (IOException ex)
            {
                _log.Debug(ex, "Log file is locked or inaccessible: {FilePath}", logFile);
            }
        }

        return new ErrorQueryResult
        {
            Errors = errors.OrderByDescending(e => e.Timestamp).Take(maxErrors).ToList(),
            TotalErrors = errors.Count,
            QueryTime = DateTimeOffset.UtcNow,
            Message = $"Parsed {errors.Count} errors from log files"
        };
    }

    private static TrackedError CreateTrackedError(
        DateTimeOffset timestamp,
        string level,
        string message,
        string sourceFile,
        int lineNumber)
    {
        // Try to extract exception type from message
        string? exceptionType = null;
        var exceptionMatch = Regex.Match(message, @"^([\w.]+Exception):");
        if (exceptionMatch.Success)
        {
            exceptionType = exceptionMatch.Groups[1].Value;
        }

        // Try to extract stack trace
        string? stackTrace = null;
        var stackIndex = message.IndexOf("   at ", StringComparison.Ordinal);
        if (stackIndex > 0)
        {
            stackTrace = message.Substring(stackIndex);
            message = message.Substring(0, stackIndex).Trim();
        }

        return new TrackedError
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 12),
            Timestamp = timestamp,
            Level = level,
            Message = message,
            ExceptionType = exceptionType,
            StackTrace = stackTrace,
            Context = $"{sourceFile}:{lineNumber}"
        };
    }
}

/// <summary>
/// A tracked error entry.
/// </summary>
public sealed class TrackedError
{
    public string? Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string? Level { get; set; } = "ERROR";
    public string? Message { get; set; }
    public string? ExceptionType { get; set; }
    public string? StackTrace { get; set; }
    public string? Context { get; set; }
    public string? InnerException { get; set; }
}

/// <summary>
/// Result of an error query.
/// </summary>
public sealed class ErrorQueryResult
{
    public List<TrackedError> Errors { get; set; } = new();
    public int TotalErrors { get; set; }
    public int QueuedErrors { get; set; }
    public DateTimeOffset QueryTime { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Error statistics.
/// </summary>
public sealed class ErrorStatistics
{
    public int TotalErrors { get; set; }
    public int ErrorsInWindow { get; set; }
    public DateTimeOffset WindowStart { get; set; }
    public DateTimeOffset WindowEnd { get; set; }
    public Dictionary<string, int> ByExceptionType { get; set; } = new();
    public Dictionary<string, int> ByContext { get; set; } = new();
    public Dictionary<string, int> ByHour { get; set; } = new();
    public TrackedError? MostRecentError { get; set; }
}
