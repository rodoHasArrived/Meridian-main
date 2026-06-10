namespace Meridian.Contracts.Workstation;

/// <summary>
/// Shared workstation projection for source-backed financial record explorers.
/// Browser and WPF clients keep native presentation primitives but consume this contract.
/// </summary>
public sealed record FinancialRecordExplorerDto(
    string ExplorerId,
    string ExplorerLabel,
    string Title,
    string Description,
    string WorkspaceId,
    string RecordSetLabel,
    bool IsBlocked,
    string? BlockedReason,
    string EmptyTitle,
    string EmptyDetail,
    IReadOnlyList<FinancialRecordExplorerScopeItemDto> ScopeItems,
    IReadOnlyList<FinancialRecordExplorerSavedViewDto> SavedViews,
    IReadOnlyList<FinancialRecordExplorerSummaryItemDto> SummaryItems,
    IReadOnlyList<FinancialRecordExplorerFilterDto> Filters,
    IReadOnlyList<FinancialRecordExplorerColumnDto> Columns,
    IReadOnlyList<FinancialRecordExplorerRowDto> Rows,
    FinancialRecordExplorerSelectedRecordDto? SelectedRecord,
    FinancialRecordExplorerRecordGraphDto RecordGraph,
    IReadOnlyList<FinancialRecordExplorerRelationshipDto> UsedIn,
    IReadOnlyList<FinancialRecordExplorerRelationshipDto> Impacts);

public sealed record FinancialRecordExplorerScopeItemDto(
    string Id,
    string Label,
    string Value);

public sealed record FinancialRecordExplorerSavedViewDto(
    string SavedViewId,
    string Name,
    string? Description,
    IReadOnlyList<FinancialRecordExplorerFilterDto> Filters,
    IReadOnlyList<string> ColumnIds,
    bool IsSystem,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record FinancialRecordExplorerSummaryItemDto(
    string Id,
    string Label,
    string Value,
    string Tone = "default");

public sealed record FinancialRecordExplorerFilterDto(
    string Id,
    string Label,
    string Value,
    string Operator = "equals",
    bool IsActive = true);

public sealed record FinancialRecordExplorerColumnDto(
    string ColumnId,
    string Header,
    string ValueType = "text",
    int Width = 140,
    bool IsKey = false,
    bool IsSortable = true);

public sealed record FinancialRecordExplorerCellDto(
    string ColumnId,
    string Value,
    string? DisplayValue = null,
    string Tone = "default",
    string? Href = null);

public sealed record FinancialRecordExplorerRowDto(
    string RecordId,
    string RecordType,
    string Label,
    string Status,
    string Tone,
    IReadOnlyList<FinancialRecordExplorerCellDto> Cells,
    FinancialRecordExplorerSelectedRecordDto Detail);

public sealed record FinancialRecordExplorerSelectedRecordDto(
    string RecordId,
    string RecordType,
    string Title,
    string Subtitle,
    string Description,
    string Status,
    string Tone,
    string? FullRecordRoute,
    IReadOnlyList<FinancialRecordExplorerFieldDto> Fields,
    IReadOnlyList<FinancialRecordExplorerProofActionDto> ProofActions,
    FinancialRecordExplorerRecordGraphDto RecordGraph,
    IReadOnlyList<FinancialRecordExplorerRelationshipDto> UsedIn,
    IReadOnlyList<FinancialRecordExplorerRelationshipDto> Impacts);

public sealed record FinancialRecordExplorerFieldDto(
    string Label,
    string Value,
    string? Detail = null,
    string Tone = "default");

public sealed record FinancialRecordExplorerProofActionDto(
    string ActionId,
    string Label,
    string? Href,
    string Method = "GET",
    bool IsEnabled = true,
    string? DisabledReason = null,
    string Kind = "proof");

public sealed record FinancialRecordExplorerRecordGraphDto(
    IReadOnlyList<FinancialRecordExplorerGraphNodeDto> Nodes,
    IReadOnlyList<FinancialRecordExplorerGraphEdgeDto> Edges);

public sealed record FinancialRecordExplorerGraphNodeDto(
    string NodeId,
    string Label,
    string NodeType,
    string Tone = "default",
    string? Href = null);

public sealed record FinancialRecordExplorerGraphEdgeDto(
    string EdgeId,
    string SourceNodeId,
    string TargetNodeId,
    string Label);

public sealed record FinancialRecordExplorerRelationshipDto(
    string RelationshipId,
    string Label,
    string TargetType,
    string TargetId,
    string Detail,
    string? Href = null,
    string Tone = "default");

/// <summary>
/// Request to create or update an operator-owned financial record explorer view.
/// System views are seeded by the read service and are never persisted through this contract.
/// </summary>
public sealed record FinancialRecordExplorerSavedViewSaveRequestDto(
    string? SavedViewId,
    string Name,
    string? Description,
    IReadOnlyList<FinancialRecordExplorerFilterDto>? Filters,
    IReadOnlyList<string>? ColumnIds);
