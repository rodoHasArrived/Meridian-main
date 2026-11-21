using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.FinancialOperations.Reconciliation.Connectors.Bai2;
using Meridian.FinancialOperations.Reconciliation.Connectors.Camt;

namespace Meridian.FinancialOperations.Onboarding.OpeningBalances;

/// <summary>
/// One incumbent opening row before account mapping. A null amount means the incumbent row is present but
/// carries no value; it is never read as zero. Position instruments use the same dimension keys as period
/// comparisons (<c>position-quantity:</c>/<c>position-market-value:</c>) so difference lineage lines up.
/// </summary>
public sealed record OnboardingOpeningRecord(
    string Kind, string ExternalAccount, string Currency, string? InstrumentId, decimal? Amount, int? RowNumber = null);

/// <summary>The parsed incumbent opening source with the exact content retained for replay.</summary>
public sealed record OnboardingOpeningSourceDocument(
    string SourceId, string Version, string PayloadJson, IReadOnlyList<string> EvidenceIds,
    IReadOnlyList<OnboardingOpeningRecord> Records, bool RequireAllRecordsMapped);

/// <summary>Reads an incumbent opening-balance source without writing to any ledger or source system.</summary>
public interface IOnboardingOpeningSourceReader
{
    Task<OnboardingOpeningSourceDocument> ReadAsync(
        OnboardingWorkspaceDto workspace, ImportOnboardingOpeningBalancesRequestDto request, CancellationToken ct = default);
}

/// <summary>
/// Captures Meridian's own opening records for the workspace start date. Observations carry only the
/// Meridian amount; the incumbent amount is joined from the imported opening source.
/// </summary>
public interface IOnboardingOpeningStateProvider
{
    Task<OnboardingSourceCaptureDto> CaptureOpeningAsync(OnboardingWorkspaceDto workspace, CancellationToken ct = default);
}

/// <summary>Default reader for camt.053/BAI2 opening balances, administrator opening packs and retained GL trial balances.</summary>
public sealed class OnboardingOpeningSourceReader(
    AccountingSystemIntegrationService? accounting = null,
    StatementIngressLimits? limits = null) : IOnboardingOpeningSourceReader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly StatementIngressLimits _limits = limits ?? StatementIngressLimits.Default;

    public async Task<OnboardingOpeningSourceDocument> ReadAsync(
        OnboardingWorkspaceDto workspace, ImportOnboardingOpeningBalancesRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(request);
        var asOf = workspace.Scope.StartDate;
        return request.SourceKind switch
        {
            OnboardingOpeningSourceKinds.StatementOpening => await ReadStatementAsync(request, asOf, ct).ConfigureAwait(false),
            OnboardingOpeningSourceKinds.AdministratorPack => ReadAdministratorPack(request, asOf),
            OnboardingOpeningSourceKinds.GlTrialBalance => ReadTrialBalance(workspace, request, asOf),
            _ => throw new OnboardingValidationException("Select a supported opening-balance source kind.")
        };
    }

    private async Task<OnboardingOpeningSourceDocument> ReadStatementAsync(
        ImportOnboardingOpeningBalancesRequestDto request, DateOnly asOf, CancellationToken ct)
    {
        var (fileName, content) = DecodeFile(request);
        var document = new StatementSourceDocument(fileName, content);
        IStatementConnector[] connectors =
        [
            new Camt053StatementConnector(_limits, StatementBalanceRole.Opening),
            new Bai2StatementConnector(_limits, StatementBalanceRole.Opening)
        ];
        var connector = connectors.FirstOrDefault(candidate => candidate.CanHandle(document))
            ?? throw new OnboardingValidationException("The opening statement is neither a camt.053 nor a BAI2 document.");
        var parsed = await connector.ParseAsync(document, ct).ConfigureAwait(false);
        var errors = parsed.Issues.Where(issue => issue.Severity == StatementParseIssue.ErrorSeverity).ToArray();
        if (errors.Length > 0)
            throw new OnboardingValidationException(
                $"The opening statement could not be read: {string.Join("; ", errors.Take(5).Select(issue => $"{issue.Code}: {issue.Message}"))}");
        var balances = parsed.Records.Where(record => record.Kind == StatementRecordKind.CashBalance).ToArray();
        if (balances.Length == 0)
            throw new OnboardingValidationException("The opening statement carries no opening booked balance.");
        if (balances.Any(record => record.TradeDate != asOf))
            throw new OnboardingValidationException(
                $"Every opening booked balance must be dated the workspace start date {asOf:yyyy-MM-dd}.");
        var records = balances.Select(record => new OnboardingOpeningRecord("Balance", record.Account.Trim(),
            (record.Currency ?? string.Empty).Trim().ToUpperInvariant(), null, record.CashAmount)).ToArray();
        return Document($"{connector.Descriptor.ConnectorId}:{fileName}", request, fileName, content, records, true);
    }

    private OnboardingOpeningSourceDocument ReadAdministratorPack(ImportOnboardingOpeningBalancesRequestDto request, DateOnly asOf)
    {
        var (fileName, content) = DecodeFile(request);
        var records = AdministratorOpeningPackParser.Parse(content.Span, asOf, _limits);
        return Document($"administrator-pack:{fileName}", request, fileName, content, records, true);
    }

    private OnboardingOpeningSourceDocument ReadTrialBalance(
        OnboardingWorkspaceDto workspace, ImportOnboardingOpeningBalancesRequestDto request, DateOnly asOf)
    {
        if (accounting is null)
            throw new OnboardingValidationException("The external GL integration is unavailable.");
        if (string.IsNullOrWhiteSpace(request.ProviderId) || string.IsNullOrWhiteSpace(request.ImportId))
            throw new OnboardingValidationException("Select the exact retained GL provider and import identity.");
        var scope = workspace.Scope;
        var import = accounting.GetRetainedImport(request.ProviderId, request.ImportId, scope.FundProfileId,
                scope.LedgerBookId, scope.TenantId, scope.CompanyId)
            ?? throw new OnboardingValidationException("The exact retained GL import is unavailable for this fund, book and scope.");
        var lines = import.TrialBalance.Where(line => line.AsOfDate == asOf).ToArray();
        if (lines.Length == 0)
            throw new OnboardingValidationException($"The retained GL import has no trial balance dated {asOf:yyyy-MM-dd}.");
        var records = lines.Select(line => new OnboardingOpeningRecord("Balance", line.AccountCode.Trim(),
            line.Currency.Trim().ToUpperInvariant(), null, line.Debit - line.Credit)).ToArray();
        var payload = JsonSerializer.Serialize(new
        {
            SourceKind = request.SourceKind,
            request.ProviderId,
            request.ImportId,
            AsOfDate = asOf,
            import.Summary.ContentHash,
            TrialBalance = lines
        }, Json);
        // A GL carries accounts outside the selected population; only explicitly mapped accounts are compared.
        return new($"gl:{request.ProviderId}:{request.ImportId}", import.Summary.ContentHash ?? request.ImportId,
            payload, import.Summary.EvidenceReferences.Distinct(StringComparer.Ordinal).ToArray(), records, false);
    }

    private (string FileName, ReadOnlyMemory<byte> Content) DecodeFile(ImportOnboardingOpeningBalancesRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.FileName) || string.IsNullOrWhiteSpace(request.ContentBase64))
            throw new OnboardingValidationException("Attach the incumbent opening-balance file.");
        byte[] content;
        try
        { content = Convert.FromBase64String(request.ContentBase64); }
        catch (FormatException) { throw new OnboardingValidationException("The opening-balance file content must be base64 encoded."); }
        if (content.Length == 0)
            throw new OnboardingValidationException("The opening-balance file is empty.");
        if (content.Length > _limits.MaxDocumentBytes)
            throw new OnboardingValidationException($"The opening-balance file exceeds the {_limits.MaxDocumentBytes:N0}-byte limit.");
        return (Path.GetFileName(request.FileName.Trim()), content);
    }

    private static OnboardingOpeningSourceDocument Document(string sourceId, ImportOnboardingOpeningBalancesRequestDto request,
        string fileName, ReadOnlyMemory<byte> content, IReadOnlyList<OnboardingOpeningRecord> records, bool requireMapped)
    {
        // The raw file is copied into the retained payload: a hash alone is not a reproducible source snapshot.
        var payload = JsonSerializer.Serialize(new
        {
            SourceKind = request.SourceKind,
            FileName = fileName,
            ContentBase64 = Convert.ToBase64String(content.Span),
            Records = records
        }, Json);
        return new(sourceId, OnboardingWorkspaceService.HashSourcePayload(Convert.ToBase64String(content.Span)),
            payload, [], records, requireMapped);
    }
}

/// <summary>
/// Parses an administrator opening pack. Header (case-insensitive, any order):
/// <c>record_type,account,currency,instrument,quantity,market_value,amount,as_of_date</c>.
/// <c>CASH</c> and <c>NAV</c> rows use <c>amount</c>; <c>POSITION</c> rows use <c>quantity</c> and/or
/// <c>market_value</c>. An empty value is missing, never zero.
/// </summary>
public static class AdministratorOpeningPackParser
{
    private static readonly string[] RequiredColumns =
        ["record_type", "account", "currency", "instrument", "quantity", "market_value", "amount", "as_of_date"];

    public static IReadOnlyList<OnboardingOpeningRecord> Parse(ReadOnlySpan<byte> content, DateOnly asOf, StatementIngressLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (content.Length > limits.MaxDocumentBytes)
            throw new OnboardingValidationException($"The administrator pack exceeds the {limits.MaxDocumentBytes:N0}-byte limit.");
        string text;
        try
        { text = new UTF8Encoding(false, true).GetString(content).TrimStart('﻿'); }
        catch (DecoderFallbackException) { throw new OnboardingValidationException("The administrator pack must be UTF-8 text."); }
        var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        if (lines.Length > limits.MaxDocumentLines)
            throw new OnboardingValidationException("The administrator pack has too many lines.");
        if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0]))
            throw new OnboardingValidationException("The administrator pack requires a header row.");
        var header = SplitLine(lines[0], 1).Select(column => column.Trim().ToLowerInvariant()).ToArray();
        var missingColumns = RequiredColumns.Where(column => !header.Contains(column, StringComparer.Ordinal)).ToArray();
        if (missingColumns.Length > 0)
            throw new OnboardingValidationException($"The administrator pack is missing column(s): {string.Join(", ", missingColumns)}.");
        if (header.Distinct(StringComparer.Ordinal).Count() != header.Length)
            throw new OnboardingValidationException("The administrator pack header repeats a column.");
        var index = RequiredColumns.ToDictionary(column => column, column => Array.IndexOf(header, column), StringComparer.Ordinal);
        var records = new List<OnboardingOpeningRecord>();
        for (var lineNumber = 2; lineNumber <= lines.Length; lineNumber++)
        {
            var line = lines[lineNumber - 1];
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (Encoding.UTF8.GetByteCount(line) > limits.MaxLineBytes)
                throw new OnboardingValidationException($"Administrator pack line {lineNumber} is too long.");
            var fields = SplitLine(line, lineNumber);
            if (fields.Length != header.Length)
                throw new OnboardingValidationException($"Administrator pack line {lineNumber} has {fields.Length} fields; expected {header.Length}.");
            string Field(string column) => fields[index[column]].Trim();
            var type = Field("record_type").ToUpperInvariant();
            var account = Field("account");
            var currency = Field("currency").ToUpperInvariant();
            if (account.Length == 0 || currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper))
                throw new OnboardingValidationException($"Administrator pack line {lineNumber} requires an account and an ISO currency.");
            if (!DateOnly.TryParseExact(Field("as_of_date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || date != asOf)
                throw new OnboardingValidationException(
                    $"Administrator pack line {lineNumber} must be dated the workspace start date {asOf:yyyy-MM-dd}.");
            switch (type)
            {
                case "CASH":
                    records.Add(new("Balance", account, currency, null, Amount(Field("amount"), lineNumber), lineNumber));
                    break;
                case "NAV":
                    records.Add(new("Nav", account, currency, null, Amount(Field("amount"), lineNumber), lineNumber));
                    break;
                case "POSITION":
                    var instrument = Field("instrument");
                    if (instrument.Length == 0)
                        throw new OnboardingValidationException($"Administrator pack line {lineNumber} requires an instrument.");
                    var quantity = Amount(Field("quantity"), lineNumber);
                    var marketValue = Amount(Field("market_value"), lineNumber);
                    if (quantity is null && marketValue is null)
                        throw new OnboardingValidationException($"Administrator pack line {lineNumber} carries neither a quantity nor a market value.");
                    if (quantity is not null)
                        records.Add(new("Position", account, currency, $"position-quantity:{instrument}", quantity, lineNumber));
                    if (marketValue is not null)
                        records.Add(new("Position", account, currency, $"position-market-value:{instrument}", marketValue, lineNumber));
                    break;
                default:
                    throw new OnboardingValidationException($"Administrator pack line {lineNumber} has unsupported record type '{type}'.");
            }
            if (records.Count > limits.MaxRecords)
                throw new OnboardingValidationException("The administrator pack has too many records.");
        }
        if (records.Count == 0)
            throw new OnboardingValidationException("The administrator pack contains no opening rows.");
        return records;
    }

    private static decimal? Amount(string value, int lineNumber)
    {
        if (value.Length == 0)
            return null;
        if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
            throw new OnboardingValidationException($"Administrator pack line {lineNumber} has an unreadable amount '{value}'.");
        return amount;
    }

    private static string[] SplitLine(string line, int lineNumber)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                { current.Append('"'); i++; }
                else if (c == '"')
                    quoted = false;
                else
                    current.Append(c);
            }
            else if (c == '"' && current.Length == 0)
                quoted = true;
            else if (c == ',')
            { fields.Add(current.ToString()); current.Clear(); }
            else
                current.Append(c);
        }
        if (quoted)
            throw new OnboardingValidationException($"Administrator pack line {lineNumber} has an unterminated quoted field.");
        fields.Add(current.ToString());
        return fields.ToArray();
    }
}
