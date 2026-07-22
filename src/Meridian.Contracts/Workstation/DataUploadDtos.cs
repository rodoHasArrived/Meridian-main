namespace Meridian.Contracts.Workstation;

/// <summary>
/// Upload-template catalog surfaced by the Data workspace before source files are accepted.
/// </summary>
/// <remarks>
/// The workbook fields describe the multi-sheet onboarding workbook (a single generated
/// <c>.xlsx</c> spanning every template). They are optional so existing single-file CSV
/// consumers keep working unchanged; when populated they let the browser offer a workbook
/// download and a workbook-scoped upload preview alongside the per-template CSV flow.
/// </remarks>
public sealed record DataUploadTemplateCatalogDto(
    IReadOnlyList<DataUploadTemplateDto> Templates,
    IReadOnlyList<string> AcceptedFileExtensions,
    int MaxPreviewRows,
    long MaxFileBytes,
    string? WorkbookFileName = null,
    IReadOnlyList<string>? WorkbookAcceptedFileExtensions = null,
    long WorkbookMaxFileBytes = 0);

/// <summary>
/// A governed source-data template that operators can download and use for upload preview.
/// </summary>
public sealed record DataUploadTemplateDto(
    string TemplateId,
    string Label,
    string Description,
    string DataDomain,
    string TargetWorkflow,
    string FileName,
    string ContentType,
    string HeaderLine,
    IReadOnlyList<DataUploadTemplateFieldDto> Fields,
    IReadOnlyList<string> SampleRows,
    IReadOnlyList<string> ValidationNotes,
    IReadOnlyList<string>? SourceKinds = null,
    IReadOnlyList<string>? SetupChecklist = null,
    IReadOnlyList<string>? MappingGuidance = null);

/// <summary>
/// Template-field metadata used by the browser and desktop shells for source-file preparation.
/// </summary>
public sealed record DataUploadTemplateFieldDto(
    string Name,
    string Label,
    bool Required,
    string Example,
    string Description);

/// <summary>
/// Bounded upload preview result. The result retains evidence and validation output only.
/// </summary>
public sealed record DataUploadPreviewResultDto(
    string UploadId,
    string TemplateId,
    string TemplateLabel,
    string FileName,
    long FileSizeBytes,
    string ContentType,
    string UploadedBy,
    DateTimeOffset UploadedAtUtc,
    string RetainedPath,
    int ParsedRowCount,
    int PreviewRowCount,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyDictionary<string, string>> PreviewRows,
    IReadOnlyList<DataUploadValidationIssueDto> Issues,
    string Status,
    string NextAction);

/// <summary>
/// Result returned after a retained bank-statement source file is applied as account evidence.
/// </summary>
public sealed record BankStatementImportResultDto(
    string UploadId,
    Guid BatchId,
    Guid AccountId,
    string AccountCode,
    string BankName,
    DateOnly StatementDate,
    string FileName,
    long FileSizeBytes,
    string ImportedBy,
    DateTimeOffset ImportedAtUtc,
    string RetainedPath,
    int LineCount,
    IReadOnlyList<DataUploadValidationIssueDto> Issues,
    string Status,
    string NextAction);

/// <summary>
/// Per-sheet preview returned for one worksheet of an uploaded onboarding workbook.
/// </summary>
public sealed record DataUploadWorkbookSheetPreviewDto(
    string SheetName,
    string? TemplateId,
    string? TemplateLabel,
    string? DataDomain,
    int ParsedRowCount,
    int PreviewRowCount,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyDictionary<string, string>> PreviewRows,
    IReadOnlyList<DataUploadValidationIssueDto> Issues,
    string Status);

/// <summary>
/// Bounded, multi-sheet preview result for an uploaded onboarding workbook. Like the CSV
/// preview it retains the raw workbook as evidence and returns validation output only; no
/// rows are committed to governed stores.
/// </summary>
public sealed record DataUploadWorkbookPreviewResultDto(
    string UploadId,
    string FileName,
    long FileSizeBytes,
    string ContentType,
    string UploadedBy,
    DateTimeOffset UploadedAtUtc,
    string RetainedPath,
    int SheetCount,
    int TotalParsedRowCount,
    IReadOnlyList<DataUploadWorkbookSheetPreviewDto> Sheets,
    IReadOnlyList<DataUploadValidationIssueDto> CrossSheetIssues,
    string Status,
    string NextAction);

/// <summary>
/// Validation issue returned from a source upload preview.
/// </summary>
/// <remarks>
/// <paramref name="SheetName"/> and <paramref name="CellReference"/> are populated by the
/// workbook preview so issues can point at an exact cell (for example
/// <c>Entities!D7</c>); the single-file CSV path leaves them null.
/// </remarks>
public sealed record DataUploadValidationIssueDto(
    string Severity,
    string Field,
    string Message,
    int? RowNumber,
    string? SheetName = null,
    string? CellReference = null);
