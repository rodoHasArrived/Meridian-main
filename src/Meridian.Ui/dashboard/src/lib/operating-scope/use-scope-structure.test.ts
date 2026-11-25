import { renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { useScopeStructure } from "@/lib/operating-scope/use-scope-structure";
import { loadScopeStructure, UNAVAILABLE_SCOPE_STRUCTURE } from "@/lib/operating-scope/structure-options";
import { ApiError } from "@/lib/api-errors";

const GRAPH = { entities: [{ entityId: "E-1", name: "Entity One" }], funds: [], accounts: [] };

describe("useScopeStructure", () => {
  it("does not load until needed, then loads once for the session", async () => {
    const load = vi.fn().mockResolvedValue(GRAPH);
    const { result, rerender } = renderHook(({ needed }) => useScopeStructure(needed, load), { initialProps: { needed: false } });
    expect(load).not.toHaveBeenCalled();
    expect(result.current.structure).toBe(UNAVAILABLE_SCOPE_STRUCTURE);

    rerender({ needed: true });
    await waitFor(() => expect(result.current.structure.status).toBe("ready"));
    expect(result.current.structure.entities).toEqual([{ id: "E-1", label: "Entity One" }]);
    expect(result.current.loading).toBe(false);

    rerender({ needed: false });
    rerender({ needed: true });
    expect(load).toHaveBeenCalledTimes(1);
    expect(load.mock.calls[0]?.[0]?.signal).toBeInstanceOf(AbortSignal);
  });

  it("stays unavailable when the read is refused", async () => {
    const load = vi.fn().mockRejectedValue(new Error("403"));
    const { result } = renderHook(() => useScopeStructure(true, load));
    await waitFor(() => expect(load).toHaveBeenCalled());
    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.structure).toBe(UNAVAILABLE_SCOPE_STRUCTURE);
  });

  it("is ready with only the accounts a permission-limited operator may see", async () => {
    // /api/workstation/scope-options for a trading-only operator: no entity or fund it may read,
    // and its account's fund and entity withheld as null.
    const scopeOptions = vi.fn().mockResolvedValue({
      entities: [],
      funds: [],
      accounts: [{ accountId: "A-1", displayName: "One brokerage", fundId: null, entityId: null }]
    });
    const graph = vi.fn();
    const load = (options: Parameters<typeof loadScopeStructure>[0]) => loadScopeStructure(options, { scopeOptions, graph });
    const { result } = renderHook(() => useScopeStructure(true, load));

    await waitFor(() => expect(result.current.structure.status).toBe("ready"));
    expect(result.current.structure.entities).toEqual([]);
    expect(result.current.structure.funds).toEqual([]);
    expect(result.current.structure.accounts).toEqual([{ id: "A-1", label: "One brokerage", fundId: null, entityId: null }]);
    expect(graph).not.toHaveBeenCalled();
  });

  it("reads the organization graph on a host without the scope-options route", async () => {
    const scopeOptions = vi.fn().mockRejectedValue(new ApiError({ path: "/api/workstation/scope-options", status: 404 }));
    const graph = vi.fn().mockResolvedValue(GRAPH);
    const load = (options: Parameters<typeof loadScopeStructure>[0]) => loadScopeStructure(options, { scopeOptions, graph });
    const { result } = renderHook(() => useScopeStructure(true, load));

    await waitFor(() => expect(result.current.structure.status).toBe("ready"));
    expect(result.current.structure.entities).toEqual([{ id: "E-1", label: "Entity One" }]);
    expect(graph).toHaveBeenCalledTimes(1);
    expect(graph.mock.calls[0]?.[0]?.signal).toBeInstanceOf(AbortSignal);
  });
});
