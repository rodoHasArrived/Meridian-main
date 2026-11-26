import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { axe } from "jest-axe";
import { NotificationCenter } from "@/components/meridian/notification-center";
import { PriceAlertsProvider } from "@/lib/price-alerts/service";
import * as api from "@/lib/api";
import type { OperatorInbox, SystemOverviewResponse } from "@/types";

function memoryStorage() {
  const map = new Map<string, string>();
  return {
    getItem: (key: string) => map.get(key) ?? null,
    setItem: (key: string, value: string) => void map.set(key, value),
    removeItem: (key: string) => void map.delete(key)
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
    await waitFor(() => expect(screen.getByRole("button", { name: "Notifications — 1 unread; operator inbox unavailable" })).toBeInTheDocument());
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: /Notifications — 1 unread/ }));
    expect(screen.getByRole("status")).toHaveTextContent("Operator inbox unavailable");
    expect(screen.getByText("Alpaca")).toBeInTheDocument();
    expect(screen.queryByText("You're all caught up.")).not.toBeInTheDocument();
    expect((await axe(screen.getByRole("dialog", { name: "Notifications" }))).violations).toEqual([]);
  });

  it("retries the unavailable inbox without losing the independent system feed", async () => {
    const fetchInbox = vi.spyOn(api, "getOperatorInbox")
      .mockRejectedValueOnce(new Error("inbox offline"))
      .mockResolvedValue(inbox);
    const user = userEvent.setup();
    renderCenter();

    await user.click(await screen.findByRole("button", { name: /Notifications — 1 unread; operator inbox unavailable/ }));
    await user.click(screen.getByRole("button", { name: "Retry inbox" }));

    expect(await screen.findByText("Reconciliation break")).toBeInTheDocument();
    expect(screen.getByText("Alpaca")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Retry inbox" })).not.toBeInTheDocument();
    expect(fetchInbox).toHaveBeenCalledTimes(2);
  });

  it("does not claim an empty inbox while its evidence is still loading", async () => {
    vi.spyOn(api, "getOperatorInbox").mockReturnValue(new Promise<OperatorInbox>(() => {}));
    const user = userEvent.setup();
    render(
      <MemoryRouter>
        <PriceAlertsProvider options={{ storage: memoryStorage() }}>
          <NotificationCenter overview={null} />
        </PriceAlertsProvider>
      </MemoryRouter>
    );
    await user.click(screen.getByRole("button", { name: "Notifications — none unread; operator inbox loading" }));
    expect(screen.getByRole("status")).toHaveTextContent("Counts include loaded feeds only.");
    expect(screen.queryByText("You're all caught up.")).not.toBeInTheDocument();
    expect(screen.getByText("No notifications from the loaded feeds.")).toBeInTheDocument();
  });
});
