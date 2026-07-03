import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, render, screen } from "@testing-library/react";
import { FreshnessChip } from "@/components/ui/freshness-chip";

describe("FreshnessChip", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-07-02T12:00:00.000Z"));
  });

  afterEach(() => {
    cleanup();
    vi.useRealTimers();
  });

  it("renders the fresh state for a recent timestamp", () => {
    render(
      <FreshnessChip label="Quotes" staleBudgetMs={10_000} timestamp="2026-07-02T11:59:58.000Z" />
    );
    const chip = screen.getByLabelText("Quotes updated 2s ago.");
    expect(chip.getAttribute("data-freshness-state")).toBe("fresh");
  });

  it("transitions to aging as time passes without new data", () => {
    render(
      <FreshnessChip label="Quotes" staleBudgetMs={5_000} timestamp="2026-07-02T11:59:58.000Z" />
    );
    expect(screen.getByText("2s ago").getAttribute("data-freshness-state")).toBe("fresh");

    act(() => {
      vi.advanceTimersByTime(10_000);
    });

    expect(screen.getByLabelText(/Quotes is stale/).getAttribute("data-freshness-state")).toBe("aging");
  });
});
