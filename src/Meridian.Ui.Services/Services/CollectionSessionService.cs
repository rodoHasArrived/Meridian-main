using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Meridian.Ui.Services;

/// <summary>
/// Service for managing collection sessions (#65/27 - P0 Critical).
/// Tracks discrete data collection sessions with comprehensive statistics.
/// </summary>
public sealed class CollectionSessionService
{
    private static readonly Lazy<CollectionSessionService> _instance = new(() => new CollectionSessionService());
    private readonly ConfigService _configService;
    private readonly NotificationService _notificationService;
    private readonly string _sessionsFilePath;
    private CollectionSessionsConfig? _sessionsConfig;
    private CollectionSession? _activeSession;

    public static CollectionSessionService Instance => _instance.Value;

    private CollectionSessionService()
    {
        _configService = new ConfigService();
        _notificationService = NotificationService.Instance;
        _sessionsFilePath = Path.Combine(AppContext.BaseDirectory, "sessions.json");
    }

    /// <summary>
    /// Loads all sessions from storage.
    /// </summary>
    public async Task<CollectionSessionsConfig> LoadSessionsAsync(CancellationToken ct = default)
    {
        if (_sessionsConfig != null)
        {
            return _sessionsConfig;
        }

        try
        {
            if (File.Exists(_sessionsFilePath))
            {
                var json = await File.ReadAllTextAsync(_sessionsFilePath);
                _sessionsConfig = JsonSerializer.Deserialize<CollectionSessionsConfig>(json, DesktopJsonOptions.Compact);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load sessions: {ex.Message}");
        }

        _sessionsConfig ??= new CollectionSessionsConfig { Sessions = Array.Empty<CollectionSession>() };
        return _sessionsConfig;
    }

    /// <summary>
    /// Saves sessions to storage.
    /// </summary>
    public async Task SaveSessionsAsync(CancellationToken ct = default)
    {
        if (_sessionsConfig == null)
            return;

        try
        {
            var json = JsonSerializer.Serialize(_sessionsConfig, DesktopJsonOptions.PrettyPrint);
            await File.WriteAllTextAsync(_sessionsFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save sessions: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets all sessions.
    /// </summary>
    public async Task<CollectionSession[]> GetSessionsAsync(CancellationToken ct = default)
    {
        var config = await LoadSessionsAsync();
        return config.Sessions ?? Array.Empty<CollectionSession>();
    }

    /// <summary>
    /// Gets the currently active session, if any.
    /// </summary>
    public async Task<CollectionSession?> GetActiveSessionAsync(CancellationToken ct = default)
    {
        var config = await LoadSessionsAsync();
        if (string.IsNullOrEmpty(config.ActiveSessionId))
        {
            return null;
        }

        return config.Sessions?.FirstOrDefault(s => s.Id == config.ActiveSessionId);
    }

    /// <summary>
    /// Creates a new collection session.
    /// </summary>
    public async Task<CollectionSession> CreateSessionAsync(string name, string? description = null, string[]? tags = null, CancellationToken ct = default)
    {
        var config = await LoadSessionsAsync();
        var appConfig = await _configService.LoadConfigAsync();

        var session = new CollectionSession
        {
            Name = name,
            Description = description,
            Status = "Pending",
            Tags = tags,
            Symbols = appConfig?.Symbols?.Select(s => s.Symbol ?? "").Where(s => !string.IsNullOrEmpty(s)).ToArray() ?? Array.Empty<string>(),
            EventTypes = DetermineEventTypes(appConfig),
            Provider = appConfig?.DataSource,
            Statistics = new CollectionSessionStatistics(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var sessions = config.Sessions?.ToList() ?? new List<CollectionSession>();
        sessions.Add(session);
        config.Sessions = sessions.ToArray();

        await SaveSessionsAsync();

        SessionCreated?.Invoke(this, new CollectionSessionEventArgs { Session = session });

        return session;
    }

    /// <summary>
    /// Creates a daily session with auto-generated name.
    /// </summary>
    public async Task<CollectionSession> CreateDailySessionAsync(CancellationToken ct = default)
    {
        var config = await LoadSessionsAsync();
        var pattern = config.SessionNamingPattern ?? "{date}-{mode}";
        var date = DateTime.UtcNow.ToString(FormatHelpers.IsoDateFormat);
        var mode = "regular-hours";

        var name = pattern
            .Replace("{date}", date)
            .Replace("{mode}", mode);

        return await CreateSessionAsync(name, $"Auto-generated daily session for {date}", new[] { "auto", "daily" });
    }

    /// <summary>
    /// Starts a collection session.
    /// </summary>
    public async Task StartSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var config = await LoadSessionsAsync();
        var session = config.Sessions?.FirstOrDefault(s => s.Id == sessionId);

        if (session == null)
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        // Check if another session is active
        if (!string.IsNullOrEmpty(config.ActiveSessionId) && config.ActiveSessionId != sessionId)
        {
            var activeSession = config.Sessions?.FirstOrDefault(s => s.Id == config.ActiveSessionId);
            if (activeSession != null && activeSession.Status == "Active")
            {
                throw new InvalidOperationException($"Session '{activeSession.Name}' is already active. Stop it before starting a new session.");
            }
        }

        session.Status = "Active";
        session.StartedAt = DateTime.UtcNow;
        session.UpdatedAt = DateTime.UtcNow;
        config.ActiveSessionId = sessionId;

        _activeSession = session;
        await SaveSessionsAsync();

        SessionStarted?.Invoke(this, new CollectionSessionEventArgs { Session = session });

        await _notificationService.NotifyAsync(
            "Collection Session Started",
            $"Session '{session.Name}' is now active",
            NotificationType.Info);
    }

    /// <summary>
    /// Stops a collection session.
    /// </summary>
    public async Task StopSessionAsync(string sessionId, bool generateManifest = true, CancellationToken ct = default)
    {
        var config = await LoadSessionsAsync();
        var session = config.Sessions?.FirstOrDefault(s => s.Id == sessionId);

        if (session == null)
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        session.Status = "Completed";
        session.EndedAt = DateTime.UtcNow;
        session.UpdatedAt = DateTime.UtcNow;

        // Calculate quality score based on statistics
        if (session.Statistics != null)
        {
            session.QualityScore = (float)CalculateQualityScore(session.Statistics);
        }

        if (config.ActiveSessionId == sessionId)
        {
            config.ActiveSessionId = null;
        }

        _activeSession = null;
        await SaveSessionsAsync();

        // Generate manifest if configured
        if (generateManifest && config.GenerateManifestOnComplete)
        {
            try
            {
                var manifestService = ManifestService.Instance;
                var manifest = await manifestService.GenerateManifestForSessionAsync(session);
                session.ManifestPath = manifest.Item2;
                await SaveSessionsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to generate manifest: {ex.Message}");
            }
        }

        SessionCompleted?.Invoke(this, new CollectionSessionEventArgs { Session = session });

        await _notificationService.NotifyAsync(
            "Collection Session Completed",
            $"Session '{session.Name}' completed with quality score {session.QualityScore:F1}%",
            NotificationType.Success);
    }

    /// <summary>
    /// Pauses a collection session.
    /// </summary>
    public async Task PauseSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var config = await LoadSessionsAsync();
        var session = config.Sessions?.FirstOrDefault(s => s.Id == sessionId);

        if (session == null || session.Status != "Active")
        {
            throw new InvalidOperationException($"Session {sessionId} is not active");
        }

        session.Status = "Paused";
        session.UpdatedAt = DateTime.UtcNow;

        await SaveSessionsAsync();
        SessionPaused?.Invoke(this, new CollectionSessionEventArgs { Session = session });
    }

    /// <summary>
    /// Resumes a paused collection session.
    /// </summary>
    public async Task ResumeSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var config = await LoadSessionsAsync();
        var session = config.Sessions?.FirstOrDefault(s => s.Id == sessionId);

        if (session == null || session.Status != "Paused")
        {
            throw new InvalidOperationException($"Session {sessionId} is not paused");
        }

        session.Status = "Active";
        session.UpdatedAt = DateTime.UtcNow;

        await SaveSessionsAsync();
        SessionResumed?.Invoke(this, new CollectionSessionEventArgs { Session = session });
    }

    /// <summary>
    /// Updates session statistics with new event data.
    /// </summary>
    public async Task UpdateSessionStatisticsAsync(string sessionId, long newEvents, long newBytes, string eventType, CancellationToken ct = default)
    {
        var config = await LoadSessionsAsync();
        var session = config.Sessions?.FirstOrDefault(s => s.Id == sessionId);

        if (session?.Statistics == null)
            return;

        session.Statistics.TotalEvents += newEvents;
        session.Statistics.TotalBytes += newBytes;

        switch (eventType.ToLower())
        {
            case "trade":
                session.Statistics.TradeEvents += newEvents;
                break;
            case "quote":
                session.Statistics.QuoteEvents += newEvents;
                break;
            case "depth":
                session.Statistics.DepthEvents += newEvents;
                break;
            case "bar":
                session.Statistics.BarEvents += newEvents;
                break;
        }

        // Calculate events per second
        if (session.StartedAt.HasValue)
        {
            var duration = DateTime.UtcNow - session.StartedAt.Value;
            if (duration.TotalSeconds > 0)
            {
                session.Statistics.EventsPerSecond = (float)(session.Statistics.TotalEvents / duration.TotalSeconds);
            }
        }

        session.UpdatedAt = DateTime.UtcNow;
        await SaveSessionsAsync();

        StatisticsUpdated?.Invoke(this, new CollectionSessionEventArgs { Session = session });
    }

    /// <summary>
    /// Records a gap detected during collection.
    /// </summary>
    public async Task RecordGapDetectedAsync(string sessionId, CancellationToken ct = default)
    {
        var config = await LoadSessionsAsync();
        var session = config.Sessions?.FirstOrDefault(s => s.Id == sessionId);

        if (session?.Statistics == null)
            return;

        session.Statistics.GapsDetected++;
        session.UpdatedAt = DateTime.UtcNow;
        await SaveSessionsAsync();
    }

    /// <summary>
    /// Records a sequence error detected during collection.
    /// </summary>
    public async Task RecordSequenceErrorAsync(string sessionId, CancellationToken ct = default)
    {
        var config = await LoadSessionsAsync();
        var session = config.Sessions?.FirstOrDefault(s => s.Id == sessionId);

        if (session?.Statistics == null)
            return;

        session.Statistics.SequenceErrors++;
        session.UpdatedAt = DateTime.UtcNow;
        await SaveSessionsAsync();
    }

    /// <summary>
    /// Deletes a session.
    /// </summary>
    public async Task DeleteSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var config = await LoadSessionsAsync();
        var sessions = config.Sessions?.ToList() ?? new List<CollectionSession>();
        var session = sessions.FirstOrDefault(s => s.Id == sessionId);

        if (session == null)
            return;

        if (session.Status == "Active")
        {
            throw new InvalidOperationException("Cannot delete an active session. Stop it first.");
        }

        sessions.Remove(session);
        config.Sessions = sessions.ToArray();

        if (config.ActiveSessionId == sessionId)
        {
            config.ActiveSessionId = null;
        }

        await SaveSessionsAsync();
        SessionDeleted?.Invoke(this, new CollectionSessionEventArgs { Session = session });
    }

    /// <summary>
    /// Updates session notes.
    /// </summary>
    public async Task UpdateSessionNotesAsync(string sessionId, string? notes, CancellationToken ct = default)
    {
        var config = await LoadSessionsAsync();
        var session = config.Sessions?.FirstOrDefault(s => s.Id == sessionId);

        if (session == null)
            return;

        session.Notes = notes;
        session.UpdatedAt = DateTime.UtcNow;
        await SaveSessionsAsync();
    }

    /// <summary>
    /// Updates session tags.
    /// </summary>
    public async Task UpdateSessionTagsAsync(string sessionId, string[]? tags, CancellationToken ct = default)
    {
        var config = await LoadSessionsAsync();
        var session = config.Sessions?.FirstOrDefault(s => s.Id == sessionId);

        if (session == null)
            return;

        session.Tags = tags;
        session.UpdatedAt = DateTime.UtcNow;
        await SaveSessionsAsync();
    }

    /// <summary>
    /// Gets session summary report as formatted text.
    /// </summary>
    public string GenerateSessionSummary(CollectionSession session)
    {
        var stats = session.Statistics ?? new CollectionSessionStatistics();
        var duration = session.EndedAt.HasValue && session.StartedAt.HasValue
            ? session.EndedAt.Value - session.StartedAt.Value
            : TimeSpan.Zero;

        return $@"Collection Session: {session.Name}
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Started: {session.StartedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A"} UTC
Ended: {session.EndedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A"} UTC
Duration: {FormatDuration(duration)}

Symbols Collected: {session.Symbols.Length}
Total Events: {stats.TotalEvents:N0}
  - Trades: {stats.TradeEvents:N0}
  - Quotes: {stats.QuoteEvents:N0}
  - L2 Depth: {stats.DepthEvents:N0}
  - Bars: {stats.BarEvents:N0}

Data Volume: {FormatBytes(stats.TotalBytes)} (compressed: {FormatBytes(stats.CompressedBytes)})
Compression Ratio: {stats.CompressionRatio:F1}x

Data Quality:
  - Gaps Detected: {stats.GapsDetected}
  - Gaps Filled: {stats.GapsFilled}
  - Sequence Errors: {stats.SequenceErrors}
  - Quality Score: {session.QualityScore:F1}%

Session Files: {stats.FileCount}
Verification: {(session.ManifestPath != null ? "✓ Manifest generated" : "Pending")}
";
    }

    private static string[] DetermineEventTypes(AppConfig? config)
    {
        var eventTypes = new List<string>();

        if (config?.Symbols != null)
        {
            foreach (var symbol in config.Symbols)
            {
                if (symbol.SubscribeTrades)
                    eventTypes.Add("Trade");
                if (symbol.SubscribeDepth)
                    eventTypes.Add("Depth");
            }
        }

        return eventTypes.Distinct().ToArray();
    }

    private static double CalculateQualityScore(CollectionSessionStatistics stats)
    {
        // Base score of 100
        var score = 100.0;

        // Deduct for gaps (2 points each, max 20 points)
        score -= Math.Min(stats.GapsDetected * 2, 20);

        // Deduct for sequence errors (1 point each, max 10 points)
        score -= Math.Min(stats.SequenceErrors * 1, 10);

        // Bonus for gaps filled (1 point each, max 10 points)
        score += Math.Min(stats.GapsFilled * 1, 10);

        return Math.Max(0, Math.Min(100, score));
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";
        }
        if (duration.TotalMinutes >= 1)
        {
            return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        }
        return $"{duration.Seconds}s";
    }

    private static string FormatBytes(long bytes) => FormatHelpers.FormatBytes(bytes);

    // Events
    public event EventHandler<CollectionSessionEventArgs>? SessionCreated;
    public event EventHandler<CollectionSessionEventArgs>? SessionStarted;
    public event EventHandler<CollectionSessionEventArgs>? SessionPaused;
    public event EventHandler<CollectionSessionEventArgs>? SessionResumed;
    public event EventHandler<CollectionSessionEventArgs>? SessionCompleted;
    public event EventHandler<CollectionSessionEventArgs>? SessionDeleted;
    public event EventHandler<CollectionSessionEventArgs>? StatisticsUpdated;
}

/// <summary>
/// Event args for collection session events.
/// </summary>
public sealed class CollectionSessionEventArgs : EventArgs
{
    public CollectionSession? Session { get; set; }
}
