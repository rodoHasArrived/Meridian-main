import { beforeEach, describe, expect, it, vi } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { buildAppShellViewState, type AppShellWorkspacePayload } from "@/app-shell.view-model";
import { buildWorkspaceAttentionCounts } from "@/lib/daily-control-tower";
import { DailyControlTowerScreen } from "@/screens/daily-control-tower-screen";
import { renderWithRouter } from "@/test/render";
import type { DataWorkspaceResponse, TradingWorkspaceResponse } from "@/types";
import { commandOperatorTriage, getOperatorTriageInbox, type OperatorTriageInbox, type OperatorTriageItem } from "@/lib/operator-inbox-triage";

vi.mock("@/lib/operator-inbox-triage", async (load) => ({ ...await load<typeof import("@/lib/operator-inbox-triage")>(), getOperatorTriageInbox: vi.fn(), commandOperatorTriage: vi.fn(), bulkAssignOperatorTriage: vi.fn() }));
vi.mock("@/lib/table-workbench", async (load) => ({ ...await load<typeof import("@/lib/table-workbench")>(), loadTableViews: vi.fn(async () => []), saveTableView: vi.fn() }));

// The shared host inbox: one blocker and one informational item, neither of which is a
// workspace signal, so the grid interleaves them by severity with the workspace rows.
const inboxBlocker: OperatorTriageItem = { workItemId: "cash-break", label: "Cash break triage", detail: "Cash break needs an owner.", workspace: "accounting", targetRoute: "/accounting/reconciliation", kind: "ReconciliationBreak",
  tone: "Critical", createdAt: "2026-05-14T18:00:00Z", ownerId: null, dueAt: null, version: 2, sourceVersion: 4, acknowledgedAt: null, snoozedUntil: null,
  capabilities: { canAssign: true, canAcknowledge: true, canSnooze: true, canUnsnooze: false, disabledReason: null }, activity: [] };
const inboxInfo: OperatorTriageItem = { ...inboxBlocker, workItemId: "pack-note", label: "Pack reviewer note", detail: "Reviewer left a note.", workspace: "reporting", targetRoute: "/reporting/report-packs", kind: null, tone: "Info", ownerId: "operator-1" };
const inboxResponse: OperatorTriageInbox = { asOf: "2026-05-14T21:30:00Z", actorId: "operator-1", items: [inboxBlocker, inboxInfo], persistenceAvailable: true, unavailableReason: null };

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

function gridRowKeys() {
  const grid = screen.getByRole("treegrid", { name: "Today queue" });
  return [...grid.querySelectorAll<HTMLElement>("tbody tr[data-dense-row-id]")].map((row) => row.dataset.denseRowId);
}

async function renderLoaded(options: Parameters<typeof renderScreen>[0] = {}) {
  const result = renderScreen(options);
  await screen.findByRole("row", { name: "Cash break triage" });
  return result;
}

describe("DailyControlTowerScreen", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getOperatorTriageInbox).mockResolvedValue(inboxResponse);
  });

  it("renders the ranked queue and defaults the evidence pane to the leading decision", async () => {
    await renderLoaded();

    expect(
      screen.getByRole("heading", { name: "What needs an operator decision now" })
    ).toBeInTheDocument();

    // The headline and the default evidence follow the worst queued workspace decision -- the same
    // one the masthead attention pill names on every route -- even when an inbox blocker is older.
    expect(
      screen.getByRole("row", { name: "Brokerage sync failed" })
    ).toHaveAttribute("aria-selected", "true");
    expect(
      screen.getByRole("region", { name: /Brokerage sync failed evidence summary/i })
    ).toBeInTheDocument();
    const confidence = screen.getByLabelText("Daily control tower confidence");
    expect(confidence).toHaveTextContent("3 workspace signals in the queue · 2 of 2 shared inbox items");
    expect(confidence).toHaveTextContent("Stale update");
    expect(confidence).toHaveTextContent("May 14, 2026");
    expect(screen.queryByLabelText("Daily control tower decision drivers")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Daily control tower trust posture")).not.toBeInTheDocument();
    expect(screen.getByText("More evidence").closest("details")).not.toHaveAttribute("open");
  });

  it("keeps read-only inbox capability visible with its reason behind a compact disclosure", async () => {
    const user = userEvent.setup();
    vi.mocked(getOperatorTriageInbox).mockResolvedValue({
      ...inboxResponse,
      persistenceAvailable: false,
      unavailableReason: "Seeded read-only inbox. Use a configured shared host for durable triage actions."
    });
    await renderLoaded();

    const warning = screen.getByText("Read-only inbox");
    expect(warning).toBeVisible();
    expect(warning.closest("details")).not.toHaveAttribute("open");
    await user.click(warning);
    expect(screen.getByText("Inbox is read-only. Use a configured shared host for durable triage actions.")).toBeVisible();
    warning.focus();
    await user.keyboard("{Escape}");
    expect(warning.closest("details")).not.toHaveAttribute("open");
    expect(warning).toHaveFocus();
    expect(screen.queryByText(/^Seeded read-only inbox/)).not.toBeInTheDocument();
    expect(screen.getByRole("treegrid", { name: "Today queue" })).toBeInTheDocument();
  });

  it("interleaves workspace signals and shared-inbox items in one grid with a Source column", async () => {
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
    const { container } = await renderLoaded();

    // One grid under the one Queue heading; the badge counts exactly its workspace-signal rows.
    const grid = screen.getByRole("treegrid", { name: "Today queue" });
    expect(screen.getByRole("region", { name: "Queue" })).toContainElement(grid);
    expect(screen.getAllByRole("treegrid")).toHaveLength(1);
    expect(grid.querySelectorAll('tbody tr[data-dense-row-id^="workspace:"]')).toHaveLength(total);
    expect(grid.querySelectorAll('tbody tr[data-dense-row-id^="inbox:"]')).toHaveLength(2);
    expect(within(grid).getByRole("columnheader", { name: "Source" })).toBeInTheDocument();
    expect(screen.getByRole("row", { name: "Brokerage sync failed" })).toHaveTextContent("Workspace signal");
    expect(screen.getByRole("row", { name: "Cash break triage" })).toHaveTextContent("Shared inbox");
    expect(screen.getByRole("row", { name: "Cash break triage" })).toHaveTextContent("Unassigned");

    // Severity first (both blockers lead), then age (the older inbox blocker first), then source rank.
    const keys = gridRowKeys();
    expect(keys.slice(0, 2)).toEqual(["inbox:cash-break", "workspace:work-item:brokerage-sync"]);
    expect(keys.slice(2, 4).every((key) => key?.startsWith("workspace:"))).toBe(true);
    expect(keys[4]).toBe("inbox:pack-note");

    // Source filter counts are per source; the workspace count is the sidebar badge's count.
    const source = screen.getByRole("tablist", { name: "Queue source" });
    expect(within(source).getByRole("tab", { name: /Workspace signals/ })).toHaveTextContent(String(total));
    expect(within(source).getByRole("tab", { name: /Shared inbox/ })).toHaveTextContent("2");
    expect(within(source).getByRole("tab", { name: /All sources/ })).toHaveTextContent(String(total + 2));
    expect(container.querySelector("[data-today-inbox-state]")).toHaveAttribute("data-today-inbox-state", "ready");
    expect(screen.getByRole("group", { name: "Inbox filters" })).toContainElement(screen.getByRole("button", { name: "Assign 0 selected" }));
    expect(screen.getByLabelText("Daily control tower confidence")).toHaveTextContent("2 of 2 shared inbox items");

    const results = await axe(container);
    expect(results.violations).toEqual([]);
  });

  it("keeps secondary table tools available without another toolbar row", async () => {
    const user = userEvent.setup();
    await renderLoaded();
    const tools = screen.getByText("Table tools", { selector: "summary" });
    expect(tools.closest("details")).not.toHaveAttribute("open");
    expect(screen.getByRole("searchbox")).toBeVisible();
    expect(screen.getByRole("combobox", { name: "Inbox visibility" })).toBeVisible();
    expect(screen.getByRole("button", { name: "Export CSV" })).not.toBeVisible();
    await user.click(tools);
    expect(screen.getByRole("button", { name: "Export CSV" })).toBeVisible();
    tools.focus();
    await user.keyboard("{Escape}");
    expect(tools.closest("details")).not.toHaveAttribute("open");
  });

  it("filters rows of the one grid by source", async () => {
    const user = userEvent.setup();
    await renderLoaded();
    const source = screen.getByRole("tablist", { name: "Queue source" });

    await user.click(within(source).getByRole("tab", { name: /Shared inbox/ }));
    expect(gridRowKeys()).toEqual(["inbox:cash-break", "inbox:pack-note"]);
    // The leading workspace decision is filtered out, so the first visible row is selected.
    expect(screen.getByRole("row", { name: "Cash break triage" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("region", { name: "Cash break triage triage detail" })).toBeInTheDocument();

    await user.click(within(source).getByRole("tab", { name: /Workspace signals/ }));
    expect(gridRowKeys().every((key) => key?.startsWith("workspace:"))).toBe(true);
    expect(screen.queryByRole("group", { name: "Inbox filters" })).not.toBeInTheDocument();
    expect(screen.getByRole("region", { name: /Brokerage sync failed evidence summary/i })).toBeInTheDocument();

    await user.click(within(source).getByRole("tab", { name: /All sources/ }));
    expect(gridRowKeys()).toHaveLength(5);
  });

  it("keeps inbox triage on inbox rows and an unconfirmed command across source filtering", async () => {
    const user = userEvent.setup();
    vi.mocked(commandOperatorTriage).mockRejectedValueOnce(new Error("connection interrupted"))
      .mockResolvedValueOnce({ status: "Applied", workItemId: inboxBlocker.workItemId, replayed: true });
    await renderLoaded({ search: "" });

    await user.click(screen.getByRole("row", { name: "Cash break triage" }));
    const detail = screen.getByRole("region", { name: "Cash break triage triage detail" });
    expect(screen.queryByRole("region", { name: /evidence summary/i })).not.toBeInTheDocument();
    // Blockers are never snoozed; the route stays reachable from the grid and the rail.
    expect(within(detail).getByRole("button", { name: "Snooze" })).toBeDisabled();
    expect(screen.getByRole("link", { name: "Open Cash break triage in its workspace" })).toHaveAttribute("href", "/accounting/reconciliation");

    await user.click(within(detail).getByRole("button", { name: "Acknowledge" }));
    await user.type(screen.getByLabelText("Reason"), "Investigation started");
    await user.click(screen.getByRole("button", { name: "Apply triage action" }));
    expect(await screen.findByRole("button", { name: "Retry same command" })).toBeEnabled();

    // Filtering the inbox rows out of the grid keeps the unconfirmed command and its retry.
    const source = screen.getByRole("tablist", { name: "Queue source" });
    await user.click(within(source).getByRole("tab", { name: /Workspace signals/ }));
    expect(screen.queryByRole("row", { name: "Cash break triage" })).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Retry same command" }));
    await waitFor(() => expect(commandOperatorTriage).toHaveBeenCalledTimes(2));
    expect(vi.mocked(commandOperatorTriage).mock.calls[0][0]).toBe("cash-break");
    expect(vi.mocked(commandOperatorTriage).mock.calls[1][1]).toEqual(vi.mocked(commandOperatorTriage).mock.calls[0][1]);
  });

  it("bulk-assigns checked inbox rows and offers no checkbox on workspace rows", async () => {
    const user = userEvent.setup();
    await renderLoaded();
    expect(within(screen.getByRole("row", { name: "Brokerage sync failed" })).queryByRole("checkbox")).not.toBeInTheDocument();
    await user.click(screen.getByRole("checkbox", { name: "Select Cash break triage for assignment" }));
    await user.click(screen.getByRole("button", { name: "Assign 1 selected" }));
    expect(screen.getByRole("dialog")).toHaveAccessibleName("Assign 1 selected items");
  });

  it("assigns the selected inbox row from the keyboard and moves j/k across both sources", async () => {
    const user = userEvent.setup();
    await renderLoaded();
    screen.getByRole("row", { name: "Cash break triage" }).focus();
    await user.keyboard("j");
    await waitFor(() => expect(screen.getByRole("row", { name: "Brokerage sync failed" })).toHaveFocus());
    // The evidence rail follows a workspace row; the triage rail follows an inbox row.
    expect(screen.getByRole("region", { name: /Brokerage sync failed evidence summary/i })).toBeInTheDocument();
    await user.keyboard("k");
    await waitFor(() => expect(screen.getByRole("row", { name: "Cash break triage" })).toHaveFocus());
    await user.keyboard("a");
    expect(screen.getByRole("dialog")).toHaveAccessibleName("Assign item");
    expect(screen.getByRole("dialog")).toHaveTextContent("Cash break triage");
  });

  it("keeps workspace signals when the shared inbox fails, and storage-unavailable actions disabled", async () => {
    vi.mocked(getOperatorTriageInbox).mockRejectedValue(new Error("Forbidden"));
    const { container, unmount } = renderScreen();
    const failure = await screen.findByRole("alert");
    expect(failure).toHaveTextContent("Inbox triage is unavailable. Workspace signals in this queue are unaffected");
    expect(screen.getAllByRole("alert")).toHaveLength(1);
    expect(screen.getAllByRole("button", { name: "Retry inbox" })).toHaveLength(1);
    expect(gridRowKeys()).toHaveLength(3);
    expect(container.querySelector("[data-today-inbox-state]")).toHaveAttribute("data-today-inbox-state", "unavailable");
    expect(screen.getByLabelText("Daily control tower confidence")).toHaveTextContent("Shared inbox unavailable");
    unmount();

    vi.mocked(getOperatorTriageInbox).mockResolvedValue({ ...inboxResponse, persistenceAvailable: false, unavailableReason: "Production triage storage is unavailable." });
    const user = userEvent.setup();
    await renderLoaded();
    expect(screen.getByRole("alert")).toHaveTextContent("Production triage storage is unavailable");
    await user.click(screen.getByRole("row", { name: "Pack reviewer note" }));
    expect(screen.getByRole("button", { name: "Acknowledge" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Assign" })).toBeDisabled();
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
    await user.click(within(status).getByRole("button", { name: "Refresh evidence: control tower" }));

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

  it("shows the combined ranked queue without a scope selection", async () => {
    await renderLoaded({ search: "" });
    expect(screen.queryByRole("region", { name: "Choose Control Tower scope" })).not.toBeInTheDocument();
    expect(screen.getByRole("treegrid", { name: "Today queue" })).toBeInTheDocument();
    expect(screen.getByLabelText("Daily control tower confidence")).toHaveTextContent("All authorized scopes");
    expect(screen.getByRole("columnheader", { name: "Workspace" })).toBeInTheDocument();
    // Owner is tracked for inbox rows only; workspace signals say so rather than inventing one.
    expect(screen.getByRole("columnheader", { name: "Owner" })).toBeInTheDocument();
    expect(screen.getByRole("row", { name: "Brokerage sync failed" })).toHaveTextContent("Not tracked");
    expect(screen.getByRole("row", { name: "Pack reviewer note" })).toHaveTextContent("operator-1");
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
