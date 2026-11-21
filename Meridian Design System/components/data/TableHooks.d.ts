import type React from "react";
export interface TableSort { column: string; direction: "asc" | "desc"; }
export interface TableView { query: string; sortStack: TableSort[]; filters: Record<string, string[] | undefined>; groupBy: string | string[] | null; }
export interface TableState<T> extends TableView {
  data: T[]; rawData: T[]; setData: React.Dispatch<React.SetStateAction<T[]>>;
  setQuery: React.Dispatch<React.SetStateAction<string>>; sortBy: TableSort | null;
  toggleSort(column: string, additive?: boolean): void;
  sortInfo(column: string): { rank: number; direction: "asc" | "desc"; multi: boolean } | null;
  toggleFilter(column: string, value: string): void; clearAllFilters(): void;
  setGroupBy: React.Dispatch<React.SetStateAction<string | string[] | null>>;
  getViewState(): TableView; applyViewState(state?: Partial<TableView>): void;
  setFilters: React.Dispatch<React.SetStateAction<Record<string, string[] | undefined>>>;
  filterCount: number; resultCount: number; exportCSV(filename?: string): void;
}
export interface TableColumn { key: string; width?: number; pinned?: boolean; [key: string]: unknown; }
export interface ColumnState<C extends TableColumn> {
  visibleColumns: Array<C & { pinned: boolean; pinnedOffset?: number }>;
  allColumns: C[]; order: string[]; widths: Record<string, number>; hidden: Record<string, boolean>; pinnedKeys: string[];
  resize(key: string, width: number): void; reorder(from: string, to: string): void; move(key: string, direction: number): void;
  togglePin(key: string): void; toggleVisible(key: string): void; reset(): void;
}
export interface RowSelection<T, K> {
  isSelected(row: T): boolean; onRowClick(row: T, index: number, event?: { shiftKey?: boolean; metaKey?: boolean; ctrlKey?: boolean }): void;
  toggle(row: T): void; toggleAllLoaded(): void; selectAllMatching(): void; clear(): void;
  selectedKeys: Set<K>; selectedRows: T[]; count: number | "all"; allMatching: boolean; allLoadedSelected: boolean; someSelected: boolean;
}
export interface AsyncTableState<T, S> {
  rows: T[]; total: number | null; status: "idle" | "loading" | "loadingMore" | "ready" | "error" | "empty";
  error: unknown; query: string; setQuery(query: string): void; sort: S; setSort: React.Dispatch<React.SetStateAction<S>>;
  loadMore(): void; hasMore: boolean; reload(): void;
}
export const TableHooks: {
  useTableState<T>(initialData?: T[], localStorageKey?: string | null): TableState<T>;
  useTableColumns<C extends TableColumn>(baseColumns: C[], options?: { persistKey?: string | null; defaultWidth?: number }): ColumnState<C>;
  useRowSelection<T, K = string>(rows: T[], getKey?: (row: T) => K): RowSelection<T, K>;
  useAsyncTableData<T, S = TableSort | null>(fetchPage: (request: { offset: number; limit: number; sort: S; query: string; signal?: AbortSignal }) => Promise<{ rows: T[]; total?: number | null }>, options?: { limit?: number; mode?: "append" | "replace"; debounceMs?: number; initialSort?: S; initialQuery?: string }): AsyncTableState<T, S>;
};
