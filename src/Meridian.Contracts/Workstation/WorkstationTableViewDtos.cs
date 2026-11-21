namespace Meridian.Contracts.Workstation;

/// <summary>Personal table presentation. Contains no financial records or authority.</summary>
public sealed record WorkstationTableViewDto(
    string ViewId,
    string Label,
    string SearchText,
    IReadOnlyList<string> ColumnIds,
    string Density,
    string? SortColumnId = null,
    string? SortDirection = null,
    IReadOnlyDictionary<string, string>? Filters = null,
    bool IsReadOnly = false,
    string? Origin = null)
{
    public string? Description { get; init; }
    public IReadOnlyList<FinancialRecordExplorerFilterDto>? ExplorerFilters { get; init; }
}

public sealed record WorkstationTableViewSaveRequest(
    string Label,
    string SearchText,
    IReadOnlyList<string> ColumnIds,
    string Density,
    string? SortColumnId = null,
    string? SortDirection = null,
    IReadOnlyDictionary<string, string>? Filters = null)
{
    public string? Description { get; init; }
    public IReadOnlyList<FinancialRecordExplorerFilterDto>? ExplorerFilters { get; init; }
}

public sealed record WorkstationTableExportRequest(
    IReadOnlyList<string> ColumnIds, string SearchText = "", string? SortColumnId = null, string? SortDirection = null,
    IReadOnlyDictionary<string, string>? Filters = null);

public sealed record WorkstationTableExportDto(string Filename, string Csv, int RowCount, DateTimeOffset AsOf);
