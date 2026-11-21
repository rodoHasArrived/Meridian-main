import { renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useVisibleInterval } from "./use-visible-interval";

function setVisibility(state: "visible" | "hidden") {
  Object.defineProperty(document, "visibilityState", { configurable: true, get: () => state });
  document.dispatchEvent(new Event("visibilitychange"));
}

describe("useVisibleInterval", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    setVisibility("visible");
  });

  afterEach(() => {
    vi.useRealTimers();
    setVisibility("visible");
  });

  it("polls on the interval while visible", () => {
    const callback = vi.fn();
    renderHook(() => useVisibleInterval(callback, 1_000));

    vi.advanceTimersByTime(3_000);
    expect(callback).toHaveBeenCalledTimes(3);
  });

  it("stops polling while hidden and refreshes once on return", () => {
    const callback = vi.fn();
    renderHook(() => useVisibleInterval(callback, 1_000));

    setVisibility("hidden");
    vi.advanceTimersByTime(5_000);
    expect(callback).not.toHaveBeenCalled();

    setVisibility("visible");
    expect(callback).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(1_000);
    expect(callback).toHaveBeenCalledTimes(2);
  });

  it("does not poll when the interval is null", () => {
    const callback = vi.fn();
    renderHook(() => useVisibleInterval(callback, null));

    vi.advanceTimersByTime(10_000);
    expect(callback).not.toHaveBeenCalled();
  });

  it("clears the interval and listener on unmount", () => {
    const callback = vi.fn();
    const { unmount } = renderHook(() => useVisibleInterval(callback, 1_000));
    unmount();

    vi.advanceTimersByTime(5_000);
    setVisibility("hidden");
    setVisibility("visible");
    expect(callback).not.toHaveBeenCalled();
  });
});
