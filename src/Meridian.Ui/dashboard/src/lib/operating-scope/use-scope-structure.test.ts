import { renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { useScopeStructure } from "@/lib/operating-scope/use-scope-structure";
import { UNAVAILABLE_SCOPE_STRUCTURE } from "@/lib/operating-scope/structure-options";

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
});
