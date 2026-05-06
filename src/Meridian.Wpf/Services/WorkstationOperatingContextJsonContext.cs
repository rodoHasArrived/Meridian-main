using System.Text.Json.Serialization;

namespace Meridian.Wpf.Services;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(WorkstationOperatingContextService.WorkstationOperatingContextStorageModel))]
internal sealed partial class WorkstationOperatingContextJsonContext : JsonSerializerContext;
