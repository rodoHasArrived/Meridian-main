namespace Meridian.Contracts.Workstation;

/// <summary>
/// Upload-template catalog surfaced by the Data workspace before source files are accepted.
/// </summary>
public sealed record DataUploadTemplateCatalogDto(
    IReadOnlyList<DataUploadTemplateDto> Templates,
    IReadOnlyList<string> AcceptedFileExtensions,
    int MaxPreviewRows,
    long MaxFileBytes);

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
    IReadOnlyList<string> ValidationNotes);

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
/// Validation issue returned from a source upload preview.
/// </summary>
public sealed record DataUploadValidationIssueDto(
    string Severity,
    string Field,
    string Message,
    int? RowNumber);
