using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.Storage.Archival;

namespace Meridian.FinancialOperations.Reconciliation.Connectors;

public sealed partial class StatementImportService
{
    private const int ReviewPageSize = 100;

    public async Task<StatementImportCommitResultDto> RetainReviewedCaseAdmissionsAsync(
        string reviewId, string tenantId, string companyId, IReadOnlyDictionary<string, string> casesByRow,
        CancellationToken ct = default)
    {
        var review = await GetReviewAsync(reviewId, tenantId, companyId, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("The retained review is unavailable for row case admission.");
        await using var ownership = await AcquireReviewLeaseAsync(reviewId, ct).ConfigureAwait(false);
        var receiptPath = ReviewPath(reviewId, "receipt");
        var receipt = JsonSerializer.Deserialize(await File.ReadAllBytesAsync(receiptPath, ct).ConfigureAwait(false),
            StatementImportReviewJsonContext.Default.StatementImportReviewReceipt)
            ?? throw new InvalidDataException("The retained import receipt is unreadable.");
        var outcomes = receipt.Outcomes.Select(row =>
        {
            if (!casesByRow.TryGetValue(row.RowId, out var caseId))
                return row;
            if (row.Disposition != "AcceptAsBreak")
                throw new InvalidDataException("Case admission requires an explicit accepted row disposition.");
            return row with { Status = "AcceptedAsBreak", CaseIds = [caseId] };
        }).ToArray();
        var additions = casesByRow.Where(pair => !receipt.Result.CaseIds.Contains(pair.Value, StringComparer.Ordinal)).ToArray();
        var links = additions.Select(pair => new StatementImportReconciliationCaseLinkDto(pair.Value, pair.Key,
            $"/accounting/reconciliation?reconciliationBreak={Uri.EscapeDataString(pair.Value)}",
            "Imported row requires review", "Open", "Normal", review.Rows.Single(row => row.RowId == pair.Key).Record.Description ?? "Operator requested investigation.",
            "Review retained statement row evidence and resolve the case.")).ToArray();
        var result = receipt.Result with
        {
            RowOutcomes = outcomes.Take(ReviewPageSize).ToArray(),
            TotalRows = outcomes.Length,
            CaseCount = receipt.Result.CaseCount + additions.Length,
            CaseIds = receipt.Result.CaseIds.Concat(additions.Select(pair => pair.Value)).ToArray(),
            ReconciliationCaseLinks = receipt.Result.ReconciliationCaseLinks.Concat(links).ToArray(),
            ReconciliationCaseRoutes = receipt.Result.ReconciliationCaseRoutes.Concat(links.Select(link => link.Route)).ToArray()
        };
        await AtomicFileWriter.WriteAsync(receiptPath, JsonSerializer.SerializeToUtf8Bytes(receipt with { Result = result, Outcomes = outcomes },
            StatementImportReviewJsonContext.Default.StatementImportReviewReceipt), ct).ConfigureAwait(false);
        return result;
    }

    public static IReadOnlyList<StatementImportRowDispositionDto> ParseRowDispositions(string json)
        => string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize(json,
            StatementImportReviewJsonContext.Default.StatementImportRowDispositionDtoArray)
            ?? throw new InvalidDataException("Row dispositions must be an array.");

    public async Task ValidateReviewedCommitAsync(StatementImportCommitRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ReviewId))
        {
            if (request.RowDispositions.Count != 0)
                throw new InvalidDataException("Row dispositions require a retained preview.");
            return;
        }
        RequireReviewScope(request);
        var review = await GetReviewAsync(request.ReviewId, request.TenantId!, request.CompanyId!, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The retained preview is unavailable. Preview again.");
        await ValidateCurrentReviewAsync(request, review, ct).ConfigureAwait(false);
        var (connector, _) = ResolveConnector(request.Document, request.ConnectorId);
        var parse = RequireCurrencyEvidence(await connector!.ParseAsync(request.Document, ct).ConfigureAwait(false));
        await ApplyReviewedDispositionsAsync(request, parse, ct).ConfigureAwait(false);
    }

    public async Task<StatementImportPreviewDto> PreviewReviewAsync(
        StatementImportCommitRequest request, CancellationToken ct = default)
    {
        RequireReviewScope(request);
        var preview = await PreviewAsync(request.Document, request.ConnectorId, ct).ConfigureAwait(false);
        if (request.Document.Content.Length > _ingressLimits.MaxDocumentBytes)
            return preview;
        var (connector, _) = ResolveConnector(request.Document, request.ConnectorId);
        if (connector is null)
            return preview;
        var parse = RequireCurrencyEvidence(await connector.ParseAsync(request.Document, ct).ConfigureAwait(false));
        if (parse.TotalRetainedRows > _ingressLimits.MaxRecords)
            return preview;
        // Account mismatch is an authority error, not an exception that may be excluded.
        EnsureParsedAccountAuthority(request.Document, parse.Records, request.ExternalAccountId);
        var binding = ComputeReviewBinding(request, parse);
        var reviewId = "sir-" + Sha256Digest.ComputeUtf8(binding);
        var canExclude = !parse.HasErrors && parse.TotalRetainedRows == parse.Records.Count;
        var rows = parse.Records.Select((record, index) => new StatementImportReviewRowDto(
            "row-" + Sha256Digest.ComputeUtf8($"{reviewId}\n{index}"), index, null, ToReviewRecord(record),
            [], canExclude, parse.HasErrors ? "Correct the blocking source or mapping errors before excluding normalized rows."
                : !canExclude ? "Supplemental account, activity, lot, or borrow evidence has no exact row mapping; exclude rows in the source and preview again." : null,
            !parse.HasErrors && request.AllowAcceptAsBreak)).ToArray();
        var snapshot = new StatementImportReviewSnapshot(reviewId, binding, request.TenantId!, request.CompanyId!,
            request.FundAccountId, request.ExternalAccountId, request.SourceInstitution, request.SourceKind,
            request.PeriodStart, request.PeriodEnd, request.AccountingScope, rows);
        var path = ReviewPath(reviewId, "preview");
        if (!File.Exists(path))
            await AtomicFileWriter.WriteAsync(path, JsonSerializer.SerializeToUtf8Bytes(snapshot,
                StatementImportReviewJsonContext.Default.StatementImportReviewSnapshot), ct).ConfigureAwait(false);
        return preview with
        {
            ReviewId = reviewId,
            Rows = rows.Take(ReviewPageSize).ToArray(),
            TotalRows = rows.Length,
            RowsRoute = ReviewRoute(reviewId, "rows")
        };
    }

    public async Task<StatementImportReviewSnapshot?> GetReviewAsync(
        string reviewId, string tenantId, string companyId, CancellationToken ct = default)
    {
        var path = ReviewPath(reviewId, "preview");
        if (!File.Exists(path))
            return null;
        var review = JsonSerializer.Deserialize(await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false),
            StatementImportReviewJsonContext.Default.StatementImportReviewSnapshot)
            ?? throw new InvalidDataException("The retained import review is unreadable.");
        if (!string.Equals(review.ReviewId, reviewId, StringComparison.Ordinal)
            || !string.Equals("sir-" + Sha256Digest.ComputeUtf8(review.Binding), reviewId, StringComparison.Ordinal))
            throw new InvalidDataException("The retained import review identity failed validation.");
        ValidateRetainedRows(review);
        return string.Equals(review.TenantId, tenantId, StringComparison.Ordinal)
               && string.Equals(review.CompanyId, companyId, StringComparison.Ordinal) ? review : null;
    }

    public static StatementImportReviewRowsPageDto ReviewRows(StatementImportReviewSnapshot review, int offset, int limit)
    {
        ValidateReviewPage(offset, limit);
        return new(review.Rows.Skip(offset).Take(limit).ToArray(), review.Rows.Count, offset, limit);
    }

    public async Task<StatementImportRowOutcomesPageDto?> GetReviewOutcomesAsync(
        StatementImportReviewSnapshot review, int offset, int limit, CancellationToken ct = default)
    {
        ValidateReviewPage(offset, limit);
        var path = ReviewPath(review.ReviewId, "receipt");
        if (!File.Exists(path))
            return null;
        var receipt = JsonSerializer.Deserialize(await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false),
            StatementImportReviewJsonContext.Default.StatementImportReviewReceipt)
            ?? throw new InvalidDataException("The retained row outcome receipt is unreadable.");
        return new(receipt.Outcomes.Skip(offset).Take(limit).ToArray(), receipt.Outcomes.Count, offset, limit);
    }

    public async Task<StatementImportCommitResultDto> CommitAsync(StatementImportCommitRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ReviewId))
        {
            if (request.RowDispositions.Count != 0)
                throw new InvalidDataException("Row dispositions require the exact retained import review.");
            return await CommitCoreAsync(request, ct).ConfigureAwait(false);
        }
        RequireReviewScope(request);
        var review = await GetReviewAsync(request.ReviewId, request.TenantId!, request.CompanyId!, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The retained preview is unavailable in this scope. Preview the statement again.");
        var commandHash = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(
            request.RowDispositions.OrderBy(row => row.RowId, StringComparer.Ordinal).ToArray(),
            StatementImportReviewJsonContext.Default.StatementImportRowDispositionDtoArray));
        await using var ownership = await AcquireReviewLeaseAsync(request.ReviewId, ct).ConfigureAwait(false);
        var receiptPath = ReviewPath(request.ReviewId, "receipt");
        if (File.Exists(receiptPath))
        {
            var retained = JsonSerializer.Deserialize(await File.ReadAllBytesAsync(receiptPath, ct).ConfigureAwait(false),
                StatementImportReviewJsonContext.Default.StatementImportReviewReceipt)
                ?? throw new InvalidDataException("The retained import receipt is unreadable.");
            if (!string.Equals(retained.CommandHash, commandHash, StringComparison.Ordinal))
                throw new InvalidOperationException("This review was already committed with different row decisions. Create a new preview before changing them.");
            // Revalidate source/mapping/accounting scope even when returning an exact retained receipt.
            await ValidateCurrentReviewAsync(request, review, ct).ConfigureAwait(false);
            return retained.Result with { Duplicate = true };
        }
        var result = await CommitCoreAsync(request, ct).ConfigureAwait(false);
        var decisions = request.RowDispositions.ToDictionary(row => row.RowId, StringComparer.Ordinal);
        var outcomes = review.Rows.Select(row =>
        {
            decisions.TryGetValue(row.RowId, out var decision);
            var excluded = decision?.Disposition == "Exclude";
            return new StatementImportRowOutcomeDto(row.RowId, row.NormalizedIndex,
                decision?.Disposition ?? "Include", decision?.Reason,
                excluded ? "Excluded" : decision?.Disposition == "AcceptAsBreak" ? "AwaitingCaseAdmission" : "Imported", result.RunId, []);
        }).ToArray();
        result = result with
        {
            ReviewId = review.ReviewId,
            RowOutcomes = outcomes.Take(ReviewPageSize).ToArray(),
            TotalRows = outcomes.Length,
            OutcomesRoute = ReviewRoute(review.ReviewId, "outcomes"),
            NextAction = outcomes.Any(row => row.Status == "Excluded")
                ? $"{outcomes.Count(row => row.Status == "Excluded")} normalized row(s) excluded with retained reasons. Review the import outcomes and reconciliation cases."
                : result.NextAction
        };
        var receipt = new StatementImportReviewReceipt(commandHash, request.ImportedBy, DateTimeOffset.UtcNow, result, outcomes);
        await AtomicFileWriter.WriteAsync(receiptPath, JsonSerializer.SerializeToUtf8Bytes(receipt,
            StatementImportReviewJsonContext.Default.StatementImportReviewReceipt), ct).ConfigureAwait(false);
        return result;
    }

    private async Task<StatementParseResult> ApplyReviewedDispositionsAsync(
        StatementImportCommitRequest request, StatementParseResult parse, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ReviewId))
            return parse;
        RequireReviewScope(request);
        var review = await GetReviewAsync(request.ReviewId, request.TenantId!, request.CompanyId!, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The retained preview is unavailable in this scope. Preview again.");
        if (!string.Equals(review.Binding, ComputeReviewBinding(request, parse), StringComparison.Ordinal))
            throw new InvalidOperationException("The source, executed mapping, or accounting scope changed after preview. Preview again before committing.");
        var rowIds = review.Rows.ToDictionary(row => row.RowId, StringComparer.Ordinal);
        var excluded = new HashSet<int>();
        var dispositioned = new HashSet<string>(StringComparer.Ordinal);
        foreach (var decision in request.RowDispositions)
        {
            if (!rowIds.TryGetValue(decision.RowId, out var row))
                throw new InvalidDataException("A row disposition does not belong to this preview.");
            if (!dispositioned.Add(decision.RowId))
                throw new InvalidDataException("A row was dispositioned more than once.");
            if (decision.Disposition is not ("Exclude" or "AcceptAsBreak"))
                throw new InvalidDataException("Unknown row disposition.");
            if (string.IsNullOrWhiteSpace(decision.Reason))
                throw new InvalidDataException("Every excluded row requires a reason.");
            if (decision.Disposition == "AcceptAsBreak")
            {
                if (!row.CanAcceptAsBreak || !request.AllowAcceptAsBreak)
                    throw new InvalidDataException("Accept-as-break requires the authoritative row-to-case coordinator.");
                continue;
            }
            if (parse.TotalRetainedRows != parse.Records.Count || !row.CanExclude || !excluded.Add(row.NormalizedIndex))
                throw new InvalidDataException("The row cannot be excluded or was dispositioned more than once.");
        }
        return parse with { Records = parse.Records.Where((_, index) => !excluded.Contains(index)).ToArray() };
    }

    private async Task ValidateCurrentReviewAsync(StatementImportCommitRequest request, StatementImportReviewSnapshot review, CancellationToken ct)
    {
        if (request.Document.Content.Length > _ingressLimits.MaxDocumentBytes)
            throw new InvalidDataException("Statement exceeds the import size limit.");
        var (connector, issue) = ResolveConnector(request.Document, request.ConnectorId);
        if (connector is null)
            throw new InvalidDataException(issue?.Message ?? "Connector unavailable.");
        var parse = RequireCurrencyEvidence(await connector.ParseAsync(request.Document, ct).ConfigureAwait(false));
        if (parse.HasErrors || parse.TotalRetainedRows > _ingressLimits.MaxRecords)
            throw new InvalidDataException("Statement source validation failed. Correct the source before importing.");
        EnsureParsedAccountAuthority(request.Document, parse.Records, request.ExternalAccountId);
        if (!string.Equals(review.Binding, ComputeReviewBinding(request, parse), StringComparison.Ordinal))
            throw new InvalidOperationException("The source, mapping, or scope changed since preview.");
    }

    private static string ComputeReviewBinding(StatementImportCommitRequest request, StatementParseResult parse)
        => string.Join('\n', request.TenantId, request.CompanyId, request.SourceKind.Trim().ToLowerInvariant(),
            request.SourceInstitution.Trim(), request.FundAccountId.Trim(), request.ExternalAccountId.Trim(),
            request.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), request.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            request.ToleranceProfileId?.Trim() ?? StatementToleranceProfile.DefaultProfileId,
            request.AccountingScope is null ? "no-accounting-scope" : $"{request.AccountingScope.FundProfileId}|{request.AccountingScope.LedgerBookId:D}|{request.AccountingScope.AccountingPeriodId:D}|{request.AccountingScope.AsOfDate:yyyy-MM-dd}",
            parse.ConnectorId, parse.ProfileId, parse.ExecutedMappingFingerprint, parse.Fingerprint.Sha256, request.AllowAcceptAsBreak.ToString(),
            Sha256Digest.Compute(request.Document.Content.Span), Sha256Digest.ComputeUtf8(RenderCanonicalArtifact(parse.Records)));

    private static void ValidateRetainedRows(StatementImportReviewSnapshot review)
    {
        var records = review.Rows.Select((row, index) =>
        {
            if (row.NormalizedIndex != index || row.RowId != "row-" + Sha256Digest.ComputeUtf8($"{review.ReviewId}\n{index}"))
                throw new InvalidDataException("Retained preview row identity or normalized order changed.");
            var record = row.Record;
            if (record.QuantityExact != record.Quantity.ToString(CultureInfo.InvariantCulture)
                || record.PriceExact != record.Price.ToString(CultureInfo.InvariantCulture)
                || record.CashAmountExact != record.CashAmount.ToString(CultureInfo.InvariantCulture)
                || row.SourceRowNumber is not null)
                throw new InvalidDataException("Retained preview amount or source-row metadata changed.");
            if (!Enum.TryParse<StatementRecordKind>(record.Kind, out var kind)
                || !DateOnly.TryParseExact(record.TradeDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var tradeDate))
                throw new InvalidDataException("Retained preview row record is invalid.");
            return new StatementCanonicalRecord(kind, record.Account, record.Symbol, record.Quantity, record.Price, record.CashAmount,
                record.ActivityType, tradeDate, record.SettlementDate is null ? null : DateOnly.ParseExact(record.SettlementDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                record.Currency, record.FeesCommission, record.ExternalTransactionId, record.ActivityCategory, record.ActivitySubtype,
                record.ProviderActivityCode, record.RelatedTransactionId, record.OrderId, record.Description);
        }).ToArray();
        if (Sha256Digest.ComputeUtf8(RenderCanonicalArtifact(records)) != review.Binding.Split('\n')[^1])
            throw new InvalidDataException("Retained preview rows do not match the reviewed normalized source digest.");
    }

    private static void RequireReviewScope(StatementImportCommitRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TenantId) || string.IsNullOrWhiteSpace(request.CompanyId))
            throw new InvalidDataException("Import review requires server-authorized tenant and company scope.");
    }

    private static StatementRecordPreviewDto ToReviewRecord(StatementCanonicalRecord record)
        => new(record.Kind.ToString(), record.Account, record.Symbol, record.Quantity, record.Price, record.CashAmount,
            record.ActivityType, record.TradeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), record.SettlementDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            record.Currency, record.FeesCommission, record.ExternalTransactionId, record.ActivityCategory, record.ActivitySubtype,
            record.ProviderActivityCode, record.RelatedTransactionId, record.OrderId, record.Description)
        {
            QuantityExact = record.Quantity.ToString(CultureInfo.InvariantCulture),
            PriceExact = record.Price.ToString(CultureInfo.InvariantCulture),
            CashAmountExact = record.CashAmount.ToString(CultureInfo.InvariantCulture)
        };

    private static string ReviewRoute(string reviewId, string kind)
        => $"/api/workstation/reconciliation/statement-imports/reviews/{reviewId}/{kind}";

    private string ReviewPath(string reviewId, string kind)
    {
        if (reviewId.Length != 68 || !reviewId.StartsWith("sir-", StringComparison.Ordinal) || reviewId.AsSpan(4).ContainsAnyExcept("0123456789abcdef"))
            throw new InvalidDataException("Invalid retained review id.");
        var path = _retainedPathGuard.ResolvePath("reconciliation", "statement-import-reviews", $"{reviewId}.{kind}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private async Task<FileStream> AcquireReviewLeaseAsync(string reviewId, CancellationToken ct)
    {
        var path = ReviewPath(reviewId, "lease");
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(50, ct).ConfigureAwait(false); }
        }
    }

    private static void ValidateReviewPage(int offset, int limit)
    {
        if (offset < 0 || limit is < 1 or > 200)
            throw new InvalidDataException("Row pages require a non-negative offset and a limit from 1 to 200.");
    }
}

public sealed record StatementImportReviewSnapshot(
    string ReviewId, string Binding, string TenantId, string CompanyId, string FundAccountId,
    string ExternalAccountId, string SourceInstitution, string SourceKind, DateOnly PeriodStart, DateOnly PeriodEnd,
    StatementAccountingScope? AccountingScope, IReadOnlyList<StatementImportReviewRowDto> Rows);

internal sealed record StatementImportReviewReceipt(
    string CommandHash, string Actor, DateTimeOffset CommittedAtUtc, StatementImportCommitResultDto Result,
    IReadOnlyList<StatementImportRowOutcomeDto> Outcomes);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StatementImportReviewSnapshot))]
[JsonSerializable(typeof(StatementImportReviewReceipt))]
[JsonSerializable(typeof(StatementImportRowDispositionDto[]))]
internal sealed partial class StatementImportReviewJsonContext : JsonSerializerContext;
