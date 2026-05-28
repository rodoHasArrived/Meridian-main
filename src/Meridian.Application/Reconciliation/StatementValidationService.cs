using System.Globalization;

namespace Meridian.Application.Reconciliation;

public sealed class StatementValidationService(IStatementValidationReferenceData referenceData)
{
    private static readonly string[] DefaultRequiredFields =
    [
        StatementValidationFields.Account,
        StatementValidationFields.SecurityIdentifier,
        StatementValidationFields.Quantity,
        StatementValidationFields.Price,
        StatementValidationFields.CashAmount,
        StatementValidationFields.ActivityCode,
        StatementValidationFields.TradeDate,
        StatementValidationFields.Currency
    ];

    private static readonly HashSet<string> DefaultOptionalFields = new(StringComparer.OrdinalIgnoreCase)
    {
        StatementValidationFields.FeeAmount,
        StatementValidationFields.SettlementDate,
        StatementValidationFields.Description
    };

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd",
        "yyyyMMdd",
        "MM/dd/yyyy",
        "M/d/yyyy",
        "yyyy-MM-ddTHH:mm:ssK",
        "yyyy-MM-ddTHH:mm:ss.fffK"
    ];

    private static readonly HashSet<string> DecimalFields = new(StringComparer.OrdinalIgnoreCase)
    {
        StatementValidationFields.Quantity,
        StatementValidationFields.Price,
        StatementValidationFields.CashAmount,
        StatementValidationFields.FeeAmount
    };

    private static readonly HashSet<string> DateFields = new(StringComparer.OrdinalIgnoreCase)
    {
        StatementValidationFields.TradeDate,
        StatementValidationFields.SettlementDate
    };

    public async Task<StatementValidationResultDto> ValidateAsync(
        StatementValidationRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var issues = new List<StatementValidationIssueDto>();
        var normalizedPath = ValidateSourceFile(request.SourcePath, issues);
        ValidatePeriod(request, issues);

        var importFingerprint = string.Empty;
        if (normalizedPath is not null)
        {
            importFingerprint = await ComputeFileFingerprintAsync(normalizedPath, ct).ConfigureAwait(false);
        }

        await ValidateRunReferencesAsync(request, importFingerprint, issues, ct).ConfigureAwait(false);

        if (normalizedPath is not null)
        {
            await ValidateRowsAsync(normalizedPath, request, issues, ct).ConfigureAwait(false);
        }

        return new StatementValidationResultDto(
            SourcePath: request.SourcePath,
            AccountId: request.AccountId,
            ImportFingerprint: importFingerprint,
            IsBlocked: issues.Any(static issue => issue.Severity == StatementValidationIssueSeverity.Blocker),
            Issues: issues);
    }

    private static string? ValidateSourceFile(string sourcePath, List<StatementValidationIssueDto> issues)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            issues.Add(Blocker(StatementValidationIssueCode.SourceFileUnreadable, null, null, "Statement source file path is required."));
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(sourcePath);
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return fullPath;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            issues.Add(Blocker(StatementValidationIssueCode.SourceFileUnreadable, null, null, $"Statement source file '{sourcePath}' could not be read: {ex.Message}"));
            return null;
        }
    }

    private static void ValidatePeriod(StatementValidationRequestDto request, List<StatementValidationIssueDto> issues)
    {
        if (request.StatementPeriodStart is null || request.StatementPeriodEnd is null)
        {
            issues.Add(Blocker(StatementValidationIssueCode.MissingStatementPeriod, null, null, "Statement period start and end are required."));
            return;
        }

        if (request.StatementPeriodStart > request.StatementPeriodEnd)
        {
            issues.Add(Blocker(StatementValidationIssueCode.InvalidStatementPeriod, null, null, "Statement period start must be on or before the statement period end."));
        }
    }

    private async Task ValidateRunReferencesAsync(
        StatementValidationRequestDto request,
        string importFingerprint,
        List<StatementValidationIssueDto> issues,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.AccountId)
            || !await referenceData.AccountExistsAsync(request.AccountId.Trim(), ct).ConfigureAwait(false))
        {
            issues.Add(Blocker(StatementValidationIssueCode.MissingAccount, null, StatementValidationFields.Account, $"Statement account '{request.AccountId}' is not known."));
        }

        if (string.IsNullOrWhiteSpace(request.MappingProfileId)
            || !await referenceData.MappingProfileExistsAsync(request.MappingProfileId.Trim(), ct).ConfigureAwait(false))
        {
            issues.Add(Blocker(StatementValidationIssueCode.MissingMappingProfile, null, null, $"Statement mapping profile '{request.MappingProfileId}' does not exist."));
        }

        if (string.IsNullOrWhiteSpace(request.ToleranceProfileId)
            || !await referenceData.ToleranceProfileExistsAsync(request.ToleranceProfileId.Trim(), ct).ConfigureAwait(false))
        {
            issues.Add(Blocker(StatementValidationIssueCode.MissingToleranceProfile, null, null, $"Statement tolerance profile '{request.ToleranceProfileId}' does not exist."));
        }

        if (!string.IsNullOrWhiteSpace(importFingerprint)
            && await referenceData.ImportExistsAsync(importFingerprint, ct).ConfigureAwait(false))
        {
            issues.Add(Blocker(StatementValidationIssueCode.DuplicateImport, null, null, "Statement source file has already been imported."));
        }
    }

    private async Task ValidateRowsAsync(
        string sourcePath,
        StatementValidationRequestDto request,
        List<StatementValidationIssueDto> issues,
        CancellationToken ct)
    {
        var lines = await File.ReadAllLinesAsync(sourcePath, ct).ConfigureAwait(false);
        if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0]))
        {
            issues.Add(Blocker(StatementValidationIssueCode.MissingRequiredField, null, null, "Statement source file must include a header row."));
            return;
        }

        var headers = SplitCsvLine(lines[0]);
        var headerLookup = headers
            .Select((header, index) => new { Header = header, Index = index })
            .Where(static item => !string.IsNullOrWhiteSpace(item.Header))
            .GroupBy(static item => item.Header, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First().Index, StringComparer.OrdinalIgnoreCase);

        var mapping = BuildFieldMapping(request, headers);
        var requiredFields = request.RequiredFields.Count == 0 ? DefaultRequiredFields : request.RequiredFields;
        foreach (var requiredField in requiredFields)
        {
            if (!mapping.TryGetValue(requiredField, out var sourceColumn) || !headerLookup.ContainsKey(sourceColumn))
            {
                issues.Add(Blocker(StatementValidationIssueCode.MissingRequiredField, null, requiredField, $"Required mapped field '{requiredField}' is not present in the statement header."));
            }
        }

        AddOptionalColumnIssues(headers, requiredFields, mapping, request.Policy, issues);

        for (var index = 1; index < lines.Length; index++)
        {
            ct.ThrowIfCancellationRequested();
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var rowNumber = index + 1;
            var values = SplitCsvLine(line);
            await ValidateRowAsync(values, rowNumber, mapping, headerLookup, request, issues, ct).ConfigureAwait(false);
        }
    }

    private async Task ValidateRowAsync(
        IReadOnlyList<string> values,
        int rowNumber,
        IReadOnlyDictionary<string, string> mapping,
        IReadOnlyDictionary<string, int> headerLookup,
        StatementValidationRequestDto request,
        List<StatementValidationIssueDto> issues,
        CancellationToken ct)
    {
        var parsedDates = new Dictionary<string, DateOnly>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in mapping.Keys)
        {
            if (!TryGetMappedValue(values, mapping, headerLookup, field, out var value))
            {
                continue;
            }

            if (DateFields.Contains(field) && !string.IsNullOrWhiteSpace(value))
            {
                if (!TryParseDate(value, out var parsedDate))
                {
                    AddMalformedIssue(field, rowNumber, value, request, issues);
                }
                else
                {
                    parsedDates[field] = parsedDate;
                }
            }

            if (DecimalFields.Contains(field) && !string.IsNullOrWhiteSpace(value) && !TryParseDecimal(value, out _))
            {
                AddMalformedIssue(field, rowNumber, value, request, issues);
            }
        }

        foreach (var requiredField in request.RequiredFields.Count == 0 ? DefaultRequiredFields : request.RequiredFields)
        {
            if (TryGetMappedValue(values, mapping, headerLookup, requiredField, out var requiredValue)
                && string.IsNullOrWhiteSpace(requiredValue))
            {
                issues.Add(Blocker(StatementValidationIssueCode.MissingRequiredField, rowNumber, requiredField, $"Required field '{requiredField}' is blank on statement row {rowNumber}."));
            }
        }

        if (TryGetMappedValue(values, mapping, headerLookup, StatementValidationFields.Currency, out var currency)
            && !string.IsNullOrWhiteSpace(currency)
            && !await referenceData.CurrencySupportedAsync(currency.Trim().ToUpperInvariant(), ct).ConfigureAwait(false))
        {
            issues.Add(SoftOrBlocker(request.Policy.UnsupportedCurrencyIsBlocker, StatementValidationIssueCode.UnsupportedCurrency, rowNumber, StatementValidationFields.Currency, $"Currency '{currency}' is not supported."));
        }

        if (TryGetMappedValue(values, mapping, headerLookup, StatementValidationFields.ActivityCode, out var activityCode)
            && !string.IsNullOrWhiteSpace(activityCode)
            && !await referenceData.TryMapActivityTypeAsync(activityCode.Trim(), ct).ConfigureAwait(false))
        {
            issues.Add(SoftOrBlocker(request.Policy.UnknownActivityTypeIsBlocker, StatementValidationIssueCode.UnknownActivityType, rowNumber, StatementValidationFields.ActivityCode, $"Transaction code '{activityCode}' does not map to a canonical activity type."));
        }

        if (TryGetMappedValue(values, mapping, headerLookup, StatementValidationFields.SecurityIdentifier, out var securityIdentifier)
            && !string.IsNullOrWhiteSpace(securityIdentifier)
            && !await referenceData.TryResolveSecurityAsync(securityIdentifier.Trim(), ct).ConfigureAwait(false))
        {
            issues.Add(SoftOrBlocker(request.Policy.UnresolvedSecurityIsBlocker, StatementValidationIssueCode.UnresolvedSecurity, rowNumber, StatementValidationFields.SecurityIdentifier, $"Security identifier '{securityIdentifier}' could not be resolved and will be marked unresolved."));
        }

        if (parsedDates.TryGetValue(StatementValidationFields.TradeDate, out var tradeDate)
            && request.StatementPeriodStart is DateOnly start
            && request.StatementPeriodEnd is DateOnly end
            && (tradeDate < start || tradeDate > end))
        {
            issues.Add(SoftOrBlocker(request.Policy.OutOfPeriodRowsAreBlockers, StatementValidationIssueCode.OutOfPeriodRow, rowNumber, StatementValidationFields.TradeDate, $"Trade date '{tradeDate:yyyy-MM-dd}' is outside the statement period {start:yyyy-MM-dd} to {end:yyyy-MM-dd}."));
        }
    }

    private static Dictionary<string, string> BuildFieldMapping(StatementValidationRequestDto request, IReadOnlyList<string> headers)
    {
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var knownFields = DefaultRequiredFields.Concat(DefaultOptionalFields);
        foreach (var knownField in knownFields)
        {
            var matchingHeader = headers.FirstOrDefault(header => string.Equals(header, knownField, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(matchingHeader))
            {
                mapping[knownField] = matchingHeader.Trim();
            }
        }

        foreach (var item in request.FieldMappings)
        {
            mapping[item.Key.Trim()] = item.Value.Trim();
        }

        return mapping;
    }

    private static void AddOptionalColumnIssues(
        IReadOnlyList<string> headers,
        IReadOnlyCollection<string> requiredFields,
        IReadOnlyDictionary<string, string> mapping,
        StatementValidationPolicyDto policy,
        List<StatementValidationIssueDto> issues)
    {
        var mappedColumns = new HashSet<string>(mapping.Values, StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers.Where(static header => !string.IsNullOrWhiteSpace(header)))
        {
            if (mappedColumns.Contains(header) || requiredFields.Contains(header, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var code = header.Contains("fee", StringComparison.OrdinalIgnoreCase)
                ? StatementValidationIssueCode.UnknownOptionalFeeColumn
                : StatementValidationIssueCode.UnsupportedOptionalField;
            var blocker = code == StatementValidationIssueCode.UnknownOptionalFeeColumn
                ? policy.UnknownOptionalFeeColumnsAreBlockers
                : policy.UnsupportedOptionalFieldsAreBlockers;

            issues.Add(SoftOrBlocker(blocker, code, null, header, $"Optional statement column '{header}' is not mapped by the active profile."));
        }
    }

    private static bool TryGetMappedValue(
        IReadOnlyList<string> values,
        IReadOnlyDictionary<string, string> mapping,
        IReadOnlyDictionary<string, int> headerLookup,
        string field,
        out string value)
    {
        value = string.Empty;
        if (!mapping.TryGetValue(field, out var sourceColumn) || !headerLookup.TryGetValue(sourceColumn, out var index))
        {
            return false;
        }

        if (index >= values.Count)
        {
            return true;
        }

        value = values[index].Trim();
        return true;
    }

    private static void AddMalformedIssue(
        string field,
        int rowNumber,
        string value,
        StatementValidationRequestDto request,
        List<StatementValidationIssueDto> issues)
    {
        var code = DateFields.Contains(field)
            ? StatementValidationIssueCode.MalformedDate
            : StatementValidationIssueCode.MalformedDecimal;
        var requiredFields = request.RequiredFields.Count == 0 ? DefaultRequiredFields : request.RequiredFields;
        var message = $"Field '{field}' value '{value}' on statement row {rowNumber} is malformed.";
        issues.Add(requiredFields.Contains(field, StringComparer.OrdinalIgnoreCase)
            ? Blocker(code, rowNumber, field, message)
            : SoftOrBlocker(false, code, rowNumber, field, message));
    }

    private static bool TryParseDate(string value, out DateOnly result) =>
        DateOnly.TryParseExact(
            value.Trim(),
            DateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out result);

    private static bool TryParseDecimal(string value, out decimal result) =>
        decimal.TryParse(
            value.Trim(),
            NumberStyles.Number | NumberStyles.AllowCurrencySymbol,
            CultureInfo.InvariantCulture,
            out result);

    private static async Task<string> ComputeFileFingerprintAsync(string sourcePath, CancellationToken ct)
    {
        await using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 64 * 1024, useAsync: true);
        var hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string[] SplitCsvLine(string line) =>
        line.Split(',', StringSplitOptions.TrimEntries);

    private static StatementValidationIssueDto Blocker(StatementValidationIssueCode code, int? rowNumber, string? field, string message) =>
        new(code, StatementValidationIssueSeverity.Blocker, rowNumber, field, message);

    private static StatementValidationIssueDto SoftOrBlocker(bool blocker, StatementValidationIssueCode code, int? rowNumber, string? field, string message) =>
        new(code, blocker ? StatementValidationIssueSeverity.Blocker : StatementValidationIssueSeverity.Soft, rowNumber, field, message);
}

public interface IStatementValidationReferenceData
{
    Task<bool> AccountExistsAsync(string accountId, CancellationToken ct = default);
    Task<bool> MappingProfileExistsAsync(string mappingProfileId, CancellationToken ct = default);
    Task<bool> ToleranceProfileExistsAsync(string toleranceProfileId, CancellationToken ct = default);
    Task<bool> ImportExistsAsync(string importFingerprint, CancellationToken ct = default);
    Task<bool> CurrencySupportedAsync(string currency, CancellationToken ct = default);
    Task<bool> TryMapActivityTypeAsync(string transactionCode, CancellationToken ct = default);
    Task<bool> TryResolveSecurityAsync(string securityIdentifier, CancellationToken ct = default);
}

public sealed record StatementValidationRequestDto
{
    public StatementValidationRequestDto(
        string SourcePath,
        string AccountId,
        DateOnly? StatementPeriodStart,
        DateOnly? StatementPeriodEnd,
        string MappingProfileId,
        string ToleranceProfileId,
        IReadOnlyDictionary<string, string>? FieldMappings = null,
        IReadOnlyList<string>? RequiredFields = null,
        StatementValidationPolicyDto? Policy = null)
    {
        this.SourcePath = SourcePath;
        this.AccountId = AccountId;
        this.StatementPeriodStart = StatementPeriodStart;
        this.StatementPeriodEnd = StatementPeriodEnd;
        this.MappingProfileId = MappingProfileId;
        this.ToleranceProfileId = ToleranceProfileId;
        this.FieldMappings = FieldMappings ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        this.RequiredFields = RequiredFields ?? [];
        this.Policy = Policy ?? StatementValidationPolicyDto.Default;
    }

    public string SourcePath { get; init; }
    public string AccountId { get; init; }
    public DateOnly? StatementPeriodStart { get; init; }
    public DateOnly? StatementPeriodEnd { get; init; }
    public string MappingProfileId { get; init; }
    public string ToleranceProfileId { get; init; }
    public IReadOnlyDictionary<string, string> FieldMappings { get; init; }
    public IReadOnlyList<string> RequiredFields { get; init; }
    public StatementValidationPolicyDto Policy { get; init; }
}

public sealed record StatementValidationResultDto(
    string SourcePath,
    string AccountId,
    string ImportFingerprint,
    bool IsBlocked,
    IReadOnlyList<StatementValidationIssueDto> Issues);

public sealed record StatementValidationIssueDto(
    StatementValidationIssueCode Code,
    StatementValidationIssueSeverity Severity,
    int? RowNumber,
    string? Field,
    string Message);

public sealed record StatementValidationPolicyDto(
    bool UnresolvedSecurityIsBlocker = false,
    bool UnknownOptionalFeeColumnsAreBlockers = false,
    bool UnsupportedOptionalFieldsAreBlockers = false,
    bool OutOfPeriodRowsAreBlockers = false,
    bool UnsupportedCurrencyIsBlocker = false,
    bool UnknownActivityTypeIsBlocker = false)
{
    public static StatementValidationPolicyDto Default { get; } = new();
}

public enum StatementValidationIssueSeverity
{
    Soft,
    Blocker
}

public enum StatementValidationIssueCode
{
    SourceFileUnreadable,
    MissingAccount,
    MissingStatementPeriod,
    InvalidStatementPeriod,
    DuplicateImport,
    MissingMappingProfile,
    MissingToleranceProfile,
    MissingRequiredField,
    MalformedDate,
    MalformedDecimal,
    UnsupportedCurrency,
    UnknownActivityType,
    UnresolvedSecurity,
    UnknownOptionalFeeColumn,
    UnsupportedOptionalField,
    OutOfPeriodRow
}

public static class StatementValidationFields
{
    public const string Account = "account";
    public const string SecurityIdentifier = "securityIdentifier";
    public const string Quantity = "quantity";
    public const string Price = "price";
    public const string CashAmount = "cashAmount";
    public const string ActivityCode = "activityCode";
    public const string TradeDate = "tradeDate";
    public const string Currency = "currency";
    public const string FeeAmount = "feeAmount";
    public const string SettlementDate = "settlementDate";
    public const string Description = "description";
}
