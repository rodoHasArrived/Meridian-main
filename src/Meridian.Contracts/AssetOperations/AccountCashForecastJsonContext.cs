using System.Text.Json.Serialization;

namespace Meridian.Contracts.AssetOperations;

/// <summary>
/// Provides source-generated JSON metadata for account cash forecast requests, scopes, and snapshots.
/// </summary>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AccountCashForecastRequestDto))]
[JsonSerializable(typeof(AccountCashForecastSnapshotDto))]
[JsonSerializable(typeof(AccountCashForecastScopeDto))]
[JsonSerializable(typeof(AccountCashForecastSnapshotDto[]))]
public partial class AccountCashForecastJsonContext : JsonSerializerContext;
