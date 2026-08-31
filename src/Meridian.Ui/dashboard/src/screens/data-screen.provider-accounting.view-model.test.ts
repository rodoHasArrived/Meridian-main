import { describe, expect, it } from "vitest";
import {
  buildProviderAccountingPanelState,
  formatResetCountdown
} from "@/screens/data-screen.provider-accounting.view-model";
import {
  buildProviderAccountingCatalogFixture,
  buildProviderConnectionHealthFixture,
  buildProviderRateLimitsFixture
} from "@/screens/data-screen.provider-accounting.test-fixtures";

const now = Date.parse("2026-07-13T12:00:00Z");

describe("provider accounting view model", () => {
  it("projects truthful registration failures and current retry posture", () => {
    const panel = buildProviderAccountingPanelState(
      buildProviderAccountingCatalogFixture(),
      buildProviderRateLimitsFixture(),
      now,
      buildProviderConnectionHealthFixture(null)
    );

    expect(panel.registrationTitle).toBe("1 provider registration failure");
    expect(panel.registrationFailures).toEqual([
      expect.objectContaining({
        stage: "Activate",
        module: "nyse-module",
        error: "InvalidOperationException: Provider construction failed."
      })
    ]);
    expect(panel.rateLimits).toEqual([
      expect.objectContaining({
        provider: "NYSE",
        surface: "Historical",
        status: "Rate limited",
        requestUsage: "8 / 10",
        remaining: "2",
        resetCountdown: "1m 5s",
        failureReason: "Current rate-limit reason: provider response.",
        retryPosture: "Retry after 1m 5s.",
        connectionPosture: "Unknown — reachability unavailable; no runtime diagnostics."
      })
    ]);
    expect(panel.historyPosture).toContain("not retained");
  });

  it("distinguishes explicit disconnection from unavailable reachability", () => {
    const panel = buildProviderAccountingPanelState(
      buildProviderAccountingCatalogFixture(),
      buildProviderRateLimitsFixture(),
      now,
      buildProviderConnectionHealthFixture(false)
    );

    expect(panel.rateLimits[0].connectionPosture)
      .toBe("Disconnected — runtime probe reports unreachable (socket closed).");
  });

  it("distinguishes disabled, reconnecting, degraded, connected, and disconnected states", () => {
    const cases = [
      ["disabled", false, true, 0, "Disabled — provider runtime is not enabled."],
      ["reconnecting", true, false, 3, "Reconnecting — attempt 3; runtime is recovering."],
      ["degraded", true, false, 0, "Degraded — runtime lost healthy reachability."],
      ["connected", true, true, 0, "Connected — runtime probe reports reachable."],
      ["disconnected", true, false, 0, "Disconnected — runtime probe reports unreachable."]
    ] as const;

    for (const [connectionState, isEnabled, isConnected, reconnectAttempts, expected] of cases) {
      const health = buildProviderConnectionHealthFixture(isConnected);
      health.providers[0] = {
        ...health.providers[0],
        isEnabled,
        connectionState,
        reconnectAttempts,
        lastFailureKind: null
      };
      const panel = buildProviderAccountingPanelState(
        buildProviderAccountingCatalogFixture(),
        buildProviderRateLimitsFixture(),
        now,
        health
      );

      expect(panel.rateLimits[0].connectionPosture).toBe(expected);
    }
  });

  it("counts discovery failures when no module registration attempt failed", () => {
    const catalog = buildProviderAccountingCatalogFixture();
    const panel = buildProviderAccountingPanelState(
      {
        ...catalog,
        registrationReport: {
          ...catalog.registrationReport!,
          failedModuleCount: 0,
          failures: [{
            stage: "type-load",
            subject: "Meridian.Providers.BrokenAssembly",
            moduleId: null,
            errorType: "TypeLoadException",
            errorMessage: "Provider type could not load."
          }]
        }
      },
      buildProviderRateLimitsFixture(),
      now,
      buildProviderConnectionHealthFixture(null)
    );

    expect(panel.registrationTitle).toBe("1 provider registration failure");
    expect(panel.registrationFailures).toHaveLength(1);
  });

  it("fails closed when registration and runtime state are unavailable", () => {
    const panel = buildProviderAccountingPanelState(null, null, now);

    expect(panel.registrationTitle).toBe("Registration report unavailable");
    expect(panel.registrationSummary).toContain("cannot be inferred");
    expect(panel.rateLimitSummary).toBe("Current provider rate-limit state is unavailable.");
    expect(panel.rateLimits).toEqual([]);
  });

  it("formats reset countdowns without inventing expired capacity", () => {
    expect(formatResetCountdown("2026-07-13T12:00:01Z", now)).toBe("1s");
    expect(formatResetCountdown("2026-07-13T11:59:59Z", now)).toBe("Reset due");
    expect(formatResetCountdown(null, now)).toBe("No reset pending");
    expect(formatResetCountdown("not-a-date", now)).toBe("Reset time unavailable");
  });
});
