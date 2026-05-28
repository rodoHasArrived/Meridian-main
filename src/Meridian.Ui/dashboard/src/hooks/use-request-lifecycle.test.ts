import { act, renderHook } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { useState } from "react";
import { useRequestLifecycle } from "@/hooks/use-request-lifecycle";

describe("useRequestLifecycle", () => {
  it("Scenario_FeedBurst_StaleArrivalDiscarded keeps the newest refresh result", () => {
    const { result } = renderHook(() => {
      const lifecycle = useRequestLifecycle({ operation: "quote refresh", maxRetries: 2 });
      const [value, setValue] = useState("idle");
      return { lifecycle, value, setValue };
    });

    let older: ReturnType<typeof result.current.lifecycle.start>;
    let newer: ReturnType<typeof result.current.lifecycle.start>;
    act(() => {
      older = result.current.lifecycle.start();
      newer = result.current.lifecycle.start();
    });
    expect(older).not.toBeNull();
    expect(newer).not.toBeNull();

    act(() => {
      older?.safeSetState(result.current.setValue, "older");
      result.current.lifecycle.markStale(older!.version);
    });
    expect(result.current.value).toBe("idle");
    expect(result.current.lifecycle.status.staleDiscardCount).toBe(1);

    act(() => {
      newer?.safeSetState(result.current.setValue, "newer");
      result.current.lifecycle.succeed(newer!);
    });
    expect(result.current.value).toBe("newer");
    expect(result.current.lifecycle.status.phase).toBe("succeeded");
  });

  it("Scenario_OperatorNavigatesAway_UnmountAbortsAndBlocksLateState", () => {
    const abortSpy = vi.fn();
    const originalAbort = AbortController.prototype.abort;
    AbortController.prototype.abort = function abort(this: AbortController) {
      abortSpy();
      return originalAbort.call(this);
    };

    try {
      let tokenVersion = 0;
      const { result, unmount } = renderHook(() => {
        const lifecycle = useRequestLifecycle({ operation: "readiness refresh" });
        const [value, setValue] = useState("mounted");
        return { lifecycle, value, setValue };
      });

      act(() => {
        const token = result.current.lifecycle.start();
        tokenVersion = token!.version;
      });
      unmount();

      expect(abortSpy).toHaveBeenCalledTimes(1);
      expect(result.current.lifecycle.isCurrentVersion(tokenVersion)).toBe(false);
    } finally {
      AbortController.prototype.abort = originalAbort;
    }
  });

  it("Scenario_RefreshButtonBurst_DropModeRunsOnlyOneInFlightRequest", () => {
    const { result } = renderHook(() => useRequestLifecycle({ operation: "backfill preview" }));

    let first: ReturnType<typeof result.current.start>;
    let dropped: ReturnType<typeof result.current.start>;
    act(() => {
      first = result.current.start({ busyMode: "drop" });
      dropped = result.current.start({ busyMode: "drop" });
    });

    expect(first).not.toBeNull();
    expect(dropped).toBeNull();
    expect(result.current.status.inFlight).toBe(true);
    expect(result.current.status.version).toBe(first?.version);

    act(() => {
      result.current.succeed(first!);
    });

    let next: ReturnType<typeof result.current.start>;
    act(() => {
      next = result.current.start({ busyMode: "drop" });
    });
    expect(next).not.toBeNull();
    expect(next?.version).toBeGreaterThan(first!.version);
  });

  it("Scenario_RetryableProviderFailure_ExposesBackoffMetadataForUserStatus", () => {
    const { result } = renderHook(() => useRequestLifecycle({
      operation: "provider routing refresh",
      maxRetries: 2,
      baseBackoffMs: 250,
      backoffMultiplier: 3
    }));

    let token: ReturnType<typeof result.current.start>;
    act(() => {
      token = result.current.start();
    });
    act(() => {
      result.current.fail(token!, new Error("rate limited"), { fallback: "Provider refresh failed." });
    });

    expect(result.current.status.phase).toBe("failed");
    expect(result.current.status.error).toContain("rate limited");
    expect(result.current.status.backoff).toMatchObject({
      attempt: 1,
      retryCount: 0,
      nextRetryDelayMs: 250,
      maxRetries: 2
    });
  });
});
