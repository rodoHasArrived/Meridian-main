using System.Text.Json.Serialization;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Services;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(QuantScriptExecutionRecord))]
[JsonSerializable(typeof(QuantScriptTemplateCatalogManifest))]
internal sealed partial class QuantScriptStorageJsonContext : JsonSerializerContext;
