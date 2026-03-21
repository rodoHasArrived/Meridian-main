using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Meridian.Application.Logging;
using Meridian.Application.Serialization;
using Serilog;

namespace Meridian.Storage.Archival;

/// <summary>
/// Manages schema versions for long-term format preservation.
/// Ensures backward compatibility and supports schema migration.
/// </summary>
public sealed class SchemaVersionManager
{
    private readonly ILogger _log = LoggingSetup.ForContext<SchemaVersionManager>();
    private readonly string _schemaDirectory;
    private readonly Dictionary<string, SchemaDefinition> _schemas;
    private readonly Dictionary<(string, string), SchemaMigration> _migrations;

    public SchemaVersionManager(string schemaDirectory)
    {
        _schemaDirectory = schemaDirectory;
        _schemas = new Dictionary<string, SchemaDefinition>(StringComparer.OrdinalIgnoreCase);
        _migrations = new Dictionary<(string, string), SchemaMigration>();

        Directory.CreateDirectory(_schemaDirectory);
        RegisterBuiltInSchemas();
    }

    /// <summary>
    /// Register a new schema version.
    /// </summary>
    public void RegisterSchema(SchemaDefinition schema)
    {
        var key = $"{schema.EventType}_v{schema.Version}";
        _schemas[key] = schema;
        _log.Information("Registered schema {Key}", key);
    }

    /// <summary>
    /// Register a migration between schema versions.
    /// </summary>
    public void RegisterMigration(SchemaMigration migration)
    {
        var key = ($"{migration.EventType}_v{migration.FromVersion}", $"{migration.EventType}_v{migration.ToVersion}");
        _migrations[key] = migration;
        _log.Information("Registered migration from {From} to {To}",
            key.Item1, key.Item2);
    }

    /// <summary>
    /// Get the current (latest) schema for an event type.
    /// </summary>
    public SchemaDefinition? GetCurrentSchema(string eventType)
    {
        return _schemas.Values
            .Where(s => s.EventType.Equals(eventType, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.Version)
            .FirstOrDefault();
    }

    /// <summary>
    /// Get a specific schema version.
    /// </summary>
    public SchemaDefinition? GetSchema(string eventType, string version)
    {
        var key = $"{eventType}_v{version}";
        return _schemas.TryGetValue(key, out var schema) ? schema : null;
    }

    /// <summary>
    /// Get all versions of a schema.
    /// </summary>
    public IReadOnlyList<SchemaDefinition> GetSchemaHistory(string eventType)
    {
        return _schemas.Values
            .Where(s => s.EventType.Equals(eventType, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Version)
            .ToList();
    }

    /// <summary>
    /// Migrate data from one schema version to another.
    /// </summary>
    public async Task<MigrationResult> MigrateAsync(
        Stream input,
        Stream output,
        string eventType,
        string fromVersion,
        string toVersion,
        CancellationToken ct = default)
    {
        var result = new MigrationResult
        {
            EventType = eventType,
            FromVersion = fromVersion,
            ToVersion = toVersion,
            StartedAt = DateTime.UtcNow
        };

        var fromKey = $"{eventType}_v{fromVersion}";
        var toKey = $"{eventType}_v{toVersion}";

        // Find migration path
        var migrationPath = FindMigrationPath(eventType, fromVersion, toVersion);
        if (migrationPath == null)
        {
            result.Success = false;
            result.Error = $"No migration path found from {fromVersion} to {toVersion}";
            return result;
        }

        _log.Information("Migrating {EventType} from v{From} to v{To} via {StepCount} steps",
            eventType, fromVersion, toVersion, migrationPath.Count);

        using var reader = new StreamReader(input);
        await using var writer = new StreamWriter(output);

        var currentVersion = fromVersion;
        var buffer = new List<JsonDocument>();

        // Read all records
        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var doc = JsonDocument.Parse(line);
                buffer.Add(doc);
                result.RecordsProcessed++;
            }
            catch (Exception ex)
            {
                result.RecordsFailed++;
                _log.Warning(ex, "Failed to parse record during migration");
            }
        }

        // Apply migrations in sequence
        foreach (var migration in migrationPath)
        {
            for (var i = 0; i < buffer.Count; i++)
            {
                try
                {
                    var migrated = ApplyMigration(buffer[i], migration);
                    buffer[i].Dispose();
                    buffer[i] = migrated;
                }
                catch (Exception ex)
                {
                    result.RecordsFailed++;
                    _log.Warning(ex, "Failed to apply migration {From} -> {To}",
                        migration.FromVersion, migration.ToVersion);
                }
            }
        }

        // Write migrated records
        foreach (var doc in buffer)
        {
            await writer.WriteLineAsync(doc.RootElement.GetRawText());
            doc.Dispose();
        }

        result.Success = true;
        result.CompletedAt = DateTime.UtcNow;

        _log.Information("Migration completed: {Processed} records processed, {Failed} failed",
            result.RecordsProcessed, result.RecordsFailed);

        return result;
    }

    /// <summary>
    /// Validate data against a schema.
    /// </summary>
    public SchemaValidationResult Validate(JsonDocument document, SchemaDefinition schema)
    {
        var result = new SchemaValidationResult
        {
            SchemaId = $"{schema.EventType}_v{schema.Version}",
            IsValid = true
        };

        var root = document.RootElement;

        // Check required fields
        foreach (var field in schema.Fields.Where(f => f.Required))
        {
            if (!root.TryGetProperty(field.Name, out var prop) ||
                prop.ValueKind == JsonValueKind.Null)
            {
                result.IsValid = false;
                result.Errors.Add($"Missing required field: {field.Name}");
            }
        }

        // Validate field types
        foreach (var field in schema.Fields)
        {
            if (root.TryGetProperty(field.Name, out var prop))
            {
                var typeValid = ValidateFieldType(prop, field.Type);
                if (!typeValid)
                {
                    result.Warnings.Add($"Field {field.Name} has unexpected type: expected {field.Type}");
                }

                // Validate constraints
                if (field.Constraints != null)
                {
                    var constraintResult = ValidateConstraints(prop, field.Constraints);
                    if (!constraintResult.IsValid)
                    {
                        result.Errors.AddRange(constraintResult.Errors);
                        result.IsValid = false;
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Export schema to JSON Schema format.
    /// </summary>
    public async Task ExportSchemaAsync(SchemaDefinition schema, string outputPath, CancellationToken ct = default)
    {
        var jsonSchema = new
        {
            schema = "https://json-schema.org/draft/2020-12/schema",
            id = $"https://marketdatacollector.io/schemas/{schema.EventType}/{schema.Version}",
            title = $"{schema.EventType} Event",
            description = schema.Description,
            type = "object",
            properties = schema.Fields.ToDictionary(
                f => f.Name,
                f => new
                {
                    type = MapToJsonSchemaType(f.Type),
                    description = f.Description
                }),
            required = schema.Fields.Where(f => f.Required).Select(f => f.Name).ToList()
        };

        var json = JsonSerializer.Serialize(jsonSchema, MarketDataJsonContext.PrettyPrintOptions);
        await AtomicFileWriter.WriteAsync(outputPath, json, ct);

        _log.Information("Exported schema {EventType} v{Version} to {Path}",
            schema.EventType, schema.Version, outputPath);
    }

    /// <summary>
    /// Export all schemas to the schema directory.
    /// </summary>
    public async Task ExportAllSchemasAsync(CancellationToken ct = default)
    {
        foreach (var schema in _schemas.Values)
        {
            var fileName = $"{schema.EventType}_v{schema.Version}.schema.json";
            var path = Path.Combine(_schemaDirectory, fileName);
            await ExportSchemaAsync(schema, path, ct);
        }

        // Export schema registry
        var registry = new SchemaRegistry
        {
            Version = "1.0",
            GeneratedAt = DateTime.UtcNow,
            Schemas = _schemas.Values.Select(s => new SchemaRegistryEntry
            {
                EventType = s.EventType,
                Version = s.Version,
                Introduced = s.IntroducedAt,
                Deprecated = s.DeprecatedAt,
                FieldCount = s.Fields.Count
            }).ToList()
        };

        var registryPath = Path.Combine(_schemaDirectory, "registry.json");
        var registryJson = JsonSerializer.Serialize(registry, MarketDataJsonContext.PrettyPrintOptions);
        await AtomicFileWriter.WriteAsync(registryPath, registryJson, ct);

        _log.Information("Exported {Count} schemas to {Directory}", _schemas.Count, _schemaDirectory);
    }

    private List<SchemaMigration>? FindMigrationPath(string eventType, string fromVersion, string toVersion)
    {
        // Simple BFS to find migration path
        var visited = new HashSet<string>();
        var queue = new Queue<(string Version, List<SchemaMigration> Path)>();

        queue.Enqueue((fromVersion, new List<SchemaMigration>()));
        visited.Add(fromVersion);

        while (queue.Count > 0)
        {
            var (currentVersion, path) = queue.Dequeue();

            if (currentVersion == toVersion)
            {
                return path;
            }

            // Find all migrations from current version
            foreach (var migration in _migrations.Values.Where(m =>
                m.EventType.Equals(eventType, StringComparison.OrdinalIgnoreCase) &&
                m.FromVersion == currentVersion))
            {
                if (!visited.Contains(migration.ToVersion))
                {
                    visited.Add(migration.ToVersion);
                    var newPath = new List<SchemaMigration>(path) { migration };
                    queue.Enqueue((migration.ToVersion, newPath));
                }
            }
        }

        return null;
    }

    private JsonDocument ApplyMigration(JsonDocument source, SchemaMigration migration)
    {
        var root = source.RootElement;
        var dict = new Dictionary<string, object?>();

        // Copy existing properties
        foreach (var prop in root.EnumerateObject())
        {
            dict[prop.Name] = JsonElementToObject(prop.Value);
        }

        // Apply field renames
        foreach (var (oldName, newName) in migration.FieldRenames)
        {
            if (dict.TryGetValue(oldName, out var value))
            {
                dict.Remove(oldName);
                dict[newName] = value;
            }
        }

        // Apply default values for new fields
        foreach (var (fieldName, defaultValue) in migration.NewFieldDefaults)
        {
            if (!dict.ContainsKey(fieldName))
            {
                dict[fieldName] = defaultValue;
            }
        }

        // Remove deprecated fields
        foreach (var fieldName in migration.RemovedFields)
        {
            dict.Remove(fieldName);
        }

        // Apply transformations
        if (migration.Transformations != null)
        {
            foreach (var (fieldName, transform) in migration.Transformations)
            {
                if (dict.TryGetValue(fieldName, out var value))
                {
                    dict[fieldName] = transform(value);
                }
            }
        }

        // Convert back to JsonDocument
        var json = JsonSerializer.Serialize(dict);
        return JsonDocument.Parse(json);
    }

    private static object? JsonElementToObject(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Array => element.EnumerateArray().Select(JsonElementToObject).ToArray(),
        JsonValueKind.Object => element.EnumerateObject()
            .ToDictionary(p => p.Name, p => JsonElementToObject(p.Value)),
        _ => element.GetRawText()
    };

    private static bool ValidateFieldType(JsonElement element, SchemaFieldType expectedType) => expectedType switch
    {
        SchemaFieldType.String => element.ValueKind == JsonValueKind.String,
        SchemaFieldType.Integer => element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out _),
        SchemaFieldType.Decimal => element.ValueKind == JsonValueKind.Number,
        SchemaFieldType.Boolean => element.ValueKind is JsonValueKind.True or JsonValueKind.False,
        SchemaFieldType.DateTime => element.ValueKind == JsonValueKind.String && DateTime.TryParse(element.GetString(), out _),
        SchemaFieldType.Array => element.ValueKind == JsonValueKind.Array,
        SchemaFieldType.Object => element.ValueKind == JsonValueKind.Object,
        _ => true
    };

    private static SchemaValidationResult ValidateConstraints(JsonElement element, FieldConstraints constraints)
    {
        var result = new SchemaValidationResult { IsValid = true };

        if (constraints.MinValue.HasValue && element.ValueKind == JsonValueKind.Number)
        {
            var value = element.GetDouble();
            if (value < constraints.MinValue.Value)
            {
                result.IsValid = false;
                result.Errors.Add($"Value {value} is below minimum {constraints.MinValue}");
            }
        }

        if (constraints.MaxValue.HasValue && element.ValueKind == JsonValueKind.Number)
        {
            var value = element.GetDouble();
            if (value > constraints.MaxValue.Value)
            {
                result.IsValid = false;
                result.Errors.Add($"Value {value} is above maximum {constraints.MaxValue}");
            }
        }

        if (constraints.Pattern != null && element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString() ?? "";
            if (!System.Text.RegularExpressions.Regex.IsMatch(value, constraints.Pattern))
            {
                result.IsValid = false;
                result.Errors.Add($"Value does not match pattern {constraints.Pattern}");
            }
        }

        if (constraints.AllowedValues != null && constraints.AllowedValues.Length > 0)
        {
            var value = element.ToString();
            if (!constraints.AllowedValues.Contains(value))
            {
                result.IsValid = false;
                result.Errors.Add($"Value {value} is not in allowed values");
            }
        }

        return result;
    }

    private static string MapToJsonSchemaType(SchemaFieldType type) => type switch
    {
        SchemaFieldType.String => "string",
        SchemaFieldType.Integer => "integer",
        SchemaFieldType.Decimal => "number",
        SchemaFieldType.Boolean => "boolean",
        SchemaFieldType.DateTime => "string",
        SchemaFieldType.Array => "array",
        SchemaFieldType.Object => "object",
        _ => "string"
    };

    private void RegisterBuiltInSchemas()
    {
        // Trade v1.0
        RegisterSchema(new SchemaDefinition
        {
            EventType = "Trade",
            Version = "1.0.0",
            IntroducedAt = new DateTime(2025, 1, 1),
            Description = "Trade execution event",
            Fields = new List<SchemaField>
            {
                new() { Name = "Timestamp", Type = SchemaFieldType.DateTime, Required = true, Description = "Event timestamp in UTC" },
                new() { Name = "Symbol", Type = SchemaFieldType.String, Required = true, Description = "Ticker symbol" },
                new() { Name = "Price", Type = SchemaFieldType.Decimal, Required = true, Description = "Trade price" },
                new() { Name = "Size", Type = SchemaFieldType.Integer, Required = true, Description = "Trade size in shares" },
                new() { Name = "Side", Type = SchemaFieldType.String, Required = false, Description = "Aggressor side (Buy/Sell/Unknown)" },
                new() { Name = "Exchange", Type = SchemaFieldType.String, Required = false, Description = "Exchange code" }
            }
        });

        // Trade v2.0 - Added TradeId and Conditions
        RegisterSchema(new SchemaDefinition
        {
            EventType = "Trade",
            Version = "2.0.0",
            IntroducedAt = new DateTime(2026, 1, 1),
            Description = "Trade execution event with trade identifiers",
            Fields = new List<SchemaField>
            {
                new() { Name = "Timestamp", Type = SchemaFieldType.DateTime, Required = true, Description = "Event timestamp in UTC" },
                new() { Name = "Symbol", Type = SchemaFieldType.String, Required = true, Description = "Ticker symbol" },
                new() { Name = "Price", Type = SchemaFieldType.Decimal, Required = true, Description = "Trade price" },
                new() { Name = "Size", Type = SchemaFieldType.Integer, Required = true, Description = "Trade size in shares" },
                new() { Name = "Side", Type = SchemaFieldType.String, Required = false, Description = "Aggressor side (Buy/Sell/Unknown)" },
                new() { Name = "Exchange", Type = SchemaFieldType.String, Required = false, Description = "Exchange code" },
                new() { Name = "TradeId", Type = SchemaFieldType.String, Required = false, Description = "Unique trade identifier" },
                new() { Name = "Conditions", Type = SchemaFieldType.Array, Required = false, Description = "Trade condition codes" }
            }
        });

        // Quote v1.0
        RegisterSchema(new SchemaDefinition
        {
            EventType = "Quote",
            Version = "1.0.0",
            IntroducedAt = new DateTime(2025, 1, 1),
            Description = "Best bid/offer quote",
            Fields = new List<SchemaField>
            {
                new() { Name = "Timestamp", Type = SchemaFieldType.DateTime, Required = true, Description = "Event timestamp in UTC" },
                new() { Name = "Symbol", Type = SchemaFieldType.String, Required = true, Description = "Ticker symbol" },
                new() { Name = "BidPrice", Type = SchemaFieldType.Decimal, Required = true, Description = "Best bid price" },
                new() { Name = "BidSize", Type = SchemaFieldType.Integer, Required = true, Description = "Bid size in shares" },
                new() { Name = "AskPrice", Type = SchemaFieldType.Decimal, Required = true, Description = "Best ask price" },
                new() { Name = "AskSize", Type = SchemaFieldType.Integer, Required = true, Description = "Ask size in shares" },
                new() { Name = "Exchange", Type = SchemaFieldType.String, Required = false, Description = "Exchange code" }
            }
        });

        // Register migration from Trade v1 to v2
        RegisterMigration(new SchemaMigration
        {
            EventType = "Trade",
            FromVersion = "1.0.0",
            ToVersion = "2.0.0",
            Description = "Add TradeId and Conditions fields",
            NewFieldDefaults = new Dictionary<string, object?>
            {
                ["TradeId"] = null,
                ["Conditions"] = Array.Empty<string>()
            }
        });
    }
}

/// <summary>
/// Schema definition for an event type.
/// </summary>
public sealed class SchemaDefinition
{
    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("introducedAt")]
    public DateTime IntroducedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("deprecatedAt")]
    public DateTime? DeprecatedAt { get; set; }

    [JsonPropertyName("fields")]
    public List<SchemaField> Fields { get; set; } = new();
}

/// <summary>
/// Field definition within a schema.
/// </summary>
public sealed class SchemaField
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public SchemaFieldType Type { get; set; } = SchemaFieldType.String;

    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("example")]
    public string? Example { get; set; }

    [JsonPropertyName("constraints")]
    public FieldConstraints? Constraints { get; set; }
}

/// <summary>
/// Field type enum.
/// </summary>
public enum SchemaFieldType : byte
{
    String,
    Integer,
    Decimal,
    Boolean,
    DateTime,
    Array,
    Object
}

/// <summary>
/// Constraints for field validation.
/// </summary>
public sealed class FieldConstraints
{
    [JsonPropertyName("minValue")]
    public double? MinValue { get; set; }

    [JsonPropertyName("maxValue")]
    public double? MaxValue { get; set; }

    [JsonPropertyName("pattern")]
    public string? Pattern { get; set; }

    [JsonPropertyName("allowedValues")]
    public string[]? AllowedValues { get; set; }
}

/// <summary>
/// Migration definition between schema versions.
/// </summary>
public sealed class SchemaMigration
{
    public string EventType { get; set; } = string.Empty;
    public string FromVersion { get; set; } = string.Empty;
    public string ToVersion { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Dictionary<string, string> FieldRenames { get; set; } = new();
    public Dictionary<string, object?> NewFieldDefaults { get; set; } = new();
    public List<string> RemovedFields { get; set; } = new();
    public Dictionary<string, Func<object?, object?>>? Transformations { get; set; }
}

/// <summary>
/// Result of a migration operation.
/// </summary>
public sealed class MigrationResult
{
    public string EventType { get; set; } = string.Empty;
    public string FromVersion { get; set; } = string.Empty;
    public string ToVersion { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Error { get; set; }
    public long RecordsProcessed { get; set; }
    public long RecordsFailed { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public TimeSpan Duration => CompletedAt - StartedAt;
}

/// <summary>
/// Result of schema validation.
/// </summary>
public sealed class SchemaValidationResult
{
    public string SchemaId { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Schema registry metadata.
/// </summary>
public sealed class SchemaRegistry
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("generatedAt")]
    public DateTime GeneratedAt { get; set; }

    [JsonPropertyName("schemas")]
    public List<SchemaRegistryEntry> Schemas { get; set; } = new();
}

/// <summary>
/// Entry in the schema registry.
/// </summary>
public sealed class SchemaRegistryEntry
{
    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("introduced")]
    public DateTime Introduced { get; set; }

    [JsonPropertyName("deprecated")]
    public DateTime? Deprecated { get; set; }

    [JsonPropertyName("fieldCount")]
    public int FieldCount { get; set; }
}
