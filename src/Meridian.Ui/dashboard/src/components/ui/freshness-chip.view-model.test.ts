import { describe, expect, it } from "vitest";
import {
  buildFreshnessChipViewModel,
  freshnessInputFromLifecycle
} from "@/components/ui/freshness-chip.view-model";
import type { RequestLifecycleStatus } from "@/hooks/use-request-lifecycle";

const NOW = Date.parse("2026-07-02T12:00:00.000Z");

function baseInput(overrides: Partial<Parameters<typeof buildFreshnessChipViewModel>[0]> = {}) {
  return {
    timestamp: "2026-07-02T11:59:55.000Z",
    staleBudgetMs: 10_000,
    now: NOW,
    label: "Quotes",
    ...overrides
  };
}

function lifecycleStatus(overrides: Partial<RequestLifecycleStatus> = {}): RequestLifecycleStatus {
  return {
    operation: "test refresh",
    phase: "succeeded",
    inFlight: false,
    version: 1,
    message: "Refresh complete.",
    error: null,
    startedAt: "2026-07-02T11:59:54.000Z",
    settledAt: "2026-07-02T11:59:55.000Z",
    lastSucceededAt: "2026-07-02T11:59:55.000Z",
    staleDiscardCount: 0,
    backoff: { attempt: 1, retryCount: 0, nextRetryDelayMs: null, maxRetries: 0 },
    ...overrides
  };
}

describe("buildFreshnessChipViewModel", () => {
  it("reports fresh within the staleness budget", () => {
    const vm = buildFreshnessChipViewModel(baseInput());
    expect(vm.state).toBe("fresh");
    expect(vm.badgeVariant).toBe("outline");
    expect(vm.text).toBe("5s ago");
    expect(vm.ariaLabel).toBe("Quotes updated 5s ago.");
  });

  it("reports aging past the staleness budget", () => {
    const vm = buildFreshnessChipViewModel(baseInput({ timestamp: "2026-07-02T11:58:00.000Z" }));
    expect(vm.state).toBe("aging");
    expect(vm.badgeVariant).toBe("warning");
    expect(vm.text).toBe("Stale · 2m ago");
  });

  it("suppresses the aging tone while a refresh is in flight", () => {
    const vm = buildFreshnessChipViewModel(
      baseInput({ timestamp: "2026-07-02T11:58:00.000Z", inFlight: true })
    );
    expect(vm.state).toBe("fresh");
  });

  it("reports failing when an error is present, keeping the last success age", () => {
    const vm = buildFreshnessChipViewModel(
      baseInput({ timestamp: "2026-07-02T11:58:00.000Z", errorMessage: "HTTP 503" })
    );
    expect(vm.state).toBe("failing");
    expect(vm.badgeVariant).toBe("danger");
    expect(vm.text).toBe("Failing · 2m ago");
    expect(vm.ariaLabel).toContain("HTTP 503");
    expect(vm.ariaLabel).toContain("Last success 2m ago.");
  });

  it("reports unknown when no timestamp exists", () => {
    const vm = buildFreshnessChipViewModel(baseInput({ timestamp: null }));
    expect(vm.state).toBe("unknown");
    expect(vm.text).toBe("No data");
    expect(vm.ariaLabel).toBe("Quotes has not been updated yet.");
  });

  it("gives live precedence over everything else", () => {
    const vm = buildFreshnessChipViewModel(
      baseInput({ live: true, errorMessage: "ignored", timestamp: null })
    );
    expect(vm.state).toBe("live");
    expect(vm.dot).toBe(true);
    expect(vm.badgeVariant).toBe("success");
  });
});

describe("freshnessInputFromLifecycle", () => {
  it("maps lastSucceededAt and reports no error on success", () => {
    const input = freshnessInputFromLifecycle(lifecycleStatus(), 10_000, "Quotes");
    expect(input.timestamp).toBe("2026-07-02T11:59:55.000Z");
    expect(input.errorMessage).toBeNull();
  });

  it("surfaces the error and retry detail on failure", () => {
    const input = freshnessInputFromLifecycle(
      lifecycleStatus({
        phase: "failed",
        error: "HTTP 503",
        backoff: { attempt: 2, retryCount: 1, nextRetryDelayMs: 2000, maxRetries: 3 }
      }),
      10_000,
      "Quotes"
    );
    expect(input.errorMessage).toBe("HTTP 503");
    expect(input.detail).toBe("Retrying in 2s");
  });
});
