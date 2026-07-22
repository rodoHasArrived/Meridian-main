using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.DirectLending;

namespace Meridian.Application.DirectLending;

public sealed class DirectLendingServicerStatementService : IDirectLendingServicerStatementService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDirectLendingService _directLendingService;

    // Lock-ordering invariant for _gate:
    //   * _gate is the single monitor guarding the two in-memory stores below (_batches and
    //     _importedRowHashes). Because there is exactly one lock, no lock-acquisition order exists
    //     to violate — deadlock would require a second lock or lock reentrancy across threads.
    //   * Critical sections under _gate must stay purely in-memory and synchronous: never await,
    //     and never call _directLendingService or any other collaborator while holding _gate.
    //     All such I/O (portfolio/loan loads, batch creation, payment application) is performed
    //     BEFORE or AFTER the lock, never inside it. Honouring this keeps the lock uncontended and
    //     free of any deadlock/reentrancy risk.
    private readonly object _gate = new();
    private readonly Dictionary<Guid, ServicerStatementPreviewDto> _batches = new();
    private readonly HashSet<string> _importedRowHashes = new(StringComparer.Ordinal);

    public DirectLendingServicerStatementService(IDirectLendingService directLendingService)
    {
        _directLendingService = directLendingService;
    }

    public async Task<ServicerStatementPreviewDto> PreviewAsync(ServicerStatementImportRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var rows = await NormalizeRowsAsync(request, ct).ConfigureAwait(false);
        var mappedRows = new List<ServicerStatementRowDto>(rows.Count);
        var issues = new List<ServicerStatementValidationIssueDto>();
        var portfolio = await _directLendingService.GetPortfolioSummaryAsync(ct).ConfigureAwait(false);
        var contractByLoan = new Dictionary<Guid, LoanContractDetailDto>();
        var loanBySymbol = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var loanByBorrower = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var loanByFacility = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        foreach (var loan in portfolio.Loans)
        {
            ct.ThrowIfCancellationRequested();
            var contract = await _directLendingService.GetLoanAsync(loan.LoanId, ct).ConfigureAwait(false);
            if (contract is null)
            {
                continue;
            }

            contractByLoan[loan.LoanId] = contract;
            loanByBorrower.TryAdd(contract.Borrower.BorrowerName, loan.LoanId);
            loanByFacility.TryAdd(contract.FacilityName, loan.LoanId);
            if (!string.IsNullOrWhiteSpace(contract.CurrentTerms.SecurityMasterReference?.Symbol))
            {
                loanBySymbol.TryAdd(contract.CurrentTerms.SecurityMasterReference.Symbol, loan.LoanId);
            }
        }

        foreach (var row in rows)
        {
            var rowIssues = new List<ServicerStatementValidationIssueDto>();
            var loanId = ResolveLoanId(row, contractByLoan, loanBySymbol, loanByBorrower, loanByFacility);
            var contract = loanId is Guid id && contractByLoan.TryGetValue(id, out var resolvedContract) ? resolvedContract : null;
            var status = row.Status;

            if (loanId is null)
            {
                status = ServicerStatementRowStatus.Unmapped;
                rowIssues.Add(Issue("servicer-statement.loan-unmapped", "Error", "Row does not resolve to a Direct Lending loan.", row.RowId, "loanId"));
            }

            if (row.StatementDate is DateOnly rowStatementDate && rowStatementDate != request.StatementDate)
            {
                rowIssues.Add(Issue("servicer-statement.statement-date-mismatch", "Warning", "Row statement date differs from the batch statement date.", row.RowId, "statementDate"));
            }

            if (row.Currency is { } currency && contract is not null && currency != contract.CurrentTerms.BaseCurrency)
            {
                status = ServicerStatementRowStatus.Rejected;
                rowIssues.Add(Issue("servicer-statement.currency-mismatch", "Error", "Row currency does not match the loan base currency.", row.RowId, "currency"));
            }

            if (request.Kind is ServicerStatementKind.Remittance or ServicerStatementKind.Mixed && row.Kind is ServicerStatementKind.Remittance or ServicerStatementKind.Mixed)
            {
                if ((row.GrossAmount ?? 0m) <= 0m)
                {
                    status = ServicerStatementRowStatus.Rejected;
                    rowIssues.Add(Issue("servicer-statement.amount-missing", "Error", "Remittance rows require a positive gross amount.", row.RowId, "grossAmount"));
                }
                else if (status == ServicerStatementRowStatus.Mapped && rowIssues.All(static issue => !IsHardIssue(issue)))
                {
                    status = ServicerStatementRowStatus.ReadyToApply;
                }
            }

            if (string.IsNullOrWhiteSpace(row.FundAccountId ?? request.FundAccountId))
            {
                rowIssues.Add(Issue("servicer-statement.fund-account-unmapped", "Warning", "Row has no mapped fund account.", row.RowId, "fundAccountId"));
            }

            if ((row.Kind is ServicerStatementKind.Remittance or ServicerStatementKind.Mixed) &&
                string.IsNullOrWhiteSpace(row.CashAccountId ?? request.CashAccountId))
            {
                rowIssues.Add(Issue("servicer-statement.cash-account-unmapped", "Warning", "Remittance row has no mapped cash account.", row.RowId, "cashAccountId"));
            }

            lock (_gate)
            {
                if (_importedRowHashes.Contains(row.RowHash))
                {
                    status = ServicerStatementRowStatus.Duplicate;
                    rowIssues.Add(Issue("servicer-statement.duplicate-row", "Error", "The same servicer row hash has already been imported.", row.RowId, "rowHash"));
                }
            }

            var securityId = contract?.CurrentTerms.SecurityMasterReference?.SecurityId ?? row.SecurityId;
            var symbol = contract?.CurrentTerms.SecurityMasterReference?.Symbol ?? row.SecuritySymbol;
            var normalized = row with
            {
                Status = status,
                LoanId = loanId,
                SecurityId = securityId,
                SecuritySymbol = symbol,
                FundAccountId = row.FundAccountId ?? request.FundAccountId,
                CashAccountId = row.CashAccountId ?? request.CashAccountId,
                StatementDate = row.StatementDate ?? request.StatementDate,
                Issues = rowIssues,
                EvidenceRoute = row.EvidenceRoute
            };
            mappedRows.Add(normalized);
            issues.AddRange(rowIssues);
        }

        return BuildPreview(null, request, ComputeHash(request.RawPayload ?? JsonSerializer.Serialize(request.Rows ?? [], JsonOptions)), mappedRows, issues);
    }

    public async Task<ServicerStatementImportResultDto> ImportAsync(ServicerStatementImportRequestDto request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        var preview = await PreviewAsync(request, ct).ConfigureAwait(false);
        if (preview.Issues.Any(IsHardIssue))
        {
            return new ServicerStatementImportResultDto(Guid.Empty, "Rejected", string.Empty, preview);
        }

        var batch = await _directLendingService.CreateServicerReportBatchAsync(
            ToServicerReportBatchRequest(request, preview),
            metadata,
            ct).ConfigureAwait(false);

        var evidenceRoute = $"/api/loans/servicer-statements/{batch.ServicerReportBatchId:D}";
        var rows = preview.Rows
            .Select(row => row with { EvidenceRoute = $"{evidenceRoute}/rows#row-{Uri.EscapeDataString(row.RowId)}" })
            .ToArray();
        var imported = BuildPreview(batch.ServicerReportBatchId, request, preview.RawPayloadHash, rows, preview.Issues) with
        {
            Status = "Imported",
            EvidenceRoute = evidenceRoute
        };

        lock (_gate)
        {
            _batches[batch.ServicerReportBatchId] = imported;
            foreach (var row in rows)
            {
                _importedRowHashes.Add(row.RowHash);
            }
        }

        return new ServicerStatementImportResultDto(batch.ServicerReportBatchId, "Imported", evidenceRoute, imported);
    }

    public Task<ServicerStatementPreviewDto?> GetBatchAsync(Guid batchId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_batches.TryGetValue(batchId, out var batch) ? batch : null);
        }
    }

    public Task<IReadOnlyList<ServicerStatementRowDto>> GetRowsAsync(Guid batchId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ServicerStatementRowDto>>(_batches.TryGetValue(batchId, out var batch) ? batch.Rows : []);
        }
    }

    public Task<IReadOnlyList<ServicerStatementPreviewDto>> ListBatchesAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ServicerStatementPreviewDto>>(_batches.Values.OrderByDescending(static batch => batch.StatementDate).ThenBy(static batch => batch.ServicerName, StringComparer.OrdinalIgnoreCase).ToArray());
        }
    }

    public async Task<ServicerStatementApplyResultDto> ApplyAsync(Guid batchId, ServicerStatementApplyRequestDto request, DirectLendingCommandMetadataDto? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ServicerStatementPreviewDto? batch;
        lock (_gate)
        {
            _batches.TryGetValue(batchId, out batch);
        }

        if (batch is null)
        {
            return new ServicerStatementApplyResultDto(batchId, request.RowIds.Count, 0, request.RowIds.Count, [], [Issue("servicer-statement.batch-not-found", "Error", "Servicer statement batch was not found.")], "NotFound");
        }

        var selected = request.RowIds.Count == 0
            ? batch.Rows.Where(static row => row.Status == ServicerStatementRowStatus.ReadyToApply).ToArray()
            : batch.Rows.Where(row => request.RowIds.Contains(row.RowId, StringComparer.Ordinal)).ToArray();
        var issues = new List<ServicerStatementValidationIssueDto>();
        var applied = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in selected)
        {
            ct.ThrowIfCancellationRequested();
            if (row.LoanId is not Guid loanId)
            {
                issues.Add(Issue("servicer-statement.apply-unmapped", "Error", "Cannot apply an unmapped row.", row.RowId, "loanId"));
                continue;
            }

            if (row.Issues?.Any(IsHardIssue) == true || row.Status is ServicerStatementRowStatus.Unmapped or ServicerStatementRowStatus.Duplicate or ServicerStatementRowStatus.Rejected)
            {
                issues.Add(Issue("servicer-statement.apply-blocked", "Error", "Cannot apply a row with hard validation issues.", row.RowId));
                continue;
            }

            var mode = request.DefaultMode ?? row.SuggestedApplyMode ?? InferApplyMode(row);
            try
            {
                var result = mode switch
                {
                    ServicerStatementApplyMode.ApplyPrincipalPayment => await _directLendingService.ApplyPrincipalPaymentAsync(
                        loanId,
                        new ApplyPrincipalPaymentRequest(row.PrincipalAmount ?? row.GrossAmount ?? 0m, row.EffectiveDate ?? row.StatementDate ?? batch.StatementDate, row.ExternalRef),
                        metadata,
                        ct).ConfigureAwait(false),
                    ServicerStatementApplyMode.ApplyMixedPayment => await _directLendingService.ApplyMixedPaymentAsync(
                        loanId,
                        new ApplyMixedPaymentRequest(
                            row.GrossAmount ?? 0m,
                            row.EffectiveDate ?? row.StatementDate ?? batch.StatementDate,
                            new PaymentBreakdownDto(row.InterestAmount ?? 0m, 0m, row.FeeAmount ?? 0m, row.PenaltyAmount ?? 0m, row.PrincipalAmount ?? 0m),
                            row.ExternalRef),
                        metadata,
                        ct).ConfigureAwait(false),
                    ServicerStatementApplyMode.AssessFee => await _directLendingService.AssessFeeAsync(
                        loanId,
                        new AssessFeeRequest("Servicer fee", row.FeeAmount ?? row.GrossAmount ?? 0m, row.EffectiveDate ?? row.StatementDate ?? batch.StatementDate, row.ExternalRef),
                        metadata,
                        ct).ConfigureAwait(false),
                    ServicerStatementApplyMode.ChargePenalty => await _directLendingService.ChargePrepaymentPenaltyAsync(
                        loanId,
                        new ChargePrepaymentPenaltyRequest(row.PenaltyAmount ?? row.GrossAmount ?? 0m, row.EffectiveDate ?? row.StatementDate ?? batch.StatementDate, row.ExternalRef),
                        metadata,
                        ct).ConfigureAwait(false),
                    _ => null
                };

                if (result is null)
                {
                    issues.Add(Issue("servicer-statement.apply-mode-unsupported", "Warning", $"Apply mode '{mode}' is retained as evidence only in this slice.", row.RowId));
                    continue;
                }

                applied.Add(row.RowId);
            }
            catch (DirectLendingCommandException ex)
            {
                issues.Add(Issue("servicer-statement.apply-command-failed", "Error", ex.Message, row.RowId));
            }
        }

        var rows = batch.Rows
            .Select(row => applied.Contains(row.RowId) ? row with { Status = ServicerStatementRowStatus.Applied } : row)
            .ToArray();
        var updated = batch with
        {
            Rows = rows,
            AppliedRowCount = rows.Count(static row => row.Status == ServicerStatementRowStatus.Applied),
            Status = issues.Any(IsHardIssue) ? "ReviewRequired" : "Applied"
        };
        lock (_gate)
        {
            _batches[batchId] = updated;
        }

        return new ServicerStatementApplyResultDto(batchId, selected.Length, applied.Count, selected.Length - applied.Count, rows, issues, updated.Status);
    }

    private async Task<List<ServicerStatementRowDto>> NormalizeRowsAsync(ServicerStatementImportRequestDto request, CancellationToken ct)
    {
        if (request.Rows is { Count: > 0 })
        {
            return request.Rows.Select((row, index) => NormalizeRow(request, row, index + 1)).ToList();
        }

        if (string.IsNullOrWhiteSpace(request.RawPayload))
        {
            return [];
        }

        if (request.SourceFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            var parsed = JsonSerializer.Deserialize<IReadOnlyList<ServicerStatementRowDto>>(request.RawPayload, JsonOptions) ?? [];
            return parsed.Select((row, index) => NormalizeRow(request, row, index + 1)).ToList();
        }

        await Task.Yield();
        return ParseCsv(request);
    }

    private static List<ServicerStatementRowDto> ParseCsv(ServicerStatementImportRequestDto request)
    {
        var lines = (request.RawPayload ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2)
        {
            return [];
        }

        var headers = SplitCsvLine(lines[0]).Select(static h => h.Trim()).ToArray();
        var rows = new List<ServicerStatementRowDto>();
        for (var i = 1; i < lines.Length; i++)
        {
            var values = SplitCsvLine(lines[i]);
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var j = 0; j < headers.Length && j < values.Count; j++)
            {
                fields[headers[j]] = values[j].Trim();
            }

            var rowKind = ParseKind(Get(fields, "kind"), request.Kind);
            var rowHash = ComputeHash(lines[i]);
            var rawJson = JsonSerializer.Serialize(fields, JsonOptions);
            rows.Add(new ServicerStatementRowDto(
                RowId: Get(fields, "rowId") ?? (i).ToString(CultureInfo.InvariantCulture),
                Kind: rowKind,
                RowNumber: i,
                Status: ServicerStatementRowStatus.Mapped,
                LoanId: ParseGuid(Get(fields, "loanId")),
                SecurityId: ParseGuid(Get(fields, "securityId")),
                SecuritySymbol: Get(fields, "securitySymbol") ?? Get(fields, "symbol"),
                ExternalLoanReference: Get(fields, "externalLoanReference") ?? Get(fields, "externalRef"),
                ServicerLoanNumber: Get(fields, "servicerLoanNumber"),
                BorrowerReference: Get(fields, "borrowerReference") ?? Get(fields, "borrower"),
                FundAccountId: Get(fields, "fundAccountId"),
                CashAccountId: Get(fields, "cashAccountId"),
                StatementDate: ParseDate(Get(fields, "statementDate")) ?? request.StatementDate,
                EffectiveDate: ParseDate(Get(fields, "effectiveDate")),
                SettlementDate: ParseDate(Get(fields, "settlementDate")),
                PrincipalOutstanding: ParseDecimal(Get(fields, "principalOutstanding")),
                InterestAccruedUnpaid: ParseDecimal(Get(fields, "interestAccruedUnpaid")),
                FeesAccruedUnpaid: ParseDecimal(Get(fields, "feesAccruedUnpaid")),
                PenaltyAccruedUnpaid: ParseDecimal(Get(fields, "penaltyAccruedUnpaid")),
                CommitmentAvailable: ParseDecimal(Get(fields, "commitmentAvailable")),
                GrossAmount: ParseDecimal(Get(fields, "grossAmount")) ?? ParseDecimal(Get(fields, "amount")),
                PrincipalAmount: ParseDecimal(Get(fields, "principalAmount")),
                InterestAmount: ParseDecimal(Get(fields, "interestAmount")),
                FeeAmount: ParseDecimal(Get(fields, "feeAmount")),
                PenaltyAmount: ParseDecimal(Get(fields, "penaltyAmount")),
                Currency: ParseCurrency(Get(fields, "currency")),
                ExternalRef: Get(fields, "externalRef") ?? Get(fields, "paymentRef"),
                RowHash: rowHash,
                RawPayloadJson: rawJson,
                SuggestedApplyMode: ParseApplyMode(Get(fields, "applyMode")),
                Issues: []));
        }

        return rows;
    }

    private static ServicerStatementRowDto NormalizeRow(ServicerStatementImportRequestDto request, ServicerStatementRowDto row, int rowNumber)
    {
        var rawPayload = string.IsNullOrWhiteSpace(row.RawPayloadJson)
            ? JsonSerializer.Serialize(row, JsonOptions)
            : row.RawPayloadJson;
        return row with
        {
            Kind = request.Kind == ServicerStatementKind.Mixed ? row.Kind : request.Kind,
            RowNumber = row.RowNumber > 0 ? row.RowNumber : rowNumber,
            RowId = string.IsNullOrWhiteSpace(row.RowId) ? rowNumber.ToString(CultureInfo.InvariantCulture) : row.RowId,
            Status = row.Status == ServicerStatementRowStatus.Applied ? row.Status : ServicerStatementRowStatus.Mapped,
            StatementDate = row.StatementDate ?? request.StatementDate,
            FundAccountId = row.FundAccountId ?? request.FundAccountId,
            CashAccountId = row.CashAccountId ?? request.CashAccountId,
            RowHash = string.IsNullOrWhiteSpace(row.RowHash) ? ComputeHash(rawPayload) : row.RowHash,
            RawPayloadJson = rawPayload,
            Issues = row.Issues ?? []
        };
    }

    private static ServicerStatementPreviewDto BuildPreview(Guid? batchId, ServicerStatementImportRequestDto request, string rawPayloadHash, IReadOnlyList<ServicerStatementRowDto> rows, IReadOnlyList<ServicerStatementValidationIssueDto> issues)
    {
        var mapped = rows.Count(static row => row.Status is ServicerStatementRowStatus.Mapped or ServicerStatementRowStatus.ReadyToApply or ServicerStatementRowStatus.Applied);
        var hardIssueCount = issues.Count(IsHardIssue);
        var status = hardIssueCount > 0
            ? "ReviewRequired"
            : rows.Count == 0
                ? "Empty"
                : "Ready";
        var suggestions = new List<string>();
        if (rows.Any(static row => row.Status == ServicerStatementRowStatus.Unmapped))
        {
            suggestions.Add("Map unmapped rows to Direct Lending loans or Security Master symbols before import.");
        }
        if (rows.Any(static row => row.Status == ServicerStatementRowStatus.ReadyToApply))
        {
            suggestions.Add("Review ready remittance rows and apply selected accepted rows.");
        }
        if (issues.Any(static issue => issue.IssueCode.Contains("fund-account", StringComparison.OrdinalIgnoreCase)))
        {
            suggestions.Add("Attach fund account evidence before close review.");
        }

        return new ServicerStatementPreviewDto(
            batchId,
            request.Kind,
            request.ServicerName,
            request.StatementDate,
            request.SourceFormat,
            request.SourceFileName,
            rawPayloadHash,
            rows.Count,
            mapped,
            rows.Count(static row => row.Status == ServicerStatementRowStatus.Unmapped),
            rows.Count(static row => row.Status == ServicerStatementRowStatus.Duplicate),
            rows.Count(static row => row.Status == ServicerStatementRowStatus.ReadyToApply),
            rows.Count(static row => row.Status == ServicerStatementRowStatus.Applied),
            status,
            rows,
            issues,
            suggestions,
            batchId is Guid id ? $"/api/loans/servicer-statements/{id:D}" : null,
            request.StatementRunId);
    }

    private static CreateServicerReportBatchRequest ToServicerReportBatchRequest(ServicerStatementImportRequestDto request, ServicerStatementPreviewDto preview)
    {
        var positionLines = preview.Rows
            .Where(static row => row.Kind is ServicerStatementKind.Position or ServicerStatementKind.Mixed)
            .Select(row => new ServicerPositionReportLineImportDto(
                row.LoanId!.Value,
                row.PrincipalOutstanding,
                row.InterestAccruedUnpaid,
                row.FeesAccruedUnpaid,
                row.PenaltyAccruedUnpaid,
                row.CommitmentAvailable,
                null,
                null,
                null,
                row.RawPayloadJson))
            .ToArray();
        var transactionLines = preview.Rows
            .Where(static row => row.Kind is ServicerStatementKind.Remittance or ServicerStatementKind.Mixed)
            .Select(row => new ServicerTransactionReportLineImportDto(
                row.LoanId!.Value,
                row.SuggestedApplyMode?.ToString() ?? "ServicerRemittance",
                row.EffectiveDate ?? row.StatementDate ?? request.StatementDate,
                row.EffectiveDate,
                row.SettlementDate,
                row.GrossAmount ?? 0m,
                row.PrincipalAmount,
                row.InterestAmount,
                row.FeeAmount,
                row.PenaltyAmount,
                row.Currency,
                row.ExternalRef,
                row.RawPayloadJson))
            .ToArray();

        return new CreateServicerReportBatchRequest(
            request.ServicerName,
            request.Kind.ToString(),
            request.SourceFormat,
            request.StatementDate,
            request.SourceFileName,
            preview.RawPayloadHash,
            request.Notes,
            positionLines,
            transactionLines);
    }

    private static Guid? ResolveLoanId(ServicerStatementRowDto row, IReadOnlyDictionary<Guid, LoanContractDetailDto> contractByLoan, IReadOnlyDictionary<string, Guid> loanBySymbol, IReadOnlyDictionary<string, Guid> loanByBorrower, IReadOnlyDictionary<string, Guid> loanByFacility)
    {
        if (row.LoanId is Guid rowLoanId && contractByLoan.ContainsKey(rowLoanId))
        {
            return rowLoanId;
        }
        if (!string.IsNullOrWhiteSpace(row.SecuritySymbol) && loanBySymbol.TryGetValue(row.SecuritySymbol, out var symbolLoanId))
        {
            return symbolLoanId;
        }
        if (!string.IsNullOrWhiteSpace(row.BorrowerReference) && loanByBorrower.TryGetValue(row.BorrowerReference, out var borrowerLoanId))
        {
            return borrowerLoanId;
        }
        if (!string.IsNullOrWhiteSpace(row.ExternalLoanReference) && loanByFacility.TryGetValue(row.ExternalLoanReference, out var facilityLoanId))
        {
            return facilityLoanId;
        }
        return null;
    }

    private static ServicerStatementApplyMode InferApplyMode(ServicerStatementRowDto row)
        => (row.InterestAmount ?? 0m) > 0m || (row.FeeAmount ?? 0m) > 0m || (row.PenaltyAmount ?? 0m) > 0m
            ? ServicerStatementApplyMode.ApplyMixedPayment
            : ServicerStatementApplyMode.ApplyPrincipalPayment;

    private static ServicerStatementValidationIssueDto Issue(string code, string severity, string message, string? rowId = null, string? field = null)
        => new(code, severity, message, rowId, field);

    private static bool IsHardIssue(ServicerStatementValidationIssueDto issue)
        => issue.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase) ||
           issue.Severity.Equals("Critical", StringComparison.OrdinalIgnoreCase);

    private static string ComputeHash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static List<string> SplitCsvLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (ch == ',' && !quoted)
            {
                values.Add(current.ToString());
                current.Clear();
                continue;
            }
            current.Append(ch);
        }
        values.Add(current.ToString());
        return values;
    }

    private static string? Get(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static Guid? ParseGuid(string? value)
        => Guid.TryParse(value, out var parsed) ? parsed : null;

    private static decimal? ParseDecimal(string? value)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static DateOnly? ParseDate(string? value)
        => DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;

    private static CurrencyCode? ParseCurrency(string? value)
        => Enum.TryParse<CurrencyCode>(value, ignoreCase: true, out var parsed) ? parsed : null;

    private static ServicerStatementKind ParseKind(string? value, ServicerStatementKind fallback)
        => Enum.TryParse<ServicerStatementKind>(value, ignoreCase: true, out var parsed) ? parsed : fallback;

    private static ServicerStatementApplyMode? ParseApplyMode(string? value)
        => Enum.TryParse<ServicerStatementApplyMode>(value, ignoreCase: true, out var parsed) ? parsed : null;
}
