namespace Meridian.Ui.Services.Contracts;

/// <summary>
/// Interface for schema services used by shared UI services.
/// Implemented by platform-specific schema services (WPF).
/// </summary>
public interface ISchemaService
{
    /// <summary>
    /// Gets the JSON schema for a specific event type.
    /// </summary>
    /// <param name="eventType">The event type (e.g., "Trade", "BboQuote", "LOBSnapshot", "HistoricalBar").</param>
    /// <returns>JSON schema string, or null if not available.</returns>
    string? GetJsonSchema(string eventType);
}
