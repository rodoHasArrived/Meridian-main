namespace Meridian.FinancialOperations.Reconciliation.Connectors;

/// <summary>
/// Resolves statement connectors by id and auto-detects the right connector for an
/// uploaded document. The CSV connector is consulted last because it is the text
/// catch-all; structured formats (OFX, Flex XML, activity snapshots) sniff first.
/// </summary>
public sealed class StatementConnectorRegistry
{
    private readonly IReadOnlyList<IStatementConnector> _connectors;

    public StatementConnectorRegistry(IEnumerable<IStatementConnector> connectors)
    {
        ArgumentNullException.ThrowIfNull(connectors);
        _connectors = connectors
            .OrderBy(static connector =>
                string.Equals(connector.Descriptor.ConnectorId, CsvStatementConnector.ConnectorId, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ToArray();
    }

    public IReadOnlyList<StatementConnectorDescriptor> List() =>
        _connectors.Select(static connector => connector.Descriptor).ToArray();

    public IStatementConnector Resolve(string connectorId)
    {
        var connector = _connectors.FirstOrDefault(candidate =>
            string.Equals(candidate.Descriptor.ConnectorId, connectorId?.Trim(), StringComparison.OrdinalIgnoreCase));
        return connector
            ?? throw new NotSupportedException($"Statement connector '{connectorId}' is not registered.");
    }

    public IStatementConnector? Detect(StatementSourceDocument document) =>
        _connectors.FirstOrDefault(connector => connector.CanHandle(document));
}
