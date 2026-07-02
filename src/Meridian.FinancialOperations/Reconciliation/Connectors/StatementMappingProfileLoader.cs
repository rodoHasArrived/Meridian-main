namespace Meridian.FinancialOperations.Reconciliation.Connectors;

/// <summary>
/// Validates declarative mapping-profile documents and materializes them into the runtime
/// <see cref="StatementMappingProfile"/> records the reconciliation engine already consumes.
/// </summary>
public static class StatementMappingProfileLoader
{
    private static readonly string[] SupportedFormats = [StatementMappingProfileDocument.CsvFormat, StatementMappingProfileDocument.OfxFormat];

    /// <summary>Canonical fields every profile must map for rows to classify and date correctly.</summary>
    private static readonly StatementCanonicalField[] MandatoryFields =
    [
        StatementCanonicalField.Account,
        StatementCanonicalField.ActivityType,
        StatementCanonicalField.TradeDate
    ];

    public static IReadOnlyList<string> Validate(StatementMappingProfileDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var errors = new List<string>();

        if (document.SchemaVersion != StatementMappingProfileDocument.CurrentSchemaVersion)
        {
            errors.Add($"Profile schema version {document.SchemaVersion} is not supported. Expected {StatementMappingProfileDocument.CurrentSchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(document.ProfileId))
        {
            errors.Add("Profile id is required.");
        }

        if (string.IsNullOrWhiteSpace(document.DisplayName))
        {
            errors.Add("Profile display name is required.");
        }

        if (!SupportedFormats.Contains(document.Format?.Trim().ToLowerInvariant()))
        {
            errors.Add($"Profile format '{document.Format}' is not supported. Use '{StatementMappingProfileDocument.CsvFormat}' or '{StatementMappingProfileDocument.OfxFormat}'.");
        }

        if (document.Csv is { Delimiter.Length: not 1 })
        {
            errors.Add("CSV delimiter must be a single character.");
        }

        if (document.Csv is { Quote.Length: not 1 })
        {
            errors.Add("CSV quote must be a single character.");
        }

        if (!string.IsNullOrWhiteSpace(document.Culture))
        {
            try
            {
                _ = System.Globalization.CultureInfo.GetCultureInfo(document.Culture, predefinedOnly: true);
            }
            catch (System.Globalization.CultureNotFoundException)
            {
                errors.Add($"Profile culture '{document.Culture}' is not a known culture.");
            }
        }

        if (document.Fields is null || document.Fields.Count == 0)
        {
            errors.Add("Profile must map at least one field.");
            return errors;
        }

        var seenCanonical = new HashSet<StatementCanonicalField>();
        var seenColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in document.Fields)
        {
            if (!Enum.TryParse<StatementCanonicalField>(field.CanonicalField, ignoreCase: true, out var canonical))
            {
                errors.Add($"Field '{field.CanonicalField}' is not a canonical statement field.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(field.SourceColumn))
            {
                errors.Add($"Field '{field.CanonicalField}' must name a source column.");
                continue;
            }

            if (!seenCanonical.Add(canonical))
            {
                errors.Add($"Canonical field '{canonical}' is mapped more than once.");
            }

            if (!seenColumns.Add(field.SourceColumn))
            {
                errors.Add($"Source column '{field.SourceColumn}' is mapped more than once.");
            }
        }

        foreach (var mandatory in MandatoryFields)
        {
            if (!seenCanonical.Contains(mandatory))
            {
                errors.Add($"Profile must map the '{mandatory}' canonical field.");
            }
        }

        foreach (var code in document.ActivityCodes ?? [])
        {
            if (string.IsNullOrWhiteSpace(code.SourceCode) || string.IsNullOrWhiteSpace(code.CanonicalActivityType))
            {
                errors.Add("Activity code mappings require both a source code and a canonical activity type.");
                break;
            }
        }

        return errors;
    }

    /// <summary>
    /// Materializes the document into the runtime profile the existing reconciliation paths
    /// consume. Aliases, culture, and CSV options are connector-level concerns and are not
    /// carried into the runtime shape.
    /// </summary>
    public static StatementMappingProfile ToRuntimeProfile(StatementMappingProfileDocument document)
    {
        var errors = Validate(document);
        if (errors.Count > 0)
        {
            throw new InvalidDataException($"Statement mapping profile '{document.ProfileId}' is invalid: {string.Join(" ", errors)}");
        }

        var fieldMappings = document.Fields
            .Select(field => new StatementFieldMapping(
                Enum.Parse<StatementCanonicalField>(field.CanonicalField, ignoreCase: true),
                field.SourceColumn.Trim(),
                field.Required))
            .ToArray();

        var codeMappings = (document.ActivityCodes ?? [])
            .Select(code => new StatementTransactionCodeMapping(code.SourceCode.Trim(), code.CanonicalActivityType.Trim()))
            .ToArray();

        return new StatementMappingProfile(
            document.ProfileId.Trim(),
            document.DisplayName.Trim(),
            fieldMappings,
            codeMappings);
    }

    public static StatementFieldMapping? FindDocumentField(
        StatementMappingProfileDocument document,
        StatementCanonicalField canonicalField)
    {
        var field = document.Fields.FirstOrDefault(candidate =>
            Enum.TryParse<StatementCanonicalField>(candidate.CanonicalField, ignoreCase: true, out var parsed)
            && parsed == canonicalField);
        return field is null
            ? null
            : new StatementFieldMapping(canonicalField, field.SourceColumn, field.Required);
    }
}
