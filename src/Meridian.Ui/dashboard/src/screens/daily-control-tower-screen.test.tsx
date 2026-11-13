import { describe, expect, it, vi } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { buildAppShellViewState, type AppShellWorkspacePayload } from "@/app-shell.view-model";
import { buildWorkspaceAttentionCounts } from "@/lib/daily-control-tower";
import { DailyControlTowerScreen } from "@/screens/daily-control-tower-screen";
import { renderWithRouter } from "@/test/render";
import type { DataWorkspaceResponse, TradingWorkspaceResponse } from "@/types";

// A loaded inbox: the stub ignores any fallback, so every finance-queue assertion below holds
// after the shared inbox has replaced its loading state.
vi.mock("@/components/meridian/operator-triage-queue", () => ({
  OperatorTriageQueue: () => <table aria-label="Stub operator inbox"><tbody><tr><td>Stub inbox item</td></tr></tbody></table>
}));

// A payload that produces a multi-row finance queue so row selection is
// observable: two trading work items plus one data-provider warning.
const payload: AppShellWorkspacePayload = {
  session: {
    displayName: "Ops Desk",
    role: "Operator",
    environment: "paper",
    activeWorkspace: "trading",
    commandCount: 7
  },
  overview: null,
  strategy: null,
  trading: {
    readiness: {
      asOf: "2026-05-14T21:30:00Z",
      overallStatus: "Blocked",
      readyForPaperOperation: false,
      acceptanceGates: [],
      activeSession: null,
      sessions: [],
      replay: null,
      controls: { circuitBreakerOpen: false },
      promotion: null,
      trustGate: null,
      brokerageSync: null,
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
      warnings: []
    }
  } as unknown as TradingWorkspaceResponse,
  portfolio: null,
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
  } as unknown as DataWorkspaceResponse,
  accounting: null,
  reporting: null,
  workflowSummary: null
};

function renderScreen({
  onEditOperatingScope = vi.fn(),
  onRefresh = vi.fn(),
  refreshing = false,
  search = "?symbol=MSFT"
}: {
  onEditOperatingScope?: () => void;
  onRefresh?: () => void;
  refreshing?: boolean;
  search?: string;
} = {}) {
  const shell = buildAppShellViewState({
    pathname: "/",
    search,
    loading: false,
    error: null,
    workspaceErrors: {},
    payload
  });
  return renderWithRouter(
    <DailyControlTowerScreen
      viewModel={shell.workflowContinuity}
      trustStrip={shell.trustStrip}
      attention={shell.attention}
      onEditOperatingScope={onEditOperatingScope}
      onRefresh={onRefresh}
      refreshing={refreshing}
    />
  );
}

describe("DailyControlTowerScreen", () => {
  it("renders the ranked finance queue and defaults the evidence pane to the leading decision", () => {
    renderScreen();

    expect(
      screen.getByRole("heading", { name: "What needs an operator decision now" })
    ).toBeInTheDocument();

    // The queue is listed finance-first, but the headline and the default evidence follow the
    // worst queued decision -- the same one the masthead attention pill names on every route.
    expect(
      screen.getByRole("row", { name: "Brokerage sync failed" })
    ).toHaveAttribute("aria-selected", "true");
    expect(
      screen.getByRole("region", { name: /Brokerage sync failed evidence summary/i })
    ).toBeInTheDocument();
    expect(screen.getByLabelText("Daily control tower confidence")).toHaveTextContent("3 items in the finance queue");
    expect(screen.getByLabelText("Daily control tower confidence")).toHaveTextContent("Stale update");
    expect(screen.getByLabelText("Daily control tower confidence")).toHaveTextContent("May 14, 2026");
    expect(screen.queryByLabelText("Daily control tower decision drivers")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Daily control tower trust posture")).not.toBeInTheDocument();
    expect(screen.getByText("More evidence").closest("details")).not.toHaveAttribute("open");
  });

  it("lists every item the Today badge counts after the inbox loads", async () => {
    const shell = buildAppShellViewState({
      pathname: "/",
      search: "?symbol=MSFT",
      loading: false,
      error: null,
      workspaceErrors: {},
      payload
    });
    const { total } = buildWorkspaceAttentionCounts(shell.workflowContinuity);
    expect(total).toBeGreaterThan(0);
    const { container } = renderScreen();

    // The badge, the confidence line and the table read one list, so they cannot disagree.
    const queue = screen.getByRole("treegrid", { name: "Daily control tower finance queue" });
    expect(queue.querySelectorAll("tbody tr[data-dense-row-id]")).toHaveLength(total);
    expect(screen.getByRole("region", { name: "Daily control tower confidence" }))
      .toHaveTextContent(`${total} items in the finance queue`);

    // One queue, two sources, each named and described; the counted source comes first.
    const financeQueue = screen.getByRole("region", { name: "Finance queue" });
    const inbox = screen.getByRole("region", { name: "Operator inbox" });
    expect(within(financeQueue).getByRole("heading", { name: "Finance queue" })).toBeInTheDocument();
    expect(financeQueue).toContainElement(queue);
    expect(financeQueue).toHaveTextContent("Source: workspace signals. The sidebar badges count these.");
    expect(within(inbox).getByRole("heading", { name: "Operator inbox" })).toBeInTheDocument();
    expect(within(inbox).getByRole("table", { name: "Stub operator inbox" })).toHaveTextContent("Stub inbox item");
    expect(inbox).toHaveTextContent("Source: shared inbox. Assign, snooze and acknowledge here.");
    expect(financeQueue.compareDocumentPosition(inbox) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    // Both sources sit under the one Queue heading.
    const queueSection = screen.getByRole("region", { name: "Queue" });
    expect(queueSection).toContainElement(financeQueue);
    expect(queueSection).toContainElement(inbox);

    const results = await axe(container);
    expect(results.violations).toEqual([]);
  });

  it("filters the one Today queue by source without losing either list", async () => {
    const user = userEvent.setup();
    renderScreen();
    const source = screen.getByRole("tablist", { name: "Queue source" });

    await user.click(within(source).getByRole("tab", { name: /Shared inbox/ }));
    expect(screen.queryByRole("region", { name: "Finance queue" })).not.toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Operator inbox" })).toBeInTheDocument();

    await user.click(within(source).getByRole("tab", { name: /Workspace signals/ }));
    expect(screen.getByRole("region", { name: "Finance queue" })).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Operator inbox" })).not.toBeInTheDocument();

    await user.click(within(source).getByRole("tab", { name: /All sources/ }));
    expect(screen.getByRole("region", { name: "Finance queue" })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Operator inbox" })).toBeInTheDocument();
  });

  it("states the workstation status in one line that names only the open issues", () => {
    renderScreen();

    const status = screen.getByRole("region", { name: "Workstation status" });
    const line = within(status).getByTestId("status-row-issues");
    const priority = screen.getByRole("region", { name: "Priority decision" });
    expect(within(priority).getAllByRole("link").filter((link) => !link.closest("details"))).toHaveLength(1);
    expect(priority.querySelectorAll('[data-testid="status-row-issues"]')).toHaveLength(1);
    expect(line).toHaveTextContent("Blocked");
    expect(line).toHaveTextContent("Decisions:");
    expect(line).toHaveTextContent("Brokerage sync failed · 2 more");
    expect(line).toHaveTextContent("Providers:");
    expect(line).toHaveTextContent("1 warning");
    // This shell never confirmed its provenance, which is an issue in its own right.
    expect(line).toHaveTextContent("Provenance:");
    expect(line).toHaveTextContent("UNKNOWN");
    // Ready signals are not listed in the line; they are behind Details.
    expect(line).not.toHaveTextContent("Workspace data");
    expect(status.querySelector("details")).not.toHaveAttribute("open");
    expect(status).toHaveTextContent("Workspace data");
  });

  it("matches the masthead attention state for the same data", () => {
    const shell = buildAppShellViewState({
      pathname: "/",
      search: "?symbol=MSFT",
      loading: false,
      error: null,
      workspaceErrors: {},
      payload
    });
    renderScreen();

    expect(shell.attention.tone).toBe("blocked");
    expect(shell.attention.title).toBe("Brokerage sync failed");
    const headline = screen.getByRole("region", { name: "Priority decision" });
    expect(headline).toHaveTextContent("Brokerage sync failed");
    expect(headline).toHaveTextContent("Blocked");
    expect(screen.getByRole("region", { name: "Workstation status" })).toHaveAttribute("data-status-tone", "blocked");
  });

  it("exposes provider and refresh remediation with the status row", async () => {
    const user = userEvent.setup();
    const onEditOperatingScope = vi.fn();
    const onRefresh = vi.fn();
    renderScreen({ onEditOperatingScope, onRefresh, search: "" });

    const status = screen.getByRole("region", { name: "Workstation status" });
    expect(within(status).getByRole("link", { name: "Open provider posture" }))
      .toHaveAttribute("href", "/data/providers");

    await user.click(screen.getByRole("button", { name: "Set operating scope" }));
    await user.click(within(status).getByRole("button", { name: "Refresh control tower evidence" }));

    expect(onEditOperatingScope).toHaveBeenCalledTimes(1);
    expect(onRefresh).toHaveBeenCalledTimes(1);
  });

  it("preserves compatible operating scope in provider recovery", () => {
    renderScreen({ search: "?symbol=MSFT" });

    const status = screen.getByRole("region", { name: "Workstation status" });
    expect(within(status).getByRole("link", { name: "Open provider posture" }))
      .toHaveAttribute("href", "/data/providers?symbol=MSFT");
  });

  it("updates the evidence pane in place when another queue row is selected", async () => {
    const user = userEvent.setup();
    renderScreen();

    const reportPackRow = screen.getByRole("row", {
      name: "Report pack approval waiting"
    });
    expect(reportPackRow).not.toHaveAttribute("aria-selected", "true");

    await user.click(reportPackRow);

    // Selection moved without navigating away: the evidence region now reflects
    // the clicked row, and the pressed state follows it.
    expect(reportPackRow).toHaveAttribute("aria-selected", "true");
    expect(
      screen.getByRole("region", { name: /Report pack approval waiting evidence summary/i })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("row", { name: "Brokerage sync failed" })
    ).not.toHaveAttribute("aria-selected", "true");
  });

  it("shows the combined ranked queue without a scope selection", () => {
    renderScreen({ search: "" });
    expect(screen.queryByRole("region", { name: "Choose Control Tower scope" })).not.toBeInTheDocument();
    expect(screen.getByRole("treegrid", { name: "Daily control tower finance queue" })).toBeInTheDocument();
    expect(screen.getByLabelText("Daily control tower confidence")).toHaveTextContent("All authorized scopes");
    expect(screen.getByRole("columnheader", { name: "Workspace" })).toBeInTheDocument();
    expect(screen.queryByRole("columnheader", { name: "Owner" })).not.toBeInTheDocument();
  });

  it("supports keyboard selection and detail focus through the shared dense-row contract", async () => {
    const user = userEvent.setup();
    renderScreen();

    const leadingRow = screen.getByRole("row", { name: "Report pack approval waiting" });
    leadingRow.focus();
    await user.keyboard("{ArrowDown}");

    const providerRow = screen.getByRole("row", { name: "Alpaca provider warning" });
    expect(providerRow).toHaveAttribute("aria-selected", "true");
    providerRow.focus();
    await user.keyboard("{Enter}");
    await waitFor(() => {
      expect(screen.getByRole("region", { name: /Alpaca provider warning evidence summary/i })).toHaveFocus();
    });

    await user.keyboard("{Escape}");
    expect(screen.getByRole("row", { name: "Alpaca provider warning" })).toHaveFocus();
  });

  it("reveals secondary proof only when the operator expands more evidence", async () => {
    const user = userEvent.setup();
    renderScreen();

    const disclosure = screen.getByText("More evidence").closest("details");
    expect(disclosure).not.toHaveAttribute("open");

    await user.click(screen.getByText("More evidence"));

    expect(disclosure).toHaveAttribute("open");
    expect(disclosure).toHaveTextContent("Audit Trail");
    expect(disclosure).toHaveTextContent("Evidence Packet");
  });

  it("shows the leading action once outside the ranked queue", () => {
    renderScreen();

    const priorityPanel = screen.getByRole("region", { name: "Priority decision" });
    expect(within(priorityPanel as HTMLElement).getAllByRole("link").filter((link) => !link.closest("details"))).toHaveLength(1);
  });

  it("has no accessibility violations", async () => {
    const { container } = renderScreen();
    const results = await axe(container);
    expect(results.violations).toEqual([]);
  });
});
