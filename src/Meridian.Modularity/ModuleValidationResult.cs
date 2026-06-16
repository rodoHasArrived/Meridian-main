namespace Meridian.Modularity;

/// <summary>
/// Result of a module's self-validation step. Mirrors the shape of the existing provider-module
/// validation result so the two are familiar, but lives in the shared modularity namespace.
/// </summary>
/// <param name="IsValid">Whether the module passed validation and may proceed to registration.</param>
/// <param name="FailureReason">Human-readable explanation when <paramref name="IsValid"/> is false.</param>
public sealed record ModuleValidationResult(bool IsValid, string? FailureReason = null)
{
    /// <summary>Singleton valid result — avoids repeated allocations on the happy path.</summary>
    public static readonly ModuleValidationResult Valid = new(true);

    /// <summary>Creates a failure result with the specified reason.</summary>
    public static ModuleValidationResult Failure(string reason) => new(false, reason);
}
