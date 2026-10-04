import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";
import {
  buildAppShellViewState,
  buildCommandPaletteTriggerState,
  buildDevelopmentFixtureNoticeViewModel,
  normalizeWorkspace,
  resolveAppShellLocation,
  resolveAppShellCommandPaletteShortcut,
  type AppShellWorkspacePayload
} from "@/app-shell.view-model";
import { appendOperatingScopeToRoute } from "@/app-shell.operating-scope";
import type { SessionInfo } from "@/types";

const emptyPayload: AppShellWorkspacePayload = {
  session: null,
  overview: null,
  strategy: null,
  trading: null,
  portfolio: null,
  data: null,
  accounting: null,
  reporting: null,
  workflowSummary: null
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
  it("keeps the skip link outside screenshot bounds until keyboard focus", () => {
    const shellStyles = readFileSync(resolve(process.cwd(), "src/styles/app-shell.css"), "utf8");
    const skipLinkRule = shellStyles.match(/\.skip-link\s*\{(?<rule>[\s\S]*?)\}/)?.groups?.rule ?? "";
    const focusedSkipLinkRule = shellStyles.match(/\.skip-link:focus\s*\{(?<rule>[\s\S]*?)\}/)?.groups?.rule ?? "";

    expect(skipLinkRule).toContain("left: -100vw");
    expect(skipLinkRule).not.toContain("transform:");
    expect(focusedSkipLinkRule).toContain("left: 0.75rem");
  });

  it("lets every masthead track shrink rather than spill past the viewport", () => {
    const shellStyles = readFileSync(resolve(process.cwd(), "src/styles/app-shell.css"), "utf8");
    const mastheadRule = shellStyles.match(/\.workstation-masthead\s*\{(?<rule>[\s\S]*?)\}/)?.groups?.rule ?? "";
    const columns = mastheadRule.match(/grid-template-columns:(?<value>[^;]*);/)?.groups?.value ?? "";

    expect(columns).not.toBe("");
    // A track floor beats the child's own `min-width: 0`, so any non-zero minimum here
    // adds to a row minimum that the 980px stack does not rescue in between.
    expect(columns).not.toMatch(/minmax\(\s*(?!0[,)])[^,]+,/);
    expect(columns.match(/minmax\(/g) ?? []).toHaveLength(3);
  });

  it("ellipsizes the session identity instead of pushing it off the masthead", () => {
    const shellStyles = readFileSync(resolve(process.cwd(), "src/styles/app-shell.css"), "utf8");
    const nameRule = shellStyles.match(/\.workstation-session-name\s*\{(?<rule>[\s\S]*?)\}/)?.groups?.rule ?? "";
    const roleRule = shellStyles.match(/\.workstation-session-role\s*\{(?<rule>[\s\S]*?)\}/)?.groups?.rule ?? "";

    for (const rule of [nameRule, roleRule]) {
      expect(rule).toContain("white-space: nowrap");
      // Flex items default to `min-width: auto` and refuse to shrink below their text,
      // which would leave the overflow rules dead and the card spilling.
      expect(rule).toContain("min-width: 0");
      expect(rule).toContain("overflow: hidden");
      expect(rule).toContain("text-overflow: ellipsis");
    }
  });

  it("holds the environment badge at full width while the rest of the session card gives way", () => {
    const shellStyles = readFileSync(resolve(process.cwd(), "src/styles/app-shell.css"), "utf8");
    const badgeRule =
      shellStyles.match(/\.workstation-session-card\s*>\s*:first-child\s*\{(?<rule>[\s\S]*?)\}/)?.groups?.rule ?? "";

    // PAPER vs LIVE is the one thing in this card an operator cannot afford to lose.
    expect(badgeRule).toContain("flex: none");
  });

  it("keeps route-owned continuity builders outside shell internals", () => {
    const source = readFileSync(resolve(process.cwd(), "src/app-shell.view-model.ts"), "utf8");
    const continuitySource = readFileSync(resolve(process.cwd(), "src/app-shell.workflow-continuity.ts"), "utf8");
    const continuityTypesSource = readFileSync(resolve(process.cwd(), "src/app-shell.workflow-continuity-types.ts"), "utf8");
    const operatingScopeSource = readFileSync(resolve(process.cwd(), "src/app-shell.operating-scope.ts"), "utf8");
    const continuityViewModelSource = readFileSync(resolve(process.cwd(), "src/app-shell.workflow-continuity-view-model.ts"), "utf8");
    const statusPanelSource = readFileSync(resolve(process.cwd(), "src/app-shell.status-panel.ts"), "utf8");
    const trustStripSource = readFileSync(resolve(process.cwd(), "src/app-shell.trust-strip.ts"), "utf8");
    const commandPaletteSource = readFileSync(resolve(process.cwd(), "src/app-shell.command-palette.ts"), "utf8");
    const demoFixtureSource = readFileSync(resolve(process.cwd(), "src/app-shell.development-fixture-notice.ts"), "utf8");
    const routeFocusSource = readFileSync(resolve(process.cwd(), "src/app-shell.route-focus.ts"), "utf8");

    expect(continuitySource).toContain("const workflowContinuityTrails");
    expect(continuitySource).toContain("const primaryOperatorWorkflowStepDefinitions");
    expect(continuitySource).toContain("function resolvePrimaryOperatorWorkflowStepId");
    expect(continuitySource).toContain("function selectWorkflowContinuityTrail");
    expect(continuitySource).toContain("function findActiveWorkflowStepIndex");
    expect(continuitySource).toContain("function scoreWorkflowTrailWorkspaceAffinity");
    expect(continuitySource).toContain("function scoreWorkflowStepRouteMatch");
    expect(continuitySource).toContain('title: "Market Data To Paper"');
    expect(continuitySource).toContain('title: "Accounting Closeout"');
    expect(continuityTypesSource).toContain("export interface AppShellWorkflowContinuityViewModel");
    expect(continuityTypesSource).toContain("export interface AppShellDecisionBrief");
    expect(continuityTypesSource).toContain("export interface AppShellOperatorFocusItem");
    expect(continuityTypesSource).toContain("export interface AppShellEvidenceTimelineItem");
    expect(continuityTypesSource).toContain("export type AppShellWorkflowContinuityStatusTone");
    expect(operatingScopeSource).toContain("export function readOperatingScopeFromSearch");
    expect(operatingScopeSource).toContain("export function buildOperatingScopeFromSearch");
    expect(operatingScopeSource).toContain("export function appendOperatingScopeToRoute");
    expect(operatingScopeSource).toContain("export function summarizeOperatingScopeForRoute");
    expect(operatingScopeSource).toContain("function operatingScopeKeysForRoute");
    expect(operatingScopeSource).toContain("function appendSearchValue");
    expect(continuityViewModelSource).toContain("export function buildWorkflowContinuityViewModel");
    expect(continuityViewModelSource).toContain('from "@/app-shell.workflow-continuity-types"');
    expect(continuityViewModelSource).toContain("interface WorkflowContinuityStatusContext");
    expect(continuityViewModelSource).toContain("buildTrustedDataContinuityStatus");
    expect(continuityViewModelSource).toContain("buildStrategyContinuityStatus");
    expect(continuityViewModelSource).toContain("buildPaperReadinessContinuityStatus");
    expect(continuityViewModelSource).toContain("buildProviderSetupContinuityStatus");
    expect(continuityViewModelSource).toContain("buildPortfolioLedgerContinuityStatus");
    expect(continuityViewModelSource).toContain("buildFinancialOperationsWorkflowStepStatus");
    expect(continuityViewModelSource).toContain("buildAccountingEvidenceTimelineItems");
    expect(continuityViewModelSource).toContain("buildDataEvidenceTimelineItems");
    expect(continuityViewModelSource).toContain("buildPortfolioEvidenceTimelineItems");
    expect(continuityViewModelSource).toContain("buildStrategyEvidenceTimelineItems");
    expect(continuityViewModelSource).toContain("buildTradingEvidenceTimelineItems");
    expect(continuityViewModelSource).toContain("buildAccountingOperatorFocusItems");
    expect(continuityViewModelSource).toContain("buildDataOperatorFocusItems");
    expect(continuityViewModelSource).toContain("buildPortfolioOperatorFocusItems");
    expect(continuityViewModelSource).toContain("buildAccountingLinkedContextItem");
    expect(continuityViewModelSource).toContain("buildAccountingReconciliationContinuityStatus");
    expect(continuityViewModelSource).toContain("buildDataLinkedContextItem");
    expect(continuityViewModelSource).toContain("buildPortfolioLinkedContextItem");
    expect(continuityViewModelSource).toContain("buildReportingOperatorFocusItems");
    expect(continuityViewModelSource).toContain("buildReportingLinkedContextItem");
    expect(continuityViewModelSource).toContain("buildReportingGovernedReportContinuityStatus");
    expect(continuityViewModelSource).toContain("buildStrategyOperatorFocusItems");
    expect(continuityViewModelSource).toContain("buildTradingLinkedContextItem");
    expect(continuityViewModelSource).toContain("buildTradingOperatorFocusItems");
    expect(statusPanelSource).toContain("export function buildShellStatusPanel");
    expect(statusPanelSource).toContain("export function buildShellFailureItems");
    expect(statusPanelSource).toContain("function formatUserVisibleWorkspaceError");
    expect(statusPanelSource).toContain("function looksLikeRawTechnicalResponse");
    expect(statusPanelSource).toContain('title: "Workspace data unavailable"');
    expect(trustStripSource).toContain("export function buildTrustStripState");
    expect(trustStripSource).toContain("function buildProviderTrustStripItem");
    expect(trustStripSource).toContain("function formatCount");
    expect(trustStripSource).toContain("__APP_VERSION__");
    expect(trustStripSource).toContain("Provider posture has not loaded yet.");
    expect(commandPaletteSource).toContain("export function buildCommandPaletteTriggerState");
    expect(commandPaletteSource).toContain("export function resolveAppShellCommandPaletteShortcut");
    expect(commandPaletteSource).toContain("export function isAppShellEditableShortcutTarget");
    expect(commandPaletteSource).toContain('const COMMAND_PALETTE_DIALOG_ID = "command-palette-dialog"');
    expect(demoFixtureSource).toContain("export function buildDevelopmentFixtureNoticeViewModel");
    expect(demoFixtureSource).toContain("const developmentFixtureDemoSteps");
    expect(demoFixtureSource).toContain("function isCurrentDevelopmentFixtureDemoStep");
    expect(demoFixtureSource).toContain("Showing demo data because live Meridian data is unavailable");
    expect(routeFocusSource).toContain("export function buildRouteFocusState");
    expect(routeFocusSource).toContain("function normalizeHashTarget");
    expect(routeFocusSource).toContain("function formatHashTargetLabel");
    expect(routeFocusSource).toContain('fallbackElementId: "workbench-content"');
    expect(source).toContain("buildWorkflowContinuityViewModel");
    expect(source).toContain("buildShellStatusPanel");
    expect(source).toContain("buildShellFailureItems");
    expect(source).toContain("buildTrustStripState");
    expect(source).toContain("buildCommandPaletteTriggerState");
    expect(source).toContain("buildRouteFocusState");
    expect(source).not.toContain("buildTrustedDataContinuityStatus");
    expect(source).not.toContain("buildStrategyContinuityStatus");
    expect(source).not.toContain("buildPaperReadinessContinuityStatus");
    expect(source).not.toContain("buildProviderSetupContinuityStatus");
    expect(source).not.toContain("buildPortfolioLedgerContinuityStatus");
    expect(source).not.toContain("buildFinancialOperationsWorkflowStepStatus");
    expect(source).not.toContain("buildAccountingEvidenceTimelineItems");
    expect(source).not.toContain("buildDataEvidenceTimelineItems");
    expect(source).not.toContain("buildPortfolioEvidenceTimelineItems");
    expect(source).not.toContain("buildStrategyEvidenceTimelineItems");
    expect(source).not.toContain("buildTradingEvidenceTimelineItems");
    expect(source).not.toContain("buildAccountingOperatorFocusItems");
    expect(source).not.toContain("buildDataOperatorFocusItems");
    expect(source).not.toContain("buildPortfolioOperatorFocusItems");
    expect(source).not.toContain("buildAccountingLinkedContextItem");
    expect(source).not.toContain("buildAccountingReconciliationContinuityStatus");
    expect(source).not.toContain("buildDataLinkedContextItem");
    expect(source).not.toContain("buildPortfolioLinkedContextItem");
    expect(source).not.toContain("buildReportingOperatorFocusItems");
    expect(source).not.toContain("buildReportingLinkedContextItem");
    expect(source).not.toContain("buildReportingGovernedReportContinuityStatus");
    expect(source).not.toContain("buildStrategyOperatorFocusItems");
    expect(source).not.toContain("buildTradingLinkedContextItem");
    expect(source).not.toContain("buildTradingOperatorFocusItems");
    expect(source).not.toContain("buildResearchContinuityStatus");
    expect(source).not.toContain("buildOperatorFocusCandidateFromResearchRun");
    expect(source).not.toContain("const workflowContinuityTrails");
    expect(source).not.toContain("const primaryOperatorWorkflowStepDefinitions");
    expect(source).not.toContain("function resolvePrimaryOperatorWorkflowStepId");
    expect(source).not.toContain("function selectWorkflowContinuityTrail");
    expect(source).not.toContain("function findActiveWorkflowStepIndex");
    expect(source).not.toContain("function scoreWorkflowTrailWorkspaceAffinity");
    expect(source).not.toContain("function scoreWorkflowStepRouteMatch");
    expect(source).not.toContain("export interface AppShellWorkflowContinuityViewModel");
    expect(source).not.toContain("export interface AppShellDecisionBrief");
    expect(source).not.toContain("export interface AppShellOperatorFocusItem");
    expect(source).not.toContain("export interface AppShellEvidenceTimelineItem");
    expect(source).not.toContain("export type AppShellWorkflowContinuityStatusTone");
    expect(source).not.toContain("function buildWorkflowContinuityViewModel");
    expect(source).not.toContain("interface WorkflowContinuityStatusContext");
    expect(source).not.toContain('title: "Market Data To Paper"');
    expect(source).not.toContain('title: "Accounting Closeout"');
    expect(source).not.toContain("function buildTrustedDataContinuityStatus");
    expect(source).not.toContain("function buildStrategyContinuityStatus");
    expect(source).not.toContain("function buildPaperReadinessContinuityStatus");
    expect(source).not.toContain("function buildProviderSetupContinuityStatus");
    expect(source).not.toContain("function buildPortfolioLedgerContinuityStatus");
    expect(source).not.toContain("function buildFinancialOperationsWorkflowStepStatus");
    expect(source).not.toContain("function buildAccountingEvidenceTimelineItems");
    expect(source).not.toContain("function buildDataEvidenceTimelineItems");
    expect(source).not.toContain("function buildPortfolioEvidenceTimelineItems");
    expect(source).not.toContain("function buildStrategyEvidenceTimelineItems");
    expect(source).not.toContain("function buildTradingEvidenceTimelineItems");
    expect(source).not.toContain("function buildDataFocusItems");
    expect(source).not.toContain("function buildPortfolioFocusItems");
    expect(source).not.toContain("function buildStrategyFocusItems");
    expect(source).not.toContain("function buildTradingFocusItems");
    expect(source).not.toContain("function buildAccountingLinkedContextItem");
    expect(source).not.toContain("function buildDataLinkedContextItem");
    expect(source).not.toContain("function buildPortfolioLinkedContextItem");
    expect(source).not.toContain("function buildReportingLinkedContextItem");
    expect(source).not.toContain("function buildTradingLinkedContextItem");
    expect(source).not.toContain("function buildLinkedContextItem");
    expect(source).not.toContain("function buildReconciliationContinuityStatus");
    expect(source).not.toContain("function buildGovernedReportContinuityStatus");
    expect(source).not.toContain("function buildCloseSupportContinuityStatus");
    expect(source).not.toContain("function buildAccountingFocusItems");
    expect(source).not.toContain("function buildReportingFocusItems");
    expect(source).not.toContain("function readOperatingScopeFromSearch");
    expect(source).not.toContain("function buildOperatingScopeFromSearch");
    expect(source).not.toContain("function appendOperatingScopeToRoute");
    expect(source).not.toContain("function summarizeOperatingScopeForRoute");
    expect(source).not.toContain("function operatingScopeKeysForRoute");
    expect(source).not.toContain("function appendSearchValue");
    expect(source).not.toContain("function buildShellStatusPanel");
    expect(source).not.toContain("function buildShellFailureItems");
    expect(source).not.toContain("function formatUserVisibleWorkspaceError");
    expect(source).not.toContain("function looksLikeRawTechnicalResponse");
    expect(source).not.toContain("function buildTrustStripState");
    expect(source).not.toContain("function buildProviderTrustStripItem");
    expect(source).not.toContain("function titleCase");
    expect(source).not.toContain("function formatCount");
    expect(source).not.toContain("__APP_VERSION__");
    expect(source).not.toContain("Provider posture has not loaded yet.");
    expect(source).not.toContain("export function buildCommandPaletteTriggerState");
    expect(source).not.toContain("export function resolveAppShellCommandPaletteShortcut");
    expect(source).not.toContain("export function isAppShellEditableShortcutTarget");
    expect(source).not.toContain("export function buildDevelopmentFixtureNoticeViewModel");
    expect(source).not.toContain("const developmentFixtureDemoSteps");
    expect(source).not.toContain("function isCurrentDevelopmentFixtureDemoStep");
    expect(source).not.toContain("Showing demo data because live Meridian data is unavailable");
    expect(source).not.toContain("export function buildRouteFocusState");
    expect(source).not.toContain("function normalizeHashTarget");
    expect(source).not.toContain("function formatHashTargetLabel");
  });

  it("normalizes route paths to workspace keys", () => {
    expect(normalizeWorkspace("/")).toBe("trading");
    expect(normalizeWorkspace("/trading/orders")).toBe("trading");
    expect(normalizeWorkspace("/portfolio/positions")).toBe("portfolio");
    expect(normalizeWorkspace("/accounting/reconciliation")).toBe("accounting");
    expect(normalizeWorkspace("/reporting/report-packs")).toBe("reporting");
    expect(normalizeWorkspace("/strategy/runs")).toBe("strategy");
    expect(normalizeWorkspace("/data/backfills")).toBe("data");
    expect(normalizeWorkspace("/settings/integrations")).toBe("settings");
    expect(normalizeWorkspace("/strategy")).toBe("strategy");
    expect(normalizeWorkspace("/data-operations/backfills")).toBe("data");
    expect(normalizeWorkspace("/accounting/security-master")).toBe("accounting");
    expect(normalizeWorkspace("/unknown")).toBe("trading");
  });

  it("models the root as shell home without adding an eighth workspace", () => {
    expect(resolveAppShellLocation("/")).toEqual({ kind: "home" });
    expect(resolveAppShellLocation("/accounting/reconciliation")).toEqual({
      kind: "workspace",
      workspaceKey: "accounting"
    });
  });

  it("treats the root route as the Daily Control Tower shell focus", () => {
    const state = buildAppShellViewState({
      pathname: "/",
      loading: false,
      error: null,
      workspaceErrors: {},
      payload: sessionPayload
    });

    expect(state.routeFocus).toMatchObject({
      announcement: "Daily Control Tower loaded.",
      documentTitle: "Daily Control Tower - Meridian",
      fallbackElementId: "workbench-content"
    });
    expect(state.location).toEqual({ kind: "home" });
    expect(state.workflowContinuity.contextValue).toBe("Daily Control Tower / Today");
    expect(state.workflowContinuity.title).toBe("Daily Control Tower");
    expect(state.workflowContinuity.steps.map((step) => step.label)).toEqual([
      "Today",
      "Exceptions",
      "Close",
      "Reconciliation",
      "Ledger",
      "Reports",
      "Evidence",
      "Data Health"
    ]);
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
    expect(state.workflowContinuity).toMatchObject({
      operatorFocusSummary: "Loading cross-workspace operator posture.",
      operatorFocusEmptyText: "Ranked focus actions will appear after workspace data loads.",
      operatorFocusItems: [],
      evidenceTimelineSummary: "Loading cross-workspace evidence timeline.",
      evidenceTimelineEmptyText: "Recent audit and workflow events will appear after workspace data loads.",
      evidenceTimelineItems: [],
      linkedContextSummary: "Loading linked operating context.",
      linkedContextEmptyText: "Portfolio-aware context links will appear after workspace data loads.",
      linkedContextItems: []
    });
    expect(state.workflowContinuity.disclosure).toMatchObject({
      label: "Supporting workflow evidence",
      summary: "Supporting context is collapsed while the workstation recovers. Expand sections for diagnostics and handoffs."
    });
    expect(state.workflowContinuity.disclosure.panels.map((panel) => [panel.id, panel.defaultExpanded])).toEqual([
      ["linked-context", false],
      ["operator-focus", false],
      ["evidence-timeline", false]
    ]);
    expect(state.statusPanel).toMatchObject({
      id: "workstation-shell-status-loading",
      titleId: "workstation-shell-status-loading-title",
      detailId: "workstation-shell-status-loading-detail",
      tone: "loading",
      role: "status",
      title: "Preparing workspace",
      detail: "Loading session state, operator workspaces, and the initial evidence views.",
      itemListLabel: "Workspace data loading status",
      actionLabel: null,
      items: [
        {
          key: "session-state",
          label: "Session state",
          detail: "Resolving operator context and environment guardrails."
        },
        {
          key: "workspace-payloads",
          label: "Workspace data",
          detail: "Loading Trading, Portfolio, Accounting, Reporting, Strategy, Data, and Settings."
        },
        {
          key: "evidence-slices",
          label: "Evidence slices",
          detail: "Preparing readiness, reconciliation, provider, and report-pack evidence."
        }
      ]
    });
    expect(state.routeFocus).toMatchObject({
      routeKey: "/trading",
      announcement: "Trading Workstation loaded.",
      documentTitle: "Trading Workstation - Meridian",
      targetElementId: null,
      fallbackElementId: "workbench-content"
    });
  });

  it("lets Accounting render a route-level loading workspace during bootstrap", () => {
    const state = buildAppShellViewState({
      pathname: "/accounting",
      loading: true,
      error: null,
      workspaceErrors: {},
      payload: emptyPayload
    });

    expect(state.activeWorkspace.label).toBe("Accounting");
    expect(state.canRenderRoutes).toBe(true);
    expect(state.statusPanel).toMatchObject({
      id: "workstation-shell-status-loading",
      tone: "loading"
    });
  });

  it("builds a shell trust strip from session, fixture, and provider posture", () => {
    const state = buildAppShellViewState({
      pathname: "/data/providers",
      loading: false,
      error: null,
      workspaceErrors: { reporting: "Report-pack preview failed." },
      usingDevelopmentFixtures: true,
      payload: {
        ...sessionPayload,
        data: {
          metrics: [],
          providers: [
            {
              provider: "Alpaca",
              status: "Warning",
              capability: "paper",
              latency: "120 ms",
              note: "Heartbeat delayed"
            }
          ],
          backfills: [],
          exports: []
        }
      }
    });

    expect(state.trustStrip).toMatchObject({
      ariaLabel: "Workstation build, environment, provenance, and provider posture",
      items: [
        {
          id: "build",
          label: "Build",
          value: "v0.1.0",
          tone: "ready"
        },
        {
          id: "mode",
          label: "Environment",
          value: "Paper",
          tone: "ready"
        },
        {
          id: "source",
          label: "Provenance",
          value: "SEEDED",
          tone: "review",
          detail: "Seeded demo data. This is seeded demo data shipped without live credentials. It is not real market or account data.",
          href: "/data/providers",
          actionLabel: "Connect live source"
        },
        {
          id: "providers",
          label: "Providers",
          value: "1 warning",
          tone: "review",
          detail: "1 provider warning; review provider trust before relying on fresh data.",
          href: "/data/providers",
          actionLabel: "Open provider posture"
        }
      ]
    });
  });

  it("keeps environment, provenance, and provider operator state independent", () => {
    const liveSimulatedState = buildAppShellViewState({
      pathname: "/data/providers",
      operatingContextSymbol: "MSFT",
      loading: false,
      error: null,
      workspaceErrors: {},
      dataProvenance: "simulated",
      payload: {
        ...sessionPayload,
        session: {
          ...sessionPayload.session!,
          environment: "live"
        },
        data: {
          metrics: [],
          providers: [
            {
              provider: "Databento",
              status: "Blocked",
              capability: "market-data",
              latency: "timeout",
              note: "Credential verification failed."
            }
          ],
          backfills: [],
          exports: []
        }
      }
    });

    expect(liveSimulatedState.trustStrip.items.find((item) => item.id === "mode")).toMatchObject({
      value: "Live",
      tone: "review",
      href: "/trading/readiness"
    });
    expect(liveSimulatedState.trustStrip.items.find((item) => item.id === "source")).toMatchObject({
      value: "SIMULATED",
      tone: "review",
      href: "/data/providers"
    });
    expect(liveSimulatedState.trustStrip.items.find((item) => item.id === "providers")).toMatchObject({
      value: "1 blocked",
      tone: "blocked",
      href: "/data/providers",
      actionLabel: "Open provider posture"
    });
    expect(liveSimulatedState.workflowContinuity.operatorFocusItems).toContainEqual(
      expect.objectContaining({
        label: "Databento provider blocked",
        tone: "blocked"
      })
    );
    expect(liveSimulatedState.workflowContinuity.linkedContextItems).toContainEqual(
      expect.objectContaining({
        statusLabel: "Provider blocked",
        tone: "blocked"
      })
    );

    const researchRealState = buildAppShellViewState({
      pathname: "/strategy",
      loading: false,
      error: null,
      workspaceErrors: {},
      dataProvenance: "real",
      payload: {
        ...sessionPayload,
        session: {
          ...sessionPayload.session!,
          environment: "research"
        }
      }
    });

    expect(researchRealState.trustStrip.items.find((item) => item.id === "mode")).toMatchObject({
      value: "Demo",
      detail: "Session Ops is operating in research mode, shown as Demo."
    });
    expect(researchRealState.trustStrip.items.find((item) => item.id === "source")).toMatchObject({
      value: "REAL",
      tone: "ready",
      href: null,
      actionLabel: null
    });
  });

  it("uses design-document research-to-paper title with canonical Strategy copy", () => {
    const state = buildAppShellViewState({
      pathname: "/strategy",
      loading: false,
      error: null,
      workspaceErrors: {},
      payload: sessionPayload
    });

    expect(state.workflowContinuity).toMatchObject({
      title: "Research To Paper",
      summary: expect.stringContaining("Strategy comparison")
    });
    expect(state.workflowContinuity.summary).not.toContain("strategy comparison");
  });

  it("routes retained research overview events into the Strategy workspace", () => {
    const state = buildAppShellViewState({
      pathname: "/strategy",
      loading: false,
      error: null,
      workspaceErrors: {},
      payload: {
        ...sessionPayload,
        overview: {
          recentEvents: [
            {
              id: "research-legacy-event",
              source: "Research",
              type: "warning",
              message: "Legacy research continuity event needs Strategy review.",
              timestamp: "2026-05-14T22:00:00Z"
            }
          ]
        }
      } as unknown as AppShellWorkspacePayload
    });

    expect(state.workflowContinuity.evidenceTimelineItems).toContainEqual(
      expect.objectContaining({
        label: "Strategy warning",
        workspaceLabel: "Strategy",
        route: "/strategy",
        tone: "review"
      })
    );
  });

  it("routes shell trust strip failures to diagnostics and live readiness", () => {
    const state = buildAppShellViewState({
      pathname: "/trading",
      loading: false,
      error: "Bootstrap failed.",
      workspaceErrors: {
        data: "Provider posture timed out."
      },
      dataProvenance: "real",
      payload: {
        ...emptyPayload,
        session: {
          displayName: "Live Desk",
          role: "Trader",
          environment: "live",
          activeWorkspace: "trading",
          commandCount: 12
        }
      }
    });

    expect(state.trustStrip.items.find((item) => item.id === "mode")).toMatchObject({
      value: "Live",
      tone: "review",
      href: "/trading/readiness",
      actionLabel: "Review readiness"
    });
    expect(state.trustStrip.items.find((item) => item.id === "source")).toMatchObject({
      value: "REAL",
      tone: "ready",
      href: null,
      actionLabel: null
    });
    expect(state.trustStrip.items.find((item) => item.id === "providers")).toMatchObject({
      value: "Unavailable",
      tone: "blocked",
      detail: "Data workspace failed: Provider posture timed out.",
      href: "/settings#backend-capability-coverage",
      actionLabel: "Open diagnostics"
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

  it("includes query parameters in the route focus key for subject and symbol handoffs", () => {
    const state = buildAppShellViewState({
      pathname: "/data/quotes",
      search: "?symbol=AAPL",
      loading: false,
      error: null,
      workspaceErrors: {},
      payload: sessionPayload
    });

    expect(state.routeFocus).toMatchObject({
      routeKey: "/data/quotes?symbol=AAPL",
      announcement: "Data Workstation loaded.",
      documentTitle: "Data Workstation - Meridian",
      targetElementId: null,
      fallbackElementId: "workbench-content"
    });
    expect(state.workflowContinuity).toMatchObject({
      title: "Market Data To Paper",
      primaryOperatorFlowLabel: "Primary operator workflow",
      primaryOperatorFlowSummary: "Import -> Validate -> Reconcile -> Investigate -> Approve -> Report",
      contextLabel: "Operating context",
      contextValue: "Data / AAPL",
      routeLabel: "/data/quotes?symbol=AAPL",
      nextActionLabel: "Next: Readiness",
      nextActionHref: "/trading/readiness?symbol=AAPL",
      subjectSymbol: "AAPL",
      clearSubjectAriaLabel: "Clear AAPL operating context"
    });
    expect(state.workflowContinuity.steps.map((step) => [step.id, step.active, step.next, step.href])).toEqual([
      ["market-data", true, false, "/data/quotes?symbol=AAPL"],
      ["readiness", false, true, "/trading/readiness?symbol=AAPL"],
      ["provider-setup", false, false, "/settings#alpaca-provider-setup"]
    ]);
    expect(state.workflowContinuity.steps.map((step) => [step.label, step.statusLabel])).toEqual([
      ["Market data", "Current / Waiting"],
      ["Readiness", "Next / Waiting"],
      ["Provider setup", "Available"]
    ]);
    expect(state.workflowContinuity.primaryOperatorFlowSteps.map((step) => [step.id, step.label, step.active, step.href])).toEqual([
      ["import", "Import", false, "/data/providers?symbol=AAPL"],
      ["validate", "Validate", true, "/data/operations?symbol=AAPL"],
      ["reconcile", "Reconcile", false, "/accounting/reconciliation?symbol=AAPL"],
      ["investigate", "Investigate", false, "/portfolio?symbol=AAPL"],
      ["approve", "Approve", false, "/accounting/approvals?symbol=AAPL"],
      ["report", "Report", false, "/reporting/report-packs?symbol=AAPL"]
    ]);
  });

  it("keeps a persisted operating symbol in cross-workspace workflow routes", () => {
    const state = buildAppShellViewState({
      pathname: "/portfolio",
      operatingContextSymbol: "msft",
      loading: false,
      error: null,
      workspaceErrors: {},
      payload: sessionPayload
    });

    expect(state.workflowContinuity).toMatchObject({
      title: "Trading Controls",
      contextValue: "Portfolio / MSFT",
      subjectSymbol: "MSFT",
      clearSubjectAriaLabel: "Clear MSFT operating context",
      summary: expect.stringContaining("Subject: MSFT.")
    });
    expect(state.workflowContinuity.title).not.toContain("accounting");
    expect(state.workflowContinuity.steps.map((step) => [step.id, step.href])).toEqual([
      ["trading-readiness", "/trading/readiness?symbol=MSFT"],
      ["trading-cockpit", "/trading?symbol=MSFT"],
      ["portfolio-exposure", "/portfolio?symbol=MSFT"],
      ["reconciliation", "/accounting/reconciliation?symbol=MSFT"],
      ["report-packs", "/reporting/report-packs?symbol=MSFT"]
    ]);

    const dataState = buildAppShellViewState({
      pathname: "/data/quotes",
      operatingContextSymbol: "msft",
      loading: false,
      error: null,
      workspaceErrors: {},
      payload: sessionPayload
    });

    expect(dataState.workflowContinuity.steps.map((step) => [step.id, step.href])).toEqual([
      ["market-data", "/data/quotes?symbol=MSFT"],
      ["readiness", "/trading/readiness?symbol=MSFT"],
      ["provider-setup", "/settings#alpaca-provider-setup"]
    ]);
  });

  it("preserves account, run, provider, and date scope across institutional workflow routes", () => {
    const state = buildAppShellViewState({
      pathname: "/portfolio",
      search: "?symbol=msft&fundAccountId=fund-1&runId=run-9&provider=Alpaca&from=2026-05-01&to=2026-05-15",
      loading: false,
      error: null,
      workspaceErrors: {},
      payload: sessionPayload
    });

    expect(state.workflowContinuity).toMatchObject({
      contextValue: "Portfolio / MSFT",
      clearSubjectAriaLabel: "Clear operating scope: Subject MSFT, Account fund-1, Run Selected run, Provider Alpaca, Window 2026-05-01 to 2026-05-15"
    });
    expect(state.workflowContinuity.operatingScope.summary)
      .toBe("Subject: MSFT / Account: fund-1 / Run: Selected run / Provider: Alpaca / Window: 2026-05-01 to 2026-05-15");
    expect(state.workflowContinuity.operatingScope.items.map((item) => [item.label, item.value])).toEqual([
      ["Subject", "MSFT"],
      ["Account", "fund-1"],
      ["Run", "Selected run"],
      ["Provider", "Alpaca"],
      ["Window", "2026-05-01 to 2026-05-15"]
    ]);
    expect(state.workflowContinuity.steps.map((step) => [step.id, step.href])).toEqual([
      ["trading-readiness", "/trading/readiness?symbol=MSFT&fundAccountId=fund-1&runId=run-9&provider=Alpaca&from=2026-05-01&to=2026-05-15"],
      ["trading-cockpit", "/trading?symbol=MSFT&fundAccountId=fund-1&runId=run-9&provider=Alpaca&from=2026-05-01&to=2026-05-15"],
      ["portfolio-exposure", "/portfolio?symbol=MSFT&fundAccountId=fund-1&runId=run-9&provider=Alpaca&from=2026-05-01&to=2026-05-15"],
      ["reconciliation", "/accounting/reconciliation?symbol=MSFT&fundAccountId=fund-1&runId=run-9&provider=Alpaca&from=2026-05-01&to=2026-05-15"],
      ["report-packs", "/reporting/report-packs?symbol=MSFT&fundAccountId=fund-1&runId=run-9&provider=Alpaca&from=2026-05-01&to=2026-05-15"]
    ]);
  });

  it("uses workspace-specific run labels without exposing raw run identity", () => {
    const accountingState = buildAppShellViewState({
      pathname: "/accounting/ledger",
      search: "?runId=run-42",
      loading: false,
      error: null,
      workspaceErrors: {},
      payload: sessionPayload
    });
    const reportingState = buildAppShellViewState({
      pathname: "/reporting/run-status",
      search: "?runId=report-run-board-202605",
      loading: false,
      error: null,
      workspaceErrors: {},
      payload: sessionPayload
    });

    expect(accountingState.workflowContinuity.operatingScope).toMatchObject({
      runId: "run-42",
      summary: "Run: Selected ledger run"
    });
    expect(reportingState.workflowContinuity.operatingScope).toMatchObject({
      runId: "report-run-board-202605",
      summary: "Run: Selected report run"
    });
    expect(accountingState.workflowContinuity.operatingScope.summary).not.toContain("run-42");
    expect(reportingState.workflowContinuity.operatingScope.summary).not.toContain("report-run-board-202605");
  });


  it("does not overwrite authoritative query parameters when applying operating scope", () => {
    const scopedRoute = appendOperatingScopeToRoute(
      "/strategy?runId=run-victim",
      {
        label: "Operating scope",
        summary: "Run: run-attacker",
        subjectSymbol: null,
        fundAccountId: null,
        runId: "run-attacker",
        provider: null,
        hasScope: true,
        clearAriaLabel: "Clear operating scope",
        items: [],
        queryParams: [{ key: "runId", value: "run-attacker", scopeKey: "runId" }]
      }
    );

    expect(scopedRoute).toBe("/strategy?runId=run-victim");
  });

  it("keeps institutional operating scope in cross-workspace focus, evidence, and linked-context handoffs", () => {
    const state = buildAppShellViewState({
      pathname: "/portfolio",
      search: "?symbol=MSFT&fundAccountId=fund-1&runId=run-9&provider=Alpaca&from=2026-05-01&to=2026-05-15",
      loading: false,
      error: null,
      workspaceErrors: {},
      payload: {
        ...sessionPayload,
        trading: {
          readiness: {
            acceptanceGates: [
              {
                gateId: "replay-gate",
                label: "Replay audit",
                status: "Blocked",
                detail: "Replay evidence is stale for the active paper session."
              }
            ],
            workItems: [
              {
                workItemId: "brokerage-sync",
                kind: "BrokerageSync",
                label: "Brokerage sync failed",
                detail: "Account sync failed after the last provider heartbeat.",
                tone: "Critical",
                createdAt: "2026-05-14T20:00:00Z",
                runId: "run-9",
                fundAccountId: "fund-1",
                auditReference: "audit-1",
                workspace: "portfolio",
                targetRoute: "/portfolio/brokerage-sync",
                targetPageTag: "BrokerageSync"
              }
            ],
            controls: {
              circuitBreakerOpen: false
            },
            replay: null,
            brokerageSync: null
          }
        },
        data: {
          providers: [
            {
              provider: "Alpaca",
              status: "Healthy",
              capability: "quotes",
              latency: "95ms",
              note: "Streaming quote path is healthy."
            }
          ],
          backfills: [],
          exports: []
        },
        portfolio: {
          positions: [
            {
              symbol: "MSFT",
              side: "Long",
              quantity: "10",
              averagePrice: "410.00",
              markPrice: "415.00",
              dayPnl: "+$50",
              unrealizedPnl: "+$50",
              exposure: "$4,150"
            }
          ],
          risk: {
            state: "Healthy",
            summary: "Exposure is inside guardrails."
          }
        },
        accounting: {
          breakQueue: [],
          reconciliationQueue: []
        },
        reporting: {
          reporting: {
            reportPackTargets: ["monthly-board-pack"]
          }
        }
      } as unknown as AppShellWorkspacePayload
    });

    expect(state.workflowContinuity.operatorFocusItems.find((item) => item.label === "Brokerage sync failed")).toMatchObject({
      route: "/settings?fundAccountId=fund-1&provider=Alpaca#alpaca-provider-setup"
    });
    expect(state.workflowContinuity.operatorFocusCommandItems.find((item) => item.label === "Replay audit")).toMatchObject({
      route: "/trading/readiness?symbol=MSFT&fundAccountId=fund-1&runId=run-9&provider=Alpaca&from=2026-05-01&to=2026-05-15"
    });
    expect(state.workflowContinuity.decisionBrief).toMatchObject({
      actionHref: "/settings?fundAccountId=fund-1&provider=Alpaca#alpaca-provider-setup"
    });
    expect(state.workflowContinuity.linkedContextItems.find((item) => item.label === "Trading cockpit")).toMatchObject({
      route: "/trading?symbol=MSFT&fundAccountId=fund-1&runId=run-9&provider=Alpaca&from=2026-05-01&to=2026-05-15"
    });
    expect(state.workflowContinuity.linkedContextItems.find((item) => item.label === "Quote evidence")).toMatchObject({
      route: "/data/quotes?symbol=MSFT&provider=Alpaca&from=2026-05-01&to=2026-05-15"
    });
    expect(state.workflowContinuity.linkedContextItems.find((item) => item.label === "Evidence packet")).toMatchObject({
      route: "/reporting/evidence?symbol=MSFT&fundAccountId=fund-1&runId=run-9&provider=Alpaca&from=2026-05-01&to=2026-05-15"
    });
    expect(state.workflowContinuity.evidenceTimelineItems[0]).toMatchObject({
      route: "/settings?fundAccountId=fund-1&provider=Alpaca#alpaca-provider-setup"
    });
  });

  it("builds portfolio-aware linked context for the active operating symbol", () => {
    const state = buildAppShellViewState({
      pathname: "/portfolio",
      operatingContextSymbol: "msft",
      loading: false,
      error: null,
      workspaceErrors: {},
      payload: {
        ...sessionPayload,
        data: {
          providers: [
            {
              provider: "Alpaca",
              status: "Healthy",
              capability: "quotes",
              latency: "95ms",
              note: "Streaming quote path is healthy."
            }
          ],
          backfills: [],
          exports: []
        },
        trading: {
          positions: [
            {
              symbol: "MSFT",
              side: "Long",
              quantity: "10",
              averagePrice: "410.00",
              markPrice: "415.00",
              dayPnl: "+$50",
              unrealizedPnl: "+$50",
              exposure: "$4,150"
            }
          ],
          openOrders: [
            {
              orderId: "order-1",
              symbol: "MSFT",
              side: "Buy",
              type: "Limit",
              quantity: "5",
              limitPrice: "412.00",
              status: "Working",
              submittedAt: "2026-05-15T14:00:00Z"
            }
          ],
          fills: [],
          risk: {
            state: "Healthy",
            summary: "Trading risk is inside guardrails."
          }
        },
        portfolio: {
          positions: [
            {
              symbol: "MSFT",
              side: "Long",
              quantity: "10",
              averagePrice: "410.00",
              markPrice: "415.00",
              dayPnl: "+$50",
              unrealizedPnl: "+$50",
              exposure: "$4,150"
            }
          ],
          risk: {
            state: "Healthy",
            summary: "Exposure is inside guardrails."
          }
        },
        accounting: {
          breakQueue: [
            {
              status: "Open"
            }
          ],
          reconciliationQueue: []
        },
        reporting: {
          reporting: {
            reportPackTargets: ["monthly-board-pack"]
          }
        }
      } as unknown as AppShellWorkspacePayload
    });

    expect(state.workflowContinuity).toMatchObject({
      linkedContextSummary: "MSFT needs 2 checks before action across 5 workspaces; 2 review.",
      linkedContextPostureLabel: "2 review",
      linkedContextPostureTone: "review",
      linkedContextPrimaryActionLabel: "Open Trading cockpit",
      linkedContextPrimaryActionHref: "/trading?symbol=MSFT"
    });
    expect(state.workflowContinuity.linkedContextItems.map((item) => [
      item.label,
      item.route,
      item.workspaceLabel,
      item.statusLabel,
      item.tone
    ])).toEqual([
      ["Trading cockpit", "/trading?symbol=MSFT", "Trading", "Orders open", "review"],
      ["Reconciliation", "/accounting/reconciliation?symbol=MSFT", "Accounting", "Breaks open", "review"],
      ["Quote evidence", "/data/quotes?symbol=MSFT", "Data", "Trusted", "ready"],
      ["Portfolio exposure", "/portfolio?symbol=MSFT", "Portfolio", "Holding loaded", "ready"],
      ["Evidence packet", "/reporting/evidence?symbol=MSFT", "Reporting", "Packet ready", "ready"]
    ]);
    expect(state.workflowContinuity.linkedContextItems[0].ariaLabel)
      .toBe("Trading: Trading cockpit. 1 open order and 0 recent fills are loaded for MSFT. Orders open. active subject.");
    expect(state.workflowContinuity.linkedContextPrimaryActionAriaLabel)
      .toBe("Open Trading cockpit from active subject; Trading status Orders open.");
  });

  it("turns a linked subject into a global decision brief when no blockers are loaded", () => {
    const state = buildAppShellViewState({
      pathname: "/data/quotes",
      search: "?symbol=MSFT",
      loading: false,
      error: null,
      workspaceErrors: {},
      payload: sessionPayload
    });

    expect(state.workflowContinuity.decisionBrief).toEqual({
      label: "Decision brief",
      title: "Continue MSFT decision",
      summary: "MSFT needs 5 checks before action across 5 workspaces; 5 pending.",
      reasonLabel: "Context",
      reason: "Open quote, tape, depth, and history evidence for MSFT.",
      statusLabel: "5 pending",
      statusTone: "pending",
      evidenceLabel: null,
      actionLabel: "Open Quote evidence",
      actionHref: "/data/quotes?symbol=MSFT",
      actionAriaLabel: "Open Quote evidence from active subject; Data status Waiting."
    });
  });

  it("surfaces the Alpaca provider setup handoff as the active workflow step", () => {
    const state = buildAppShellViewState({
      pathname: "/settings",
      hash: "#alpaca-provider-setup",
      loading: false,
      error: null,
      workspaceErrors: {},
      payload: sessionPayload
    });

    expect(state.workflowContinuity).toMatchObject({
      contextValue: "Settings / Provider setup",
      nextActionLabel: "Stay on Provider setup",
      nextActionHref: "/settings#alpaca-provider-setup"
    });
    expect(state.workflowContinuity.steps.map((step) => [step.id, step.active, step.next, step.statusLabel, step.statusTone])).toEqual([
      ["market-data", false, false, "Waiting", "pending"],
      ["readiness", false, false, "Waiting", "pending"],
      ["provider-setup", true, false, "Current / Available", "ready"]
    ]);
    expect(state.workflowContinuity.steps[2].ariaLabel)
      .toBe("Provider setup, current workflow step, Available");
  });

  it("derives status-aware workflow continuity for accounting closeout routes", () => {
    const state = buildAppShellViewState({
      pathname: "/accounting/reconciliation",
      loading: false,
      error: null,
      workspaceErrors: {
        trading: "Readiness endpoint timed out."
      },
      payload: {
        ...sessionPayload,
        data: {
          providers: [{ status: "Healthy" }],
          backfills: [],
          exports: []
        },
        strategy: {
          metrics: [],
          runs: [{ status: "Needs Review" }]
        },
        portfolio: {
          positions: [{ symbol: "AAPL" }],
          risk: { state: "Healthy" }
        },
        accounting: {
          breakQueue: [{ status: "Open" }],
          reconciliationQueue: [{ openBreakCount: 2 }]
        },
        reporting: {
          reporting: {
            reportPackTargets: ["monthly-board-pack"]
          }
        }
      } as unknown as AppShellWorkspacePayload
    });

    expect(state.workflowContinuity.title).toBe("Accounting Closeout");
    expect(state.workflowContinuity.contextValue).toBe("Accounting / Match Records");
    expect(state.workflowContinuity.nextActionLabel).toBe("Next: Resolve Exceptions");
    expect(state.workflowContinuity.summary).toContain("5 steps need operator attention.");
    expect(state.workflowContinuity.steps.map((step) => [step.id, step.statusLabel, step.statusTone])).toEqual([
      ["receive-activity", "2 breaks", "review"],
      ["match-records", "Current / 2 breaks", "review"],
      ["resolve-exceptions", "Next / 2 breaks", "review"],
      ["approve-results", "2 breaks", "review"],
      ["produce-evidence", "1 recipients", "ready"],
      ["close-support", "Review", "review"]
    ]);
    expect(state.workflowContinuity.steps.find((step) => step.id === "match-records")?.ariaLabel)
      .toBe("Match Records, current workflow step, 2 breaks");
  });

  it("uses source-backed financial operations summary for accounting closeout steps", () => {
    const state = buildAppShellViewState({
      pathname: "/accounting/reconciliation",
      search: "?fundAccountId=53bf0251-17f6-4fb7-8dbe-6fb4966e2749",
      loading: false,
      error: null,
      workspaceErrors: {},
      payload: {
        ...sessionPayload,
        accounting: {
          breakQueue: [],
          reconciliationQueue: []
        },
        workflowSummary: {
          generatedAt: "2026-06-01T12:00:00Z",
          hasOperatingContext: true,
          operatingContextLabel: "Northwind Income",
          fundDisplayName: "Northwind Income",
          workspaces: [
            {
              workspaceId: "accounting",
              workspaceTitle: "Accounting",
              statusLabel: "Financial operations exceptions require review",
              statusDetail: "Period 2026-05 has 1 unresolved exception before approval and evidence production can complete.",
              statusTone: "Warning",
              nextAction: {
                label: "Resolve Exceptions",
                detail: "Open reconciliation casework, assign breaks, and retain resolution evidence.",
                targetPageTag: "FundReconciliation",
                tone: "Primary"
              },
              primaryBlocker: {
                code: "financial-operations-exceptions",
                label: "1 unresolved exception",
                detail: "Approval and close evidence remain blocked until every break is matched, resolved, or explicitly closed.",
                tone: "Warning",
                isBlocking: true
              },
              evidence: [
                { label: "Core flow", value: "Resolve Exceptions", tone: "Warning" },
                { label: "Workflows", value: "1", tone: "Neutral" },
                { label: "Breaks", value: "1", tone: "Warning" },
                { label: "Approval", value: "Pending", tone: "Warning" },
                { label: "Evidence", value: "2", tone: "Success" }
              ]
            }
          ]
        }
      } as unknown as AppShellWorkspacePayload
    });

    expect(state.workflowContinuity.steps.map((step) => [step.id, step.statusLabel, step.statusTone])).toEqual([
      ["receive-activity", "Complete", "ready"],
      ["match-records", "Current / Complete", "ready"],
      ["resolve-exceptions", "Next / 1 breaks", "review"],
      ["approve-results", "Waiting", "pending"],
      ["produce-evidence", "Waiting", "pending"],
      ["close-support", "Waiting", "pending"]
    ]);
    expect(state.workflowContinuity.operatorFocusItems[0]).toMatchObject({
      label: "1 unresolved exception",
      route: "/accounting/reconciliation?fundAccountId=53bf0251-17f6-4fb7-8dbe-6fb4966e2749",
      actionLabel: "Resolve Exceptions",
      tone: "review"
    });
  });

  it("maps financial operations close readiness into the Close Support continuity step", () => {
    const state = buildAppShellViewState({
      pathname: "/accounting/operations-continuity",
      search: "?fundAccountId=53bf0251-17f6-4fb7-8dbe-6fb4966e2749",
      loading: false,
      error: null,
      workspaceErrors: {},
      payload: {
        ...sessionPayload,
        accounting: {
          breakQueue: [],
          reconciliationQueue: []
        },
        workflowSummary: {
          generatedAt: "2026-06-01T12:00:00Z",
          hasOperatingContext: true,
          operatingContextLabel: "Northwind Income",
          fundDisplayName: "Northwind Income",
          workspaces: [
            {
              workspaceId: "accounting",
              workspaceTitle: "Accounting",
              statusLabel: "Financial operations close readiness blocked",
              statusDetail: "Period 2026-05 close readiness score is 80; review blockers before producing the evidence package.",
              statusTone: "Warning",
              nextAction: {
                label: "Review Close Readiness",
                detail: "Open close support, review period lock evidence, and retain the evidence package decision.",
                targetPageTag: "OperationsClose",
                tone: "Primary"
              },
              primaryBlocker: {
                code: "financial-operations-close-readiness",
                label: "Close evidence is incomplete",
                detail: "Close support cannot lock the period until retained evidence and reopen evidence are complete.",
                tone: "Warning",
                isBlocking: true
              },
              evidence: [
                { label: "Core flow", value: "Produce Evidence", tone: "Warning" },
                { label: "Workflows", value: "1", tone: "Neutral" },
                { label: "Breaks", value: "0", tone: "Success" },
                { label: "Approval", value: "Approved", tone: "Success" },
                { label: "Evidence", value: "6", tone: "Success" },
                { label: "Close", value: "80", tone: "Warning" },
                { label: "Period lock", value: "Missing", tone: "Warning" }
              ]
            }
          ]
        }
      } as unknown as AppShellWorkspacePayload
    });

    expect(state.workflowContinuity.contextValue).toBe("Accounting / Scoped workflow");
    expect(state.workflowContinuity.nextActionLabel).toBe("Stay on Close Support");
    expect(state.workflowContinuity.steps.map((step) => [step.id, step.statusLabel, step.statusTone])).toEqual([
      ["receive-activity", "Complete", "ready"],
      ["match-records", "Complete", "ready"],
      ["resolve-exceptions", "Complete", "ready"],
      ["approve-results", "Complete", "ready"],
      ["produce-evidence", "Complete", "ready"],
      ["close-support", "Current / Close blocked", "review"]
    ]);
    expect(state.workflowContinuity.steps.find((step) => step.id === "close-support")?.href)
      .toBe("/accounting/operations-continuity?fundAccountId=53bf0251-17f6-4fb7-8dbe-6fb4966e2749");
  });

  it("surfaces reviewed automation guardrails from the financial operations summary", () => {
    const state = buildAppShellViewState({
      pathname: "/accounting/reconciliation",
      search: "?fundAccountId=53bf0251-17f6-4fb7-8dbe-6fb4966e2749",
      loading: false,
      error: null,
      workspaceErrors: {},
      payload: {
        ...sessionPayload,
        accounting: {
          breakQueue: [],
          reconciliationQueue: []
        },
        workflowSummary: {
          generatedAt: "2026-06-01T12:00:00Z",
          hasOperatingContext: true,
          operatingContextLabel: "Northwind Income",
          fundDisplayName: "Northwind Income",
          workspaces: [
            {
              workspaceId: "accounting",
              workspaceTitle: "Accounting",
              statusLabel: "Financial operations control flow active",
              statusDetail: "Period 2026-05 is in the Match Records stage with source-backed workflow gates, checklist, and audit trail.",
              statusTone: "Info",
              nextAction: {
                label: "Match Records",
                detail: "Open the governed operations continuity workflow and continue the next source-backed control step.",
                targetPageTag: "OperationsContinuity",
                tone: "Primary"
              },
              primaryBlocker: {
                code: "financial-operations-in-progress",
                label: "Financial operations flow in progress",
                detail: "Continue the source-backed receive, match, resolve, approve, and evidence workflow before close.",
                tone: "Info",
                isBlocking: false
              },
              evidence: [
                { label: "Core flow", value: "Match Records", tone: "Info" },
                { label: "Workflows", value: "1", tone: "Neutral" },
                { label: "Breaks", value: "0", tone: "Success" },
                { label: "Approval", value: "Pending", tone: "Warning" },
                { label: "Evidence", value: "2", tone: "Success" },
                { label: "Close", value: "Pending", tone: "Info" },
                { label: "Reviewed automation", value: "Suggested matches require review", tone: "Warning" }
              ]
            }
          ]
        }
      } as unknown as AppShellWorkspacePayload
    });

    expect(state.workflowContinuity.operatorFocusItems[0]).toMatchObject({
      label: "Reviewed automation requires operator review",
      detail: "Suggested matches require review; automation can suggest, classify, extract, match, summarize, draft, and flag, but cannot approve, post, publish, release payments, or erase evidence.",
      route: "/accounting/operations-continuity?fundAccountId=53bf0251-17f6-4fb7-8dbe-6fb4966e2749",
      actionLabel: "Match Records",
      tone: "review"
    });
  });

  it("ranks cross-workspace operator focus items for the shell continuity dock", () => {
    const state = buildAppShellViewState({
      pathname: "/portfolio",
      loading: false,
      error: null,
      workspaceErrors: {},
      payload: {
        ...sessionPayload,
        trading: {
          readiness: {
            acceptanceGates: [
              {
                gateId: "replay-gate",
                label: "Replay audit",
                status: "Blocked",
                detail: "Replay evidence is stale for the active paper session."
              }
            ],
            workItems: [
              {
                workItemId: "brokerage-sync",
                kind: "BrokerageSync",
                label: "Brokerage sync failed",
                detail: "Account sync failed after the last provider heartbeat.",
                tone: "Critical",
                createdAt: "2026-05-14T20:00:00Z",
                runId: null,
                fundAccountId: "fund-1",
                auditReference: "audit-1",
                workspace: "portfolio",
                targetRoute: "/portfolio/brokerage-sync",
                targetPageTag: "BrokerageSync"
              },
              {
                workItemId: "report-pack",
                kind: "ReportPackApproval",
                label: "Report pack approval waiting",
                detail: "Monthly board pack still needs an operator sign-off.",
                tone: "Warning",
                createdAt: "2026-05-14T21:00:00Z",
                runId: "run-1",
                fundAccountId: null,
                auditReference: "audit-2",
                workspace: "reporting",
                targetRoute: "/reporting/report-packs",
                targetPageTag: "ReportPackApproval"
              }
            ],
            controls: {
              circuitBreakerOpen: false
            },
            replay: null,
            brokerageSync: null
          }
        },
        data: {
          providers: [
            {
              provider: "Alpaca",
              status: "Warning",
              capability: "paper",
              latency: "120ms",
              note: "Paper endpoint returned intermittent quote gaps.",
              recommendedAction: "Review paper provider posture."
            }
          ],
          backfills: [],
          exports: []
        },
        portfolio: {
          positions: [],
          risk: {
            state: "Healthy",
            summary: "Exposure is within guardrails."
          }
        },
        accounting: {
          breakQueue: [],
          reconciliationQueue: [],
          reporting: {
            reportPackTargets: ["monthly-board-pack"]
          }
        },
        reporting: {
          reporting: {
            reportPackTargets: ["monthly-board-pack"]
          }
        }
      } as unknown as AppShellWorkspacePayload
    });

    expect(state.workflowContinuity.operatorFocusSummary)
      .toBe("4 focus items across workspaces: 2 blocked and 2 review.");
    expect(state.workflowContinuity.disclosure.panels).toContainEqual({
      id: "operator-focus",
      label: "Operator focus",
      summary: "3 focus items",
      ariaLabel: "Expand operator focus. 3 focus items loaded.",
      defaultExpanded: true
    });
    expect(state.workflowContinuity.disclosure.panels).toContainEqual({
      id: "evidence-timeline",
      label: "Evidence timeline",
      summary: "2 evidence events",
      ariaLabel: "Expand evidence timeline. 2 evidence events loaded.",
      defaultExpanded: true
    });
    expect(state.workflowContinuity.operatorFocusOverflowLabel).toBe("+1 more focus item");
    expect(state.workflowContinuity.operatorFocusItems.map((item) => [
      item.label,
      item.route,
      item.workspaceLabel,
      item.actionLabel,
      item.tone
    ])).toEqual([
      ["Brokerage sync failed", "/settings#alpaca-provider-setup", "Settings", "Fix provider setup", "blocked"],
      ["Replay audit", "/trading/readiness", "Trading", "Open readiness", "blocked"],
      ["Report pack approval waiting", "/reporting/report-packs", "Reporting", "Open report packs", "review"]
    ]);
    expect(state.workflowContinuity.operatorFocusCommandItems.map((item) => item.label)).toEqual([
      "Brokerage sync failed",
      "Replay audit",
      "Report pack approval waiting",
      "Alpaca provider warning"
    ]);
    expect(state.workflowContinuity.operatorFocusItems[0].ariaLabel)
      .toBe("Settings: Brokerage sync failed. Account sync failed after the last provider heartbeat. Fix provider setup.");
    expect(state.workflowContinuity.decisionBrief).toMatchObject({
      label: "Decision brief",
      title: "Brokerage sync failed",
      summary: "Settings is the highest-priority loaded issue. 4 focus items across workspaces: 2 blocked and 2 review.",
      reasonLabel: "Why now",
      reason: "Account sync failed after the last provider heartbeat.",
      statusLabel: "Blocked",
      statusTone: "blocked",
      evidenceLabel: "Latest evidence: Reporting 2026-05-14 21:00 UTC",
      actionLabel: "Fix provider setup",
      actionHref: "/settings#alpaca-provider-setup",
      actionAriaLabel: "Settings: Brokerage sync failed. Account sync failed after the last provider heartbeat. Fix provider setup."
    });
    expect(state.workflowContinuity.evidenceTimelineSummary)
      .toBe("2 evidence events across 2 workspaces. Latest: Reporting at 2026-05-14 21:00 UTC.");
    expect(state.workflowContinuity.evidenceTimelineItems.map((item) => [
      item.label,
      item.workspaceLabel,
      item.timestampLabel,
      item.route,
      item.tone
    ])).toEqual([
      ["Report pack approval waiting", "Reporting", "2026-05-14 21:00 UTC", "/reporting/report-packs", "review"],
      ["Brokerage sync failed", "Settings", "2026-05-14 20:00 UTC", "/settings#alpaca-provider-setup", "blocked"]
    ]);
    expect(state.workflowContinuity.evidenceTimelineItems[0].ariaLabel)
      .toBe("Reporting: Report pack approval waiting. Monthly board pack still needs an operator sign-off. Audit: audit-2. 2026-05-14 21:00 UTC. Open evidence.");
  });

  it("keeps available routes open when only some workspace slices fail", () => {
    const state = buildAppShellViewState({
      pathname: "/accounting",
      loading: false,
      error: "Data unavailable",
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
      title: "Some workspace data is unavailable",
      actionLabel: "Retry failed areas",
      actionAriaLabel: "Retry failed workspace areas",
      secondaryActionLabel: "Review diagnostics",
      secondaryActionAriaLabel: "Review Settings diagnostics for failed workspace areas",
      secondaryActionHref: "/settings#backend-capability-coverage",
      itemListLabel: "Workspace data issues"
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
      title: "Workspace data unavailable",
      detail: "Network offline",
      actionLabel: "Retry workspace data",
      actionAriaLabel: "Retry workspace data",
      itemListLabel: "Workspace data issues"
    });
  });

  it("hides raw technical response bodies in failed workspace copy", () => {
    const rawBody = "<!DOCTYPE HTML><html><body><h1>404</h1><p>File not found</p></body></html>";
    const state = buildAppShellViewState({
      pathname: "/reporting",
      loading: false,
      error: rawBody,
      workspaceErrors: {
        reporting: rawBody
      },
      payload: emptyPayload
    });

    expect(state.statusPanel).toMatchObject({
      title: "Workspace data unavailable",
      detail: "Meridian could not load workspace data. Try again or open diagnostics."
    });
    expect(state.statusPanel?.items[0]).toMatchObject({
      label: "Reporting",
      detail: "Workspace data unavailable. Try again or open diagnostics.",
      ariaLabel: "Reporting: Workspace data unavailable. Try again or open diagnostics."
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
      detail: "Showing demo data because live Meridian data is unavailable; use the evidence path for watchlist, quotes, readiness, and Alpaca setup.",
      workflowLabel: "Evidence path",
      retryLabel: "Retrying live data",
      retryAriaLabel: "Retrying live Meridian workspace data",
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

  it("marks the watchlist demo step active on the market data desk watchlist view", () => {
    const state = buildDevelopmentFixtureNoticeViewModel({
      pathname: "/data/quotes",
      search: "?view=watchlist"
    });

    expect(state.steps.map((step) => [step.id, step.active])).toEqual([
      ["watchlist", true],
      ["quotes", false],
      ["readiness", false],
      ["connect", false]
    ]);
    expect(state.steps.find((step) => step.id === "watchlist")).toMatchObject({
      href: "/data/quotes?view=watchlist"
    });
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
      title: "Some workspace data is unavailable",
      detail: "1 workspace area did not load. Available routes remain open while that area recovers."
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
      placeholder: "Go to route, action, evidence...",
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
