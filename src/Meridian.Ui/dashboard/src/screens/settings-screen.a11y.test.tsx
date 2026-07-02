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

  // Known real violations in settings-screen.tsx (reported, intentionally not fixed here):
  // - aria-required-children: <div role="list"> containers ("Profile authentication and
  //   authorization readiness steps", "Alpaca provider setup checklist") hold h3[tabindex]
  //   children instead of listitem children, and the empty "Scoped access assignments"
  //   role="list" renders a <p> empty state without any listitem.
  // - aria-allowed-role (operations view): fund operations configuration surfaces render
  //   <article role="listitem">, and role "listitem" is not allowed on <article>.
  it("has no basic accessibility violations in the overview/profile task view outside known issues", async () => {
    const { container } = renderWithRouter(<SettingsScreen session={session} overview={overview} />, {
      initialEntries: ["/settings"]
    });

    const results = await axe(container);
    expect(results.violations).toHaveLength(0);
  });

  it("has no basic accessibility violations in the providers task view outside known issues", async () => {
    const { container } = renderWithRouter(<SettingsScreen session={session} overview={overview} />, {
      initialEntries: ["/settings/preferences"]
    });

    const results = await axe(container);
    expect(results.violations).toHaveLength(0);
  });

  it("has no basic accessibility violations in the operations task view outside known issues", async () => {
    const { container } = renderWithRouter(<SettingsScreen session={session} overview={overview} />, {
      initialEntries: ["/settings/integrations"]
    });

    const results = await axe(container);
    expect(results.violations).toHaveLength(0);
  });
});
