import React from "react";

export interface TableColumn {
  key: string;
  label: string;
  align?: "left" | "right";
  sortable?: boolean;
  filterable?: boolean;
}

export interface TableState {
  data: any[];
  rawData: any[];
  setData: (data: any[]) => void;
  query: string;
  setQuery: (q: string) => void;
  sortBy: { column: string; direction: "asc" | "desc" } | null;
  toggleSort: (column: string) => void;
  filters: { [column: string]: string[] | undefined };
  toggleFilter: (column: string, value: string) => void;
  clearAllFilters: () => void;
  exportCSV: (filename?: string) => void;
  filterCount: number;
  resultCount: number;
}

export function useTableState(
  initialData?: any[],
  localStorageKey?: string | null
): TableState;

export interface FilteredDataTableProps {
  data?: any[];
  columns?: TableColumn[];
  title?: string;
  onExport?: (state: TableState) => void;
  localStorageKey?: string | null;
}

export function FilteredDataTable(
  props: FilteredDataTableProps
): React.ReactElement;
