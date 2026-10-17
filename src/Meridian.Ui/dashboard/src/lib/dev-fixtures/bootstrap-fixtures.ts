import type { FirstRunStatus } from "../../features/first-run/types";
import { WORKSTATION_API_ENDPOINTS } from "../workstation-endpoints";

// This represents an already prepared sample desk, never the activation state of a real host.
const fixtureFirstRunStatus: FirstRunStatus = {
  isComplete: true,
  goal: "monitor-investments",
  starterKitId: "personal-portfolio",
  dataChoice: "sample",
  workspace: {
    id: "dev-fixture-workspace",
    name: "Meridian Demo Workspace",
    isSample: true,
    badge: "SAMPLE · PAPER",
    safetyMessage: "Development fixtures only. No live provider data or backend mutations.",
    samplePackVersion: "dev-fixtures-v1"
  },
  starterKits: [{
    id: "personal-portfolio",
    name: "Personal Portfolio Desk",
    goal: "monitor-investments",
    description: "Explore sample holdings, watchlists, and portfolio changes.",
    defaultRoute: "/portfolio"
  }],
  outcomes: [
    { key: "workspace-opened", label: "Open or create a workspace", actionLabel: "Open workspace", route: "/portfolio" },
    { key: "data-imported", label: "Import sample or real data", actionLabel: "Import data", route: "/accounting/statement-import" },
    { key: "validation-resolved", label: "Resolve one validation issue", actionLabel: "Review breaks", route: "/accounting/reconciliation/match" },
    { key: "report-run", label: "Run one report", actionLabel: "Run report", route: "/reporting/run" },
    { key: "result-saved", label: "Save or export one useful result", actionLabel: "Review results", route: "/reporting/library" }
  ].map((outcome) => ({ ...outcome, isComplete: true, completedAtUtc: "2026-05-08T15:00:00.000Z" })),
  recommendedActions: [{
    label: "Explore the sample desk", route: "/portfolio", description: "Review development fixture data."
  }],
  sampleWorkspace: null
};

export const bootstrapFixtureRoutes = {
  [WORKSTATION_API_ENDPOINTS.firstRunStatus]: fixtureFirstRunStatus,
  [WORKSTATION_API_ENDPOINTS.firstRunStatus.replace(/\/$/, "")]: fixtureFirstRunStatus,
  "/api/demo/mode": { enabled: true, provenance: "seeded" }
} satisfies Record<string, unknown>;
