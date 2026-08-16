using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using Meridian.Contracts.Integrity;
using Meridian.Domain.Reconciliation;
using Meridian.Storage.Archival;

namespace Meridian.Infrastructure.Reconciliation;

internal sealed record BrokerStatementFileSnapshot(string Path, byte[] Content, string Sha256);

internal sealed record BrokerStatementSourceSnapshots(
    BrokerStatementFileSnapshot Source,
    BrokerStatementFileSnapshot ParseArtifact);

internal static class BrokerStatementSourceSnapshot
{
    public static async Task<BrokerStatementSourceSnapshots> CaptureAsync(
        BrokerStatementImportRequest request,
        long maximumBytes,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var source = await CaptureFileAsync(request.SourcePath, maximumBytes, ct).ConfigureAwait(false);
        BrokerStatementFileSnapshot parseArtifact;
        if (PathsEqual(request.SourcePath, request.EffectiveParsePath))
        {
            parseArtifact = source;
        }
        else
        {
            parseArtifact = await CaptureFileAsync(request.EffectiveParsePath, maximumBytes, ct).ConfigureAwait(false);
        }

        AssertHash(request.SourceFileHash, source.Sha256, "Source file");
        AssertHash(request.CanonicalArtifactHash, parseArtifact.Sha256, "Canonical artifact");
        return new BrokerStatementSourceSnapshots(source, parseArtifact);
    }

    private static async Task<BrokerStatementFileSnapshot> CaptureFileAsync(
        string path,
        long maximumBytes,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > maximumBytes)
        {
            throw new InvalidDataException($"Statement file exceeds the {maximumBytes}-byte limit.");
        }

        using var buffer = new MemoryStream(stream.Length > int.MaxValue ? 0 : (int)stream.Length);
        var chunk = new byte[64 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > maximumBytes)
            {
                throw new InvalidDataException($"Statement file exceeds the {maximumBytes}-byte limit.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        var content = buffer.ToArray();
        // Canonical lowercase is safe here: caller assertions verify via AssertHash (decoded
        // bytes, case-insensitive) and duplicate keys normalize their hash inputs to uppercase
        // before deriving the key, so previously persisted imports still match (#2691).
        return new BrokerStatementFileSnapshot(
            path,
            content,
            Sha256Digest.Compute(content));
    }

    private static void AssertHash(string? assertion, string authoritativeHash, string label)
    {
        if (string.IsNullOrWhiteSpace(assertion))
        {
            return;
        }

        var normalized = assertion.Trim();
        if (normalized.Length != 64 || normalized.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException($"{label} SHA-256 assertion must contain exactly 64 hexadecimal characters.");
        }

        var assertedBytes = Convert.FromHexString(normalized);
        var authoritativeBytes = Convert.FromHexString(authoritativeHash);
        if (!CryptographicOperations.FixedTimeEquals(assertedBytes, authoritativeBytes))
        {
            throw new InvalidDataException($"{label} SHA-256 assertion does not match the captured bytes.");
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
    }
}

public interface ICanonicalStatementStore
{
    Task<bool> ImportExistsByChecksumAsync(string checksum, CancellationToken ct = default);
    Task<bool> ImportExistsByDuplicateKeyAsync(string duplicateKey, CancellationToken ct = default);
    Task SaveImportAsync(CanonicalStatementImport import, IReadOnlyList<CanonicalStatementRow> rows, CancellationToken ct = default);
    Task<bool> TrySaveImportAsync(CanonicalStatementImport import, IReadOnlyList<CanonicalStatementRow> rows, CancellationToken ct = default);
    Task<BrokerStatementImportResult?> GetImportAsync(string importId, CancellationToken ct = default);
    Task<IReadOnlyList<CanonicalStatementImport>> ListImportsAsync(CancellationToken ct = default);
}

/// <summary>
/// Maps record identifiers onto safe file names for the per-record JSON stores. The identifiers
/// are system-generated today, but they flow through DTOs, so path separators, traversal
/// sequences, and characters invalid in file names are neutralized before touching the disk.
/// </summary>
internal static class ReconciliationRecordFileName
{
    // Longer sanitized names fall back to a hash so the result stays within
    // path-component limits on every supported file system.
    private const int MaxNameLength = 128;

    private static readonly char[] InvalidChars =
        [.. Path.GetInvalidFileNameChars(), '/', '\\'];

    public static string For(string? recordId)
    {
        if (string.IsNullOrEmpty(recordId))
        {
            return Hash(string.Empty);
        }

        var name = string.Join('_', recordId.Split(InvalidChars))
            .Replace("..", "_", StringComparison.Ordinal)
            .Trim('.', ' ');

        return string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength
            ? Hash(recordId)
            : name;
    }

    private static string Hash(string value)
        => Sha256Digest.ComputeUtf8(value);
}

public sealed class JsonCanonicalStatementStore(string dataRoot) : ICanonicalStatementStore
{
    private readonly string _folder = Path.Combine(dataRoot, "reconciliation", "statement-imports");

    public Task<bool> ImportExistsByChecksumAsync(string checksum, CancellationToken ct = default)
        => Task.FromResult(Directory.Exists(_folder) && Directory.EnumerateFiles(_folder, "*.json").Any(path => File.ReadAllText(path).Contains(checksum, StringComparison.Ordinal)));

    public Task<bool> ImportExistsByDuplicateKeyAsync(string duplicateKey, CancellationToken ct = default)
    {
        if (!Directory.Exists(_folder))
            return Task.FromResult(false);

        var exists = Directory.EnumerateFiles(_folder, "*.json")
            .Select(path => JsonSerializer.Deserialize<ImportEnvelope>(File.ReadAllText(path))?.import)
            .Any(import => string.Equals(import?.DuplicateKey, duplicateKey, StringComparison.Ordinal));

        return Task.FromResult(exists);
    }

    public async Task SaveImportAsync(CanonicalStatementImport import, IReadOnlyList<CanonicalStatementRow> rows, CancellationToken ct = default)
    {
        if (!await TrySaveImportAsync(import, rows, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Statement already imported (atomic duplicate-key claim already exists).");
        }
    }

    public async Task<bool> TrySaveImportAsync(
        CanonicalStatementImport import,
        IReadOnlyList<CanonicalStatementRow> rows,
        CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { import, rows });
        var fileName = ReconciliationRecordFileName.For(import.ImportId);
        var targetPath = Path.Combine(_folder, $"{fileName}.json");
        Directory.CreateDirectory(_folder);
        var temporaryPath = Path.Combine(_folder, $".{fileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, payload, Encoding.UTF8, ct).ConfigureAwait(false);
            // Same-volume rename with overwrite disabled is the atomic uniqueness boundary across
            // concurrent processes. Exactly one importer can claim this duplicate key.
            File.Move(temporaryPath, targetPath, overwrite: false);
            return true;
        }
        catch (IOException) when (File.Exists(targetPath))
        {
            return false;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task<IReadOnlyList<CanonicalStatementImport>> ListImportsAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_folder))
            return Task.FromResult<IReadOnlyList<CanonicalStatementImport>>([]);
        var imports = Directory.EnumerateFiles(_folder, "*.json")
            .Select(path => JsonSerializer.Deserialize<ImportEnvelope>(File.ReadAllText(path))?.import)
            .Where(x => x is not null)
            .Cast<CanonicalStatementImport>()
            .OrderByDescending(x => x.ImportedAtUtc)
            .ToList();
        return Task.FromResult<IReadOnlyList<CanonicalStatementImport>>(imports);
    }

    public async Task<BrokerStatementImportResult?> GetImportAsync(
        string importId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(importId);
        var path = Path.Combine(_folder, $"{ReconciliationRecordFileName.For(importId.Trim())}.json");
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var envelope = await JsonSerializer.DeserializeAsync<ImportEnvelope>(stream, cancellationToken: ct)
            .ConfigureAwait(false);
        if (envelope?.import is null)
        {
            throw new InvalidDataException($"Canonical statement import '{importId}' retained a null import payload.");
        }

        if (!string.Equals(envelope.import.ImportId, importId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Canonical statement import '{importId}' retained mismatched identity '{envelope.import.ImportId}'.");
        }

        return new BrokerStatementImportResult(envelope.import, envelope.rows ?? []);
    }

    private sealed record ImportEnvelope(
        CanonicalStatementImport import,
        IReadOnlyList<CanonicalStatementRow>? rows = null);
}

public sealed class CsvBrokerStatementService(ICanonicalStatementStore store) : IBrokerStatementService
{
    private const long MaximumStatementBytes = 32L * 1024 * 1024;
    private const int MaximumRows = 100_000;
    private const int MaximumLineCharacters = 64 * 1024;
    private static readonly string[] ExpectedColumns =
        ["account", "symbol", "quantity", "price", "cashAmount", "activityType", "tradeDate"];
    // The canonical artifact written by StatementImportService is this parser's own input, so the
    // accepted optional suffix must stay a prefix-compatible superset of what that writer emits, in
    // the same order. Widening here keeps narrower artifacts written before a column was added
    // readable, because a shorter header is still a valid prefix of the full list. Only the first
    // four are carried onto CanonicalStatementRow; the remainder are artifact-level provenance that
    // reconciliation does not consume, and are tolerated rather than mapped.
    private static readonly string[] OptionalCanonicalColumns =
    [
        "settlementDate", "currency", "feesCommission", "externalTransactionId", "activityCategory",
        "activitySubtype", "providerActivityCode", "relatedTransactionId", "orderId", "description"
    ];

    public async Task<BrokerStatementValidationResult> ValidateAsync(BrokerStatementImportRequest request, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (!File.Exists(request.SourcePath))
            errors.Add("Source file not found.");
        if (!File.Exists(request.EffectiveParsePath))
            errors.Add("Canonical statement artifact not found.");
        if (!IsSupportedStatementSource(request.Broker))
            errors.Add("Unsupported broker or custodian statement source.");
        if (errors.Count > 0)
            return new BrokerStatementValidationResult(false, errors, 0);

        try
        {
            var snapshots = await BrokerStatementSourceSnapshot
                .CaptureAsync(request, MaximumStatementBytes, ct)
                .ConfigureAwait(false);
            var rows = await ParseFileAsync(
                    snapshots.ParseArtifact.Content,
                    request,
                    importId: "validation",
                    ct)
                .ConfigureAwait(false);
            ValidateRowAccounts(rows, request.ExternalAccountId);
            return new BrokerStatementValidationResult(true, errors, rows.Count);
        }
        catch (InvalidDataException ex)
        {
            errors.Add(ex.Message);
            return new BrokerStatementValidationResult(false, errors, 0);
        }
    }

    public async Task<BrokerStatementImportResult> ImportAsync(BrokerStatementImportRequest request, CancellationToken ct = default)
    {
        var snapshots = await BrokerStatementSourceSnapshot
            .CaptureAsync(request, MaximumStatementBytes, ct)
            .ConfigureAwait(false);
        var sourceFileHash = snapshots.Source.Sha256;
        var canonicalArtifactHash = snapshots.ParseArtifact.Sha256;
        var compatibleDuplicateKeys = request.AccountingScope is null
            ? StatementDuplicateKey.CreateCompatibleKeys(
                request.FundAccountId,
                request.StatementPeriodStart,
                request.StatementPeriodEnd,
                sourceFileHash,
                canonicalArtifactHash)
            : StatementDuplicateKey.CreateCompatibleKeys(
                request.FundAccountId,
                request.StatementPeriodStart,
                request.StatementPeriodEnd,
                sourceFileHash,
                canonicalArtifactHash,
                request.AccountingScope);
        var duplicateKey = compatibleDuplicateKeys[0];

        foreach (var candidate in compatibleDuplicateKeys)
        {
            if (await store.ImportExistsByDuplicateKeyAsync(candidate, ct).ConfigureAwait(false))
            {
                throw new StatementAlreadyImportedException(candidate);
            }
        }

        var importId = duplicateKey;
        var normalizedRequest = request with
        {
            SourceFileHash = sourceFileHash,
            CanonicalArtifactHash = canonicalArtifactHash
        };
        var rows = await ParseFileAsync(
                snapshots.ParseArtifact.Content,
                normalizedRequest,
                importId,
                ct)
            .ConfigureAwait(false);
        ValidateRowAccounts(rows, normalizedRequest.ExternalAccountId);
        var import = new CanonicalStatementImport(
            importId,
            normalizedRequest.Broker,
            normalizedRequest.StatementPeriodEnd,
            DateTimeOffset.UtcNow,
            normalizedRequest.SourcePath,
            sourceFileHash,
            rows.Count,
            rows.Count)
        {
            SourceInstitution = normalizedRequest.SourceInstitution,
            FundAccountId = normalizedRequest.FundAccountId,
            ExternalAccountId = normalizedRequest.ExternalAccountId,
            StatementPeriodStart = normalizedRequest.StatementPeriodStart,
            StatementPeriodEnd = normalizedRequest.StatementPeriodEnd,
            OriginalFileName = normalizedRequest.OriginalFileName,
            MappingProfileId = normalizedRequest.MappingProfileId,
            ToleranceProfileId = normalizedRequest.ToleranceProfileId,
            ImportedBy = normalizedRequest.ImportedBy,
            SourceFileHash = sourceFileHash,
            CanonicalArtifactHash = canonicalArtifactHash,
            DuplicateKey = duplicateKey,
            AccountingScope = normalizedRequest.AccountingScope
        };
        if (!await store.TrySaveImportAsync(import, rows, ct).ConfigureAwait(false))
        {
            throw new StatementAlreadyImportedException(duplicateKey);
        }

        return new BrokerStatementImportResult(import, rows);
    }

    private static async Task<IReadOnlyList<CanonicalStatementRow>> ParseFileAsync(
        byte[] content,
        BrokerStatementImportRequest request,
        string importId,
        CancellationToken ct)
    {
        var rows = new List<CanonicalStatementRow>();
        await using var stream = new MemoryStream(content, writable: false);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        var headerRecord = await ReadCsvRecordAsync(reader, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("Statement CSV is empty.");
        var header = ParseCsvLine(headerRecord.Text, rowNumber: 1);
        var optionalColumnCount = header.Count - ExpectedColumns.Length;
        if (optionalColumnCount < 0 ||
            optionalColumnCount > OptionalCanonicalColumns.Length ||
            !header.Take(ExpectedColumns.Length).SequenceEqual(ExpectedColumns, StringComparer.OrdinalIgnoreCase) ||
            !header.Skip(ExpectedColumns.Length).SequenceEqual(
                OptionalCanonicalColumns.Take(optionalColumnCount),
                StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Invalid statement CSV header. Expected the canonical prefix {string.Join(',', ExpectedColumns)} " +
                $"followed only by the optional columns {string.Join(',', OptionalCanonicalColumns)} in order.");
        }

        var sourceRowNumber = headerRecord.PhysicalLineCount;
        while (await ReadCsvRecordAsync(reader, ct).ConfigureAwait(false) is { } record)
        {
            var recordStartLine = sourceRowNumber + 1;
            sourceRowNumber += record.PhysicalLineCount;
            var line = record.Text;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (rows.Count >= MaximumRows)
            {
                throw new InvalidDataException($"Statement CSV exceeds the {MaximumRows}-row limit.");
            }

            var fields = ParseCsvLine(line, recordStartLine);
            if (fields.Count != header.Count)
            {
                throw new InvalidDataException(
                    $"Statement CSV row {recordStartLine} has {fields.Count} columns; expected {header.Count} from the validated header.");
            }

            if (!decimal.TryParse(fields[2], NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity) ||
                !decimal.TryParse(fields[3], NumberStyles.Number, CultureInfo.InvariantCulture, out var price) ||
                !decimal.TryParse(fields[4], NumberStyles.Number, CultureInfo.InvariantCulture, out var cashAmount) ||
                !DateOnly.TryParse(fields[6], CultureInfo.InvariantCulture, DateTimeStyles.None, out var tradeDate))
            {
                throw new InvalidDataException($"Statement CSV row {recordStartLine} contains an invalid numeric or date value.");
            }

            // Capture the four optional canonical columns (settlementDate, currency, feesCommission,
            // externalTransactionId) that the header validation already guaranteed are present in
            // order. These flow into currency-aware, external-id-based matching downstream instead
            // of being discarded at the canonical-row boundary.
            DateOnly? settlementDate = null;
            var currency = "USD";
            decimal? feesCommission = null;
            string? externalTransactionId = null;
            if (fields.Count > 7 && !string.IsNullOrWhiteSpace(fields[7]))
            {
                // A blank optional settlement date is legitimately absent, but a nonblank malformed value
                // must not be silently dropped to null: the matcher substitutes TradeDate for a null
                // settlement date, so a bad source date could exact-match a same-day ledger transaction.
                if (!DateOnly.TryParse(fields[7], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedSettlement))
                {
                    throw new InvalidDataException(
                        $"Statement CSV row {recordStartLine} has an invalid optional settlement date '{fields[7]}'.");
                }

                settlementDate = parsedSettlement;
            }

            if (fields.Count > 8 && !string.IsNullOrWhiteSpace(fields[8]))
            {
                currency = fields[8].Trim().ToUpperInvariant();
            }

            if (fields.Count > 9 && decimal.TryParse(fields[9], NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedFees))
            {
                feesCommission = parsedFees;
            }

            if (fields.Count > 10 && !string.IsNullOrWhiteSpace(fields[10]))
            {
                externalTransactionId = fields[10].Trim();
            }

            rows.Add(new CanonicalStatementRow(
                importId,
                recordStartLine,
                fields[0],
                fields[1],
                quantity,
                price,
                cashAmount,
                fields[5],
                tradeDate,
                Hash(line))
            {
                Currency = currency,
                SettlementDate = settlementDate,
                FeesCommission = feesCommission,
                ExternalTransactionId = externalTransactionId
            });
        }

        return rows;
    }

    private static async Task<CsvRecord?> ReadCsvRecordAsync(StreamReader reader, CancellationToken ct)
    {
        var firstLine = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        if (firstLine is null)
        {
            return null;
        }

        var builder = new StringBuilder(firstLine);
        var physicalLineCount = 1;
        while (HasUnclosedQuotedField(builder))
        {
            var continuation = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (continuation is null)
            {
                throw new InvalidDataException("Statement CSV contains an unterminated quoted field.");
            }

            builder.Append('\n').Append(continuation);
            physicalLineCount++;
            if (builder.Length > MaximumLineCharacters)
            {
                throw new InvalidDataException("Statement CSV record exceeds the line limit.");
            }
        }

        return new CsvRecord(builder.ToString(), physicalLineCount);
    }

    private static bool HasUnclosedQuotedField(StringBuilder value)
    {
        var quoted = false;
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '"')
            {
                continue;
            }

            if (quoted && index + 1 < value.Length && value[index + 1] == '"')
            {
                index++;
                continue;
            }

            quoted = !quoted;
        }

        return quoted;
    }

    private sealed record CsvRecord(string Text, int PhysicalLineCount);

    private static void ValidateRowAccounts(
        IReadOnlyList<CanonicalStatementRow> rows,
        string externalAccountId)
    {
        var expectedAccount = externalAccountId.Trim();
        foreach (var row in rows)
        {
            var sourceAccount = row.Account.Trim();
            if (!string.Equals(sourceAccount, expectedAccount, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Statement row {row.SourceRowNumber} identifies account '{sourceAccount}', " +
                    $"which does not match the requested external account '{expectedAccount}'.");
            }
        }
    }

    private static IReadOnlyList<string> ParseCsvLine(string line, int rowNumber)
    {
        if (line.Length > MaximumLineCharacters || line.Contains("\0", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Statement CSV row {rowNumber} exceeds the line limit or contains a null byte.");
        }

        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var current = line[index];
            if (current == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (current == ',' && !quoted)
            {
                fields.Add(field.ToString().Trim());
                field.Clear();
            }
            else
            {
                field.Append(current);
            }
        }

        if (quoted)
        {
            throw new InvalidDataException($"Statement CSV row {rowNumber} contains an unterminated quoted field.");
        }

        fields.Add(field.ToString().Trim());
        return fields;
    }

    private static string Hash(string input)
        => HashBytes(Encoding.UTF8.GetBytes(input));

    // Deliberately NOT routed through Sha256Digest (which lowercases): these hashes become
    // persisted per-row identities on CanonicalStatementRow, so changing the casing would
    // detach retained statement rows from their identities (#2691).
    private static string HashBytes(byte[] input) => Convert.ToHexString(SHA256.HashData(input));

    private static bool IsSupportedStatementSource(string source) =>
        string.Equals(source, "samplebroker", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(source, "broker", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(source, "custodian", StringComparison.OrdinalIgnoreCase);
}

public interface IReconciliationBreakStore
{
    Task WriteAsync(IReadOnlyList<ReconciliationBreakRecord> records, CancellationToken ct = default);
    Task<IReadOnlyList<ReconciliationBreakRecord>> ListOpenAsync(CancellationToken ct = default);

    Task<ReconciliationBreakRecord?> GetAsync(string breakId, CancellationToken ct = default)
        => Task.FromResult<ReconciliationBreakRecord?>(null);

    Task<ReconciliationBreakRecord> ApplyCaseworkAsync(
        StatementBreakCaseworkUpdate update,
        CancellationToken ct = default)
        => throw new NotSupportedException(
            "Direct statement-break casework mutation is unavailable; prepare an immutable statement casework commit first.");

    Task MaterializeRunProjectionAsync(
        ReconciliationBreakRecord initialRecord,
        StatementRunProjectionAudit audit,
        IReadOnlyList<ReconciliationBreakRecord> authorizedImages,
        CancellationToken ct = default)
        => throw new NotSupportedException(
            "This reconciliation break store does not support verified statement-run projection.");

    Task<StatementRunProjectionAudit?> GetRunProjectionAuditAsync(
        string runId,
        string breakId,
        CancellationToken ct = default)
        => Task.FromResult<StatementRunProjectionAudit?>(null);

    Task MaterializeCaseworkBreakAsync(
        IStatementCaseworkCommitStore commitStore,
        string commandId,
        string inputHashSha256,
        CancellationToken ct = default)
        => throw new NotSupportedException(
            "This reconciliation break store does not support source-commit break projection.");

    Task MaterializeCaseworkAuditAsync(
        IStatementCaseworkCommitStore commitStore,
        string commandId,
        string inputHashSha256,
        CancellationToken ct = default)
        => throw new NotSupportedException(
            "This reconciliation break store does not support source-commit audit projection.");

    Task<StatementBreakCaseworkAuditEvent?> GetCaseworkAuditAsync(
        string breakId,
        string commandId,
        CancellationToken ct = default)
        => Task.FromResult<StatementBreakCaseworkAuditEvent?>(null);
}

public sealed record StatementBreakCaseworkUpdate(
    string BreakId,
    string ImportId,
    string Status,
    string Actor,
    string Action,
    string CommandId,
    string CorrelationId,
    string? Reason,
    string? Disposition,
    string? ApprovalActor,
    string? ApprovalReference,
    string? SupersedingBreakId,
    IReadOnlyList<string> EvidenceLinks,
    DateTimeOffset OccurredAtUtc);

public sealed record StatementBreakCaseworkAuditEvent(
    string EventId,
    string BreakId,
    string ImportId,
    string PreviousStatus,
    string NewStatus,
    string Actor,
    string Action,
    string CommandId,
    string CorrelationId,
    string? Reason,
    string? Disposition,
    string? ApprovalActor,
    string? ApprovalReference,
    string? SupersedingBreakId,
    IReadOnlyList<string> EvidenceLinks,
    DateTimeOffset OccurredAtUtc,
    string InputHashSha256);

public sealed class JsonReconciliationBreakStore : IReconciliationBreakStore
{
    private readonly string _folder;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonReconciliationBreakStore(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _folder = Path.Combine(dataRoot, "reconciliation", "statement-breaks");
    }

    public async Task WriteAsync(IReadOnlyList<ReconciliationBreakRecord> records, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var record in records)
            {
                ArgumentNullException.ThrowIfNull(record);
                await AtomicFileWriter
                    .WriteAsync(
                        BreakPath(record.BreakId),
                        JsonSerializer.Serialize(
                            record,
                            StatementDurabilityJsonContext.Default.ReconciliationBreakRecord),
                        ct)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ReconciliationBreakRecord>> ListOpenAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!Directory.Exists(_folder))
            return [];

        var items = new List<ReconciliationBreakRecord>();
        foreach (var path in Directory.EnumerateFiles(_folder, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var record = await JsonSerializer.DeserializeAsync(
                    stream,
                    StatementDurabilityJsonContext.Default.ReconciliationBreakRecord,
                    ct)
                .ConfigureAwait(false)
                ?? throw new InvalidDataException(
                    $"Reconciliation break artifact '{path}' retained a null payload.");
            if (record is not null && string.Equals(record.Status, "Open", StringComparison.OrdinalIgnoreCase))
            {
                items.Add(record);
            }
        }

        return items
            .OrderByDescending(static item => item.CreatedAtUtc)
            .ToArray();
    }

    public async Task<ReconciliationBreakRecord?> GetAsync(string breakId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(breakId);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadBreakCoreAsync(breakId, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<ReconciliationBreakRecord> ApplyCaseworkAsync(
        StatementBreakCaseworkUpdate update,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ValidateCaseworkUpdate(update);
        ct.ThrowIfCancellationRequested();
        throw new NotSupportedException(
            "Direct statement-break casework mutation is unavailable; use the immutable statement casework commit protocol.");
    }

    public async Task MaterializeRunProjectionAsync(
        ReconciliationBreakRecord initialRecord,
        StatementRunProjectionAudit audit,
        IReadOnlyList<ReconciliationBreakRecord> authorizedImages,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(initialRecord);
        ValidateRunProjectionAudit(initialRecord, audit);
        ValidateAuthorizedBreakImages(initialRecord, authorizedImages);
        var authoritativeRecord = authorizedImages[^1];
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var retained = await ReadBreakCoreAsync(initialRecord.BreakId, ct).ConfigureAwait(false);
            var auditPath = RunProjectionAuditPath(initialRecord.RunId, initialRecord.BreakId);
            var retainedAudit = await ReadRunProjectionAuditCoreAsync(auditPath, ct).ConfigureAwait(false);
            if (retained is not null && !authorizedImages.Any(image => SameArtifact(retained, image)))
            {
                throw new InvalidOperationException(
                    $"Statement break '{initialRecord.BreakId}' is outside its immutable match/source-commit authority chain.");
            }

            if (retainedAudit is not null && !SameArtifact(retainedAudit, audit))
            {
                throw new InvalidOperationException(
                    $"Statement break '{initialRecord.BreakId}' retains a conflicting run-projection audit.");
            }

            if (retained is null || !SameArtifact(retained, authoritativeRecord))
            {
                await AtomicFileWriter
                    .WriteAsync(
                        BreakPath(initialRecord.BreakId),
                        JsonSerializer.Serialize(
                            authoritativeRecord,
                            StatementDurabilityJsonContext.Default.ReconciliationBreakRecord),
                        ct)
                    .ConfigureAwait(false);
            }

            if (retainedAudit is null)
            {
                await AtomicFileWriter
                    .WriteAsync(
                        auditPath,
                        JsonSerializer.Serialize(
                            audit,
                            StatementDurabilityJsonContext.Default.StatementRunProjectionAudit),
                        ct)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StatementRunProjectionAudit?> GetRunProjectionAuditAsync(
        string runId,
        string breakId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(breakId);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadRunProjectionAuditCoreAsync(
                    RunProjectionAuditPath(runId, breakId),
                    ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MaterializeCaseworkBreakAsync(
        IStatementCaseworkCommitStore commitStore,
        string commandId,
        string inputHashSha256,
        CancellationToken ct = default)
    {
        var envelope = await RequirePreparedCommitAsync(
                commitStore,
                commandId,
                inputHashSha256,
                ct)
            .ConfigureAwait(false);
        var original = envelope.OriginalBreak;
        var next = envelope.NextBreak;
        if (!string.Equals(original.BreakId, next.BreakId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(original.ImportId, next.ImportId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Statement source-commit break images must identify the same retained break.", nameof(next));
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = await ReadBreakCoreAsync(next.BreakId, ct).ConfigureAwait(false);
            if (current is not null && SameArtifact(current, next))
            {
                return;
            }

            if (current is not null && !SameArtifact(current, original))
            {
                throw new InvalidOperationException(
                    $"Source statement break '{next.BreakId}' no longer matches either retained source-commit image.");
            }

            await AtomicFileWriter
                .WriteAsync(
                    BreakPath(next.BreakId),
                    JsonSerializer.Serialize(
                        next,
                        StatementDurabilityJsonContext.Default.ReconciliationBreakRecord),
                    ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MaterializeCaseworkAuditAsync(
        IStatementCaseworkCommitStore commitStore,
        string commandId,
        string inputHashSha256,
        CancellationToken ct = default)
    {
        var envelope = await RequirePreparedCommitAsync(
                commitStore,
                commandId,
                inputHashSha256,
                ct)
            .ConfigureAwait(false);
        var audit = envelope.BreakAudit;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var path = CaseworkAuditPath(audit.BreakId, audit.CommandId);
            var current = File.Exists(path)
                ? await ReadJsonAsync(
                        path,
                        StatementDurabilityJsonContext.Default.StatementBreakCaseworkAuditEvent,
                        ct)
                    .ConfigureAwait(false)
                : null;
            if (current is not null)
            {
                if (!SameArtifact(current, audit))
                {
                    throw new InvalidOperationException(
                        $"Statement break audit for command '{audit.CommandId}' conflicts with the retained source commit.");
                }

                return;
            }

            await AtomicFileWriter
                .WriteAsync(
                    path,
                    JsonSerializer.Serialize(
                        audit,
                        StatementDurabilityJsonContext.Default.StatementBreakCaseworkAuditEvent),
                    ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StatementBreakCaseworkAuditEvent?> GetCaseworkAuditAsync(
        string breakId,
        string commandId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(breakId);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var path = CaseworkAuditPath(breakId, commandId);
            return File.Exists(path)
                ? await ReadJsonAsync(
                        path,
                        StatementDurabilityJsonContext.Default.StatementBreakCaseworkAuditEvent,
                        ct)
                    .ConfigureAwait(false)
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ReconciliationBreakRecord?> ReadBreakCoreAsync(
        string breakId,
        CancellationToken ct)
    {
        var path = BreakPath(breakId);
        return File.Exists(path)
            ? await ReadJsonAsync(
                    path,
                    StatementDurabilityJsonContext.Default.ReconciliationBreakRecord,
                    ct)
                .ConfigureAwait(false)
            : null;
    }

    private static async Task<T?> ReadJsonAsync<T>(
        string path,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken ct)
        where T : class
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync(stream, typeInfo, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Statement durability artifact '{path}' retained a null payload.");
    }

    private static async Task<StatementCaseworkCommitEnvelope> RequirePreparedCommitAsync(
        IStatementCaseworkCommitStore commitStore,
        string commandId,
        string inputHashSha256,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(commitStore);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputHashSha256);
        var envelope = await commitStore.GetAsync(commandId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Statement casework command '{commandId}' cannot project before its immutable commit is prepared.");
        if (!StatementDurabilityHashing.FixedTimeEquals(envelope.InputHashSha256, inputHashSha256))
        {
            throw new InvalidOperationException(
                $"Statement casework command '{commandId}' is bound to different prepared input.");
        }

        return envelope;
    }

    private async Task<StatementRunProjectionAudit?> ReadRunProjectionAuditCoreAsync(
        string path,
        CancellationToken ct)
        => File.Exists(path)
            ? await ReadJsonAsync(
                    path,
                    StatementDurabilityJsonContext.Default.StatementRunProjectionAudit,
                    ct)
                .ConfigureAwait(false)
            : null;

    private string BreakPath(string breakId)
        => Path.Combine(_folder, $"{ReconciliationRecordFileName.For(breakId)}.json");

    private string CaseworkAuditPath(string breakId, string commandId)
        => Path.Combine(
            _folder,
            "_casework",
            "audit",
            ReconciliationRecordFileName.For(breakId),
            $"{CaseworkCommandFileName(commandId)}.json");

    private string RunProjectionAuditPath(string runId, string breakId)
        => Path.Combine(
            _folder,
            "_run-projections",
            ReconciliationRecordFileName.For(runId),
            $"{ReconciliationRecordFileName.For(breakId)}.json");

    private static string CaseworkCommandFileName(string commandId)
        => Sha256Digest.ComputeUtf8(commandId.Trim());

    private static void ValidateCaseworkUpdate(StatementBreakCaseworkUpdate update)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(update.BreakId);
        ArgumentException.ThrowIfNullOrWhiteSpace(update.ImportId);
        ArgumentException.ThrowIfNullOrWhiteSpace(update.Status);
        ArgumentException.ThrowIfNullOrWhiteSpace(update.Actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(update.Action);
        ArgumentException.ThrowIfNullOrWhiteSpace(update.CommandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(update.CorrelationId);
        if (update.OccurredAtUtc == default)
        {
            throw new ArgumentException("Statement casework occurrence time is required.", nameof(update));
        }
    }

    private static void ValidateRunProjectionAudit(
        ReconciliationBreakRecord record,
        StatementRunProjectionAudit audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        if (audit.SchemaVersion != StatementRunProjectionAudit.CurrentSchemaVersion ||
            !string.Equals(audit.RunId, record.RunId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(audit.ImportId, record.ImportId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(audit.ProjectionKind, StatementRunProjectionAudit.BreakKind, StringComparison.Ordinal) ||
            !string.Equals(audit.ProjectionId, record.BreakId, StringComparison.OrdinalIgnoreCase) ||
            !StatementDurabilityHashing.FixedTimeEquals(
                audit.ArtifactSha256,
                StatementDurabilityHashing.Hash(record)))
        {
            throw new InvalidDataException(
                $"Statement break '{record.BreakId}' run-projection audit does not bind the supplied immutable artifact.");
        }
    }

    private static void ValidateAuthorizedBreakImages(
        ReconciliationBreakRecord initialRecord,
        IReadOnlyList<ReconciliationBreakRecord> authorizedImages)
    {
        ArgumentNullException.ThrowIfNull(authorizedImages);
        if (authorizedImages.Count == 0 || !SameArtifact(authorizedImages[0], initialRecord) ||
            authorizedImages.Any(image =>
                image is null ||
                !string.Equals(image.BreakId, initialRecord.BreakId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(image.RunId, initialRecord.RunId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(image.ImportId, initialRecord.ImportId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                $"Statement break '{initialRecord.BreakId}' received an invalid immutable authority chain.");
        }
    }

    private static bool SameArtifact(ReconciliationBreakRecord left, ReconciliationBreakRecord right)
        => StatementDurabilityHashing.FixedTimeEquals(
            StatementDurabilityHashing.Hash(left),
            StatementDurabilityHashing.Hash(right));

    private static bool SameArtifact(StatementBreakCaseworkAuditEvent left, StatementBreakCaseworkAuditEvent right)
        => StatementDurabilityHashing.FixedTimeEquals(
            StatementDurabilityHashing.Hash(left),
            StatementDurabilityHashing.Hash(right));

    private static bool SameArtifact(StatementRunProjectionAudit left, StatementRunProjectionAudit right)
        => StatementDurabilityHashing.FixedTimeEquals(
            StatementDurabilityHashing.Hash(
                left,
                StatementDurabilityJsonContext.Default.StatementRunProjectionAudit),
            StatementDurabilityHashing.Hash(
                right,
                StatementDurabilityJsonContext.Default.StatementRunProjectionAudit));
}
