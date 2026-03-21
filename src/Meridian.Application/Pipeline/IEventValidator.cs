using Meridian.Domain.Events;
using Meridian.Infrastructure.Contracts;

namespace Meridian.Application.Pipeline;

/// <summary>
/// Validates <see cref="MarketEvent"/> instances before they are persisted to the WAL or
/// storage sinks. Implementations should be stateless and thread-safe so they can be
/// invoked concurrently from the pipeline consumer.
/// </summary>
[ImplementsAdr("ADR-007", "Event validation gate before WAL/sink persistence")]
public interface IEventValidator
{
    /// <summary>
    /// Validates a market event and returns a result indicating whether the event is
    /// acceptable for persistence. Invalid events should be routed to a dead-letter sink.
    /// </summary>
    /// <param name="evt">The market event to validate (passed by readonly reference for zero-copy).</param>
    /// <returns>A <see cref="ValidationResult"/> describing validity and any errors.</returns>
    ValidationResult Validate(in MarketEvent evt);
}

/// <summary>
/// Immutable result of a <see cref="IEventValidator.Validate"/> call.
/// </summary>
/// <param name="IsValid">Whether the event passed validation.</param>
/// <param name="Errors">
/// Human-readable error descriptions when <paramref name="IsValid"/> is <see langword="false"/>;
/// empty when the event is valid.
/// </param>
public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    private static readonly ValidationResult ValidInstance = new(true, Array.Empty<string>());

    /// <summary>
    /// Returns a cached valid result (no allocation on the hot path).
    /// </summary>
    public static ValidationResult Valid => ValidInstance;

    /// <summary>
    /// Creates a failed validation result with one or more error messages.
    /// </summary>
    /// <param name="errors">The validation errors.</param>
    public static ValidationResult Failed(params string[] errors) =>
        new(false, errors);

    /// <summary>
    /// Creates a failed validation result from a list of error messages.
    /// </summary>
    /// <param name="errors">The validation errors.</param>
    public static ValidationResult Failed(IReadOnlyList<string> errors) =>
        new(false, errors);
}
