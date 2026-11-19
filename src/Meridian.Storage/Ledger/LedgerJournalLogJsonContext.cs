using System.Text.Json.Serialization;
using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>Source-generated JSON metadata for the hash-chained ledger journal log.</summary>
/// <remarks>
/// A record's hash covers the exact entry JSON it stores, so this contract must stay stable:
/// changing names or options here changes what new records hash, and old records keep their own text.
/// </remarks>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(JournalEntry))]
[JsonSerializable(typeof(LedgerJournalHeader))]
[JsonSerializable(typeof(LedgerJournalChainRecord))]
[JsonSerializable(typeof(LedgerJournalChainHead))]
[JsonSerializable(typeof(LedgerJournalChainVerification))]
internal sealed partial class LedgerJournalLogJsonContext : JsonSerializerContext;
