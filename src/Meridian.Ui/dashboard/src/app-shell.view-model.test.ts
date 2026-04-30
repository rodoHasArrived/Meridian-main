import { describe, expect, it } from "vitest";
import { buildAppShellViewState, normalizeWorkspace, type AppShellWorkspacePayload } from "@/app-shell.view-model";
import type { SessionInfo } from "@/types";

const emptyPayload: AppShellWorkspacePayload = {
  session: null,
  overview: null,
  research: null,
  trading: null,
  dataOperations: null,
  governance: null
};

const sessionPayload: AppShellWorkspacePayload = {
  ...emptyPayload,
  session: {
    displayName: "Ops",
    role: "Operator",
    environment: "paper",
    activeWorkspace: "trading",
    commandCount: 4
  } satisfies SessionInfo
};

describe("app shell view model", () => {
  it("normalizes route paths to workspace keys", () => {
    expect(normalizeWorkspace("/")).toBe("trading");
    expect(normalizeWorkspace("/trading/orders")).toBe("trading");
    expect(normalizeWorkspace("/portfolio/positions")).toBe("portfolio");
    expect(normalizeWorkspace("/accounting/reconciliation")).toBe("accounting");
    expect(normalizeWorkspace("/reporting/report-packs")).toBe("reporting");
    expect(normalizeWorkspace("/strategy/runs")).toBe("strategy");
    expect(normalizeWorkspace("/data/backfills")).toBe("data");
    expect(normalizeWorkspace("/settings/integrations")).toBe("settings");
    expect(normalizeWorkspace("/research")).toBe("strategy");
    expect(normalizeWorkspace("/data-operations/backfills")).toBe("data");
    expect(normalizeWorkspace("/governance/security-master")).toBe("accounting");
    expect(normalizeWorkspace("/unknown")).toBe("trading");
  });

  it("shows a loading status while bootstrap is in progress", () => {
    const state = buildAppShellViewState({
      pathname: "/trading",
      loading: true,
      error: null,
      workspaceErrors: {},
      payload: emptyPayload
    });

    expect(state.activeWorkspace.label).toBe("Trading");
    expect(state.canRenderRoutes).toBe(false);
    expect(state.statusPanel).toMatchObject({
      id: "workstation-shell-status-loading",
      titleId: "workstation-shell-status-loading-title",
      detailId: "workstation-shell-status-loading-detail",
      tone: "loading",
      role: "status",
      title: "Booting workstation shell",
      itemListLabel: "Workspace bootstrap status",
      actionLabel: null
    });
  });

  it("keeps available routes open when only some workspace slices fail", () => {
    const state = buildAppShellViewState({
      pathname: "/accounting",
      loading: false,
      error: "Data Operations unavailable",
      workspaceErrors: {
        data: "Backfill summary timed out.",
        accounting: "Reconciliation queue unavailable."
      },
      payload: sessionPayload
    });

    expect(state.canRenderRoutes).toBe(true);
    expect(state.statusPanel).toMatchObject({
      id: "workstation-shell-status-degraded",
      titleId: "workstation-shell-status-degraded-title",
      detailId: "workstation-shell-status-degraded-detail",
      tone: "warning",
      role: "status",
      title: "Workstation bootstrap is partially degraded",
      actionLabel: "Retry failed slices",
      actionAriaLabel: "Retry failed workstation slices",
      itemListLabel: "Failed workspace slices"
    });
    expect(state.statusPanel?.items).toEqual([
      {
        key: "accounting",
        label: "Accounting",
        detail: "Reconciliation queue unavailable.",
        ariaLabel: "Accounting: Reconciliation queue unavailable."
      },
      {
        key: "data",
        label: "Data",
        detail: "Backfill summary timed out.",
        ariaLabel: "Data: Backfill summary timed out."
      }
    ]);
  });

  it("blocks routes and exposes retry copy when no payload loads", () => {
    const state = buildAppShellViewState({
      pathname: "/trading",
      loading: false,
      error: "Network offline",
      workspaceErrors: {
        trading: "Session request failed."
      },
      payload: emptyPayload
    });

    expect(state.canRenderRoutes).toBe(false);
    expect(state.statusPanel).toMatchObject({
      id: "workstation-shell-status-failed",
      titleId: "workstation-shell-status-failed-title",
      detailId: "workstation-shell-status-failed-detail",
      tone: "danger",
      role: "alert",
      ariaLive: "assertive",
      title: "Workstation bootstrap failed",
      detail: "Network offline",
      actionLabel: "Retry bootstrap",
      actionAriaLabel: "Retry workstation bootstrap",
      itemListLabel: "Bootstrap failure details"
    });
  });
});
