import { type ReactNode } from "react";
import { act, cleanup, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useNotificationCenter, type UseNotificationCenterOptions } from "@/hooks/use-notification-center";
import * as executionApi from "@/lib/api/execution-workflows.api";
import { PriceAlertsProvider } from "@/lib/price-alerts/service";
import { PRICE_ALERT_STORAGE_KEY } from "@/lib/price-alerts/types";
import type { StorageLike } from "@/lib/notification-center/read-state";
import type { OperatorInbox, SystemOverviewResponse } from "@/types";

const accountA = "11111111-1111-1111-1111-111111111111";
const accountB = "22222222-2222-2222-2222-222222222222";
const pollIntervalMs = 100;

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}

function memoryStorage(): StorageLike {
  const map = new Map<string, string>();
  return {
    getItem: (key) => map.get(key) ?? null,
    setItem: (key, value) => void map.set(key, value),
    removeItem: (key) => void map.delete(key)
  };
}

function inbox(id: string, fundAccountId = accountA): OperatorInbox {
  return {
    asOf: "2026-11-13T12:00:00Z",
    criticalCount: 1,
    warningCount: 0,
    reviewCount: 1,
    summary: "1 review item",
    items: [{
      workItemId: id,
      kind: "ReconciliationBreak",
      label: `${id} needs review`,
      detail: "Retained reconciliation evidence needs a decision.",
      tone: "Critical",
      createdAt: "2026-11-13T12:00:00Z",
      runId: null,
      fundAccountId,
      auditReference: null,
      workspace: "accounting",
      targetRoute: "/accounting/reconciliation"
    }]
  };
}

const overview = {
  recentEvents: [{
    id: "system-event",
    type: "warning",
    message: "Provider latency elevated.",
    source: "Provider monitor",
    timestamp: "2026-11-13T11:59:00Z"
  }]
} as unknown as SystemOverviewResponse;

function setup(initialFundAccountId: string | undefined = accountA, strictMode = false, inboxPollIntervalMs = pollIntervalMs) {
  const storage = memoryStorage();
  storage.setItem(PRICE_ALERT_STORAGE_KEY, JSON.stringify({
    version: 1,
    alerts: [],
    triggers: [{
      id: "price-trigger",
      alertId: "price-alert",
      symbol: "AAPL",
      condition: "above",
      field: "last",
      threshold: 200,
      triggeredPrice: 201,
      triggeredAt: "2026-11-13T11:58:00Z",
      acknowledged: false,
      note: null
    }]
  }));
  const requests: Array<{
    fundAccountId: string | undefined;
    signal: AbortSignal | undefined;
    pending: ReturnType<typeof deferred<OperatorInbox>>;
  }> = [];
  const fetchInbox = vi.fn<NonNullable<UseNotificationCenterOptions["fetchInbox"]>>((fundAccountId, options) => {
    const pending = deferred<OperatorInbox>();
    requests.push({ fundAccountId, signal: options?.signal, pending });
    return pending.promise;
  });
  const observations: Array<{ fundAccountId: string | undefined; inboxIds: string[] }> = [];
  function Wrapper({ children }: { children: ReactNode }) {
    return <PriceAlertsProvider options={{ storage }}>{children}</PriceAlertsProvider>;
  }
  const hook = renderHook(({ fundAccountId }: { fundAccountId: string | undefined }) => {
    const center = useNotificationCenter({ overview, fundAccountId, fetchInbox, storage, pollIntervalMs: inboxPollIntervalMs });
    observations.push({
      fundAccountId,
      inboxIds: center.notifications.filter((item) => item.source === "inbox").map((item) => item.sourceId)
    });
    return center;
  }, {
    initialProps: { fundAccountId: initialFundAccountId },
    wrapper: Wrapper,
    reactStrictMode: strictMode
  });
  return { ...hook, requests, fetchInbox, observations };
}

function sources(center: ReturnType<typeof useNotificationCenter>) {
  return center.notifications.map((item) => item.source).sort();
}

function inboxIds(center: ReturnType<typeof useNotificationCenter>) {
  return center.notifications.filter((item) => item.source === "inbox").map((item) => item.sourceId);
}

describe("useNotificationCenter inbox lifecycle", () => {
  let visibilityState: "visible" | "hidden";
  beforeEach(() => {
    vi.useFakeTimers();
    visibilityState = "visible";
    vi.spyOn(document, "visibilityState", "get").mockImplementation(() => visibilityState);
    vi.spyOn(executionApi, "getExecutionNotifications").mockResolvedValue([]);
  });
  afterEach(() => {
    cleanup();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it("hides the previous account's rows on every render while the new scope loads or fails", async () => {
    const { result, rerender, requests, observations } = setup();
    await act(async () => requests[0].pending.resolve(inbox("account-a")));
    expect(result.current.inboxStatus).toBe("ready");
    expect(inboxIds(result.current)).toEqual(["account-a"]);

    rerender({ fundAccountId: accountB });
    expect(requests[1].fundAccountId).toBe(accountB);
    expect(result.current.inboxStatus).toBe("loading");
    expect(sources(result.current)).toEqual(["price-alert", "system"]);
    expect(observations.filter((entry) => entry.fundAccountId === accountB).every((entry) => entry.inboxIds.length === 0)).toBe(true);

    await act(async () => requests[1].pending.reject(new Error("account B unavailable")));
    expect(result.current.inboxStatus).toBe("unavailable");
    expect(sources(result.current)).toEqual(["price-alert", "system"]);
    expect(result.current.unreadCount).toBe(2);
  });

  it("aborts obsolete scope reads and ignores a late response even when the transport ignores abort", async () => {
    const { result, rerender, requests } = setup();
    expect(requests[0].signal?.aborted).toBe(false);
    rerender({ fundAccountId: accountB });
    expect(requests[0].signal?.aborted).toBe(true);

    await act(async () => requests[1].pending.resolve(inbox("account-b", accountB)));
    await act(async () => requests[0].pending.resolve(inbox("late-account-a")));
    expect(result.current.inboxStatus).toBe("ready");
    expect(inboxIds(result.current)).toEqual(["account-b"]);
    expect(result.current.unreadCount).toBe(3);
  });

  it("keeps a new A session isolated from old A and B results after an A–B–A round trip", async () => {
    const { result, rerender, requests } = setup();
    rerender({ fundAccountId: accountB });
    rerender({ fundAccountId: accountA });
    expect(requests.map((request) => request.fundAccountId)).toEqual([accountA, accountB, accountA]);
    expect(requests[0].signal?.aborted).toBe(true);
    expect(requests[1].signal?.aborted).toBe(true);

    await act(async () => requests[2].pending.resolve(inbox("current-account-a")));
    await act(async () => requests[0].pending.resolve(inbox("obsolete-account-a")));
    await act(async () => requests[1].pending.reject(new Error("obsolete account B failure")));
    expect(result.current.inboxStatus).toBe("ready");
    expect(inboxIds(result.current)).toEqual(["current-account-a"]);
  });

  it("clears an account-scoped inbox when returning to all authorized scopes", async () => {
    const { result, rerender, requests } = setup();
    await act(async () => requests[0].pending.resolve(inbox("scoped-account-a")));
    rerender({ fundAccountId: undefined });
    expect(requests[1].fundAccountId).toBeUndefined();
    expect(result.current.inboxStatus).toBe("loading");
    expect(inboxIds(result.current)).toEqual([]);

    await act(async () => requests[1].pending.resolve(inbox("authorized-account-b", accountB)));
    expect(result.current.inboxStatus).toBe("ready");
    expect(inboxIds(result.current)).toEqual(["authorized-account-b"]);
  });

  it("does not overlap polling requests while a read remains pending", async () => {
    const { result, requests, fetchInbox } = setup();
    await act(async () => vi.advanceTimersByTimeAsync(pollIntervalMs * 5));
    expect(fetchInbox).toHaveBeenCalledTimes(1);

    await act(async () => requests[0].pending.resolve(inbox("initial")));
    await act(async () => vi.advanceTimersByTimeAsync(pollIntervalMs));
    expect(fetchInbox).toHaveBeenCalledTimes(2);
    await act(async () => vi.advanceTimersByTimeAsync(pollIntervalMs * 5));
    expect(fetchInbox).toHaveBeenCalledTimes(2);
    await act(async () => requests[1].pending.resolve(inbox("polled")));
    expect(inboxIds(result.current)).toEqual(["polled"]);
  });

  it("pauses inbox polling while hidden and refreshes once on return without overlapping a pending read", async () => {
    const { result, requests, fetchInbox } = setup();
    await act(async () => requests[0].pending.resolve(inbox("before-hidden")));
    act(() => {
      visibilityState = "hidden";
      document.dispatchEvent(new Event("visibilitychange"));
    });
    await act(async () => vi.advanceTimersByTimeAsync(pollIntervalMs * 5));
    expect(fetchInbox).toHaveBeenCalledTimes(1);

    act(() => {
      visibilityState = "visible";
      document.dispatchEvent(new Event("visibilitychange"));
    });
    expect(fetchInbox).toHaveBeenCalledTimes(2);
    await act(async () => vi.advanceTimersByTimeAsync(pollIntervalMs * 3));
    expect(fetchInbox).toHaveBeenCalledTimes(2);
    await act(async () => requests[1].pending.resolve(inbox("after-visible")));
    expect(inboxIds(result.current)).toEqual(["after-visible"]);
  });

  it("keeps scoped execution notifications and their undo state available when the inbox fails", async () => {
    vi.mocked(executionApi.getExecutionNotifications).mockResolvedValue([
      {
        id: "execution-warning",
        accountId: accountA,
        scope: "paper",
        severity: "warning",
        title: "Order needs review",
        detail: "Inspect retained execution evidence.",
        timestamp: "2026-11-13T12:00:00Z"
      },
      {
        id: "foreign-execution-warning",
        accountId: accountB,
        scope: "paper",
        severity: "warning",
        title: "Foreign account order",
        detail: "Unavailable in this account scope.",
        timestamp: "2026-11-13T12:00:00Z"
      }
    ]);
    const { result, requests } = setup();
    await act(async () => requests[0].pending.reject(new Error("inbox unavailable")));
    expect(result.current.inboxStatus).toBe("unavailable");
    expect(sources(result.current)).toEqual(["execution", "price-alert", "system"]);

    act(() => result.current.markRead("system:system-event"));
    act(() => result.current.dismiss("execution:execution-warning"));
    expect(sources(result.current)).toEqual(["price-alert", "system"]);
    act(() => result.current.restoreStatus("execution:execution-warning", { read: false }));
    expect(sources(result.current)).toEqual(["execution", "price-alert", "system"]);
    expect(result.current.notifications.find((item) => item.id === "execution:execution-warning")?.read).toBe(false);
    expect(result.current.notifications.find((item) => item.id === "system:system-event")?.read).toBe(true);
  });

  it("withholds failed-poll rows, retains the other feeds, and recovers through retry", async () => {
    const { result, requests } = setup();
    await act(async () => requests[0].pending.resolve(inbox("old-evidence")));
    await act(async () => vi.advanceTimersByTimeAsync(pollIntervalMs));
    await act(async () => requests[1].pending.reject(new Error("poll failed")));
    expect(result.current.inboxStatus).toBe("unavailable");
    expect(inboxIds(result.current)).toEqual([]);
    expect(sources(result.current)).toEqual(["price-alert", "system"]);

    act(() => result.current.retryInbox());
    expect(result.current.inboxStatus).toBe("loading");
    expect(requests).toHaveLength(3);
    await act(async () => requests[2].pending.resolve(inbox("fresh-evidence")));
    expect(result.current.inboxStatus).toBe("ready");
    expect(inboxIds(result.current)).toEqual(["fresh-evidence"]);
    expect(result.current.unreadCount).toBe(3);
  });

  it("times out a hung initial read and retries without accepting its late ignored-abort response", async () => {
    const { result, requests } = setup(accountA, false, 60_000);
    await act(async () => vi.advanceTimersByTimeAsync(15_000));
    expect(requests[0].signal?.aborted).toBe(true);
    expect(result.current.inboxStatus).toBe("unavailable");
    expect(sources(result.current)).toEqual(["price-alert", "system"]);

    act(() => result.current.retryInbox());
    expect(result.current.inboxStatus).toBe("loading");
    expect(requests).toHaveLength(2);
    await act(async () => requests[1].pending.resolve(inbox("retry-after-timeout")));
    await act(async () => requests[0].pending.resolve(inbox("late-timed-out-inbox")));
    expect(result.current.inboxStatus).toBe("ready");
    expect(inboxIds(result.current)).toEqual(["retry-after-timeout"]);
  });

  it("clears a hung poll's old rows and resumes polling while ignoring its late completion", async () => {
    const { result, requests, fetchInbox } = setup(accountA, false, 10_000);
    await act(async () => requests[0].pending.resolve(inbox("before-hung-poll")));
    await act(async () => vi.advanceTimersByTimeAsync(10_000));
    expect(requests).toHaveLength(2);
    await act(async () => vi.advanceTimersByTimeAsync(15_000));
    expect(requests[1].signal?.aborted).toBe(true);
    expect(result.current.inboxStatus).toBe("unavailable");
    expect(inboxIds(result.current)).toEqual([]);
    expect(sources(result.current)).toEqual(["price-alert", "system"]);

    // The next scheduled poll must proceed even if the timed-out promise never settled.
    await act(async () => vi.advanceTimersByTimeAsync(5_000));
    expect(requests).toHaveLength(3);
    await act(async () => requests[1].pending.resolve(inbox("late-timed-out-poll")));
    expect(result.current.inboxStatus).toBe("unavailable");
    expect(inboxIds(result.current)).toEqual([]);

    // An obsolete finally block must not release the newer request's polling slot.
    await act(async () => vi.advanceTimersByTimeAsync(10_000));
    expect(fetchInbox).toHaveBeenCalledTimes(3);
    await act(async () => requests[2].pending.resolve(inbox("recovered-by-poll")));
    expect(result.current.inboxStatus).toBe("ready");
    expect(inboxIds(result.current)).toEqual(["recovered-by-poll"]);
  });

  it("aborts a pending read and stops polling when unmounted", async () => {
    const { unmount, requests, fetchInbox } = setup();
    unmount();
    expect(requests[0].signal?.aborted).toBe(true);
    await act(async () => {
      requests[0].pending.resolve(inbox("late-after-unmount"));
      await vi.advanceTimersByTimeAsync(pollIntervalMs * 5);
    });
    expect(fetchInbox).toHaveBeenCalledTimes(1);
  });

  it("keeps the active StrictMode request usable after the discarded effect settles", async () => {
    const { result, requests } = setup(accountA, true);
    expect(requests).toHaveLength(2);
    expect(requests[0].signal?.aborted).toBe(true);
    expect(requests[1].signal?.aborted).toBe(false);
    await act(async () => requests[1].pending.resolve(inbox("strict-current")));
    await act(async () => requests[0].pending.reject(new Error("discarded StrictMode read")));
    expect(result.current.inboxStatus).toBe("ready");
    expect(inboxIds(result.current)).toEqual(["strict-current"]);
  });
});
