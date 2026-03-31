namespace Meridian.Application.Composition.Startup.StartupModels;

/// <summary>
/// Result of a single startup validation phase.
/// A non-null <see cref="ExitCode"/> means the phase failed and the process should exit with that code.
/// </summary>
public sealed record StartupValidationResult
{
    /// <summary>
    /// <c>true</c> when the phase passed and startup may continue.
    /// </summary>
    public bool Success => ExitCode is null;

    /// <summary>
    /// Non-null exit code when the phase failed. The process should return this value immediately.
    /// </summary>
    public int? ExitCode { get; init; }

    /// <summary>Optional human-readable description of the failure.</summary>
    public string? Message { get; init; }

    /// <summary>Creates a successful validation result that allows startup to continue.</summary>
    public static StartupValidationResult Ok() => new();

    /// <summary>Creates a failed validation result that stops startup with <paramref name="exitCode"/>.</summary>
    public static StartupValidationResult Fail(int exitCode, string? message = null)
        => new() { ExitCode = exitCode, Message = message };
}
