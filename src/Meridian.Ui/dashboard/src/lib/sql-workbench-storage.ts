export const SQL_WORKBENCH_STORAGE_KEY = "meridian.workstation.dataQuery.v1";
export const SQL_WORKBENCH_HISTORY_LIMIT = 50;

export interface StorageLike {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

export interface SqlWorkbenchHistoryEntry {
  sql: string;
  lastUsedAt: string;
}

export interface SqlWorkbenchSavedQuery {
  id: string;
  name: string;
  sql: string;
  updatedAt: string;
}

export interface SqlWorkbenchStorageState {
  version: 1;
  history: SqlWorkbenchHistoryEntry[];
  savedQueries: SqlWorkbenchSavedQuery[];
}

export type SqlWorkbenchStorageWriteStatus = "saved" | "unavailable" | "failed";

function emptyState(): SqlWorkbenchStorageState {
  return { version: 1, history: [], savedQueries: [] };
}

function resolveStorage(storage?: StorageLike | null): StorageLike | null {
  if (storage) {
    return storage;
  }
  if (typeof window === "undefined") {
    return null;
  }
  try {
    return window.localStorage;
  } catch {
    return null;
  }
}

export function loadSqlWorkbenchState(storage?: StorageLike | null): SqlWorkbenchStorageState {
  const target = resolveStorage(storage);
  if (!target) {
    return emptyState();
  }

  let raw: string | null;
  try {
    raw = target.getItem(SQL_WORKBENCH_STORAGE_KEY);
  } catch {
    return emptyState();
  }

  if (!raw) {
    return emptyState();
  }

  try {
    return normalizeState(JSON.parse(raw) as unknown);
  } catch {
    return emptyState();
  }
}

export function saveSqlWorkbenchState(
  state: SqlWorkbenchStorageState,
  storage?: StorageLike | null,
): SqlWorkbenchStorageWriteStatus {
  const target = resolveStorage(storage);
  if (!target) {
    return "unavailable";
  }

  try {
    target.setItem(SQL_WORKBENCH_STORAGE_KEY, JSON.stringify(normalizeState(state)));
    return "saved";
  } catch {
    return "failed";
  }
}

export function recordSqlQueryHistory(
  state: SqlWorkbenchStorageState,
  sql: string,
  now = new Date().toISOString(),
): SqlWorkbenchStorageState {
  const normalizedSql = normalizeSql(sql);
  if (!normalizedSql) {
    return state;
  }

  const history = [
    { sql: normalizedSql, lastUsedAt: now },
    ...state.history.filter((entry) => normalizeSql(entry.sql) !== normalizedSql),
  ].slice(0, SQL_WORKBENCH_HISTORY_LIMIT);

  return { ...state, history };
}

export function upsertSavedSqlQuery(
  state: SqlWorkbenchStorageState,
  name: string,
  sql: string,
  now = new Date().toISOString(),
): SqlWorkbenchStorageState {
  const normalizedName = name.trim();
  const normalizedSql = normalizeSql(sql);
  if (!normalizedName || !normalizedSql) {
    return state;
  }

  const existing = state.savedQueries.find(
    (query) => query.name.localeCompare(normalizedName, undefined, { sensitivity: "accent" }) === 0,
  );
  const savedQuery: SqlWorkbenchSavedQuery = {
    id: existing?.id ?? buildSavedQueryId(normalizedName, state.savedQueries),
    name: normalizedName,
    sql: normalizedSql,
    updatedAt: now,
  };

  return {
    ...state,
    savedQueries: [
      savedQuery,
      ...state.savedQueries.filter((query) => query.id !== savedQuery.id),
    ].sort((a, b) => a.name.localeCompare(b.name)),
  };
}

export function deleteSavedSqlQuery(
  state: SqlWorkbenchStorageState,
  queryId: string,
): SqlWorkbenchStorageState {
  return {
    ...state,
    savedQueries: state.savedQueries.filter((query) => query.id !== queryId),
  };
}

function normalizeState(value: unknown): SqlWorkbenchStorageState {
  if (!isRecord(value)) {
    return emptyState();
  }

  const history = Array.isArray(value.history)
    ? value.history.map(normalizeHistoryEntry).filter((entry): entry is SqlWorkbenchHistoryEntry => entry !== null)
    : [];
  const savedQueries = Array.isArray(value.savedQueries)
    ? value.savedQueries.map(normalizeSavedQuery).filter((query): query is SqlWorkbenchSavedQuery => query !== null)
    : [];

  return {
    version: 1,
    history: dedupeHistory(history).slice(0, SQL_WORKBENCH_HISTORY_LIMIT),
    savedQueries: dedupeSavedQueries(savedQueries),
  };
}

function normalizeHistoryEntry(value: unknown): SqlWorkbenchHistoryEntry | null {
  if (!isRecord(value)) {
    return null;
  }

  const sql = normalizeSql(asString(value.sql));
  const lastUsedAt = asString(value.lastUsedAt);
  if (!sql || !lastUsedAt) {
    return null;
  }

  return { sql, lastUsedAt };
}

function normalizeSavedQuery(value: unknown): SqlWorkbenchSavedQuery | null {
  if (!isRecord(value)) {
    return null;
  }

  const id = asString(value.id);
  const name = asString(value.name)?.trim();
  const sql = normalizeSql(asString(value.sql));
  const updatedAt = asString(value.updatedAt);
  if (!id || !name || !sql || !updatedAt) {
    return null;
  }

  return { id, name, sql, updatedAt };
}

function dedupeHistory(history: SqlWorkbenchHistoryEntry[]): SqlWorkbenchHistoryEntry[] {
  const seen = new Set<string>();
  const result: SqlWorkbenchHistoryEntry[] = [];
  for (const entry of history) {
    const normalizedSql = normalizeSql(entry.sql);
    if (!normalizedSql || seen.has(normalizedSql)) {
      continue;
    }
    seen.add(normalizedSql);
    result.push({ ...entry, sql: normalizedSql });
  }
  return result;
}

function dedupeSavedQueries(savedQueries: SqlWorkbenchSavedQuery[]): SqlWorkbenchSavedQuery[] {
  const seen = new Set<string>();
  const result: SqlWorkbenchSavedQuery[] = [];
  for (const query of savedQueries) {
    if (seen.has(query.id)) {
      continue;
    }
    seen.add(query.id);
    result.push(query);
  }
  return result.sort((a, b) => a.name.localeCompare(b.name));
}

function buildSavedQueryId(name: string, existing: SqlWorkbenchSavedQuery[]): string {
  const slug = name
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 48) || "query";
  const existingIds = new Set(existing.map((query) => query.id));
  let id = slug;
  let index = 2;
  while (existingIds.has(id)) {
    id = `${slug}-${index}`;
    index += 1;
  }
  return id;
}

function normalizeSql(sql: string | null): string | null {
  const normalized = sql?.trim();
  return normalized ? normalized : null;
}

function asString(value: unknown): string | null {
  return typeof value === "string" && value.length > 0 ? value : null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}
