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

  it("Scenario_FullRefreshInvalidatesSubsidiaryRequest_ClearsRunningStatus", () => {
    const { result } = renderHook(() => useRequestLifecycle({ operation: "trading refresh" }));

    let subsidiary: ReturnType<typeof result.current.start>;
    act(() => {
      subsidiary = result.current.start();
    });

    expect(subsidiary).not.toBeNull();
    expect(result.current.status.phase).toBe("running");
    expect(result.current.status.inFlight).toBe(true);

    act(() => {
      result.current.invalidate();
    });

    expect(result.current.status.phase).toBe("stale");
    expect(result.current.status.inFlight).toBe(false);
    expect(result.current.status.message).toBe("An older refresh response was discarded.");

    act(() => {
      expect(result.current.finish(subsidiary!)).toBe(false);
    });

    expect(result.current.status.phase).toBe("stale");
    expect(result.current.status.inFlight).toBe(false);
    expect(result.current.status.staleDiscardCount).toBe(1);
  });

  it("Scenario_RetryableProviderFailure_SchedulesRetryAndExposesBackoffMetadata", () => {
    vi.useFakeTimers();
    try {
      const onRetry = vi.fn();
      const { result } = renderHook(() => useRequestLifecycle({
        operation: "provider routing refresh",
        maxRetries: 2,
        baseBackoffMs: 250,
        backoffMultiplier: 3,
        onRetry
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

      act(() => {
        vi.advanceTimersByTime(249);
      });
      expect(onRetry).not.toHaveBeenCalled();
      act(() => {
        vi.advanceTimersByTime(1);
      });
      expect(onRetry).toHaveBeenCalledTimes(1);
      expect(onRetry).toHaveBeenCalledWith({ attempt: 2 });
    } finally {
      vi.useRealTimers();
    }
  });

  it("Scenario_NoRetryRunner_DoesNotAdvertiseARetryThatWillNeverRun", () => {
    const { result } = renderHook(() => useRequestLifecycle({
      operation: "provider routing refresh",
      maxRetries: 2,
      baseBackoffMs: 250
    }));

    let token: ReturnType<typeof result.current.start>;
    act(() => {
      token = result.current.start();
    });
    act(() => {
      result.current.fail(token!, new Error("rate limited"));
    });

    expect(result.current.status.phase).toBe("failed");
    expect(result.current.status.backoff).toMatchObject({
      attempt: 1,
      retryCount: 0,
      nextRetryDelayMs: null,
      maxRetries: 2
    });
  });

  it("Scenario_RetryBudgetExhausted_FinalFailureStopsScheduling", () => {
    vi.useFakeTimers();
    try {
      const onRetry = vi.fn();
      const { result } = renderHook(() => useRequestLifecycle({
        operation: "trading workspace refresh",
        maxRetries: 1,
        baseBackoffMs: 100,
        onRetry
      }));

      let token: ReturnType<typeof result.current.start>;
      act(() => {
        token = result.current.start();
      });
      act(() => {
        result.current.fail(token!, new Error("outage"));
      });
      expect(result.current.status.backoff.nextRetryDelayMs).toBe(100);

      act(() => {
        vi.advanceTimersByTime(100);
      });
      expect(onRetry).toHaveBeenCalledWith({ attempt: 2 });

      act(() => {
        token = result.current.start({ attempt: 2 });
      });
      act(() => {
        result.current.fail(token!, new Error("outage"));
      });

      expect(result.current.status.backoff).toMatchObject({
        attempt: 2,
        retryCount: 1,
        nextRetryDelayMs: null,
        maxRetries: 1
      });
      act(() => {
        vi.runAllTimers();
      });
      expect(onRetry).toHaveBeenCalledTimes(1);
    } finally {
      vi.useRealTimers();
    }
  });

  it("Scenario_NewerRequestSupersedesPendingRetry_TimerIsDiscarded", () => {
    vi.useFakeTimers();
    try {
      const onRetry = vi.fn();
      const { result } = renderHook(() => useRequestLifecycle({
        operation: "trading workspace refresh",
        maxRetries: 2,
        baseBackoffMs: 500,
        onRetry
      }));

      let token: ReturnType<typeof result.current.start>;
      act(() => {
        token = result.current.start();
      });
      act(() => {
        result.current.fail(token!, new Error("blip"));
      });
      expect(result.current.status.backoff.nextRetryDelayMs).toBe(500);

      // Manual refresh before the retry fires supersedes the pending timer.
      act(() => {
        token = result.current.start();
      });
      act(() => {
        vi.runAllTimers();
      });
      expect(onRetry).not.toHaveBeenCalled();
      expect(result.current.status.phase).toBe("running");
    } finally {
      vi.useRealTimers();
    }
  });

  it("Scenario_OperatorNavigatesAway_PendingRetryIsCancelledOnUnmount", () => {
    vi.useFakeTimers();
    try {
      const onRetry = vi.fn();
      const { result, unmount } = renderHook(() => useRequestLifecycle({
        operation: "trading workspace refresh",
        maxRetries: 2,
        baseBackoffMs: 500,
        onRetry
      }));

      let token: ReturnType<typeof result.current.start>;
      act(() => {
        token = result.current.start();
      });
      act(() => {
        result.current.fail(token!, new Error("blip"));
      });

      unmount();
      act(() => {
        vi.runAllTimers();
      });
      expect(onRetry).not.toHaveBeenCalled();
    } finally {
      vi.useRealTimers();
    }
  });

  it("Scenario_PollingFailsAfterSuccess_LastSucceededAtSurvivesTheFailure", () => {
    const { result } = renderHook(() => useRequestLifecycle({ operation: "quote refresh" }));

    expect(result.current.status.lastSucceededAt).toBeNull();

    let token: ReturnType<typeof result.current.start>;
    act(() => {
      token = result.current.start();
    });
    act(() => {
      result.current.succeed(token!);
    });

    const lastSucceededAt = result.current.status.lastSucceededAt;
    expect(lastSucceededAt).not.toBeNull();
    expect(result.current.status.settledAt).toBe(lastSucceededAt);

    act(() => {
      token = result.current.start();
    });
    act(() => {
      result.current.fail(token!, new Error("provider outage"));
    });

    expect(result.current.status.phase).toBe("failed");
    expect(result.current.status.lastSucceededAt).toBe(lastSucceededAt);
    expect(result.current.status.settledAt).not.toBeNull();
  });

  it("Scenario_PartialRefreshAfterSuccess_PreservesTheLastCompleteFreshness", () => {
    const { result } = renderHook(() => useRequestLifecycle({ operation: "workstation refresh" }));

    let token: ReturnType<typeof result.current.start>;
    act(() => {
      token = result.current.start();
    });
    act(() => {
      result.current.succeed(token!);
    });

    const lastSucceededAt = result.current.status.lastSucceededAt;
    expect(lastSucceededAt).not.toBeNull();

    act(() => {
      token = result.current.start();
    });
    act(() => {
      result.current.partial(token!, new Error("Provider status unavailable."), {
        message: "Workstation data partially refreshed."
      });
    });

    expect(result.current.status.phase).toBe("partial");
    expect(result.current.status.message).toBe("Workstation data partially refreshed.");
    expect(result.current.status.error).toBe("Provider status unavailable.");
    expect(result.current.status.lastSucceededAt).toBe(lastSucceededAt);
    expect(result.current.status.inFlight).toBe(false);
  });

  it("Scenario_StaleSucceedDiscarded_DoesNotAdvanceLastSucceededAt", () => {
    const { result } = renderHook(() => useRequestLifecycle({ operation: "quote refresh" }));

    let older: ReturnType<typeof result.current.start>;
    act(() => {
      older = result.current.start();
      result.current.start();
    });
    act(() => {
      result.current.succeed(older!);
    });

    expect(result.current.status.lastSucceededAt).toBeNull();
    expect(result.current.status.staleDiscardCount).toBe(1);
  });
});
