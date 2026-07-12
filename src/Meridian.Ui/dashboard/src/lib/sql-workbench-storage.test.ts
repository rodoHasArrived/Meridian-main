import { describe, expect, it } from "vitest";
import {
  SQL_WORKBENCH_HISTORY_LIMIT,
  SQL_WORKBENCH_STORAGE_KEY,
  deleteSavedSqlQuery,
  loadSqlWorkbenchState,
  recordSqlQueryHistory,
  saveSqlWorkbenchState,
  upsertSavedSqlQuery,
  type StorageLike
} from "@/lib/sql-workbench-storage";

class MemoryStorage implements StorageLike {
  private readonly values = new Map<string, string>();

  getItem(key: string) {
    return this.values.get(key) ?? null;
  }

  setItem(key: string, value: string) {
    this.values.set(key, value);
  }

  removeItem(key: string) {
    this.values.delete(key);
  }
}

describe("sql workbench storage", () => {
  it("normalizes missing or corrupt storage to an empty v1 state", () => {
    const storage = new MemoryStorage();
    expect(loadSqlWorkbenchState(storage)).toEqual({ version: 1, history: [], savedQueries: [] });

    storage.setItem(SQL_WORKBENCH_STORAGE_KEY, "{not-json");
    expect(loadSqlWorkbenchState(storage)).toEqual({ version: 1, history: [], savedQueries: [] });
  });

  it("records deduped query history with a fixed limit", () => {
    let state = { version: 1 as const, history: [], savedQueries: [] };

    for (let index = 0; index < SQL_WORKBENCH_HISTORY_LIMIT + 2; index += 1) {
      state = recordSqlQueryHistory(state, `SELECT ${index}`, `2026-01-01T00:00:${String(index).padStart(2, "0")}Z`);
    }
    state = recordSqlQueryHistory(state, " SELECT 12 ", "2026-01-01T00:01:00Z");

    expect(state.history).toHaveLength(SQL_WORKBENCH_HISTORY_LIMIT);
    expect(state.history[0]).toMatchObject({ sql: "SELECT 12", lastUsedAt: "2026-01-01T00:01:00Z" });
    expect(state.history.filter((entry) => entry.sql === "SELECT 12")).toHaveLength(1);
  });

  it("saves, updates, deletes, and reloads named queries", () => {
    const storage = new MemoryStorage();
    let state = upsertSavedSqlQuery(
      { version: 1, history: [], savedQueries: [] },
      " Catalog ",
      " SELECT * FROM meridian_files ",
      "2026-01-01T00:00:00Z",
    );
    state = upsertSavedSqlQuery(state, "Catalog", "SELECT symbol FROM meridian_files", "2026-01-02T00:00:00Z");

    expect(state.savedQueries).toHaveLength(1);
    expect(state.savedQueries[0]).toMatchObject({
      id: "catalog",
      name: "Catalog",
      sql: "SELECT symbol FROM meridian_files",
      updatedAt: "2026-01-02T00:00:00Z",
    });

    expect(saveSqlWorkbenchState(state, storage)).toBe("saved");
    const loaded = loadSqlWorkbenchState(storage);
    expect(loaded.savedQueries[0]?.name).toBe("Catalog");

    const deleted = deleteSavedSqlQuery(loaded, "catalog");
    expect(deleted.savedQueries).toHaveLength(0);
  });
});
