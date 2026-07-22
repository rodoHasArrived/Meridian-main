import { appendRouteQuery } from "@/lib/workspace";

import type { WorkflowContinuityStepStatusTone } from "@/app-shell.workflow-continuity";

export interface AppShellLinkedContextItem {
  id: string;
  label: string;
  detail: string;
  route: string;
  workspaceLabel: string;
  statusLabel: string;
  tone: WorkflowContinuityStepStatusTone;
  ariaLabel: string;
}

export function buildLinkedContextItem({
  id,
  label,
  detail,
  route,
  workspaceLabel,
  statusLabel,
  tone
}: Omit<AppShellLinkedContextItem, "ariaLabel">): AppShellLinkedContextItem {
  return {
    id,
    label,
    detail,
    route,
    workspaceLabel,
    statusLabel,
    tone,
    ariaLabel: `${workspaceLabel}: ${label}. ${detail} ${statusLabel}.`
  };
}

export function appendLinkedContextSearchValue(route: string, key: string, value: string): string {
  return appendRouteQuery(route, { [key]: value });
}

export function findLinkedContextSymbolRow<T extends { symbol: string | null | undefined }>(
  rows: T[],
  symbol: string
): T | null {
  return rows.find((row) => isSameLinkedContextSymbol(row.symbol, symbol)) ?? null;
}

export function isSameLinkedContextSymbol(value: string | null | undefined, symbol: string): boolean {
  return normalizeLinkedContextSymbol(value) === symbol;
}

export function normalizeLinkedContextSymbol(value: string | null | undefined): string | null {
  const normalized = value?.trim().toUpperCase().replace(/[^A-Z0-9._-]/g, "") ?? "";
  return normalized.length > 0 ? normalized.slice(0, 16) : null;
}
