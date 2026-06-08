using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Etl;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Plaid;
using Meridian.Contracts.Workstation;
using Meridian.PortfolioRecords.FundAccounts;

namespace Meridian.Ui.Shared.Services;

public enum BankFeedTransportKind
{
    LocalFile = 0,
    Sftp = 1,
    PlaidApi = 2
}

public sealed record BankFeedScheduleDefinition(
    string ScheduleId,
    BankFeedTransportKind TransportKind,
    TimeSpan Interval,
    DateTimeOffset? LastRunAtUtc,
    Guid? AccountId = null,
    string? BankName = null,
    EtlSourceDefinition? Source = null,
    string? PlaidItemId = null,
    DateOnly? StatementDate = null,
    string? Notes = null,
    bool IsBackfill = false,
    string RequestedBy = "bank-feed-scheduler");

public sealed record BankFeedTransportRunResult(
    string ScheduleId,
    BankFeedTransportKind TransportKind,
    bool Ran,
    bool Success,
    DateTimeOffset EvaluatedAtUtc,
    int FilesProcessed,
    int BankStatementLinesImported,
    int ApiItemsSynced,
    IReadOnlyList<Guid> ImportedBatchIds,
    IReadOnlyList<string> Errors)
{
    public static BankFeedTransportRunResult Skipped(
        BankFeedScheduleDefinition schedule,
        DateTimeOffset evaluatedAtUtc,
        string reason)
        => new(
            schedule.ScheduleId,
            schedule.TransportKind,
            Ran: false,
            Success: true,
            evaluatedAtUtc,
            FilesProcessed: 0,
            BankStatementLinesImported: 0,
            ApiItemsSynced: 0,
            ImportedBatchIds: [],
            Errors: [reason]);
}

public sealed class BankFeedTransportService
{
    private readonly IFundAccountService _fundAccountService;
    private readonly IReadOnlyList<IEtlSourceReader> _sourceReaders;
    private readonly IPlaidIngestionService? _plaidIngestionService;

    public BankFeedTransportService(
        IFundAccountService fundAccountService,
        IEnumerable<IEtlSourceReader> sourceReaders,
        IPlaidIngestionService? plaidIngestionService = null)
    {
        _fundAccountService = fundAccountService;
        _sourceReaders = sourceReaders.ToArray();
        _plaidIngestionService = plaidIngestionService;
    }

    public bool IsDue(BankFeedScheduleDefinition schedule, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (schedule.Interval <= TimeSpan.Zero)
        {
            return true;
        }

        return schedule.LastRunAtUtc is null || schedule.LastRunAtUtc.Value.Add(schedule.Interval) <= nowUtc;
    }

    public async Task<BankFeedTransportRunResult> RunIfDueAsync(
        BankFeedScheduleDefinition schedule,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (!IsDue(schedule, nowUtc))
        {
            return BankFeedTransportRunResult.Skipped(
                schedule,
                nowUtc,
                $"Next run is due after {schedule.LastRunAtUtc!.Value.Add(schedule.Interval):O}.");
        }

        return schedule.TransportKind switch
        {
            BankFeedTransportKind.LocalFile or BankFeedTransportKind.Sftp => await RunFileTransportAsync(schedule, nowUtc, ct).ConfigureAwait(false),
            BankFeedTransportKind.PlaidApi => await RunPlaidTransportAsync(schedule, nowUtc, ct).ConfigureAwait(false),
            _ => new BankFeedTransportRunResult(
                schedule.ScheduleId,
                schedule.TransportKind,
                Ran: true,
                Success: false,
                nowUtc,
                FilesProcessed: 0,
                BankStatementLinesImported: 0,
                ApiItemsSynced: 0,
                ImportedBatchIds: [],
                Errors: [$"Unsupported bank feed transport kind '{schedule.TransportKind}'."]
            )
        };
    }

    private async Task<BankFeedTransportRunResult> RunFileTransportAsync(
        BankFeedScheduleDefinition schedule,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        if (schedule.AccountId is not { } accountId)
        {
            return Failed(schedule, nowUtc, "File bank feed schedules require a target bank account id.");
        }

        if (schedule.Source is null)
        {
            return Failed(schedule, nowUtc, "File bank feed schedules require a source definition.");
        }

        var requiredSourceKind = schedule.TransportKind == BankFeedTransportKind.Sftp
            ? EtlSourceKind.Sftp
            : EtlSourceKind.Local;
        if (schedule.Source.Kind != requiredSourceKind)
        {
            return Failed(schedule, nowUtc, $"Schedule source kind '{schedule.Source.Kind}' does not match transport kind '{schedule.TransportKind}'.");
        }

        var account = await _fundAccountService.GetAccountAsync(accountId, ct).ConfigureAwait(false);
        if (account is null)
        {
            return Failed(schedule, nowUtc, $"Bank account '{accountId:D}' was not found.");
        }

        if (account.AccountType != AccountTypeDto.Bank)
        {
            return Failed(schedule, nowUtc, "File bank feed schedules can only target bank accounts.");
        }

        var reader = _sourceReaders.FirstOrDefault(candidate => candidate.Kind == schedule.Source.Kind);
        if (reader is null)
        {
            return Failed(schedule, nowUtc, $"No ETL source reader is registered for bank feed source kind '{schedule.Source.Kind}'.");
        }

        var files = await reader.ListFilesAsync(schedule.Source, ct).ConfigureAwait(false);
        var filesProcessed = 0;
        var linesImported = 0;
        var importedBatchIds = new List<Guid>();
        var errors = new List<string>();
        var jobId = $"bank-feed-{schedule.ScheduleId}-{nowUtc:yyyyMMddHHmmss}";

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var staged = await reader.StageFileAsync(jobId, schedule.Source, file, ct).ConfigureAwait(false);
                var fileBytes = await File.ReadAllBytesAsync(staged.StagedPath, ct).ConfigureAwait(false);
                var bankName = string.IsNullOrWhiteSpace(schedule.BankName)
                    ? account.BankDetails?.BankName ?? account.Institution ?? "Bank feed"
                    : schedule.BankName!.Trim();
                var batchId = BankStatementCsvImportMapper.BuildBatchId(fileBytes, accountId, bankName);
                var mapped = BankStatementCsvImportMapper.Parse(fileBytes, accountId, batchId);
                var errorIssues = mapped.Issues
                    .Where(static issue => string.Equals(issue.Severity, "Error", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (errorIssues.Length > 0)
                {
                    errors.Add($"{file.Name}: {string.Join("; ", errorIssues.Select(FormatIssue))}");
                    continue;
                }

                if (mapped.Lines.Count == 0)
                {
                    errors.Add($"{file.Name}: The bank statement CSV did not contain any transaction rows.");
                    continue;
                }

                var retainedReference = $"Retained source: {staged.StagedPath}";
                var notes = string.IsNullOrWhiteSpace(schedule.Notes)
                    ? retainedReference
                    : $"{schedule.Notes.Trim()} | {retainedReference}";
                var batch = await _fundAccountService
                    .IngestBankStatementAsync(
                        new IngestBankStatementRequest(
                            batchId,
                            accountId,
                            schedule.StatementDate ?? mapped.StatementDate!.Value,
                            bankName,
                            notes,
                            mapped.Lines,
                            schedule.RequestedBy,
                            schedule.IsBackfill),
                        ct)
                    .ConfigureAwait(false);

                filesProcessed++;
                linesImported += batch.LineCount;
                importedBatchIds.Add(batch.BatchId);

                if (schedule.Source.DeleteAfterSuccess && schedule.Source.Kind == EtlSourceKind.Local && File.Exists(file.Path))
                {
                    File.Delete(file.Path);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"{file.Name}: {ex.Message}");
            }
        }

        return new BankFeedTransportRunResult(
            schedule.ScheduleId,
            schedule.TransportKind,
            Ran: true,
            Success: errors.Count == 0,
            nowUtc,
            filesProcessed,
            linesImported,
            ApiItemsSynced: 0,
            ImportedBatchIds: importedBatchIds,
            Errors: errors);
    }

    private async Task<BankFeedTransportRunResult> RunPlaidTransportAsync(
        BankFeedScheduleDefinition schedule,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        if (_plaidIngestionService is null)
        {
            return Failed(schedule, nowUtc, "Plaid ingestion service is not registered for API bank feed schedules.");
        }

        if (string.IsNullOrWhiteSpace(schedule.PlaidItemId))
        {
            return Failed(schedule, nowUtc, "Plaid API bank feed schedules require a Plaid item id.");
        }

        var result = await _plaidIngestionService
            .SyncItemAsync(new PlaidSyncRequest(schedule.PlaidItemId.Trim(), RequestedBy: schedule.RequestedBy), ct)
            .ConfigureAwait(false);

        return new BankFeedTransportRunResult(
            schedule.ScheduleId,
            schedule.TransportKind,
            Ran: true,
            Success: result.Warnings.Count == 0,
            nowUtc,
            FilesProcessed: 0,
            BankStatementLinesImported: result.BankStatementLinesIngested,
            ApiItemsSynced: 1,
            ImportedBatchIds: [],
            Errors: result.Warnings);
    }

    private static BankFeedTransportRunResult Failed(
        BankFeedScheduleDefinition schedule,
        DateTimeOffset nowUtc,
        string error)
        => new(
            schedule.ScheduleId,
            schedule.TransportKind,
            Ran: true,
            Success: false,
            nowUtc,
            FilesProcessed: 0,
            BankStatementLinesImported: 0,
            ApiItemsSynced: 0,
            ImportedBatchIds: [],
            Errors: [error]);

    private static string FormatIssue(DataUploadValidationIssueDto issue)
        => issue.RowNumber is { } row
            ? $"row {row.ToString(CultureInfo.InvariantCulture)} {issue.Field}: {issue.Message}"
            : $"{issue.Field}: {issue.Message}";
}

internal static class BankStatementCsvImportMapper
{
    private static readonly string[] RequiredFields =
    [
        "transaction_date",
        "amount",
        "currency",
        "transaction_type",
        "description"
    ];

    public static Guid BuildBatchId(byte[] fileBytes, Guid accountId, string bankName)
    {
        var fileHash = Convert.ToHexString(SHA256.HashData(fileBytes));
        return BuildGuid($"bank-statement-batch|{accountId:N}|{bankName}|{fileHash}");
    }

    public static BankStatementCsvImportMappingResult Parse(
        byte[] fileBytes,
        Guid accountId,
        Guid batchId)
    {
        using var stream = new MemoryStream(fileBytes);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var issues = new List<DataUploadValidationIssueDto>();
        var lines = new List<BankStatementLineDto>();
        var headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            issues.Add(new DataUploadValidationIssueDto("Error", "header", "The CSV file must include a header row.", RowNumber: 1));
            return new BankStatementCsvImportMappingResult(lines, StatementDate: null, issues);
        }

        var headers = SplitCsvLine(headerLine)
            .Select(static header => header.Trim())
            .ToArray();
        var headerIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < headers.Length; index++)
        {
            if (!string.IsNullOrWhiteSpace(headers[index]))
            {
                headerIndex[headers[index]] = index;
            }
        }

        foreach (var field in RequiredFields)
        {
            if (!headerIndex.ContainsKey(field))
            {
                issues.Add(new DataUploadValidationIssueDto(
                    "Error",
                    field,
                    $"Required field '{field}' is missing from the bank statement header.",
                    RowNumber: 1));
            }
        }

        var lineNumber = 1;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var values = SplitCsvLine(line);
            if (values.Count != headers.Length)
            {
                issues.Add(new DataUploadValidationIssueDto(
                    "Error",
                    "row",
                    $"Row has {values.Count.ToString(CultureInfo.InvariantCulture)} values for {headers.Length.ToString(CultureInfo.InvariantCulture)} headers.",
                    RowNumber: lineNumber));
                continue;
            }

            if (!TryReadRequiredValue(headerIndex, values, "transaction_date", lineNumber, issues, out var transactionDateValue) ||
                !TryReadRequiredValue(headerIndex, values, "amount", lineNumber, issues, out var amountValue) ||
                !TryReadRequiredValue(headerIndex, values, "currency", lineNumber, issues, out var currencyValue) ||
                !TryReadRequiredValue(headerIndex, values, "transaction_type", lineNumber, issues, out var transactionType) ||
                !TryReadRequiredValue(headerIndex, values, "description", lineNumber, issues, out var description))
            {
                continue;
            }

            if (!TryParseDate(transactionDateValue, out var transactionDate))
            {
                issues.Add(new DataUploadValidationIssueDto("Error", "transaction_date", "Transaction date must use YYYY-MM-DD format.", RowNumber: lineNumber));
                continue;
            }

            var valueDateValue = ReadValue(headerIndex, values, "value_date");
            var valueDate = transactionDate;
            if (!string.IsNullOrWhiteSpace(valueDateValue) && !TryParseDate(valueDateValue, out valueDate))
            {
                issues.Add(new DataUploadValidationIssueDto("Error", "value_date", "Value date must use YYYY-MM-DD format.", RowNumber: lineNumber));
                continue;
            }

            if (!TryParseDecimal(amountValue, out var amount))
            {
                issues.Add(new DataUploadValidationIssueDto("Error", "amount", "Amount must be a signed decimal value.", RowNumber: lineNumber));
                continue;
            }

            decimal? closingBalance = null;
            var closingBalanceValue = ReadValue(headerIndex, values, "closing_balance");
            if (!string.IsNullOrWhiteSpace(closingBalanceValue))
            {
                if (!TryParseDecimal(closingBalanceValue, out var parsedClosingBalance))
                {
                    issues.Add(new DataUploadValidationIssueDto("Error", "closing_balance", "Closing balance must be a decimal value.", RowNumber: lineNumber));
                    continue;
                }

                closingBalance = parsedClosingBalance;
            }

            var reference = ReadValue(headerIndex, values, "reference");
            var currency = currencyValue.Trim().ToUpperInvariant();
            lines.Add(new BankStatementLineDto(
                LineId: BuildGuid(
                    $"bank-statement-line|{batchId:N}|{lineNumber.ToString(CultureInfo.InvariantCulture)}|{transactionDate:yyyyMMdd}|{valueDate:yyyyMMdd}|{amount.ToString(CultureInfo.InvariantCulture)}|{currency}|{transactionType}|{reference}|{description}"),
                BatchId: batchId,
                AccountId: accountId,
                TransactionDate: transactionDate,
                ValueDate: valueDate,
                Amount: amount,
                Currency: currency,
                TransactionType: transactionType.Trim(),
                Description: description.Trim(),
                Reference: string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
                ClosingBalance: closingBalance));
        }

        if (lines.Count == 0 && issues.Count == 0)
        {
            issues.Add(new DataUploadValidationIssueDto(
                "Error",
                "rows",
                "The bank statement CSV must contain at least one transaction row.",
                RowNumber: null));
        }

        return new BankStatementCsvImportMappingResult(
            lines,
            lines.Count == 0 ? null : lines.Max(static bankLine => bankLine.ValueDate),
            issues);
    }

    private static IReadOnlyList<string> SplitCsvLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (character == ',' && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        values.Add(current.ToString());
        return values;
    }

    private static bool TryReadRequiredValue(
        IReadOnlyDictionary<string, int> headerIndex,
        IReadOnlyList<string> values,
        string field,
        int rowNumber,
        List<DataUploadValidationIssueDto> issues,
        out string value)
    {
        value = ReadValue(headerIndex, values, field);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        issues.Add(new DataUploadValidationIssueDto("Error", field, $"Required field '{field}' is blank.", RowNumber: rowNumber));
        return false;
    }

    private static string ReadValue(
        IReadOnlyDictionary<string, int> headerIndex,
        IReadOnlyList<string> values,
        string field)
        => headerIndex.TryGetValue(field, out var index) && index >= 0 && index < values.Count
            ? values[index].Trim()
            : string.Empty;

    private static bool TryParseDate(string value, out DateOnly date)
        => DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
            || DateOnly.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static bool TryParseDecimal(string value, out decimal amount)
        => decimal.TryParse(
            value.Trim(),
            NumberStyles.Number | NumberStyles.AllowCurrencySymbol | NumberStyles.AllowParentheses,
            CultureInfo.InvariantCulture,
            out amount);

    private static Guid BuildGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var bytes = new byte[16];
        Array.Copy(hash, bytes, bytes.Length);
        return new Guid(bytes);
    }
}

internal sealed record BankStatementCsvImportMappingResult(
    IReadOnlyList<BankStatementLineDto> Lines,
    DateOnly? StatementDate,
    IReadOnlyList<DataUploadValidationIssueDto> Issues);
