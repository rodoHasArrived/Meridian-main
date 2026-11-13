import { describe, expect, it } from "vitest";
import { buildNotificationCenterModel } from "./merge";
import { emptyNotificationReadState } from "./read-state";

describe("execution notification evidence", () => {
  it("retains stable identity, journal link and dismissal without changing execution state", () => {
    const event = { id: "fill-1", accountId: "account-1", scope: "paper", severity: "success" as const, title: "Fill observed", detail: "Unknown costs remain pending", timestamp: "2026-11-10T15:00:00Z", orderId: "order-1" };
    const feeds = { inbox: [], systemEvents: [], priceTriggers: [], execution: [event], readState: emptyNotificationReadState() };
    const model = buildNotificationCenterModel(feeds);
    expect(model.notifications[0]).toMatchObject({ id: "execution:fill-1", source: "execution", route: "/trading/orders?fundAccountId=account-1&orderId=order-1", read: false });
    const dismissed = buildNotificationCenterModel({ ...feeds, readState: { ...feeds.readState, dismissedIds: ["execution:fill-1"] } });
    expect(dismissed.notifications).toEqual([]);
    expect(event.title).toBe("Fill observed");
  });
});
