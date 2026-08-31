import { describe, expect, it, vi } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { buildAppShellViewState, type AppShellWorkspacePayload } from "@/app-shell.view-model";
import { DailyControlTowerScreen } from "@/screens/daily-control-tower-screen";
import { renderWithRouter } from "@/test/render";
import type { DataWorkspaceResponse, TradingWorkspaceResponse } from "@/types";

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
      onEditOperatingScope={onEditOperatingScope}
      onRefresh={onRefresh}
      refreshing={refreshing}
    />
  );
}

describe("DailyControlTowerScreen", () => {
  it("renders the ranked finance queue and defaults the evidence pane to the top row", () => {
    renderScreen();

    expect(
      screen.getByRole("heading", { name: "What needs an operator decision now" })
    ).toBeInTheDocument();

    // The shared dense table exposes the selected row and its controlled detail
    // panel without adding a second, mouse-only selection control.
    expect(
      screen.getByRole("row", { name: "Report pack approval waiting" })
    ).toHaveAttribute("aria-selected", "true");
    expect(
      screen.getByRole("region", { name: /Report pack approval waiting evidence summary/i })
    ).toBeInTheDocument();
    expect(screen.getByLabelText("Daily control tower confidence")).toHaveTextContent("3 ranked items");
    expect(screen.getByLabelText("Daily control tower confidence")).toHaveTextContent("Stale update");
    expect(screen.getByLabelText("Daily control tower confidence")).toHaveTextContent("May 14, 2026");
    expect(screen.queryByLabelText("Daily control tower decision drivers")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Daily control tower trust posture")).not.toBeInTheDocument();
    expect(screen.getByText("More evidence").closest("details")).not.toHaveAttribute("open");
  });

  it("exposes connectivity, scope, and freshness remediation in their owning cards", async () => {
    const user = userEvent.setup();
    const onEditOperatingScope = vi.fn();
    const onRefresh = vi.fn();
    renderScreen({ onEditOperatingScope, onRefresh, search: "" });

    const confidence = screen.getByRole("region", { name: "Daily control tower confidence" });
    expect(within(confidence).getByText("Connectivity")).toBeInTheDocument();
    expect(within(confidence).getByRole("link", { name: "Open provider posture" }))
      .toHaveAttribute("href", "/data/providers");

    await user.click(within(confidence).getByRole("button", { name: "Set operating scope" }));
    await user.click(within(confidence).getByRole("button", { name: "Refresh control tower evidence" }));

    expect(onEditOperatingScope).toHaveBeenCalledTimes(1);
    expect(onRefresh).toHaveBeenCalledTimes(1);
  });

  it("preserves compatible operating scope in connectivity recovery", () => {
    renderScreen({ search: "?symbol=MSFT" });

    const confidence = screen.getByRole("region", { name: "Daily control tower confidence" });
    expect(within(confidence).getByRole("link", { name: "Open provider posture" }))
      .toHaveAttribute("href", "/data/providers?symbol=MSFT");
  });

  it("updates the evidence pane in place when another queue row is selected", async () => {
    const user = userEvent.setup();
    renderScreen();

    const brokerageRow = screen.getByRole("row", {
      name: "Brokerage sync failed"
    });
    expect(brokerageRow).not.toHaveAttribute("aria-selected", "true");

    await user.click(brokerageRow);

    // Selection moved without navigating away: the evidence region now reflects
    // the clicked row, and the pressed state follows it.
    expect(brokerageRow).toHaveAttribute("aria-selected", "true");
    expect(
      screen.getByRole("region", { name: /Brokerage sync failed evidence summary/i })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("row", { name: "Report pack approval waiting" })
    ).not.toHaveAttribute("aria-selected", "true");
  });

  it("requires an explicit scope choice before showing the combined queue", async () => {
    const user = userEvent.setup();
    renderScreen({ search: "" });

    expect(screen.getByRole("region", { name: "Choose Control Tower scope" })).toBeInTheDocument();
    expect(screen.queryByRole("treegrid", { name: "Daily control tower finance queue" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Review all scopes" }));

    expect(screen.getByRole("treegrid", { name: "Daily control tower finance queue" })).toBeInTheDocument();
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

    const priorityPanel = screen.getByText("Report pack approval waiting", {
      selector: ".mds-rpanel__title"
    }).closest(".mds-rpanel");
    expect(priorityPanel).not.toBeNull();
    expect(within(priorityPanel as HTMLElement).getAllByRole("link")).toHaveLength(1);
  });

  it("has no accessibility violations", async () => {
    const { container } = renderScreen();
    const results = await axe(container);
    expect(results.violations).toEqual([]);
  });
});
