import { act, cleanup, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ExecutionNotification } from "@/lib/api/execution-workflows.api";
import { useNotificationCenter } from "./use-notification-center";

const mocks = vi.hoisted(() => ({
  getExecutionNotifications: vi.fn(),
  getOperatorInbox: vi.fn(),
  priceAlerts: {
    state: { triggers: [] },
    acknowledgeTrigger: vi.fn(),
    acknowledgeAllTriggers: vi.fn()
  }
}));

vi.mock("@/lib/api/execution-workflows.api", () => ({ getExecutionNotifications: mocks.getExecutionNotifications }));
vi.mock("@/lib/api", () => ({ getOperatorInbox: mocks.getOperatorInbox }));
vi.mock("@/lib/price-alerts/service", () => ({ usePriceAlerts: () => mocks.priceAlerts }));

class NotificationStream {
  static instances: NotificationStream[] = [];
  readonly listeners = new Map<string, EventListener>();
  closed = false;

  constructor(readonly url: string) {
    NotificationStream.instances.push(this);
  }

  addEventListener(type: string, listener: EventListener) {
    this.listeners.set(type, listener);
  }

  close() {
    this.closed = true;
  }

  emit(items: ExecutionNotification[]) {
    this.listeners.get("execution-notifications")?.(new MessageEvent("execution-notifications", { data: JSON.stringify(items) }));
  }
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}

function notification(id: string, accountId = "account-a"): ExecutionNotification {
  return { id, accountId, scope: "paper", severity: "warning", title: id, detail: "Inspect retained order evidence.", timestamp: "2026-11-13T09:00:00Z" };
}

function memoryStorage() {
  const values = new Map<string, string>();
  return {
    getItem: (key: string) => values.get(key) ?? null,
    setItem: (key: string, value: string) => { values.set(key, value); },
    removeItem: (key: string) => { values.delete(key); }
  };
}

describe("execution notification snapshot ordering", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.resetAllMocks();
    NotificationStream.instances = [];
    vi.stubGlobal("EventSource", NotificationStream);
    mocks.getOperatorInbox.mockResolvedValue({ items: [] });
  });

  afterEach(() => {
    cleanup();
    vi.clearAllTimers();
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it("keeps a newer streamed snapshot when the earlier GET completes and preserves local dismissal", async () => {
    const initial = deferred<ExecutionNotification[]>();
    mocks.getExecutionNotifications.mockReturnValueOnce(initial.promise);
    const storage = memoryStorage();
    const { result, unmount } = renderHook(() => useNotificationCenter({ overview: null, fundAccountId: "account-a", storage }));
    const stream = NotificationStream.instances[0];
    const event = notification("newer-stream");

    await act(async () => { stream.emit([event, notification("foreign", "account-b")]); });
    expect(result.current.notifications.map((item) => item.id)).toEqual(["execution:newer-stream"]);
    await act(async () => { initial.resolve([notification("older-get")]); });
    expect(result.current.notifications.map((item) => item.id)).toEqual(["execution:newer-stream"]);

    act(() => { result.current.dismiss("execution:newer-stream"); });
    act(() => { stream.emit([event]); });
    expect(result.current.notifications).toEqual([]);
    expect(event.title).toBe("newer-stream");
    unmount();
    expect(stream.closed).toBe(true);
    expect(mocks.getExecutionNotifications.mock.calls[0][1].signal.aborted).toBe(true);
  });

  it("keeps the latest poll response when GETs complete out of order and retains it on refresh failure", async () => {
    const initial = deferred<ExecutionNotification[]>();
    const poll = deferred<ExecutionNotification[]>();
    mocks.getExecutionNotifications.mockReturnValueOnce(initial.promise).mockReturnValueOnce(poll.promise).mockRejectedValueOnce(new Error("offline"));
    const storage = memoryStorage();
    const { result } = renderHook(() => useNotificationCenter({ overview: null, fundAccountId: "account-a", storage }));

    await act(async () => { vi.advanceTimersByTime(30_000); });
    expect(mocks.getExecutionNotifications).toHaveBeenCalledTimes(2);
    await act(async () => { poll.resolve([notification("latest-poll")]); });
    await act(async () => { initial.resolve([notification("older-get")]); });
    expect(result.current.notifications.map((item) => item.id)).toEqual(["execution:latest-poll"]);
    await act(async () => { vi.advanceTimersByTime(30_000); });
    expect(result.current.notifications.map((item) => item.id)).toEqual(["execution:latest-poll"]);
  });

  it("withholds previous-account requests and queued stream events after the selected account changes", async () => {
    const previous = deferred<ExecutionNotification[]>();
    const current = deferred<ExecutionNotification[]>();
    mocks.getExecutionNotifications.mockReturnValueOnce(previous.promise).mockReturnValueOnce(current.promise);
    const storage = memoryStorage();
    const { result, rerender, unmount } = renderHook(({ account }) => useNotificationCenter({ overview: null, fundAccountId: account, storage }), { initialProps: { account: "account-a" } });
    const oldStream = NotificationStream.instances[0];

    rerender({ account: "account-b" });
    expect(oldStream.closed).toBe(true);
    expect(mocks.getExecutionNotifications.mock.calls[0][1].signal.aborted).toBe(true);
    await act(async () => {
      oldStream.emit([notification("queued-old-stream")]);
      previous.resolve([notification("old-get")]);
      current.resolve([notification("current", "account-b"), notification("foreign")]);
    });
    expect(result.current.notifications.map((item) => item.id)).toEqual(["execution:current"]);
    unmount();
    expect(NotificationStream.instances[1].closed).toBe(true);
    expect(mocks.getExecutionNotifications.mock.calls[1][1].signal.aborted).toBe(true);
  });
});
