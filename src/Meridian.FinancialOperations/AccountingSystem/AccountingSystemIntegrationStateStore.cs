using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Storage.Archival;

namespace Meridian.FinancialOperations.AccountingSystem;

/// <summary>
/// Retains review-only external-GL imports, profiles and packages across restarts. Collection reads,
/// merges and optimistic writes share a directory gate across instances in this process. This local
/// store does not replace the PostgreSQL posting package authority or fence a second host.
/// </summary>
public sealed class AccountingSystemIntegrationStateStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private static readonly string[] Collections = ["latest-imports", "mapping-profiles", "export-packages"];
    private readonly string _directory;
    private readonly SemaphoreSlim _gate;
    private string RegistrationPath => Path.Combine(_directory, "registered-collections.json");
    private string EstablishmentPath => Path.Combine(_directory, "registration-established");
    private const string EstablishmentReceipt = "meridian-external-gl-review-state-v1";

    public AccountingSystemIntegrationStateStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        _gate = Gates.GetOrAdd(_directory, static _ => new SemaphoreSlim(1, 1));
    }

    internal IReadOnlyDictionary<string, T> Load<T>(string collection, JsonSerializerOptions options)
    {
        _gate.Wait();
        try
        {
            var rows = ReadCore<T>(collection, options);
            if (File.Exists(PathFor(collection)))
                RegisterCore(collection);
            return rows;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Refreshes a read projection without turning a missing retained row into a new history.</summary>
    internal void Refresh<T>(string collection, ConcurrentDictionary<string, T> rows, JsonSerializerOptions options)
    {
        _gate.Wait();
        try
        {
            var retained = ReadCore<T>(collection, options);
            if (rows.Keys.Any(key => !retained.ContainsKey(key)))
                throw new InvalidOperationException($"Retained accounting-system integration state '{collection}' lost a previously observed record; restore it before continuing.");
            if (File.Exists(PathFor(collection)))
                RegisterCore(collection);
            foreach (var (key, value) in retained)
                rows[key] = value;
        }
        finally { _gate.Release(); }
    }

    internal async Task RetainAsync<T>(
        string collection, ConcurrentDictionary<string, T> rows, string key, T value,
        JsonSerializerOptions options, CancellationToken ct,
        AccountingSystemStateExpectation? expected = null,
        IReadOnlyList<AccountingSystemStateDependency>? dependencies = null)
    {
        // Capture before the first await: two concurrent writers must compare the state they
        // actually observed, rather than adopting a newer cache after waiting for the gate.
        expected ??= Capture(rows, key, options);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var retained = ReadCore<T>(collection, options);
            if (rows.Keys.Any(observedKey => !retained.ContainsKey(observedKey)))
                throw new InvalidOperationException($"Retained accounting-system integration state '{collection}' lost a previously observed record; restore it before continuing.");
            var actual = Capture(retained, key, options);
            if (!Same(expected, actual))
                throw new InvalidOperationException("External GL review state changed; reload before retrying this update.");
            foreach (var dependency in dependencies ?? [])
            {
                var current = ReadCore<JsonElement>(dependency.Collection, options);
                if (!Same(dependency.Expected, Capture(current, dependency.Key, options)))
                    throw new InvalidOperationException("External GL review dependency changed; prepare a new review package.");
            }
            var next = new SortedDictionary<string, T>(retained.ToDictionary(), StringComparer.OrdinalIgnoreCase) { [key] = value };
            Directory.CreateDirectory(_directory);
            // Provision only a genuinely new collection before registration. An interrupted first
            // write can retry against this known empty document; a missing registered document was
            // already refused by ReadCore and can never be provisioned as empty here.
            if (!File.Exists(PathFor(collection)))
                await AtomicFileWriter.WriteAsync(PathFor(collection), "{}", ct).ConfigureAwait(false);
            RegisterCore(collection);
            await AtomicFileWriter.WriteAsync(PathFor(collection), JsonSerializer.Serialize(next, options), ct).ConfigureAwait(false);
            foreach (var (retainedKey, retainedValue) in next)
                rows[retainedKey] = retainedValue;
        }
        finally { _gate.Release(); }
    }

    internal static AccountingSystemStateExpectation Capture<T>(IReadOnlyDictionary<string, T> rows, string key, JsonSerializerOptions options)
        => rows.TryGetValue(key, out var value) ? new(JsonSerializer.Serialize(value, options)) : new(null);

    internal static AccountingSystemStateExpectation Capture<T>(ConcurrentDictionary<string, T> rows, string key, JsonSerializerOptions options)
        => rows.TryGetValue(key, out var value) ? new(JsonSerializer.Serialize(value, options)) : new(null);

    internal static bool Same(AccountingSystemStateExpectation left, AccountingSystemStateExpectation right)
    {
        if (left.Json is null || right.Json is null)
            return left.Json == right.Json;
        using var first = JsonDocument.Parse(left.Json);
        using var second = JsonDocument.Parse(right.Json);
        return JsonElement.DeepEquals(first.RootElement, second.RootElement);
    }

    private IReadOnlyDictionary<string, T> ReadCore<T>(string collection, JsonSerializerOptions options)
    {
        var path = PathFor(collection);
        var registered = ReadRegistrationCore();
        if (!File.Exists(path))
        {
            if (registered.Collections.Contains(collection, StringComparer.Ordinal))
                throw new InvalidOperationException($"Retained accounting-system integration state '{collection}' is missing; restore it before continuing.");
            return new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        }
        try
        {
            var rows = JsonSerializer.Deserialize<Dictionary<string, T>>(File.ReadAllText(path), options);
            if (rows is null || rows.Values.Any(static row => row is null
                    || row is JsonElement element && element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined))
                throw new JsonException("The document is empty or holds a null record.");
            return new Dictionary<string, T>(rows, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            throw new InvalidOperationException($"Retained accounting-system integration state '{collection}' cannot be read; restore it before continuing.", exception);
        }
    }

    private AccountingSystemStateRegistration ReadRegistrationCore()
    {
        var established = File.Exists(EstablishmentPath);
        if (established && File.ReadAllText(EstablishmentPath) != EstablishmentReceipt)
            throw new InvalidOperationException("Retained accounting-system integration registration cannot be read; restore it before continuing.");
        if (!File.Exists(RegistrationPath))
        {
            if (established)
                throw new InvalidOperationException("Retained accounting-system integration registration is missing; restore it before continuing.");
            return new(1, []);
        }
        try
        {
            var registration = JsonSerializer.Deserialize(File.ReadAllText(RegistrationPath),
                AccountingSystemStateJsonContext.Default.AccountingSystemStateRegistration);
            if (registration is null || registration.SchemaVersion != 1 || registration.Collections is null
                || registration.Collections.Distinct(StringComparer.Ordinal).Count() != registration.Collections.Length
                || registration.Collections.Any(name => !Collections.Contains(name, StringComparer.Ordinal)))
                throw new JsonException("The collection registration is invalid.");
            foreach (var collection in registration.Collections)
                if (!File.Exists(PathFor(collection)))
                    throw new InvalidOperationException($"Retained accounting-system integration state '{collection}' is missing; restore it before continuing.");
            return registration;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Retained accounting-system integration registration cannot be read; restore it before continuing.", exception);
        }
    }

    private void RegisterCore(string collection)
    {
        var registration = ReadRegistrationCore();
        Directory.CreateDirectory(_directory);
        if (!registration.Collections.Contains(collection, StringComparer.Ordinal))
        {
            var next = registration with { Collections = registration.Collections.Append(collection).Order(StringComparer.Ordinal).ToArray() };
            AtomicFileWriter.Write(RegistrationPath, JsonSerializer.Serialize(next,
                AccountingSystemStateJsonContext.Default.AccountingSystemStateRegistration));
        }
        if (!File.Exists(EstablishmentPath))
            AtomicFileWriter.Write(EstablishmentPath, EstablishmentReceipt);
    }

    private string PathFor(string collection)
        => Collections.Contains(collection, StringComparer.Ordinal) ? Path.Combine(_directory, collection + ".json")
            : throw new ArgumentException("Unknown accounting-system state collection.", nameof(collection));
}

internal sealed record AccountingSystemStateExpectation(string? Json);
internal sealed record AccountingSystemStateDependency(string Collection, string Key, AccountingSystemStateExpectation Expected);
internal sealed record AccountingSystemStateRegistration(int SchemaVersion, string[] Collections);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AccountingSystemStateRegistration))]
internal sealed partial class AccountingSystemStateJsonContext : JsonSerializerContext;
