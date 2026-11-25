import { renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { useSignedInUsername } from "@/hooks/use-signed-in-username";

describe("useSignedInUsername", () => {
  it("returns the signed-in username once loaded", async () => {
    const load = vi.fn().mockResolvedValue("a.chen");
    const { result } = renderHook(() => useSignedInUsername(load));
    expect(result.current).toBeNull();
    await waitFor(() => expect(result.current).toBe("a.chen"));
    expect(load).toHaveBeenCalledTimes(1);
  });

  it("stays null when the identity read fails, never falling back to a desk label", async () => {
    const load = vi.fn().mockRejectedValue(new Error("offline"));
    const { result } = renderHook(() => useSignedInUsername(load));
    await waitFor(() => expect(load).toHaveBeenCalled());
    await Promise.resolve();
    expect(result.current).toBeNull();
  });

  it("aborts the read on unmount", () => {
    let signal: AbortSignal | undefined;
    const load = vi.fn((s: AbortSignal) => { signal = s; return new Promise<string | null>(() => undefined); });
    const { unmount } = renderHook(() => useSignedInUsername(load));
    unmount();
    expect(signal?.aborted).toBe(true);
  });
});
