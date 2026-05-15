namespace Meridian.Contracts.Workstation;

/// <summary>
/// Workstation-facing strategy design document persisted by the Strategy Builder.
/// </summary>
public sealed record StrategyDesignDocument(
    string DocumentId,
    string Name,
    string Description,
    string Version,
    string DatasetReference,
    IReadOnlyList<string> Universe,
    IReadOnlyList<StrategyDesignCell> Cells,
    IReadOnlyList<StrategyDesignTransition> Transitions,
    IReadOnlyDictionary<string, string>? Metadata,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// A visual, formula, code, governance, or template cell inside a strategy design.
/// </summary>
public sealed record StrategyDesignCell(
    string CellId,
    string Label,
    string Kind,
    string Purpose,
    string Source,
    IReadOnlyList<string> FieldRefs,
    IReadOnlyDictionary<string, string>? Parameters = null,
    string? DisabledReason = null);

/// <summary>
/// Directed transition between two cells in the design canvas.
/// </summary>
public sealed record StrategyDesignTransition(
    string TransitionId,
    string FromCellId,
    string ToCellId,
    string Kind,
    string Condition,
    int? MaxIterations = null,
    string? Rationale = null);

/// <summary>
/// AMX-style field vocabulary item mapped to a Meridian canonical source.
/// </summary>
public sealed record StrategyDesignFieldCatalogItem(
    string FieldId,
    string Label,
    string Source,
    string DataSet,
    string TypeName,
    string Description,
    bool IsEnabled,
    string? DisabledReason,
    IReadOnlyList<string> Synonyms);

/// <summary>
/// Strategy Builder template imported from the prototype requirements and rebuilt in Meridian terms.
/// </summary>
public sealed record StrategyDesignTemplate(
    string TemplateId,
    string Name,
    string Description,
    string Category,
    string SourcePrototype,
    IReadOnlyList<string> Tags,
    StrategyDesignDocument Document);

/// <summary>
/// Validation message returned by design validation, preview, and run endpoints.
/// </summary>
public sealed record StrategyDesignValidationMessage(
    string Code,
    string Severity,
    string TargetId,
    string Message);

/// <summary>
/// Validation state for a design document.
/// </summary>
public sealed record StrategyDesignValidationResult(
    bool IsValid,
    string Summary,
    IReadOnlyList<StrategyDesignValidationMessage> Messages);

/// <summary>
/// Preview row explaining how a cell will be compiled.
/// </summary>
public sealed record StrategyDesignPreviewRow(
    string RowId,
    string CellId,
    string Label,
    string Purpose,
    string Status,
    IReadOnlyList<string> FieldRefs,
    string Detail);

/// <summary>
/// Generated QuantScript source and metadata.
/// </summary>
public sealed record StrategyDesignCompiledScript(
    string Source,
    string DatasetFingerprint,
    IReadOnlyList<string> FieldRefs,
    IReadOnlyList<string> DisabledFieldRefs);

/// <summary>
/// Preview result returned before execution.
/// </summary>
public sealed record StrategyDesignPreviewResult(
    StrategyDesignValidationResult Validation,
    StrategyDesignCompiledScript Compiled,
    IReadOnlyList<StrategyDesignPreviewRow> Rows,
    IReadOnlyList<StrategyDesignRunTraceEntry> Trace);

/// <summary>
/// Step-level trace entry for preview and run proof.
/// </summary>
public sealed record StrategyDesignRunTraceEntry(
    string StepId,
    string Label,
    string Status,
    string Detail,
    string? CellId = null,
    DateTimeOffset? OccurredAt = null);

/// <summary>
/// Summary row for a durable design draft.
/// </summary>
public sealed record StrategyDesignDraftSummary(
    string DocumentId,
    string Name,
    string Version,
    string DatasetReference,
    int CellCount,
    int TransitionCount,
    DateTimeOffset UpdatedAt,
    string ValidationSummary);

/// <summary>
/// Request body for saving a strategy design draft.
/// </summary>
public sealed record StrategyDesignDraftSaveRequest(StrategyDesignDocument Document);

/// <summary>
/// Response returned after saving a strategy design draft.
/// </summary>
public sealed record StrategyDesignDraftSaveResponse(
    StrategyDesignDocument Document,
    StrategyDesignDraftSummary Summary,
    StrategyDesignValidationResult Validation,
    IReadOnlyList<StrategyDesignRunTraceEntry> Trace);

/// <summary>
/// Request body for a Strategy Builder backtest run.
/// </summary>
public sealed record StrategyDesignRunBacktestRequest(
    StrategyDesignDocument Document,
    IReadOnlyDictionary<string, string>? Parameters = null);

/// <summary>
/// Backtest execution response returned by the Strategy Builder route.
/// </summary>
public sealed record StrategyDesignRunBacktestResponse(
    bool Success,
    string? RunId,
    string StrategyId,
    string StrategyName,
    StrategyDesignValidationResult Validation,
    StrategyDesignCompiledScript Compiled,
    IReadOnlyList<StrategyDesignRunTraceEntry> Trace,
    IReadOnlyList<StrategyDesignPreviewRow> PreviewRows,
    IReadOnlyDictionary<string, string> Metrics,
    string? RuntimeError,
    string? PromotionCandidatePath,
    string? ReviewPacketPath);
