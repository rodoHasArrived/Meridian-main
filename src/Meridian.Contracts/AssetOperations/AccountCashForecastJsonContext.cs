using System.Text.Json.Serialization;

namespace Meridian.Contracts.AssetOperations;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AccountCashForecastRequestDto))]
[JsonSerializable(typeof(AccountCashForecastSnapshotDto))]
[JsonSerializable(typeof(AccountCashForecastScopeDto))]
[JsonSerializable(typeof(AccountCashForecastSnapshotDto[]))]
public partial class AccountCashForecastJsonContext : JsonSerializerContext;
