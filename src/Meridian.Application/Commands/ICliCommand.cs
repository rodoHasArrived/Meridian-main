using Meridian.Application.ResultTypes;

namespace Meridian.Application.Commands;

/// <summary>
/// Interface for CLI command handlers extracted from Program.cs.
/// Each implementation handles one or more related CLI flags.
/// </summary>
public interface ICliCommand
{
    /// <summary>
    /// Returns true if this command should handle the given args.
    /// </summary>
    bool CanHandle(string[] args);

    /// <summary>
    /// Executes the command. Returns a <see cref="CliResult"/> with a semantic exit code.
    /// </summary>
    Task<CliResult> ExecuteAsync(string[] args, CancellationToken ct = default);
}

/// <summary>
/// Represents the result of a CLI command execution with optional error classification.
/// Provides semantic exit codes mapped from <see cref="ErrorCode"/> categories,
/// replacing raw int returns with structured error information.
/// </summary>
/// <remarks>
/// Exit code mapping:
/// 0 = Success, 1 = General, 2 = Validation, 3 = Configuration,
/// 4 = Connection, 5 = Provider, 6 = Data Integrity, 7 = Storage, 8 = Messaging.
/// </remarks>
public readonly record struct CliResult
{
    /// <summary>Process exit code (0 = success, 1-8 = category-specific failure).</summary>
    public int ExitCode { get; }

    /// <summary>Classified error code, if available.</summary>
    public ErrorCode? Error { get; }

    /// <summary>Whether the command succeeded.</summary>
    public bool Success => ExitCode == 0;

    private CliResult(int exitCode, ErrorCode? error = null)
    {
        ExitCode = exitCode;
        Error = error;
    }

    /// <summary>Creates a successful result (exit code 0).</summary>
    public static CliResult Ok() => new(0);

    /// <summary>Creates a failure result with a classified error code.</summary>
    public static CliResult Fail(ErrorCode code) => new(code.ToExitCode(), code);

    /// <summary>Creates a failure result with a raw exit code (for backward compatibility).</summary>
    public static CliResult Fail(int exitCode = 1) => new(exitCode);

    /// <summary>Creates a result from a boolean success flag and an error code for the failure case.</summary>
    public static CliResult FromBool(bool success, ErrorCode failureCode)
        => success ? Ok() : Fail(failureCode);
}
