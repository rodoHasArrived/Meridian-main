using System.Text.Json.Serialization;

namespace Meridian.Contracts.Workstation;

[JsonConverter(typeof(JsonStringEnumConverter<FinancialRecordExplorerTone>))]
public enum FinancialRecordExplorerTone : byte
{
    Default,
    Success,
    Warning,
    Danger,
    Info
}

public sealed record FinancialRecordExplorerDto(
    string ExplorerId,
    string Title,
    string Description,
    string SourceState,
    bool IsBlocked,
    string BlockedReason,
    IReadOnlyList<FinancialRecordExplorerScopeItemDto> ScopeItems,
    IReadOnlyList<FinancialRecordExplorerSavedViewDto> SavedViews,
    IReadOnlyList<FinancialRecordExplorerSummaryItemDto> SummaryItems,
    IReadOnlyList<FinancialRecordExplorerFilterDto> Filters,
    IReadOnlyList<FinancialRecordExplorerColumnDto> Columns,
    IReadOnlyList<FinancialRecordExplorerRowDto> Rows,
    FinancialRecordExplorerSelectedRecordDto? SelectedRecord,
    IReadOnlyList<FinancialRecordExplorerProofActionDto> ProofActions,
    FinancialRecordExplorerRecordGraphDto RecordGraph);

public sealed record FinancialRecordExplorerQueryDto(
    string ViewId = "",
    string SearchText = "",
    IReadOnlyList<FinancialRecordExplorerFilterDto>? Filters = null);

public sealed record FinancialRecordExplorerScopeItemDto(
    string Label,
    string Value,
    FinancialRecordExplorerTone Tone = FinancialRecordExplorerTone.Default);

public sealed record FinancialRecordExplorerSavedViewDto(
    string ViewId,
    string Label,
    string Description,
    bool IsSystem,
    bool IsActive,
    IReadOnlyList<FinancialRecordExplorerFilterDto> Filters,
    string SearchText = "",
    IReadOnlyList<string>? ColumnIds = null);

public sealed record FinancialRecordExplorerSummaryItemDto(
    string Label,
    string Value,
    string Detail = "",
    FinancialRecordExplorerTone Tone = FinancialRecordExplorerTone.Default);

public sealed record FinancialRecordExplorerFilterDto(
    string FilterId,
    string Label,
    string Value,
    string Operator = "equals",
    FinancialRecordExplorerTone Tone = FinancialRecordExplorerTone.Default);

public sealed record FinancialRecordExplorerColumnDto(
    string ColumnId,
    string Header,
    string CellKind = "text",
    double Width = 140,
    bool IsRightAligned = false);

public sealed record FinancialRecordExplorerCellDto(
    string ColumnId,
    string DisplayValue,
    string RawValue = "",
    FinancialRecordExplorerTone Tone = FinancialRecordExplorerTone.Default,
    string LinkHref = "");

public sealed record FinancialRecordExplorerRowDto(
    string RecordId,
    string RecordType,
    string Label,
    string Source,
    string Status,
    FinancialRecordExplorerTone Tone,
    IReadOnlyList<FinancialRecordExplorerCellDto> Cells,
    FinancialRecordExplorerSelectedRecordDto Detail);

public sealed record FinancialRecordExplorerSelectedRecordDto(
    string RecordId,
    string RecordType,
    string Title,
    string Subtitle,
    string Description,
    FinancialRecordExplorerTone Tone,
    IReadOnlyList<FinancialRecordExplorerSummaryItemDto> Fields,
    IReadOnlyList<FinancialRecordExplorerProofActionDto> ProofActions,
    IReadOnlyList<FinancialRecordExplorerRelationshipDto> UsedIn,
    IReadOnlyList<FinancialRecordExplorerRelationshipDto> Impacts,
    string FullRecordHref = "");

public sealed record FinancialRecordExplorerProofActionDto(
    string ActionId,
    string Label,
    string Description,
    string Href,
    bool IsEnabled = true,
    string DisabledReason = "",
    FinancialRecordExplorerTone Tone = FinancialRecordExplorerTone.Default);

public sealed record FinancialRecordExplorerRecordGraphDto(
    IReadOnlyList<FinancialRecordExplorerGraphNodeDto> Nodes,
    IReadOnlyList<FinancialRecordExplorerGraphEdgeDto> Edges);

public sealed record FinancialRecordExplorerGraphNodeDto(
    string NodeId,
    string Label,
    string NodeType,
    FinancialRecordExplorerTone Tone = FinancialRecordExplorerTone.Default,
    string Href = "");

public sealed record FinancialRecordExplorerGraphEdgeDto(
    string SourceNodeId,
    string TargetNodeId,
    string Label,
    FinancialRecordExplorerTone Tone = FinancialRecordExplorerTone.Default);

public sealed record FinancialRecordExplorerRelationshipDto(
    string RelationshipId,
    string Label,
    string Description,
    string Href = "",
    FinancialRecordExplorerTone Tone = FinancialRecordExplorerTone.Default);

public sealed record FinancialRecordExplorerSavedViewSaveRequestDto(
    string Label,
    string Description,
    string SearchText,
    IReadOnlyList<FinancialRecordExplorerFilterDto> Filters,
    IReadOnlyList<string>? ColumnIds = null);
