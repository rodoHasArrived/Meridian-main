using System.Text.Json.Serialization;

namespace Meridian.Contracts.Schema;

/// <summary>
/// Schema definition for a data event type.
/// </summary>
public sealed class EventSchema
{
    /// <summary>
    /// Gets or sets the schema name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the schema version (semantic versioning).
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// Gets or sets the schema description.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the date when this schema was introduced.
    /// </summary>
    [JsonPropertyName("introducedAt")]
    public DateTime IntroducedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the date when this schema was deprecated.
    /// </summary>
    [JsonPropertyName("deprecatedAt")]
    public DateTime? DeprecatedAt { get; set; }

    /// <summary>
    /// Gets or sets the array of field definitions.
    /// </summary>
    [JsonPropertyName("fields")]
    public SchemaField[] Fields { get; set; } = Array.Empty<SchemaField>();

    /// <summary>
    /// Gets or sets the primary key field names.
    /// </summary>
    [JsonPropertyName("primaryKey")]
    public string[]? PrimaryKey { get; set; }

    /// <summary>
    /// Gets or sets the index definitions (array of field name arrays).
    /// </summary>
    [JsonPropertyName("indexes")]
    public string[][]? Indexes { get; set; }

    /// <summary>
    /// Gets or sets the previous version this schema migrates from.
    /// </summary>
    [JsonPropertyName("migrationFromVersion")]
    public string? MigrationFromVersion { get; set; }

    /// <summary>
    /// Gets or sets a sample record for documentation purposes.
    /// </summary>
    [JsonPropertyName("sampleRecord")]
    public Dictionary<string, object>? SampleRecord { get; set; }
}

/// <summary>
/// Schema field definition.
/// </summary>
public sealed class SchemaField
{
    /// <summary>
    /// Gets or sets the field name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the field data type.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    /// <summary>
    /// Gets or sets the field description.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the field can be null.
    /// </summary>
    [JsonPropertyName("nullable")]
    public bool Nullable { get; set; }

    /// <summary>
    /// Gets or sets the default value for the field.
    /// </summary>
    [JsonPropertyName("defaultValue")]
    public object? DefaultValue { get; set; }

    /// <summary>
    /// Gets or sets the valid range constraints for the field.
    /// </summary>
    [JsonPropertyName("validRange")]
    public FieldValidRange? ValidRange { get; set; }

    /// <summary>
    /// Gets or sets the allowed enum values for the field.
    /// </summary>
    [JsonPropertyName("enumValues")]
    public string[]? EnumValues { get; set; }

    /// <summary>
    /// Gets or sets the format specification (e.g., "date-time", "uuid").
    /// </summary>
    [JsonPropertyName("format")]
    public string? Format { get; set; }

    /// <summary>
    /// Gets or sets an example value for documentation.
    /// </summary>
    [JsonPropertyName("example")]
    public object? Example { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this field is exchange-specific.
    /// </summary>
    [JsonPropertyName("exchangeSpecific")]
    public bool ExchangeSpecific { get; set; }

    /// <summary>
    /// Gets or sets additional notes about the field.
    /// </summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

/// <summary>
/// Valid range for a field.
/// </summary>
public sealed class FieldValidRange
{
    /// <summary>
    /// Gets or sets the minimum allowed value.
    /// </summary>
    [JsonPropertyName("min")]
    public object? Min { get; set; }

    /// <summary>
    /// Gets or sets the maximum allowed value.
    /// </summary>
    [JsonPropertyName("max")]
    public object? Max { get; set; }

    /// <summary>
    /// Gets or sets the regex pattern for string validation.
    /// </summary>
    [JsonPropertyName("pattern")]
    public string? Pattern { get; set; }
}

/// <summary>
/// Data dictionary containing all schemas.
/// </summary>
public sealed class DataDictionary
{
    /// <summary>
    /// Gets or sets the data dictionary version.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    /// <summary>
    /// Gets or sets the timestamp when the dictionary was generated.
    /// </summary>
    [JsonPropertyName("generatedAt")]
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the schema definitions keyed by schema name.
    /// </summary>
    [JsonPropertyName("schemas")]
    public Dictionary<string, EventSchema> Schemas { get; set; } = new();

    /// <summary>
    /// Gets or sets the exchange code mappings (code to name).
    /// </summary>
    [JsonPropertyName("exchangeCodes")]
    public Dictionary<string, string>? ExchangeCodes { get; set; }

    /// <summary>
    /// Gets or sets the trade condition code mappings (code to description).
    /// </summary>
    [JsonPropertyName("tradeConditions")]
    public Dictionary<string, string>? TradeConditions { get; set; }

    /// <summary>
    /// Gets or sets the quote condition code mappings (code to description).
    /// </summary>
    [JsonPropertyName("quoteConditions")]
    public Dictionary<string, string>? QuoteConditions { get; set; }
}
