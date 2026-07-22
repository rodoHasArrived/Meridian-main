import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { NotificationCenter } from "@/components/meridian/notification-center";
import { PriceAlertsProvider } from "@/lib/price-alerts/service";
import * as api from "@/lib/api";
import type { OperatorInbox, SystemOverviewResponse } from "@/types";

function memoryStorage() {
  const map = new Map<string, string>();
  return {
    getItem: (key) => map.get(key) ?? null,
    setItem: (key, value) => void map.set(key, value),
    removeItem: (key) => void map.delete(key)
  };
}

const inbox: OperatorInbox = {
  asOf: "2026-07-03T12:00:00Z",
  criticalCount: 1,
  warningCount: 0,
  reviewCount: 0,
  summary: "1 critical",
  items: [
    {
      workItemId: "reconciliation-break-run-1",
      kind: "ReconciliationBreak",
      label: "Reconciliation break",
      detail: "Position break needs review.",
      tone: "Critical",
      createdAt: "2026-07-03T12:00:00Z",
      runId: "run-1",
      fundAccountId: null,
      auditReference: null,
      workspace: "accounting",
      targetRoute: "/accounting/reconciliation",
      targetPageTag: "FundReconciliation"
    }
  ]
};

const overview = {
  recentEvents: [
    { id: "evt-1", type: "warning", message: "Provider latency elevated.", source: "Alpaca", timestamp: "2026-07-03T11:59:00Z" }
  ]
} as unknown as SystemOverviewResponse;

function renderCenter() {
  return render(
    <MemoryRouter>
      <PriceAlertsProvider options={{ storage: memoryStorage() }}>
        <NotificationCenter overview={overview} />
      </PriceAlertsProvider>
    </MemoryRouter>
  );
}

describe("NotificationCenter", () => {
  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
    // Read-state persists to jsdom localStorage; clear it so tests stay isolated.
    window.localStorage.clear();
  });

  it("shows the unread count and opens a severity-grouped drawer with deep links", async () => {
    vi.spyOn(api, "getOperatorInbox").mockResolvedValue(inbox);
    const user = userEvent.setup();
    renderCenter();

    // Inbox item + system event = 2 unread once the inbox resolves.
    await waitFor(() => expect(screen.getByRole("button", { name: "Notifications — 2 unread" })).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: "Notifications — 2 unread" }));

    const dialog = screen.getByRole("dialog", { name: "Notifications" });
    expect(within(dialog).getByText("Reconciliation break")).toBeInTheDocument();
    expect(within(dialog).getByText("Alpaca")).toBeInTheDocument();
    expect(within(dialog).getByRole("link", { name: "Open Reconciliation break" })).toHaveAttribute(
      "href",
      "/accounting/reconciliation"
    );
  });

  it("marks everything read from the drawer, clearing the badge", async () => {
    vi.spyOn(api, "getOperatorInbox").mockResolvedValue(inbox);
    const user = userEvent.setup();
    renderCenter();

    await user.click(await screen.findByRole("button", { name: "Notifications — 2 unread" }));
    await user.click(screen.getByRole("button", { name: /Mark all read/ }));

    await waitFor(() => expect(screen.getByRole("button", { name: "Notifications — none unread" })).toBeInTheDocument());
  });

  it("still renders when the operator inbox fetch fails", async () => {
    vi.spyOn(api, "getOperatorInbox").mockRejectedValue(new Error("inbox offline"));
    renderCenter();

    // The system event feed still produces one unread notification.
    await waitFor(() => expect(screen.getByRole("button", { name: "Notifications — 1 unread" })).toBeInTheDocument());
  });
});
