import type {
  FinancialRecordExplorerDto,
  FinancialRecordExplorerFilterDto
} from "@/types";

export const financialRecordExplorerUrlParams = {
  explorer: "frexExplorer",
  view: "frexView",
  search: "frexSearch",
  record: "frexRecord",
  filters: "frexFilters",
  columns: "frexColumns"
} as const;

export interface ExplorerUrlState {
  viewId: string | null;
  searchText: string | null;
  recordId: string | null;
  filters: FinancialRecordExplorerFilterDto[] | null;
  columnIds: string[] | null;
}

export interface ExplorerShareState {
  explorerId: string;
  viewId: string;
  searchText: string;
  recordId: string;
  filters: FinancialRecordExplorerFilterDto[];
  columnIds: string[];
}

export interface ExplorerLocation {
  pathname: string;
  search: string;
  hash: string;
}

export function readExplorerUrlState(
  expectedExplorerId?: string | null,
  search = getWindowSearch()
): ExplorerUrlState {
  const params = new URLSearchParams(search);
  const explorerId = normalizeOptionalParam(params.get(financialRecordExplorerUrlParams.explorer));
  if (expectedExplorerId && explorerId && explorerId !== expectedExplorerId) {
    return emptyExplorerUrlState();
  }

  return {
    viewId: normalizeOptionalParam(params.get(financialRecordExplorerUrlParams.view)),
    searchText: params.has(financialRecordExplorerUrlParams.search) ? params.get(financialRecordExplorerUrlParams.search) ?? "" : null,
    recordId: normalizeOptionalParam(params.get(financialRecordExplorerUrlParams.record)),
    filters: parseUrlFilters(params.get(financialRecordExplorerUrlParams.filters)),
    columnIds: parseColumnIds(params.get(financialRecordExplorerUrlParams.columns))
  };
}

export function resolveInitialViewId(
  explorer: FinancialRecordExplorerDto | null,
  fallbackViewId: string,
  urlViewId: string | null
): string {
  if (!explorer) {
    return fallbackViewId;
  }

  if (urlViewId && explorer.savedViews.some((view) => view.viewId === urlViewId)) {
    return urlViewId;
  }

  return fallbackViewId;
}

export function resolveInitialSearchText(
  explorer: FinancialRecordExplorerDto | null,
  viewId: string,
  urlSearchText: string | null
): string {
  if (urlSearchText !== null) {
    return urlSearchText;
  }

  return explorer?.savedViews.find((view) => view.viewId === viewId)?.searchText ?? "";
}

export function resolveInitialRecordId(
  explorer: FinancialRecordExplorerDto | null,
  urlRecordId: string | null,
  viewId?: string | null
): string {
  if (!explorer) {
    return "";
  }

  if (urlRecordId && explorer.rows.some((row) => row.recordId === urlRecordId)) {
    return urlRecordId;
  }

  const selectedViewRecordId = explorer.savedViews
    .find((view) =>
      view.selectedRecordId &&
      (viewId
        ? view.viewId === viewId
        : view.isActive) &&
      explorer.rows.some((row) => row.recordId === view.selectedRecordId))
    ?.selectedRecordId;

  if (selectedViewRecordId) {
    return selectedViewRecordId;
  }

  return explorer.selectedRecord?.recordId ?? explorer.rows[0]?.recordId ?? "";
}

export function syncExplorerUrlState(state: ExplorerShareState) {
  const href = buildExplorerShareHref(state);
  if (!href || typeof window === "undefined" || typeof window.history?.replaceState !== "function") {
    return;
  }

  const current = `${window.location.pathname}${window.location.search}${window.location.hash}`;
  if (href !== current) {
    window.history.replaceState(window.history.state, "", href);
  }
}

export function buildExplorerShareHref(
  state: ExplorerShareState,
  location = getWindowLocation()
): string {
  if (!location) {
    return "#";
  }

  const params = new URLSearchParams(location.search);
  setOptionalParam(params, financialRecordExplorerUrlParams.explorer, state.explorerId);
  setOptionalParam(params, financialRecordExplorerUrlParams.view, state.viewId);
  setOptionalParam(params, financialRecordExplorerUrlParams.search, state.searchText);
  setOptionalParam(params, financialRecordExplorerUrlParams.record, state.recordId);
  if (state.filters.length > 0) {
    params.set(financialRecordExplorerUrlParams.filters, JSON.stringify(state.filters));
  } else {
    params.delete(financialRecordExplorerUrlParams.filters);
  }
  setOptionalListParam(params, financialRecordExplorerUrlParams.columns, state.columnIds);

  const query = params.toString();
  return `${location.pathname}${query ? `?${query}` : ""}${location.hash}`;
}

export function buildSavedViewDescription(
  searchText: string,
  filters: FinancialRecordExplorerFilterDto[],
  selectedViewLabel: string | undefined
): string {
  const parts = [
    searchText.trim() ? `Search: ${searchText.trim()}` : "",
    filters.length > 0 ? `Filters: ${filters.map((filter) => `${filter.label}=${filter.value}`).join(", ")}` : "",
    selectedViewLabel?.trim() ? `Based on ${selectedViewLabel.trim()}.` : ""
  ].filter((part) => part.length > 0);

  return parts.length > 0 ? parts.join(" ") : "Saved from current explorer context.";
}

function emptyExplorerUrlState(): ExplorerUrlState {
  return { viewId: null, searchText: null, recordId: null, filters: null, columnIds: null };
}

function setOptionalParam(params: URLSearchParams, key: string, value: string) {
  const trimmed = value.trim();
  if (trimmed.length > 0) {
    params.set(key, trimmed);
    return;
  }

  params.delete(key);
}

function setOptionalListParam(params: URLSearchParams, key: string, values: string[]) {
  const normalized = normalizeColumnIds(values);
  if (normalized && normalized.length > 0) {
    params.set(key, normalized.join(","));
    return;
  }

  params.delete(key);
}

function normalizeOptionalParam(value: string | null): string | null {
  const trimmed = value?.trim() ?? "";
  return trimmed.length > 0 ? trimmed : null;
}

function parseColumnIds(value: string | null): string[] | null {
  const trimmed = value?.trim() ?? "";
  if (!trimmed) {
    return null;
  }

  if (trimmed.startsWith("[")) {
    try {
      const parsed = JSON.parse(trimmed) as unknown;
      if (Array.isArray(parsed)) {
        return normalizeColumnIds(parsed.filter((item): item is string => typeof item === "string"));
      }
    } catch {
      return null;
    }
  }

  return normalizeColumnIds(trimmed.split(","));
}

function normalizeColumnIds(values: string[]): string[] | null {
  const seen = new Set<string>();
  const normalized: string[] = [];
  for (const value of values) {
    const columnId = value.trim();
    const key = columnId.toLowerCase();
    if (columnId.length === 0 || seen.has(key)) {
      continue;
    }

    seen.add(key);
    normalized.push(columnId);
  }

  return normalized.length > 0 ? normalized : null;
}

function parseUrlFilters(value: string | null): FinancialRecordExplorerFilterDto[] | null {
  if (!value) {
    return null;
  }

  try {
    const parsed = JSON.parse(value) as unknown;
    if (!Array.isArray(parsed)) {
      return null;
    }

    const filters = parsed.filter(isFinancialRecordExplorerFilter);
    return filters.length > 0 ? filters : null;
  } catch {
    return null;
  }
}

function isFinancialRecordExplorerFilter(value: unknown): value is FinancialRecordExplorerFilterDto {
  if (!value || typeof value !== "object") {
    return false;
  }

  const candidate = value as Partial<FinancialRecordExplorerFilterDto>;
  return typeof candidate.filterId === "string" &&
    typeof candidate.label === "string" &&
    typeof candidate.value === "string" &&
    typeof candidate.operator === "string";
}

function getWindowSearch(): string {
  if (typeof window === "undefined") {
    return "";
  }

  return window.location.search;
}

function getWindowLocation(): ExplorerLocation | null {
  if (typeof window === "undefined") {
    return null;
  }

  return {
    pathname: window.location.pathname,
    search: window.location.search,
    hash: window.location.hash
  };
}
