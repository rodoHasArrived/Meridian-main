using System.Text;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Parses CSV files for Security Master bulk import.
/// Expected header columns (case-insensitive):
/// Ticker, Name, AssetClass, Currency, Exchange, ISIN, CUSIP, FIGI
/// </summary>
public sealed class SecurityMasterCsvParser
{
    private static readonly Dictionary<string, string> AssetClassMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Equity", "Equity" },
        { "Option", "Option" },
        { "Future", "Future" },
        { "Bond", "Bond" },
        { "Crypto", "CryptoCurrency" },
        { "CryptoCurrency", "CryptoCurrency" },
        { "Commodity", "Commodity" },
        { "CFD", "Cfd" },
        { "Cfd", "Cfd" },
        { "Warrant", "Warrant" },
    };

    /// <summary>
    /// Parses CSV content and returns a list of CreateSecurityRequest objects.
    /// Errors during parsing are collected in the out parameter.
    /// </summary>
    /// <param name="csvContent">Raw CSV file content</param>
    /// <param name="errors">List of row-level parsing errors</param>
    /// <returns>List of successfully parsed CreateSecurityRequest records</returns>
    public IReadOnlyList<CreateSecurityRequest> Parse(string csvContent, out IReadOnlyList<string> errors)
    {
        var commands = new List<CreateSecurityRequest>();
        var errorList = new List<string>();

        var lines = csvContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        if (lines.Length == 0)
        {
            errors = errorList;
            return commands;
        }

        string[]? headers = null;
        int rowNumber = 0;

        foreach (var line in lines)
        {
            rowNumber++;

            // Skip blank lines
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var values = ParseCsvLine(line);

            // First non-blank line is the header
            if (headers == null)
            {
                headers = values.Select(v => v.Trim()).ToArray();
                continue;
            }

            // Parse data row
            var record = ParseRow(values.ToArray(), headers, rowNumber, out var rowError);
            if (rowError != null)
            {
                errorList.Add(rowError);
            }
            else if (record != null)
            {
                commands.Add(record);
            }
        }

        errors = errorList;
        return commands;
    }

    private CreateSecurityRequest? ParseRow(string[] values, string[] headers, int rowNumber, out string? error)
    {
        error = null;

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Length && i < values.Length; i++)
        {
            fields[headers[i]] = values[i].Trim();
        }

        // Required: Ticker
        if (!fields.TryGetValue("Ticker", out var ticker) || string.IsNullOrWhiteSpace(ticker))
        {
            error = $"Row {rowNumber}: Missing required field 'Ticker'";
            return null;
        }

        // Required: Name
        if (!fields.TryGetValue("Name", out var name) || string.IsNullOrWhiteSpace(name))
        {
            error = $"Row {rowNumber}: Missing required field 'Name'";
            return null;
        }

        // Required: AssetClass
        if (!fields.TryGetValue("AssetClass", out var assetClassInput) || string.IsNullOrWhiteSpace(assetClassInput))
        {
            error = $"Row {rowNumber}: Missing required field 'AssetClass'";
            return null;
        }

        // Map asset class
        if (!AssetClassMapping.TryGetValue(assetClassInput, out var assetClass))
        {
            error = $"Row {rowNumber}: Unknown AssetClass '{assetClassInput}'. Valid values: {string.Join(", ", AssetClassMapping.Keys)}";
            return null;
        }

        // Optional fields
        fields.TryGetValue("Currency", out var currency);
        if (string.IsNullOrWhiteSpace(currency))
            currency = "USD";

        fields.TryGetValue("ISIN", out var isin);
        fields.TryGetValue("CUSIP", out var cusip);
        fields.TryGetValue("FIGI", out var figi);

        // Build identifiers
        var identifiers = new List<SecurityIdentifierDto>
        {
            new(SecurityIdentifierKind.Ticker, ticker, true, DateTimeOffset.UtcNow)
        };

        if (!string.IsNullOrWhiteSpace(isin))
            identifiers.Add(new(SecurityIdentifierKind.Isin, isin, false, DateTimeOffset.UtcNow));

        if (!string.IsNullOrWhiteSpace(cusip))
            identifiers.Add(new(SecurityIdentifierKind.Cusip, cusip, false, DateTimeOffset.UtcNow));

        if (!string.IsNullOrWhiteSpace(figi))
            identifiers.Add(new(SecurityIdentifierKind.Figi, figi, false, DateTimeOffset.UtcNow));

        return new CreateSecurityRequest(
            SecurityId: Guid.NewGuid(),
            AssetClass: assetClass,
            CommonTerms: System.Text.Json.JsonDocument.Parse("{}").RootElement,
            AssetSpecificTerms: System.Text.Json.JsonDocument.Parse("{}").RootElement,
            Identifiers: identifiers,
            EffectiveFrom: DateTimeOffset.UtcNow,
            SourceSystem: "SecurityMasterImport",
            UpdatedBy: "WpfImport",
            SourceRecordId: null,
            Reason: null
        );
    }

    /// <summary>
    /// Splits a CSV line respecting quoted values and escaped quotes.
    /// </summary>
    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    // Escaped quote
                    current.Append('"');
                    i++;
                }
                else
                {
                    // Toggle quote state
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                // End of field
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        values.Add(current.ToString());
        return values;
    }
}
