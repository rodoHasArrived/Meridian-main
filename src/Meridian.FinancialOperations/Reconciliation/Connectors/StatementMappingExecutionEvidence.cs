using System.Text.Json;
using Meridian.Contracts.Integrity;

namespace Meridian.FinancialOperations.Reconciliation.Connectors;

/// <summary>Captures the mapping executed during parsing, not a later catalog revision.</summary>
internal static class StatementMappingExecutionEvidence
{
    public static string ForProfile(string connectorId, StatementMappingProfileDocument profile)
        => Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new
        {
            ConnectorId = connectorId,
            Revision = "statement-connector-v1",
            profile.ProfileId,
            profile.SchemaVersion,
            profile.Format,
            profile.Csv,
            profile.Culture,
            profile.DateFormats,
            profile.Fields,
            profile.ActivityCodes
        }));

    public static string ForBuiltIn(string connectorId)
        => Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new { ConnectorId = connectorId, Revision = "statement-connector-v1" }));
}
