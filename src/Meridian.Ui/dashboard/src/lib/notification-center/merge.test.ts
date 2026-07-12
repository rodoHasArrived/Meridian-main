import { describe, expect, it } from "vitest";
import { buildNotificationCenterModel } from "@/lib/notification-center/merge";
import { emptyNotificationReadState } from "@/lib/notification-center/read-state";
import type { OperatorWorkItem, SystemEventRecord } from "@/types";
import type { PriceAlertTrigger } from "@/lib/price-alerts/index";

function inboxItem(overrides: Partial<OperatorWorkItem> = {}): OperatorWorkItem {
  return {
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
    targetPageTag: "FundReconciliation",
    ...overrides
  };
}

function systemEvent(overrides: Partial<SystemEventRecord> = {}): SystemEventRecord {
  return {
    id: "evt-1",
    type: "warning",
    message: "Provider latency elevated.",
    source: "Alpaca",
    timestamp: "2026-07-03T11:59:00Z",
    ...overrides
  };
}

function priceTrigger(overrides: Partial<PriceAlertTrigger> = {}): PriceAlertTrigger {
  return {
    id: "trigger-1",
    alertId: "alert-1",
    symbol: "AAPL",
    condition: "above",
    field: "last",
    threshold: 200,
    triggeredPrice: 201.5,
    triggeredAt: "2026-07-03T11:58:00Z",
    acknowledged: false,
    note: null,
    ...overrides
  };
}

describe("buildNotificationCenterModel", () => {
  it("merges the three feeds with mapped severities and a deep link for inbox items", () => {
    const model = buildNotificationCenterModel({
      inbox: [inboxItem()],
      systemEvents: [systemEvent()],
      priceTriggers: [priceTrigger()],
      readState: emptyNotificationReadState()
    });

    expect(model.notifications).toHaveLength(3);
    expect(model.unreadCount).toBe(3);

    const inbox = model.notifications.find((entry) => entry.source === "inbox")!;
    expect(inbox.severity).toBe("critical");
    expect(inbox.route).toBe("/accounting/reconciliation");
    expect(inbox.dismissible).toBe(true);

    const system = model.notifications.find((entry) => entry.source === "system")!;
    expect(system.severity).toBe("warning");
    expect(system.route).toBeNull();

    const price = model.notifications.find((entry) => entry.source === "price-alert")!;
    expect(price.severity).toBe("warning");
    expect(price.route).toBe("/data/alerts");
    expect(price.dismissible).toBe(false);
  });

  it("orders unread first, then by severity, then newest", () => {
    const model = buildNotificationCenterModel({
      inbox: [
        inboxItem({ workItemId: "w-info", tone: "Info", label: "Info item", createdAt: "2026-07-03T10:00:00Z" }),
        inboxItem({ workItemId: "w-crit", tone: "Critical", label: "Critical item", createdAt: "2026-07-03T09:00:00Z" })
      ],
      systemEvents: [],
      priceTriggers: [],
      readState: { version: 1, readIds: ["inbox:w-crit"], dismissedIds: [] }
    });

    // The unread Info item sorts ahead of the read Critical item.
    expect(model.notifications.map((entry) => entry.sourceId)).toEqual(["w-info", "w-crit"]);
    expect(model.unreadCount).toBe(1);
  });

  it("applies read state from the store and the trigger's own acknowledged flag", () => {
    const model = buildNotificationCenterModel({
      inbox: [inboxItem()],
      systemEvents: [],
      priceTriggers: [priceTrigger({ acknowledged: true })],
      readState: { version: 1, readIds: ["inbox:reconciliation-break-run-1"], dismissedIds: [] }
    });

    expect(model.unreadCount).toBe(0);
    expect(model.notifications.every((entry) => entry.read)).toBe(true);
  });

  it("filters out dismissed inbox and system items", () => {
    const model = buildNotificationCenterModel({
      inbox: [inboxItem()],
      systemEvents: [systemEvent()],
      priceTriggers: [],
      readState: { version: 1, readIds: [], dismissedIds: ["inbox:reconciliation-break-run-1", "system:evt-1"] }
    });

    expect(model.notifications).toHaveLength(0);
  });

  it("counts unread by severity", () => {
    const model = buildNotificationCenterModel({
      inbox: [
        inboxItem({ workItemId: "c1", tone: "Critical" }),
        inboxItem({ workItemId: "w1", tone: "Warning" })
      ],
      systemEvents: [systemEvent({ id: "e1", type: "error" })],
      priceTriggers: [],
      readState: emptyNotificationReadState()
    });

    expect(model.severityCounts).toEqual({ critical: 2, warning: 1, info: 0, success: 0 });
  });
});
