// Versioned, URL-safe envelope for screen-local view state. Serialized as a single
// `view=<base64url(JSON)>` query param so operating-scope keys stay untouched and
// the token can be stored opaquely (e.g. on a workflow preset as a saved view).
import { appendRouteQuery } from "@/lib/workspace";

// Serializable table-state shape (search/sort/filter) persisted in a saved view. These
// types outlived the `useTableState` hook they originated in (archived 2026-07-06) and
// now live with their only consumer.
export type SortDirection = "asc" | "desc";

export interface TableSort {
  column: string;
  direction: SortDirection;
}

export type TableFilters = Record<string, string[] | undefined>;

export const VIEW_STATE_QUERY_KEY = "view";
export const VIEW_STATE_ENVELOPE_VERSION = 1 as const;
export const MAX_VIEW_STATE_TOKEN_LENGTH = 2000;

export interface ViewStateEnvelope {
  v: typeof VIEW_STATE_ENVELOPE_VERSION;
  /** Screen discriminator, e.g. "reporting-exports"; hydration ignores foreign screens. */
  screen: string;
  state: Record<string, unknown>;
}

/** Returns the encoded token, or null when the payload exceeds the URL budget. */
export function encodeViewStateEnvelope(envelope: ViewStateEnvelope): string | null {
  const token = toBase64Url(JSON.stringify(envelope));
  return token.length > MAX_VIEW_STATE_TOKEN_LENGTH ? null : token;
}

/** Defensive decode: bad base64, bad JSON, wrong version, or wrong shape all yield null. */
export function decodeViewStateEnvelope(raw: string | null | undefined): ViewStateEnvelope | null {
  if (!raw || raw.length > MAX_VIEW_STATE_TOKEN_LENGTH) {
    return null;
  }

  try {
    const parsed: unknown = JSON.parse(fromBase64Url(raw));
    if (
      typeof parsed !== "object"
      || parsed === null
      || (parsed as { v?: unknown }).v !== VIEW_STATE_ENVELOPE_VERSION
      || typeof (parsed as { screen?: unknown }).screen !== "string"
      || typeof (parsed as { state?: unknown }).state !== "object"
      || (parsed as { state?: unknown }).state === null
      || Array.isArray((parsed as { state?: unknown }).state)
    ) {
      return null;
    }

    return parsed as ViewStateEnvelope;
  } catch {
    return null;
  }
}

export function appendViewStateToRoute(route: string, envelope: ViewStateEnvelope): string {
  const token = encodeViewStateEnvelope(envelope);
  return token ? appendRouteQuery(route, { [VIEW_STATE_QUERY_KEY]: token }) : route;
}

export function readViewStateFromSearch(search: string, screen: string): ViewStateEnvelope | null {
  let raw: string | null;
  try {
    raw = new URLSearchParams(search).get(VIEW_STATE_QUERY_KEY);
  } catch {
    return null;
  }

  const envelope = decodeViewStateEnvelope(raw);
  return envelope && envelope.screen === screen ? envelope : null;
}

export function stripViewStateFromSearch(search: string): string {
  try {
    const params = new URLSearchParams(search);
    params.delete(VIEW_STATE_QUERY_KEY);
    const next = params.toString();
    return next ? `?${next}` : "";
  } catch {
    return search;
  }
}

// Table-state bridge: the serializable slice of useTableState, normalized defensively
// on the way back in. Contract for later phases; no screen consumer yet.
export function buildTableViewState(
  table: { query: string; sortBy: TableSort | null; filters: TableFilters }
): Record<string, unknown> {
  return {
    query: table.query,
    sortBy: table.sortBy,
    filters: table.filters
  };
}

export function normalizeTableViewState(state: unknown): {
  query: string;
  sortBy: TableSort | null;
  filters: TableFilters;
} {
  const source = typeof state === "object" && state !== null ? state as Record<string, unknown> : {};
  const sortBy = source.sortBy;
  const filters = source.filters;

  return {
    query: typeof source.query === "string" ? source.query : "",
    sortBy: isTableSort(sortBy) ? sortBy : null,
    filters: isTableFilters(filters) ? filters : {}
  };
}

function isTableSort(value: unknown): value is TableSort {
  return (
    typeof value === "object"
    && value !== null
    && typeof (value as TableSort).column === "string"
    && ((value as TableSort).direction === "asc" || (value as TableSort).direction === "desc")
  );
}

function isTableFilters(value: unknown): value is TableFilters {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    return false;
  }

  return Object.values(value).every(
    (entry) => entry === undefined
      || (Array.isArray(entry) && entry.every((item) => typeof item === "string"))
  );
}

function toBase64Url(text: string): string {
  const bytes = new TextEncoder().encode(text);
  let binary = "";
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }
  return btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/, "");
}

function fromBase64Url(token: string): string {
  const padded = token.replaceAll("-", "+").replaceAll("_", "/").padEnd(Math.ceil(token.length / 4) * 4, "=");
  const binary = atob(padded);
  const bytes = Uint8Array.from(binary, (char) => char.charCodeAt(0));
  return new TextDecoder().decode(bytes);
}
