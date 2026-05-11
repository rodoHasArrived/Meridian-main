import { describe, expect, it } from "vitest";
import {
  buildAppShellViewState,
  buildCommandPaletteTriggerState,
  buildDevelopmentFixtureNoticeViewModel,
  normalizeWorkspace,
  resolveAppShellCommandPaletteShortcut,
  type AppShellWorkspacePayload
} from "@/app-shell.view-model";
import type { SessionInfo } from "@/types";

const emptyPayload: AppShellWorkspacePayload = {
  session: null,
  overview: null,
  research: null,
  trading: null,
  portfolio: null,
  dataOperations: null,
  governance: null,
  reporting: null
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
      detail: "Loading session state, operator workspaces, and the initial workstation evidence slices.",
      itemListLabel: "Workspace bootstrap status",
      actionLabel: null
    });
    expect(state.routeFocus).toMatchObject({
      routeKey: "/trading",
      announcement: "Trading Workstation loaded.",
      documentTitle: "Trading Workstation - Meridian",
      targetElementId: null,
      fallbackElementId: "workbench-content"
    });
  });

  it("derives route focus state for hash-targeted workflow links", () => {
    const state = buildAppShellViewState({
      pathname: "/settings",
      hash: "#alpaca-provider-setup",
      loading: false,
      error: null,
      workspaceErrors: {},
      payload: sessionPayload
    });

    expect(state.routeFocus).toEqual({
      routeKey: "/settings#alpaca-provider-setup",
      announcement: "Settings Workstation loaded. Jumping to alpaca provider setup.",
      documentTitle: "Settings Workstation - Meridian",
      targetElementId: "alpaca-provider-setup",
      fallbackElementId: "workbench-content"
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
      secondaryActionLabel: "Review diagnostics",
      secondaryActionAriaLabel: "Review Settings capability coverage for failed workstation slices",
      secondaryActionHref: "/settings#backend-capability-coverage",
      itemListLabel: "Failed workstation slices"
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

  it("builds a retryable demo-data notice with route-aware evidence steps", () => {
    const state = buildDevelopmentFixtureNoticeViewModel({
      pathname: "/data/quotes",
      refreshing: true
    });

    expect(state).toMatchObject({
      role: "status",
      ariaLive: "polite",
      title: "Demo data",
      detail: "Showing local fixture responses because the Meridian API host is unavailable.",
      workflowLabel: "Evidence path",
      retryLabel: "Retrying live data",
      retryAriaLabel: "Retrying Meridian API host and live workstation data",
      retryDisabled: true,
      retryBusy: true
    });
    expect(state.steps.map((step) => [step.id, step.active])).toEqual([
      ["watchlist", false],
      ["quotes", true],
      ["readiness", false],
      ["connect", false]
    ]);
  });

  it("includes workflow catalog failures in the shell degraded status", () => {
    const state = buildAppShellViewState({
      pathname: "/strategy",
      loading: false,
      error: null,
      workflowError: "Workflow presets request failed.",
      workspaceErrors: {},
      payload: sessionPayload
    });

    expect(state.canRenderRoutes).toBe(true);
    expect(state.statusPanel).toMatchObject({
      tone: "warning",
      title: "Workstation bootstrap is partially degraded",
      detail: "1 workstation slice failed to load. Available routes remain open while that slice recovers."
    });
    expect(state.statusPanel?.items).toEqual([
      {
        key: "workflow-catalog",
        label: "Workflow catalog",
        detail: "Workflow presets request failed.",
        ariaLabel: "Workflow catalog: Workflow presets request failed."
      }
    ]);
  });

  it("derives accessible command palette trigger state", () => {
    expect(buildCommandPaletteTriggerState(false)).toEqual({
      label: "Open workstation command palette (Ctrl K)",
      placeholder: "Search workflows, routes, presets...",
      shortcutLabel: "Ctrl K",
      controlsId: "command-palette-dialog",
      expanded: false,
      hasPopup: "dialog"
    });

    const state = buildAppShellViewState({
      pathname: "/trading",
      commandPaletteOpen: true,
      loading: false,
      error: null,
      workspaceErrors: {},
      payload: sessionPayload
    });

    expect(state.commandPaletteTrigger).toMatchObject({
      label: "Close workstation command palette (Ctrl K)",
      controlsId: "command-palette-dialog",
      expanded: true,
      hasPopup: "dialog"
    });
  });

  it("keeps global command palette shortcuts out of editable fields until the palette is open", () => {
    expect(resolveAppShellCommandPaletteShortcut({
      key: "k",
      ctrlKey: true,
      targetIsEditable: false,
      commandPaletteOpen: false
    })).toBe("toggle-command-palette");

    expect(resolveAppShellCommandPaletteShortcut({
      key: "k",
      ctrlKey: true,
      targetIsEditable: true,
      commandPaletteOpen: false
    })).toBeNull();

    expect(resolveAppShellCommandPaletteShortcut({
      key: "k",
      metaKey: true,
      targetIsEditable: true,
      commandPaletteOpen: true
    })).toBe("toggle-command-palette");

    expect(resolveAppShellCommandPaletteShortcut({
      key: "k",
      ctrlKey: true,
      shiftKey: true,
      targetIsEditable: false,
      commandPaletteOpen: false
    })).toBeNull();
  });

  it("marks the provider setup anchor as the current demo handoff", () => {
    const state = buildDevelopmentFixtureNoticeViewModel({
      pathname: "/settings",
      hash: "#alpaca-provider-setup"
    });

    expect(state.retryLabel).toBe("Retry live data");
    expect(state.steps.find((step) => step.id === "connect")).toMatchObject({
      href: "/settings#alpaca-provider-setup",
      active: true,
      ariaLabel: "Open Alpaca paper provider setup"
    });
  });
});
