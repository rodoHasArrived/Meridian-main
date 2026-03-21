using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Meridian.Application.Config;

/// <summary>
/// Generates a JSON Schema document for <see cref="AppConfig"/> and its nested
/// configuration types so editors can validate <c>appsettings.json</c>.
/// </summary>
public sealed class ConfigJsonSchemaGenerator
{
    private const string SchemaDialect = "https://json-schema.org/draft/2020-12/schema";
    private readonly Dictionary<Type, string> _definitionNames = new();
    private readonly Dictionary<string, JsonObject> _definitions = new(StringComparer.Ordinal);
    private readonly HashSet<Type> _building = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <summary>
    /// Generates the schema document for <see cref="AppConfig"/>.
    /// </summary>
    public JsonObject GenerateSchema()
    {
        _definitionNames.Clear();
        _definitions.Clear();
        _building.Clear();

        var rootSchema = BuildDefinition(typeof(AppConfig)).DeepClone().AsObject();
        rootSchema["$schema"] = SchemaDialect;
        rootSchema["title"] = "Meridian appsettings schema";
        rootSchema["description"] = "JSON Schema for Meridian application configuration.";

        if (_definitions.Count > 1)
        {
            var defs = new JsonObject();
            foreach (var (name, definition) in _definitions.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
            {
                if (string.Equals(name, GetDefinitionName(typeof(AppConfig)), StringComparison.Ordinal))
                {
                    continue;
                }

                defs[name] = definition.DeepClone();
            }

            if (defs.Count > 0)
            {
                rootSchema["$defs"] = defs;
            }
        }

        return rootSchema;
    }

    /// <summary>
    /// Generates the schema JSON text.
    /// </summary>
    public string GenerateSchemaJson()
        => GenerateSchema().ToJsonString(_jsonOptions);

    /// <summary>
    /// Writes the generated schema to disk.
    /// </summary>
    public void WriteSchema(string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(outputPath, GenerateSchemaJson());
    }

    private JsonNode BuildSchemaForProperty(PropertyInfo property)
    {
        var nullability = new NullabilityInfoContext().Create(property);
        var schema = BuildSchemaForType(property.PropertyType);

        if (AllowsNull(property.PropertyType, nullability))
        {
            return AllowNull(schema);
        }

        return schema;
    }

    private JsonNode BuildSchemaForType(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type);
        if (underlyingType != null)
        {
            return AllowNull(BuildSchemaForType(underlyingType));
        }

        if (type == typeof(string))
        {
            return CreateTypedSchema("string");
        }

        if (type == typeof(bool))
        {
            return CreateTypedSchema("boolean");
        }

        if (type == typeof(byte) || type == typeof(short) || type == typeof(int) || type == typeof(long)
            || type == typeof(sbyte) || type == typeof(ushort) || type == typeof(uint) || type == typeof(ulong))
        {
            return CreateTypedSchema("integer");
        }

        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
        {
            return CreateTypedSchema("number");
        }

        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            return new JsonObject
            {
                ["type"] = "string",
                ["format"] = "date-time"
            };
        }

        if (type == typeof(DateOnly))
        {
            return new JsonObject
            {
                ["type"] = "string",
                ["format"] = "date"
            };
        }

        if (type == typeof(TimeOnly) || type == typeof(TimeSpan) || type == typeof(Uri))
        {
            return CreateTypedSchema("string");
        }

        if (type.IsEnum)
        {
            return new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray(Enum.GetNames(type).Select(static name => (JsonNode?)name).ToArray())
            };
        }

        if (TryGetDictionaryValueType(type, out var valueType))
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = BuildSchemaForType(valueType)
            };
        }

        if (TryGetEnumerableElementType(type, out var elementType))
        {
            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = BuildSchemaForType(elementType)
            };
        }

        var definitionName = GetDefinitionName(type);
        BuildDefinition(type);
        return new JsonObject
        {
            ["$ref"] = $"#/$defs/{definitionName}"
        };
    }

    private JsonObject BuildDefinition(Type type)
    {
        var definitionName = GetDefinitionName(type);
        if (_definitions.TryGetValue(definitionName, out var existing) && !_building.Contains(type))
        {
            return existing;
        }

        if (_building.Contains(type))
        {
            return _definitions[definitionName];
        }

        var definition = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false
        };

        _definitions[definitionName] = definition;
        _building.Add(type);

        var properties = new JsonObject();
        foreach (var property in GetSchemaProperties(type))
        {
            var propertySchema = BuildSchemaForProperty(property);
            AddDefaultValue(propertySchema, property, type);
            properties[GetJsonPropertyName(property)] = propertySchema;
        }

        definition["properties"] = properties;
        _building.Remove(type);
        return definition;
    }

    private void AddDefaultValue(JsonNode schema, PropertyInfo property, Type declaringType)
    {
        if (schema is not JsonObject schemaObject)
        {
            return;
        }

        object? defaultInstance = null;
        try
        {
            defaultInstance = Activator.CreateInstance(declaringType);
        }
        catch
        {
            // Best-effort only: some types may not have a default constructor.
        }

        if (defaultInstance == null)
        {
            return;
        }

        var defaultValue = property.GetValue(defaultInstance);
        if (defaultValue == null)
        {
            return;
        }

        if (!TryConvertDefaultValue(defaultValue, out var defaultNode))
        {
            return;
        }

        schemaObject["default"] = defaultNode;
    }

    private bool TryConvertDefaultValue(object value, out JsonNode? node)
    {
        try
        {
            node = JsonSerializer.SerializeToNode(value, value.GetType(), _jsonOptions);
            return node != null;
        }
        catch
        {
            node = null;
            return false;
        }
    }

    private static IEnumerable<PropertyInfo> GetSchemaProperties(Type type)
        => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .OrderBy(p => p.Name, StringComparer.Ordinal);

    private string GetDefinitionName(Type type)
    {
        if (_definitionNames.TryGetValue(type, out var existing))
        {
            return existing;
        }

        var baseName = type.Name;
        var candidate = baseName;
        var suffix = 2;
        while (_definitions.ContainsKey(candidate))
        {
            candidate = $"{baseName}{suffix++}";
        }

        _definitionNames[type] = candidate;
        return candidate;
    }

    private static string GetJsonPropertyName(PropertyInfo property)
        => property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name;

    private static JsonObject CreateTypedSchema(string jsonType)
        => new()
        {
            ["type"] = jsonType
        };

    private static bool AllowsNull(Type type, NullabilityInfo nullability)
        => Nullable.GetUnderlyingType(type) != null
           || (type.IsClass && nullability.ReadState != NullabilityState.NotNull);

    private static JsonObject AllowNull(JsonNode schema)
        => new()
        {
            ["anyOf"] = new JsonArray
            {
                schema.DeepClone(),
                new JsonObject
                {
                    ["type"] = "null"
                }
            }
        };

    private static bool TryGetEnumerableElementType(Type type, out Type elementType)
    {
        if (type == typeof(string))
        {
            elementType = null!;
            return false;
        }

        if (type.IsArray)
        {
            elementType = type.GetElementType()!;
            return true;
        }

        var enumerableInterface = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            ? type
            : type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        if (enumerableInterface != null)
        {
            elementType = enumerableInterface.GetGenericArguments()[0];
            return true;
        }

        elementType = null!;
        return false;
    }

    private static bool TryGetDictionaryValueType(Type type, out Type valueType)
    {
        var dictionaryInterface = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>)
            ? type
            : type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));

        if (dictionaryInterface != null && dictionaryInterface.GetGenericArguments()[0] == typeof(string))
        {
            valueType = dictionaryInterface.GetGenericArguments()[1];
            return true;
        }

        valueType = null!;
        return false;
    }
}
