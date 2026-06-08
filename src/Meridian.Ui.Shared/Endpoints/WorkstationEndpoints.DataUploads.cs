using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private const int DataUploadMaxPreviewRows = 10;
    private const long DataUploadMaxFileBytes = 5 * 1024 * 1024;
    private const string DataUploadRootEnvironmentVariable = "MERIDIAN_DATA_UPLOAD_ROOT";

    private static void MapDataUploadEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationDataUploadTemplates), () =>
        {
            return Results.Json(BuildDataUploadTemplateCatalog(), jsonOptions);
        })
        .WithName("GetWorkstationDataUploadTemplates")
        .Produces<DataUploadTemplateCatalogDto>(200);

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationDataUploadPreview), async (
            HttpContext context,
            HttpRequest request) =>
        {
            if (!HasOperationsContinuityMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!TryResolveCurrentUser(context, out var currentUser))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!request.HasFormContentType)
            {
                return MissingDataUploadPayload("contentType", "Upload preview requires multipart/form-data.");
            }

            var form = await request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
            var templateId = form["templateId"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(templateId))
            {
                return MissingDataUploadPayload("templateId", "Choose a data upload template before previewing a source file.");
            }

            var catalog = BuildDataUploadTemplateCatalog();
            var template = catalog.Templates.FirstOrDefault(candidate =>
                string.Equals(candidate.TemplateId, templateId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (template is null)
            {
                return MissingDataUploadPayload("templateId", $"Template '{templateId}' is not a supported data upload template.");
            }

            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
            {
                return MissingDataUploadPayload("file", "Choose a non-empty CSV source file before previewing.");
            }

            if (file.Length > DataUploadMaxFileBytes)
            {
                return MissingDataUploadPayload(
                    "file",
                    $"Upload preview accepts files up to {FormatBytes(DataUploadMaxFileBytes)}.");
            }

            if (!HasAcceptedDataUploadExtension(file.FileName, catalog.AcceptedFileExtensions))
            {
                return MissingDataUploadPayload(
                    "file",
                    $"Upload preview accepts {string.Join(", ", catalog.AcceptedFileExtensions)} files.");
            }

            byte[] fileBytes;
            await using (var stream = file.OpenReadStream())
            using (var buffer = new MemoryStream())
            {
                await stream.CopyToAsync(buffer, context.RequestAborted).ConfigureAwait(false);
                fileBytes = buffer.ToArray();
            }

            var uploadId = BuildDataUploadId(fileBytes);
            var safeFileName = SanitizeDataUploadFileName(file.FileName);
            var relativePath = Path.Combine("workstation", "data-uploads", uploadId, safeFileName)
                .Replace(Path.DirectorySeparatorChar, '/');
            var retainedRoot = ResolveDataUploadRoot();
            var retainedDirectory = Path.Combine(retainedRoot, uploadId);
            Directory.CreateDirectory(retainedDirectory);
            await File
                .WriteAllBytesAsync(Path.Combine(retainedDirectory, safeFileName), fileBytes, context.RequestAborted)
                .ConfigureAwait(false);

            var parsed = ParseDataUploadCsv(fileBytes, template, catalog.MaxPreviewRows);
            var status = parsed.Issues.Any(static issue => string.Equals(issue.Severity, "Error", StringComparison.OrdinalIgnoreCase))
                ? "NeedsSchemaRepair"
                : "ReadyForReview";
            var nextAction = status == "ReadyForReview"
                ? "Review the preview rows, then route this retained source file into validation and reconciliation."
                : "Repair the template headers or required fields, then upload the corrected source file again.";

            var response = new DataUploadPreviewResultDto(
                UploadId: uploadId,
                TemplateId: template.TemplateId,
                TemplateLabel: template.Label,
                FileName: safeFileName,
                FileSizeBytes: file.Length,
                ContentType: string.IsNullOrWhiteSpace(file.ContentType) ? template.ContentType : file.ContentType,
                UploadedBy: currentUser,
                UploadedAtUtc: DateTimeOffset.UtcNow,
                RetainedPath: relativePath,
                ParsedRowCount: parsed.ParsedRowCount,
                PreviewRowCount: parsed.PreviewRows.Count,
                Headers: parsed.Headers,
                PreviewRows: parsed.PreviewRows,
                Issues: parsed.Issues,
                Status: status,
                NextAction: nextAction);

            return Results.Json(response, jsonOptions);
        })
        .WithName("PreviewWorkstationDataUpload")
        .Produces<DataUploadPreviewResultDto>(200)
        .ProducesValidationProblem()
        .Produces(403);

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationBankStatementImport), async (
            HttpContext context,
            HttpRequest request) =>
        {
            if (!HasOperationsContinuityMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!TryResolveCurrentUser(context, out var currentUser))
            {
                return EndpointHelpers.Forbidden();
            }

            var fundAccountService = context.RequestServices.GetService<IFundAccountService>();
            if (fundAccountService is null)
            {
                return Results.Problem(
                    "Fund account services are not registered for bank statement import.",
                    statusCode: StatusCodes.Status501NotImplemented);
            }

            if (!request.HasFormContentType)
            {
                return MissingDataUploadPayload("contentType", "Bank statement import requires multipart/form-data.");
            }

            var form = await request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
            var accountIdValue = form["accountId"].FirstOrDefault();
            if (!Guid.TryParse(accountIdValue, out var accountId))
            {
                return MissingDataUploadPayload("accountId", "Choose a bank account before importing a bank statement.");
            }

            var bankName = form["bankName"].FirstOrDefault()?.Trim();
            if (string.IsNullOrWhiteSpace(bankName))
            {
                return MissingDataUploadPayload("bankName", "Bank statement import requires the source bank name.");
            }

            DateOnly? statementDate = null;
            var statementDateValue = form["statementDate"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(statementDateValue))
            {
                if (!TryParseDataUploadDate(statementDateValue, out var parsedStatementDate))
                {
                    return MissingDataUploadPayload("statementDate", "Statement date must use YYYY-MM-DD format.");
                }

                statementDate = parsedStatementDate;
            }

            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
            {
                return MissingDataUploadPayload("file", "Choose a non-empty CSV bank statement before importing.");
            }

            if (file.Length > DataUploadMaxFileBytes)
            {
                return MissingDataUploadPayload(
                    "file",
                    $"Bank statement import accepts files up to {FormatBytes(DataUploadMaxFileBytes)}.");
            }

            if (!HasAcceptedDataUploadExtension(file.FileName, [".csv"]))
            {
                return MissingDataUploadPayload("file", "Bank statement import accepts .csv files.");
            }

            var account = await fundAccountService.GetAccountAsync(accountId, context.RequestAborted).ConfigureAwait(false);
            if (account is null)
            {
                return MissingDataUploadPayload("accountId", $"Bank account '{accountId:D}' was not found.");
            }

            if (account.AccountType != AccountTypeDto.Bank)
            {
                return MissingDataUploadPayload("accountId", "Bank statement import can only target bank accounts.");
            }

            byte[] fileBytes;
            await using (var stream = file.OpenReadStream())
            using (var buffer = new MemoryStream())
            {
                await stream.CopyToAsync(buffer, context.RequestAborted).ConfigureAwait(false);
                fileBytes = buffer.ToArray();
            }

            var batchId = BankStatementCsvImportMapper.BuildBatchId(fileBytes, accountId, bankName);
            var parsed = BankStatementCsvImportMapper.Parse(fileBytes, accountId, batchId);
            if (parsed.Issues.Any(static issue => string.Equals(issue.Severity, "Error", StringComparison.OrdinalIgnoreCase)))
            {
                return DataUploadValidationProblem(parsed.Issues);
            }

            if (parsed.Lines.Count == 0)
            {
                return MissingDataUploadPayload("rows", "The bank statement CSV must contain at least one transaction row.");
            }

            var uploadId = BuildDataUploadId(fileBytes);
            var safeFileName = SanitizeDataUploadFileName(file.FileName);
            var relativePath = Path.Combine("workstation", "data-uploads", uploadId, safeFileName)
                .Replace(Path.DirectorySeparatorChar, '/');
            var retainedRoot = ResolveDataUploadRoot();
            var retainedDirectory = Path.Combine(retainedRoot, uploadId);
            Directory.CreateDirectory(retainedDirectory);
            await File
                .WriteAllBytesAsync(Path.Combine(retainedDirectory, safeFileName), fileBytes, context.RequestAborted)
                .ConfigureAwait(false);

            var effectiveStatementDate = statementDate ?? parsed.StatementDate!.Value;
            var notes = form["notes"].FirstOrDefault();
            var isBackfill = bool.TryParse(form["isBackfill"].FirstOrDefault(), out var parsedBackfill) && parsedBackfill;
            var batch = await fundAccountService
                .IngestBankStatementAsync(
                    new IngestBankStatementRequest(
                        batchId,
                        accountId,
                        effectiveStatementDate,
                        bankName,
                        BuildBankStatementImportNotes(notes, relativePath),
                        parsed.Lines,
                        currentUser,
                        isBackfill),
                    context.RequestAborted)
                .ConfigureAwait(false);

            var response = new BankStatementImportResultDto(
                UploadId: uploadId,
                BatchId: batch.BatchId,
                AccountId: batch.AccountId,
                AccountCode: account.AccountCode,
                BankName: batch.BankName,
                StatementDate: batch.StatementDate,
                FileName: safeFileName,
                FileSizeBytes: file.Length,
                ImportedBy: batch.LoadedBy,
                ImportedAtUtc: batch.IngestedAt,
                RetainedPath: relativePath,
                LineCount: batch.LineCount,
                Issues: parsed.Issues,
                Status: "Imported",
                NextAction: "Use the retained bank evidence in reconciliation; Meridian-owned ledger posting still requires approval.");

            return Results.Json(response, jsonOptions);
        })
        .WithName("ImportWorkstationBankStatement")
        .Produces<BankStatementImportResultDto>(200)
        .ProducesValidationProblem()
        .Produces(403)
        .Produces(501);
    }

    private static DataUploadTemplateCatalogDto BuildDataUploadTemplateCatalog()
        => new(
            Templates:
            [
                new DataUploadTemplateDto(
                    TemplateId: "trade-data",
                    Label: "Trade data",
                    Description: "Executions or fills that should enter validation before paper or accounting workflows consume them.",
                    DataDomain: "Trading",
                    TargetWorkflow: "Import -> Validate -> Reconcile -> Approve -> Report",
                    FileName: "meridian-trade-data-template.csv",
                    ContentType: "text/csv",
                    HeaderLine: "trade_id,trade_date,account_code,symbol,side,quantity,price,currency,strategy_id,source_system,source_document_id",
                    Fields:
                    [
                        RequiredUploadField("trade_id", "Trade ID", "TRD-10001", "Stable trade or fill identifier from the source system."),
                        RequiredUploadField("trade_date", "Trade date", "2026-06-01", "Trade date in YYYY-MM-DD format."),
                        RequiredUploadField("account_code", "Account code", "FUND-A-IBKR", "Meridian or broker account code used for reconciliation."),
                        RequiredUploadField("symbol", "Symbol", "AAPL", "Ticker, instrument symbol, or source security code."),
                        RequiredUploadField("side", "Side", "Buy", "Buy, Sell, SellShort, Cover, or source-equivalent side."),
                        RequiredUploadField("quantity", "Quantity", "100", "Signed or side-adjusted trade quantity."),
                        RequiredUploadField("price", "Price", "187.25", "Execution price in trade currency."),
                        OptionalUploadField("currency", "Currency", "USD", "ISO currency for the trade price."),
                        OptionalUploadField("strategy_id", "Strategy ID", "income-core", "Strategy or sleeve that produced the trade."),
                        OptionalUploadField("source_system", "Source system", "Interactive Brokers", "Originating broker, OMS, or import source."),
                        OptionalUploadField("source_document_id", "Source document ID", "confirm-20260601-001", "Retained statement, confirmation, or file identifier.")
                    ],
                    SampleRows:
                    [
                        "TRD-10001,2026-06-01,FUND-A-IBKR,AAPL,Buy,100,187.25,USD,income-core,Interactive Brokers,confirm-20260601-001"
                    ],
                    ValidationNotes:
                    [
                        "Rows remain preview-only until validation and reconciliation approve downstream use.",
                        "Include source_system and source_document_id whenever a retained broker file or confirmation exists."
                    ]),
                new DataUploadTemplateDto(
                    TemplateId: "transaction-data",
                    Label: "Transaction data",
                    Description: "Cash, dividend, fee, interest, transfer, or adjustment activity used as retained source evidence.",
                    DataDomain: "Transactions",
                    TargetWorkflow: "Import -> Validate -> Reconcile -> Approve -> Report",
                    FileName: "meridian-transaction-data-template.csv",
                    ContentType: "text/csv",
                    HeaderLine: "transaction_id,effective_date,account_code,transaction_type,amount,currency,security_id,counterparty,source_system,source_document_id",
                    Fields:
                    [
                        RequiredUploadField("transaction_id", "Transaction ID", "TXN-20001", "Stable source transaction identifier."),
                        RequiredUploadField("effective_date", "Effective date", "2026-06-01", "Accounting or settlement effective date in YYYY-MM-DD format."),
                        RequiredUploadField("account_code", "Account code", "FUND-A-CASH", "Cash or investment account code."),
                        RequiredUploadField("transaction_type", "Transaction type", "Dividend", "Dividend, Fee, Interest, Transfer, Deposit, Withdrawal, or Adjustment."),
                        RequiredUploadField("amount", "Amount", "1250.50", "Signed transaction amount in transaction currency."),
                        OptionalUploadField("currency", "Currency", "USD", "ISO currency for the transaction amount."),
                        OptionalUploadField("security_id", "Security ID", "AAPL", "Security identifier when activity is instrument-specific."),
                        OptionalUploadField("counterparty", "Counterparty", "Broker", "Broker, custodian, bank, borrower, or transfer counterparty."),
                        OptionalUploadField("source_system", "Source system", "Custodian Portal", "Originating bank, broker, custodian, or GL evidence source."),
                        OptionalUploadField("source_document_id", "Source document ID", "stmt-202606", "Retained statement, notice, or file identifier.")
                    ],
                    SampleRows:
                    [
                        "TXN-20001,2026-06-01,FUND-A-CASH,Dividend,1250.50,USD,AAPL,Broker,Custodian Portal,stmt-202606"
                    ],
                    ValidationNotes:
                    [
                        "Transaction uploads become evidence for reconciliation; Meridian-owned ledgers remain authoritative.",
                        "Use signed amounts consistently so validation can compare source activity against ledger candidates."
                    ]),
                new DataUploadTemplateDto(
                    TemplateId: "bank-statement",
                    Label: "Bank statement",
                    Description: "Bank transaction activity imported as retained account evidence for reconciliation.",
                    DataDomain: "Accounting",
                    TargetWorkflow: "Import -> Reconcile -> Approve -> Report",
                    FileName: "meridian-bank-statement-template.csv",
                    ContentType: "text/csv",
                    HeaderLine: "transaction_date,value_date,amount,currency,transaction_type,description,reference,closing_balance",
                    Fields:
                    [
                        RequiredUploadField("transaction_date", "Transaction date", "2026-06-01", "Posted transaction date in YYYY-MM-DD format."),
                        OptionalUploadField("value_date", "Value date", "2026-06-03", "Bank value date in YYYY-MM-DD format; defaults to transaction_date when blank."),
                        RequiredUploadField("amount", "Amount", "-50000.00", "Signed bank transaction amount in account currency."),
                        RequiredUploadField("currency", "Currency", "USD", "ISO currency for the bank transaction."),
                        RequiredUploadField("transaction_type", "Transaction type", "Wire", "Wire, ACH, Deposit, Withdrawal, Fee, Interest, Transfer, or source-equivalent type."),
                        RequiredUploadField("description", "Description", "Payment to broker", "Human-readable bank statement narrative."),
                        OptionalUploadField("reference", "Reference", "WIRE-20260601-001", "Bank reference, check number, or source transaction identifier."),
                        OptionalUploadField("closing_balance", "Closing balance", "950000.00", "Running or closing balance supplied by the bank for this line.")
                    ],
                    SampleRows:
                    [
                        "2026-06-01,2026-06-03,-50000.00,USD,Wire,Payment to broker,WIRE-20260601-001,950000.00"
                    ],
                    ValidationNotes:
                    [
                        "Bank statement imports are evidence for account reconciliation; Meridian ledgers remain authoritative until approved posting.",
                        "Use the bank-statement import endpoint when the CSV should be applied to a bank account instead of previewed only."
                    ]),
                new DataUploadTemplateDto(
                    TemplateId: "asset-information",
                    Label: "Asset information",
                    Description: "Security Master candidate facts and instrument metadata for operator review.",
                    DataDomain: "Security Master",
                    TargetWorkflow: "Import -> Validate -> Reconcile -> Approve -> Report",
                    FileName: "meridian-asset-information-template.csv",
                    ContentType: "text/csv",
                    HeaderLine: "asset_id,symbol,asset_name,asset_class,issuer,currency,country,maturity_date,coupon_rate,source_system",
                    Fields:
                    [
                        RequiredUploadField("asset_id", "Asset ID", "AST-30001", "Stable internal or source security identifier."),
                        RequiredUploadField("symbol", "Symbol", "91282CHT1", "Ticker, CUSIP, ISIN, FIGI, or source symbol."),
                        RequiredUploadField("asset_name", "Asset name", "US Treasury 4.25 2034", "Human-readable instrument name."),
                        RequiredUploadField("asset_class", "Asset class", "Bond", "Equity, Bond, Fund, Future, Option, FX, Crypto, Loan, or custom class."),
                        OptionalUploadField("issuer", "Issuer", "US Treasury", "Issuer, borrower, fund sponsor, or reference entity."),
                        OptionalUploadField("currency", "Currency", "USD", "Primary denomination currency."),
                        OptionalUploadField("country", "Country", "US", "ISO country or jurisdiction code."),
                        OptionalUploadField("maturity_date", "Maturity date", "2034-05-15", "Maturity date for dated instruments."),
                        OptionalUploadField("coupon_rate", "Coupon rate", "4.25", "Coupon or stated rate when applicable."),
                        OptionalUploadField("source_system", "Source system", "Custodian Security Master", "Reference-data provider or retained source.")
                    ],
                    SampleRows:
                    [
                        "AST-30001,91282CHT1,US Treasury 4.25 2034,Bond,US Treasury,USD,US,2034-05-15,4.25,Custodian Security Master"
                    ],
                    ValidationNotes:
                    [
                        "Asset information uploads are candidate facts and should not override governed Security Master approvals.",
                        "Include durable source identifiers so conflicting facts can be traced during review."
                    ]),
                new DataUploadTemplateDto(
                    TemplateId: "entity-configuration",
                    Label: "Entity configuration",
                    Description: "Fund, vehicle, sleeve, legal entity, or account structure candidates for governance review.",
                    DataDomain: "Entity setup",
                    TargetWorkflow: "Import -> Validate -> Reconcile -> Approve -> Report",
                    FileName: "meridian-entity-configuration-template.csv",
                    ContentType: "text/csv",
                    HeaderLine: "entity_id,entity_name,entity_type,parent_entity_id,base_currency,jurisdiction,ownership_percent,effective_from,source_system",
                    Fields:
                    [
                        RequiredUploadField("entity_id", "Entity ID", "ENT-40001", "Stable entity, fund, vehicle, sleeve, or account identifier."),
                        RequiredUploadField("entity_name", "Entity name", "Northwind Income Fund LP", "Display name used by operators and reports."),
                        RequiredUploadField("entity_type", "Entity type", "Fund", "Fund, Vehicle, Sleeve, LegalEntity, Account, or Portfolio."),
                        OptionalUploadField("parent_entity_id", "Parent entity ID", "ENT-40000", "Parent structure identifier when a hierarchy is known."),
                        OptionalUploadField("base_currency", "Base currency", "USD", "Functional or reporting currency."),
                        OptionalUploadField("jurisdiction", "Jurisdiction", "DE", "Jurisdiction, domicile, or registration location."),
                        OptionalUploadField("ownership_percent", "Ownership percent", "100", "Ownership percentage when this row defines a relationship."),
                        OptionalUploadField("effective_from", "Effective from", "2026-01-01", "Start date for the entity configuration."),
                        OptionalUploadField("source_system", "Source system", "Fund administrator", "Administrator, legal system, or retained setup packet.")
                    ],
                    SampleRows:
                    [
                        "ENT-40001,Northwind Income Fund LP,Fund,,USD,DE,100,2026-01-01,Fund administrator"
                    ],
                    ValidationNotes:
                    [
                        "Entity configuration uploads enter governance review before creating or changing operating structure.",
                        "Parent and ownership values are treated as candidate relationships until approved."
                    ])
            ],
            AcceptedFileExtensions: [".csv"],
            MaxPreviewRows: DataUploadMaxPreviewRows,
            MaxFileBytes: DataUploadMaxFileBytes);

    private static DataUploadTemplateFieldDto RequiredUploadField(
        string name,
        string label,
        string example,
        string description)
        => new(name, label, true, example, description);

    private static DataUploadTemplateFieldDto OptionalUploadField(
        string name,
        string label,
        string example,
        string description)
        => new(name, label, false, example, description);

    private static DataUploadParsedCsv ParseDataUploadCsv(
        byte[] fileBytes,
        DataUploadTemplateDto template,
        int maxPreviewRows)
    {
        using var stream = new MemoryStream(fileBytes);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var issues = new List<DataUploadValidationIssueDto>();
        var headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            issues.Add(new DataUploadValidationIssueDto("Error", "header", "The CSV file must include a header row.", RowNumber: 1));
            return new DataUploadParsedCsv([], [], ParsedRowCount: 0, Issues: issues);
        }

        var headers = SplitDataUploadCsvLine(headerLine)
            .Select(static header => header.Trim())
            .Where(static header => header.Length > 0)
            .ToArray();
        if (headers.Length == 0)
        {
            issues.Add(new DataUploadValidationIssueDto("Error", "header", "The CSV header row does not contain any named fields.", RowNumber: 1));
            return new DataUploadParsedCsv([], [], ParsedRowCount: 0, Issues: issues);
        }

        var headerSet = new HashSet<string>(headers, StringComparer.OrdinalIgnoreCase);
        foreach (var field in template.Fields.Where(static field => field.Required))
        {
            if (!headerSet.Contains(field.Name))
            {
                issues.Add(new DataUploadValidationIssueDto(
                    "Error",
                    field.Name,
                    $"Required field '{field.Name}' is missing from the upload header.",
                    RowNumber: 1));
            }
        }

        var previewRows = new List<IReadOnlyDictionary<string, string>>();
        var parsedRowCount = 0;
        var lineNumber = 1;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            parsedRowCount++;
            var values = SplitDataUploadCsvLine(line);
            if (values.Count != headers.Length)
            {
                issues.Add(new DataUploadValidationIssueDto(
                    "Warning",
                    "row",
                    $"Row has {values.Count.ToString(CultureInfo.InvariantCulture)} values for {headers.Length.ToString(CultureInfo.InvariantCulture)} headers.",
                    RowNumber: lineNumber));
            }

            if (previewRows.Count >= maxPreviewRows)
            {
                continue;
            }

            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < headers.Length; index++)
            {
                row[headers[index]] = index < values.Count ? values[index].Trim() : string.Empty;
            }

            previewRows.Add(row);
        }

        if (parsedRowCount == 0)
        {
            issues.Add(new DataUploadValidationIssueDto("Warning", "rows", "The CSV file contains headers but no data rows.", RowNumber: null));
        }

        return new DataUploadParsedCsv(headers, previewRows, parsedRowCount, issues);
    }

    private static BankStatementImportParseResult ParseBankStatementImportCsv(
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
            return new BankStatementImportParseResult(lines, StatementDate: null, issues);
        }

        var headers = SplitDataUploadCsvLine(headerLine)
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

        foreach (var field in BankStatementRequiredFields)
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

            var values = SplitDataUploadCsvLine(line);
            if (values.Count != headers.Length)
            {
                issues.Add(new DataUploadValidationIssueDto(
                    "Error",
                    "row",
                    $"Row has {values.Count.ToString(CultureInfo.InvariantCulture)} values for {headers.Length.ToString(CultureInfo.InvariantCulture)} headers.",
                    RowNumber: lineNumber));
                continue;
            }

            if (!TryReadRequiredBankStatementValue(headerIndex, values, "transaction_date", lineNumber, issues, out var transactionDateValue) ||
                !TryReadRequiredBankStatementValue(headerIndex, values, "amount", lineNumber, issues, out var amountValue) ||
                !TryReadRequiredBankStatementValue(headerIndex, values, "currency", lineNumber, issues, out var currencyValue) ||
                !TryReadRequiredBankStatementValue(headerIndex, values, "transaction_type", lineNumber, issues, out var transactionType) ||
                !TryReadRequiredBankStatementValue(headerIndex, values, "description", lineNumber, issues, out var description))
            {
                continue;
            }

            if (!TryParseDataUploadDate(transactionDateValue, out var transactionDate))
            {
                issues.Add(new DataUploadValidationIssueDto(
                    "Error",
                    "transaction_date",
                    "Transaction date must use YYYY-MM-DD format.",
                    RowNumber: lineNumber));
                continue;
            }

            var valueDateValue = ReadBankStatementValue(headerIndex, values, "value_date");
            var valueDate = transactionDate;
            if (!string.IsNullOrWhiteSpace(valueDateValue) &&
                !TryParseDataUploadDate(valueDateValue, out valueDate))
            {
                issues.Add(new DataUploadValidationIssueDto(
                    "Error",
                    "value_date",
                    "Value date must use YYYY-MM-DD format.",
                    RowNumber: lineNumber));
                continue;
            }

            if (!TryParseDataUploadDecimal(amountValue, out var amount))
            {
                issues.Add(new DataUploadValidationIssueDto(
                    "Error",
                    "amount",
                    "Amount must be a signed decimal value.",
                    RowNumber: lineNumber));
                continue;
            }

            decimal? closingBalance = null;
            var closingBalanceValue = ReadBankStatementValue(headerIndex, values, "closing_balance");
            if (!string.IsNullOrWhiteSpace(closingBalanceValue))
            {
                if (!TryParseDataUploadDecimal(closingBalanceValue, out var parsedClosingBalance))
                {
                    issues.Add(new DataUploadValidationIssueDto(
                        "Error",
                        "closing_balance",
                        "Closing balance must be a decimal value.",
                        RowNumber: lineNumber));
                    continue;
                }

                closingBalance = parsedClosingBalance;
            }

            var reference = ReadBankStatementValue(headerIndex, values, "reference");
            var currency = currencyValue.Trim().ToUpperInvariant();
            lines.Add(new BankStatementLineDto(
                LineId: BuildDataUploadGuid(
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

        return new BankStatementImportParseResult(
            lines,
            lines.Count == 0 ? null : lines.Max(static line => line.ValueDate),
            issues);
    }

    private static IReadOnlyList<string> SplitDataUploadCsvLine(string line)
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

    private static bool HasAcceptedDataUploadExtension(string fileName, IReadOnlyList<string> acceptedFileExtensions)
    {
        var extension = Path.GetExtension(fileName);
        return acceptedFileExtensions.Any(candidate =>
            string.Equals(candidate, extension, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildDataUploadId(byte[] fileBytes)
    {
        var hash = Convert.ToHexString(SHA256.HashData(fileBytes))[..12].ToLowerInvariant();
        return "UP-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "-" + hash;
    }

    private static Guid BuildBankStatementBatchId(byte[] fileBytes, Guid accountId, string bankName)
    {
        var fileHash = Convert.ToHexString(SHA256.HashData(fileBytes));
        return BuildDataUploadGuid($"bank-statement-batch|{accountId:N}|{bankName}|{fileHash}");
    }

    private static Guid BuildDataUploadGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var bytes = new byte[16];
        Array.Copy(hash, bytes, bytes.Length);
        return new Guid(bytes);
    }

    private static string SanitizeDataUploadFileName(string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            return "upload.csv";
        }

        foreach (var character in Path.GetInvalidFileNameChars())
        {
            safeName = safeName.Replace(character, '-');
        }

        return safeName;
    }

    private static string ResolveDataUploadRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable(DataUploadRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            return configuredRoot;
        }

        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(
            string.IsNullOrWhiteSpace(localApplicationData) ? Path.GetTempPath() : localApplicationData,
            "Meridian",
            "workstation",
            "data-uploads");
    }

    private static string FormatBytes(long bytes)
        => bytes < 1024 * 1024
            ? $"{bytes / 1024} KB"
            : $"{bytes / (1024 * 1024)} MB";

    private static IResult MissingDataUploadPayload(string field, string message)
        => Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [field] = [message]
        });

    private static IResult DataUploadValidationProblem(IReadOnlyList<DataUploadValidationIssueDto> issues)
        => Results.ValidationProblem(issues
            .GroupBy(static issue => string.IsNullOrWhiteSpace(issue.Field) ? "file" : issue.Field, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .Select(static issue => issue.RowNumber is { } row
                        ? $"Row {row.ToString(CultureInfo.InvariantCulture)}: {issue.Message}"
                        : issue.Message)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase));

    private static bool TryReadRequiredBankStatementValue(
        IReadOnlyDictionary<string, int> headerIndex,
        IReadOnlyList<string> values,
        string field,
        int rowNumber,
        List<DataUploadValidationIssueDto> issues,
        out string value)
    {
        value = ReadBankStatementValue(headerIndex, values, field);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        issues.Add(new DataUploadValidationIssueDto(
            "Error",
            field,
            $"Required field '{field}' is blank.",
            RowNumber: rowNumber));
        return false;
    }

    private static string ReadBankStatementValue(
        IReadOnlyDictionary<string, int> headerIndex,
        IReadOnlyList<string> values,
        string field)
        => headerIndex.TryGetValue(field, out var index) && index >= 0 && index < values.Count
            ? values[index].Trim()
            : string.Empty;

    private static bool TryParseDataUploadDate(string value, out DateOnly date)
        => DateOnly.TryParseExact(
                value.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date)
            || DateOnly.TryParse(
                value.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date);

    private static bool TryParseDataUploadDecimal(string value, out decimal amount)
        => decimal.TryParse(
            value.Trim(),
            NumberStyles.Number | NumberStyles.AllowCurrencySymbol | NumberStyles.AllowParentheses,
            CultureInfo.InvariantCulture,
            out amount);

    private static string? BuildBankStatementImportNotes(string? notes, string retainedPath)
    {
        var retainedNote = $"Retained source: {retainedPath}";
        return string.IsNullOrWhiteSpace(notes)
            ? retainedNote
            : $"{notes.Trim()} | {retainedNote}";
    }

    private static readonly string[] BankStatementRequiredFields =
    [
        "transaction_date",
        "amount",
        "currency",
        "transaction_type",
        "description"
    ];

    private sealed record DataUploadParsedCsv(
        IReadOnlyList<string> Headers,
        IReadOnlyList<IReadOnlyDictionary<string, string>> PreviewRows,
        int ParsedRowCount,
        IReadOnlyList<DataUploadValidationIssueDto> Issues);

    private sealed record BankStatementImportParseResult(
        IReadOnlyList<BankStatementLineDto> Lines,
        DateOnly? StatementDate,
        IReadOnlyList<DataUploadValidationIssueDto> Issues);
}
