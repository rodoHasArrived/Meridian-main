using System.Text.Json.Serialization;
using Meridian.Contracts.Ledger;

namespace Meridian.Storage.Ledger;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AccountingPostingCommandDto))]
internal sealed partial class AccountingPostingCommandFingerprintJsonContext : JsonSerializerContext;
