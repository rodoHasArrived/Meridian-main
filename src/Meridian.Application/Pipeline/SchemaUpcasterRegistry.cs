using System.Collections.Concurrent;
using Meridian.Contracts.Schema;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Application.Pipeline;

/// <summary>
/// Registry that chains <see cref="ISchemaUpcaster{T}"/> instances to migrate stored records
/// from any older schema version to the current version. Supports multi-step migration
/// (e.g., v1 → v2 → v3) by composing upcasters into an ordered chain.
/// </summary>
/// <remarks>
/// Register upcasters at startup via DI or <see cref="Register"/>. When the pipeline reads a
/// stored record whose <c>SchemaVersion</c> is older than the current version, call
/// <see cref="TryUpcast"/> to produce a current-version <see cref="MarketEvent"/>.
/// </remarks>
[ImplementsAdr("ADR-007", "Schema evolution via chained upcasters for WAL/JSONL replay")]
public sealed class SchemaUpcasterRegistry
{
    /// <summary>
    /// The current schema version that all stored events should be migrated to.
    /// Matches <see cref="MarketEvent.SchemaVersion"/> for newly created events.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    private readonly ConcurrentDictionary<int, ISchemaUpcaster<MarketEvent>> _upcasters = new();
    private readonly ILogger<SchemaUpcasterRegistry> _logger;

    // Metrics
    private long _totalUpcastAttempts;
    private long _successfulUpcasts;
    private long _failedUpcasts;

    public SchemaUpcasterRegistry(ILogger<SchemaUpcasterRegistry>? logger = null)
    {
        _logger = logger ?? NullLogger<SchemaUpcasterRegistry>.Instance;
    }

    /// <summary>Gets the total number of upcast attempts.</summary>
    public long TotalUpcastAttempts => Interlocked.Read(ref _totalUpcastAttempts);

    /// <summary>Gets the number of successful upcasts.</summary>
    public long SuccessfulUpcasts => Interlocked.Read(ref _successfulUpcasts);

    /// <summary>Gets the number of failed upcasts.</summary>
    public long FailedUpcasts => Interlocked.Read(ref _failedUpcasts);

    /// <summary>Gets the number of registered upcasters.</summary>
    public int RegisteredUpcasterCount => _upcasters.Count;

    /// <summary>
    /// Registers an upcaster for a specific source schema version.
    /// </summary>
    /// <param name="upcaster">The upcaster to register.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="upcaster"/> is null.</exception>
    /// <exception cref="InvalidOperationException">When an upcaster is already registered for the same <c>FromSchemaVersion</c>.</exception>
    public void Register(ISchemaUpcaster<MarketEvent> upcaster)
    {
        ArgumentNullException.ThrowIfNull(upcaster);

        if (!_upcasters.TryAdd(upcaster.FromSchemaVersion, upcaster))
        {
            throw new InvalidOperationException(
                $"An upcaster from schema version {upcaster.FromSchemaVersion} is already registered.");
        }

        _logger.LogInformation(
            "Registered schema upcaster from version {FromVersion} to {ToVersion}",
            upcaster.FromSchemaVersion, upcaster.ToSchemaVersion);
    }

    /// <summary>
    /// Determines whether the given schema version requires upcasting.
    /// </summary>
    /// <param name="schemaVersion">The schema version of the stored record.</param>
    /// <returns><see langword="true"/> if the version is older than <see cref="CurrentSchemaVersion"/>.</returns>
    public static bool RequiresUpcast(int schemaVersion) => schemaVersion < CurrentSchemaVersion;

    /// <summary>
    /// Attempts to upcast a JSON payload from an older schema version to a current <see cref="MarketEvent"/>.
    /// Chains multiple upcasters if the version gap spans more than one step.
    /// </summary>
    /// <param name="json">The raw JSON text of the stored record.</param>
    /// <param name="fromVersion">The schema version of the stored record.</param>
    /// <param name="result">The up-cast event, or <see langword="null"/> on failure.</param>
    /// <returns><see langword="true"/> if the upcast succeeded.</returns>
    public bool TryUpcast(string json, int fromVersion, out MarketEvent? result)
    {
        result = null;
        Interlocked.Increment(ref _totalUpcastAttempts);

        if (fromVersion >= CurrentSchemaVersion)
        {
            // Already at or above current version; no upcast needed
            return false;
        }

        var currentVersion = fromVersion;
        var currentJson = json;

        // Walk the upcaster chain from fromVersion to CurrentSchemaVersion
        while (currentVersion < CurrentSchemaVersion)
        {
            if (!_upcasters.TryGetValue(currentVersion, out var upcaster))
            {
                _logger.LogWarning(
                    "No upcaster registered for schema version {FromVersion}. " +
                    "Cannot migrate record from version {OriginalVersion} to {TargetVersion}",
                    currentVersion, fromVersion, CurrentSchemaVersion);
                Interlocked.Increment(ref _failedUpcasts);
                return false;
            }

            try
            {
                var upcastResult = upcaster.Upcast(currentJson);
                if (upcastResult == null)
                {
                    _logger.LogWarning(
                        "Upcaster from version {FromVersion} to {ToVersion} returned null for record",
                        upcaster.FromSchemaVersion, upcaster.ToSchemaVersion);
                    Interlocked.Increment(ref _failedUpcasts);
                    return false;
                }

                // If the chain reaches the current version, we're done
                if (upcaster.ToSchemaVersion >= CurrentSchemaVersion)
                {
                    result = upcastResult;
                    Interlocked.Increment(ref _successfulUpcasts);
                    return true;
                }

                // For multi-step chains, re-serialize for the next upcaster
                currentJson = System.Text.Json.JsonSerializer.Serialize(
                    upcastResult, Serialization.MarketDataJsonContext.HighPerformanceOptions);
                currentVersion = upcaster.ToSchemaVersion;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Upcaster from version {FromVersion} to {ToVersion} threw an exception",
                    upcaster.FromSchemaVersion, upcaster.ToSchemaVersion);
                Interlocked.Increment(ref _failedUpcasts);
                return false;
            }
        }

        Interlocked.Increment(ref _failedUpcasts);
        return false;
    }

    /// <summary>
    /// Returns a snapshot of schema migration statistics.
    /// </summary>
    public SchemaUpcasterStatistics GetStatistics() => new(
        TotalAttempts: TotalUpcastAttempts,
        Successful: SuccessfulUpcasts,
        Failed: FailedUpcasts,
        RegisteredUpcasters: RegisteredUpcasterCount,
        CurrentSchemaVersion: CurrentSchemaVersion);
}

/// <summary>
/// Statistics about schema upcasting operations.
/// </summary>
public sealed record SchemaUpcasterStatistics(
    long TotalAttempts,
    long Successful,
    long Failed,
    int RegisteredUpcasters,
    int CurrentSchemaVersion);
