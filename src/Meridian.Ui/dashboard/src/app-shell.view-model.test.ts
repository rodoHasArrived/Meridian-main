import { describe, expect, it } from "vitest";
import {
  buildAppShellViewState,
  buildCommandPaletteTriggerState,
  buildDevelopmentFixtureNoticeViewModel,
  appendOperatingScopeToRoute,
  normalizeWorkspace,
  resolveAppShellCommandPaletteShortcut,
  type AppShellWorkspacePayload
} from "@/app-shell.view-model";
import type { SessionInfo } from "@/types";

const emptyPayload: AppShellWorkspacePayload = {
  session: null,
  overview: null,
  strategy: null,
  trading: null,
  portfolio: null,
  data: null,
  accounting: null,
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
    expect(normalizeWorkspace("/strategy")).toBe("strategy");
    expect(normalizeWorkspace("/data-operations/backfills")).toBe("data");
    expect(normalizeWorkspace("/accounting/security-master")).toBe("accounting");
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
    expect(state.workflowContinuity).toMatchObject({
      operatorFocusSummary: "Loading cross-workspace operator posture.",
      operatorFocusEmptyText: "Ranked focus actions will appear after workstation payloads load.",
      operatorFocusItems: [],
      evidenceTimelineSummary: "Loading cross-workspace evidence timeline.",
      evidenceTimelineEmptyText: "Recent audit and workflow events will appear after workstation payloads load.",
      evidenceTimelineItems: [],
      linkedContextSummary: "Loading linked operating context.",
      linkedContextEmptyText: "Portfolio-aware context links will appear after workstation payloads load.",
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
      title: "Booting workstation shell",
      detail: "Loading session state, operator workspaces, and the initial workstation evidence slices.",
      itemListLabel: "Workspace bootstrap status",
      actionLabel: null,
      items: [
        {
          key: "session-state",
          label: "Session state",
          detail: "Resolving operator context and environment guardrails."
        },
        {
          key: "workspace-payloads",
          label: "Workspace payloads",
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
      ariaLabel: "Workstation build, mode, data source, and provider posture",
      items: [
        {
          id: "build",
          label: "Build",
          value: "v0.1.0",
          tone: "ready"
        },
        {
          id: "mode",
          label: "Mode",
          value: "Paper",
          tone: "ready"
        },
        {
          id: "source",
          label: "Source",
          value: "Demo fixtures",
          tone: "pending",
          detail: "No-host fixture payloads are visible; do not treat this as live operational readiness.",
          href: "/settings#backend-capability-coverage",
          actionLabel: "Open diagnostics"
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

  it("uses canonical Strategy copy for the strategy-to-paper workflow", () => {
    const state = buildAppShellViewState({
      pathname: "/strategy",
      loading: false,
      error: null,
      workspaceErrors: {},
      payload: sessionPayload
    });

    expect(state.workflowContinuity).toMatchObject({
      title: "Strategy To Paper",
      summary: expect.stringContaining("Strategy comparison")
    });
    expect(state.workflowContinuity.summary).not.toContain("strategy comparison");
  });

  it("routes shell trust strip failures to diagnostics and live readiness", () => {
    const state = buildAppShellViewState({
      pathname: "/trading",
      loading: false,
      error: "Bootstrap failed.",
      workspaceErrors: {
        data: "Provider posture timed out."
      },
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
      tone: "blocked",
      href: "/trading/readiness",
      actionLabel: "Review readiness"
    });
    expect(state.trustStrip.items.find((item) => item.id === "source")).toMatchObject({
      value: "Partial host",
      tone: "review",
      href: "/settings#backend-capability-coverage",
      actionLabel: "Open diagnostics"
    });
    expect(state.trustStrip.items.find((item) => item.id === "providers")).toMatchObject({
      value: "Pending",
      href: "/data/providers",
      actionLabel: "Open provider posture"
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
      nextActionLabel: "Next: Price alerts",
      nextActionHref: "/data/alerts?symbol=AAPL",
      subjectSymbol: "AAPL",
      clearSubjectAriaLabel: "Clear AAPL operating context"
    });
    expect(state.workflowContinuity.steps.map((step) => [step.id, step.active, step.next, step.href])).toEqual([
      ["watchlist", false, false, "/data/watchlist?symbol=AAPL"],
      ["quotes", true, false, "/data/quotes?symbol=AAPL"],
      ["alerts", false, true, "/data/alerts?symbol=AAPL"],
      ["readiness", false, false, "/trading/readiness?symbol=AAPL"],
      ["provider-setup", false, false, "/settings#alpaca-provider-setup"]
    ]);
    expect(state.workflowContinuity.steps.map((step) => [step.label, step.statusLabel])).toEqual([
      ["Watchlist", "Waiting"],
      ["Live quotes", "Current / Waiting"],
      ["Price alerts", "Next / Waiting"],
      ["Readiness", "Waiting"],
      ["Provider setup", "Available"]
    ]);
    expect(state.workflowContinuity.primaryOperatorFlowSteps.map((step) => [step.id, step.label, step.active, step.href])).toEqual([
      ["import", "Import", false, "/data/providers?symbol=AAPL"],
      ["validate", "Validate", true, "/data/backfills?symbol=AAPL"],
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
      pathname: "/data/watchlist",
      operatingContextSymbol: "msft",
      loading: false,
      error: null,
      workspaceErrors: {},
      payload: sessionPayload
    });

    expect(dataState.workflowContinuity.steps.map((step) => [step.id, step.href])).toEqual([
      ["watchlist", "/data/watchlist?symbol=MSFT"],
      ["quotes", "/data/quotes?symbol=MSFT"],
      ["alerts", "/data/alerts?symbol=MSFT"],
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
      clearSubjectAriaLabel: "Clear operating scope: Subject MSFT, Account fund-1, Run run-9, Provider Alpaca, Window 2026-05-01 to 2026-05-15"
    });
    expect(state.workflowContinuity.operatingScope.summary)
      .toBe("Subject: MSFT / Account: fund-1 / Run: run-9 / Provider: Alpaca / Window: 2026-05-01 to 2026-05-15");
    expect(state.workflowContinuity.operatingScope.items.map((item) => [item.label, item.value])).toEqual([
      ["Subject", "MSFT"],
      ["Account", "fund-1"],
      ["Run", "run-9"],
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
      ["watchlist", false, false, "Waiting", "pending"],
      ["quotes", false, false, "Waiting", "pending"],
      ["alerts", false, false, "Waiting", "pending"],
      ["readiness", false, false, "Waiting", "pending"],
      ["provider-setup", true, false, "Current / Available", "ready"]
    ]);
    expect(state.workflowContinuity.steps[4].ariaLabel)
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
    expect(state.workflowContinuity.summary).toContain("4 steps need operator attention.");
    expect(state.workflowContinuity.steps.map((step) => [step.id, step.statusLabel, step.statusTone])).toEqual([
      ["receive-activity", "2 breaks", "review"],
      ["match-records", "Current / 2 breaks", "review"],
      ["resolve-exceptions", "Next / 2 breaks", "review"],
      ["approve-results", "2 breaks", "review"],
      ["produce-evidence", "1 packs", "ready"]
    ]);
    expect(state.workflowContinuity.steps.find((step) => step.id === "match-records")?.ariaLabel)
      .toBe("Match Records, current workflow step, 2 breaks");
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
      title: "Resolve Brokerage sync failed",
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
      detail: "Showing local fixture responses because the Meridian API host is unavailable; use the evidence path for watchlist, quotes, readiness, and Alpaca setup.",
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
