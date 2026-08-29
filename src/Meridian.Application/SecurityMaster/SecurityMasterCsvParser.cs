using System.Text;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Parses CSV files for Security Master bulk import.
/// Expected header columns (case-insensitive):
/// Ticker, Name, AssetClass, Currency, Exchange, ISIN, CUSIP, FIGI
/// <para>
/// The accepted asset classes are DERIVED from
/// <see cref="SecurityAssetClassCatalog.IdentifierOnlyImportableAssetClasses"/> rather than kept in a
/// private table here: a CSV row carries identity only, so it can create exactly the classes whose
/// asset-specific terms are all optional. A class with a required term (an option's strike, a bond's
/// maturity) is refused with a message that says so, because the alternative — defaulting the term —
/// would mint a governed record on an economic fact nobody supplied.
/// </para>
/// </summary>
public sealed class SecurityMasterCsvParser
{
    /// <summary>
    /// Parses CSV content and returns a list of CreateSecurityRequest objects.
    /// Errors during parsing are collected in the out parameter.
    /// </summary>
    /// <param name="csvContent">Raw CSV file content</param>
    /// <param name="errors">List of row-level parsing errors</param>
    /// <param name="actor">
    /// The operator or workload on whose authority the import runs, recorded as <c>UpdatedBy</c> on
    /// every row. Callers resolve this from their authenticated session; a CSV file carries no
    /// identity of its own, so the parser will not invent one. This is deliberately separate from
    /// <c>SourceSystem</c>, which stays the constant import-source identifier that conflict detection
    /// and source precedence key on.
    /// </param>
    /// <param name="ingestedAtUtc">
    /// Server-controlled ingest time used for the request effective date and every identifier
    /// validity start. The import coordinator supplies one value for the entire file so a batch
    /// cannot acquire row-by-row or identifier-by-identifier valid times.
    /// </param>
    /// <returns>List of successfully parsed CreateSecurityRequest records</returns>
    public IReadOnlyList<CreateSecurityRequest> Parse(
        string csvContent,
        out IReadOnlyList<string> errors,
        string actor,
        DateTimeOffset? ingestedAtUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var authorityTimestamp = ingestedAtUtc ?? DateTimeOffset.UtcNow;

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
            var record = ParseRow(
                values.ToArray(),
                headers,
                rowNumber,
                actor,
                authorityTimestamp,
                out var rowError);
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

    private CreateSecurityRequest? ParseRow(
        string[] values,
        string[] headers,
        int rowNumber,
        string actor,
        DateTimeOffset ingestedAtUtc,
        out string? error)
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

        // Map asset class. Canonical names and the catalog's registered vendor aliases both resolve;
        // a class that needs asset-specific terms this file cannot carry is refused by name.
        var assetClass = SecurityAssetClassCatalog.ResolveIdentifierOnlyImportableAssetClass(assetClassInput);
        if (assetClass is null)
        {
            var importable = string.Join(", ", SecurityAssetClassCatalog.IdentifierOnlyImportableAssetClasses);
            error = SecurityAssetClassCatalog.GetOrDefault(assetClassInput).AssetClass == "Unknown"
                ? $"Row {rowNumber}: Unknown AssetClass '{assetClassInput}'. Importable values: {importable}"
                : $"Row {rowNumber}: AssetClass '{assetClassInput}' requires asset-specific terms that a CSV " +
                  $"import cannot supply. Importable values: {importable}";
            return null;
        }

        // Optional fields
        fields.TryGetValue("Currency", out var currency);
        if (string.IsNullOrWhiteSpace(currency))
            currency = "USD";

        fields.TryGetValue("Exchange", out var exchange);
        fields.TryGetValue("ISIN", out var isin);
        fields.TryGetValue("CUSIP", out var cusip);
        fields.TryGetValue("FIGI", out var figi);

        // Build identifiers
        var identifiers = new List<SecurityIdentifierDto>
        {
            new(SecurityIdentifierKind.Ticker, ticker, true, ingestedAtUtc)
        };

        if (!string.IsNullOrWhiteSpace(isin))
            identifiers.Add(new(SecurityIdentifierKind.Isin, isin, false, ingestedAtUtc));

        if (!string.IsNullOrWhiteSpace(cusip))
            identifiers.Add(new(SecurityIdentifierKind.Cusip, cusip, false, ingestedAtUtc));

        if (!string.IsNullOrWhiteSpace(figi))
            identifiers.Add(new(SecurityIdentifierKind.Figi, figi, false, ingestedAtUtc));

        return new CreateSecurityRequest(
            SecurityId: Guid.NewGuid(),
            AssetClass: assetClass,
            CommonTerms: BuildCommonTerms(name, currency, exchange),
            AssetSpecificTerms: BuildAssetSpecificTerms(),
            Identifiers: identifiers,
            EffectiveFrom: ingestedAtUtc,
            SourceSystem: "SecurityMasterImport",
            UpdatedBy: actor,
            SourceRecordId: null,
            Reason: null
        );
    }

    /// <summary>
    /// Builds the common-terms payload the create path requires. <c>displayName</c> and
    /// <c>currency</c> are mandatory there, so a row that parsed them must carry them through —
    /// emitting an empty document instead made every import row fail at create time.
    /// </summary>
    private static JsonElement BuildCommonTerms(string name, string currency, string? exchange)
    {
        var commonTerms = new Dictionary<string, object?>
        {
            ["displayName"] = name,
            ["currency"] = currency
        };

        if (!string.IsNullOrWhiteSpace(exchange))
        {
            commonTerms["exchange"] = exchange;
        }

        return JsonSerializer.SerializeToElement(commonTerms);
    }

    /// <summary>
    /// The asset-specific-terms payload for an identity-only import: the schema-version stamp and
    /// nothing else. Every importable class declares its terms optional, so an empty term set is the
    /// complete, honest contract for the row — no term is invented for a column the file never had.
    /// </summary>
    private static JsonElement BuildAssetSpecificTerms()
        => JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["schemaVersion"] = SecurityMasterSchemaVersions.LegacyAssetSpecificTerms
        });

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
