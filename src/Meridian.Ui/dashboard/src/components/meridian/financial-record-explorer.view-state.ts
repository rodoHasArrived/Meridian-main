import type {
  FinancialRecordExplorerDto,
  FinancialRecordExplorerFilterDto,
  FinancialRecordExplorerRowDto
} from "@/types";

export interface SharedExplorerFilterState {
  filterId: string;
  value: string;
}

export interface SharedExplorerViewState {
  viewId: string | null;
  searchText: string | null;
  recordId: string | null;
  filters: SharedExplorerFilterState[];
}

export interface ShareableExplorerHrefRequest {
  explorer: FinancialRecordExplorerDto | null;
  selectedViewId: string;
  searchText: string;
  selectedFilters: FinancialRecordExplorerFilterDto[];
  selectedRecordId: string;
  currentHref: string;
}

export function readSharedExplorerViewState(
  explorer: FinancialRecordExplorerDto | null,
  search: string | URLSearchParams
): SharedExplorerViewState {
  if (!explorer) {
    return createEmptySharedExplorerViewState();
  }

  const params = toSearchParams(search);
  const explorerId = params.get("frexExplorer");
  if (explorerId && explorerId !== explorer.explorerId) {
    return createEmptySharedExplorerViewState();
  }

  return {
    viewId: params.get("frexView"),
    searchText: params.get("frexSearch"),
    recordId: params.get("frexRecord"),
    filters: readSharedFilterState(params)
  };
}

export function resolveSharedViewId(
  viewId: string | null,
  views: ReadonlyArray<{ id: string }>
): string | null {
  if (!viewId) {
    return null;
  }

  return views.some((view) => view.id === viewId) ? viewId : null;
}

export function resolveSharedRecordId(
  recordId: string | null,
  rows: FinancialRecordExplorerRowDto[]
): string | null {
  if (!recordId) {
    return null;
  }

  return rows.some((row) => row.recordId === recordId) ? recordId : null;
}

export function resolveSharedFilters(
  filters: SharedExplorerFilterState[],
  explorer: FinancialRecordExplorerDto
): FinancialRecordExplorerFilterDto[] {
  const candidateFilters = [
    ...explorer.filters,
    ...explorer.savedViews.flatMap((view) => view.filters)
  ];

  return filters.map((filter) => {
    const filterId = filter.filterId.toLowerCase();
    const candidate = candidateFilters.find((item) => item.filterId.toLowerCase() === filterId);
    const candidateColumn = explorer.columns.find((column) => column.columnId.toLowerCase() === filterId);
    return {
      filterId: filter.filterId,
      label: candidate?.label ?? candidateColumn?.header ?? filter.filterId,
      value: filter.value,
      operator: candidate?.operator ?? "equals",
      tone: candidate?.tone ?? "Info"
    };
  });
}

export function buildShareableExplorerHref({
  explorer,
  selectedViewId,
  searchText,
  selectedFilters,
  selectedRecordId,
  currentHref
}: ShareableExplorerHrefRequest): string {
  if (!explorer || !currentHref) {
    return "#";
  }

  const url = new URL(currentHref, "http://localhost");
  ["frexExplorer", "frexView", "frexSearch", "frexFilter", "frexRecord"].forEach((key) => {
    url.searchParams.delete(key);
  });

  url.searchParams.set("frexExplorer", explorer.explorerId);
  if (selectedViewId) {
    url.searchParams.set("frexView", selectedViewId);
  }

  if (searchText.trim()) {
    url.searchParams.set("frexSearch", searchText.trim());
  }

  selectedFilters.forEach((filter) => {
    if (filter.filterId.trim() && filter.value.trim()) {
      url.searchParams.append("frexFilter", `${filter.filterId.trim()}:${filter.value.trim()}`);
    }
  });

  if (selectedRecordId) {
    url.searchParams.set("frexRecord", selectedRecordId);
  }

  return `${url.pathname}${url.search}${url.hash}`;
}

export function buildShareableExplorerStateSummary({
  savedViewLabel,
  searchText,
  selectedFilters,
  selectedRow
}: {
  savedViewLabel: string | null;
  searchText: string;
  selectedFilters: FinancialRecordExplorerFilterDto[];
  selectedRow: FinancialRecordExplorerRowDto | null;
}): string {
  const parts: string[] = [];
  if (savedViewLabel) {
    parts.push(`view ${savedViewLabel}`);
  }

  const trimmedSearch = searchText.trim();
  if (trimmedSearch) {
    parts.push(`search ${trimmedSearch}`);
  }

  selectedFilters.forEach((filter) => {
    const label = filter.label.trim() || filter.filterId.trim();
    const value = filter.value.trim();
    if (label && value) {
      parts.push(`filter ${label} ${filter.operator} ${value}`);
    }
  });

  if (selectedRow) {
    parts.push(`record ${selectedRow.label}`);
  }

  return parts.length > 0 ? parts.join("; ") : "current explorer state";
}

function createEmptySharedExplorerViewState(): SharedExplorerViewState {
  return { viewId: null, searchText: null, recordId: null, filters: [] };
}

function readSharedFilterState(params: URLSearchParams): SharedExplorerFilterState[] {
  return params.getAll("frexFilter")
    .map((value) => {
      const separatorIndex = value.indexOf(":");
      if (separatorIndex <= 0) {
        return null;
      }

      const filterId = value.slice(0, separatorIndex).trim();
      const filterValue = value.slice(separatorIndex + 1).trim();
      return filterId && filterValue ? { filterId, value: filterValue } : null;
    })
    .filter((value): value is SharedExplorerFilterState => Boolean(value));
}

function toSearchParams(search: string | URLSearchParams): URLSearchParams {
  if (search instanceof URLSearchParams) {
    return search;
  }

  const queryStart = search.indexOf("?");
  const queryText = queryStart >= 0 ? search.slice(queryStart + 1) : search.replace(/^\?/, "");
  const hashStart = queryText.indexOf("#");
  return new URLSearchParams(hashStart >= 0 ? queryText.slice(0, hashStart) : queryText);
}
