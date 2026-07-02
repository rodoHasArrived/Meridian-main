using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Execution.Sdk;

namespace Meridian.FinancialOperations.Reconciliation.Connectors.Alpaca;

/// <summary>
/// The retained document produced by an Alpaca statement fetch: account activity
/// (orders/fills/cash) plus an optional portfolio snapshot (positions + balance), so a
/// single fetch covers transactional and position data. Serialized as the connector's
/// source document, giving remote fetches the same raw-evidence trail as file uploads.
/// </summary>
public sealed record AlpacaStatementSnapshot(
    string ProviderId,
    string AccountId,
    DateTimeOffset RetrievedAt,
    BrokerageActivitySnapshotDto? Activity,
    BrokeragePortfolioSnapshotDto? Portfolio);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AlpacaStatementSnapshot))]
internal sealed partial class AlpacaStatementSnapshotJsonContext : JsonSerializerContext;
