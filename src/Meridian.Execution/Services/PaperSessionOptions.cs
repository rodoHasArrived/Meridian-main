namespace Meridian.Execution.Services;

/// <summary>
/// Configuration options for paper trading session durable storage.
/// Bind from <c>appsettings.json</c> using the <see cref="SectionKey"/> section.
/// </summary>
public sealed class PaperSessionOptions
{
    /// <summary>Configuration section key for <c>IOptions</c> binding.</summary>
    public const string SectionKey = "PaperTrading:Sessions";

    /// <summary>
    /// Root directory under which each session's files are stored.
    /// Session files are written to <c>{BaseDirectory}/{sessionId}/</c>.
    /// When <see langword="null"/> or empty, sessions are kept in-memory only
    /// and will not survive a process restart.
    /// </summary>
    public string? BaseDirectory { get; set; }
}
