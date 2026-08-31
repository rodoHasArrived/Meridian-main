import { beforeEach, describe, expect, it, vi } from "vitest";
import { axe } from "jest-axe";
import { SettingsScreen } from "@/screens/settings-screen";
import { renderWithRouter } from "@/test/render";
import type { SessionInfo, SystemOverviewResponse } from "@/types";

const apiMocks = vi.hoisted(() => ({
  listScopedAccessAssignments: vi.fn()
}));

vi.mock("@/lib/api", async (importActual) => ({
  ...(await importActual<typeof import("@/lib/api")>()),
  listScopedAccessAssignments: apiMocks.listScopedAccessAssignments
}));

const session: SessionInfo = {
  displayName: "Andrew Rowden",
  role: "Fund Manager",
  environment: "paper",
  activeWorkspace: "settings",
  commandCount: 42
};

const overview: SystemOverviewResponse = {
  systemStatus: "Degraded",
  providersOnline: 2,
  providersTotal: 3,
  activeRuns: 1,
  openPositions: 5,
  activeBackfills: 0,
  symbolsMonitored: 120,
  storageHealth: "Warning",
  lastHeartbeatUtc: "2026-05-01T00:00:00Z",
  metrics: [],
  recentEvents: [
    {
      id: "evt-1",
      type: "warning",
      message: "Brokerage sync delayed.",
      source: "Provider health",
      timestamp: "2026-05-01T00:00:00Z"
    }
  ]
};

describe("SettingsScreen accessibility", () => {
  beforeEach(() => {
    apiMocks.listScopedAccessAssignments.mockReset();
    apiMocks.listScopedAccessAssignments.mockImplementation(() => new Promise(() => undefined));
  });

  it("has no basic accessibility violations in the Settings task chooser", async () => {
    const { container } = renderWithRouter(<SettingsScreen session={session} overview={overview} />, {
      initialEntries: ["/settings"]
    });

    const results = await axe(container);
    expect(results.violations).toHaveLength(0);
  });

  it("has no basic accessibility violations in the Preferences task view", async () => {
    const { container } = renderWithRouter(<SettingsScreen session={session} overview={overview} />, {
      initialEntries: ["/settings/preferences"]
    });

    const results = await axe(container);
    expect(results.violations).toHaveLength(0);
  });

  it("has no basic accessibility violations in the canonical Accounting Systems task view", async () => {
    const { container } = renderWithRouter(<SettingsScreen session={session} overview={overview} />, {
      initialEntries: ["/settings/accounting-systems"]
    });

    const results = await axe(container);
    expect(results.violations).toHaveLength(0);
  });

  it.each([
    ["access", "/settings/access"],
    ["providers", "/settings/providers"],
    ["guided provider setup", "/settings/providers/alpaca/setup"],
    ["advanced provider controls", "/settings/providers/alpaca/advanced"],
    ["diagnostics", "/settings/diagnostics"],
    ["advanced diagnostics", "/settings/diagnostics/advanced"]
  ])("has no basic accessibility violations in the %s route view", async (_view, route) => {
    const { container } = renderWithRouter(<SettingsScreen session={session} overview={overview} />, {
      initialEntries: [route]
    });

    const results = await axe(container);
    expect(results.violations).toHaveLength(0);
  });
});
