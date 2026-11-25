// @vitest-environment node
import { describe, expect, it } from "vitest";
import type { AppShellOperatorFocusItem } from "@/app-shell.workflow-continuity-types";
import {
  ATTENTION_COUNT_RULE, attentionItemFromInbox, buildAttentionItems, countAttention, needsAttention
} from "@/lib/attention-items";
import type { OperatorTriageItem } from "@/lib/operator-inbox-triage";

const NOW = Date.parse("2026-05-14T12:00:00Z");

const signal = (id: string, tone: AppShellOperatorFocusItem["tone"], route = "/accounting/reconciliation"): AppShellOperatorFocusItem => ({
  id, label: id, detail: `${id} detail`, route, workspaceLabel: "Accounting", actionLabel: "Open", tone, ariaLabel: id
});

const inbox = (workItemId: string, overrides: Partial<OperatorTriageItem> = {}): OperatorTriageItem => ({
  workItemId, label: workItemId, detail: `${workItemId} detail`, workspace: "Data", targetRoute: "/data/providers", kind: null,
  tone: "Warning", createdAt: "2026-05-14T09:00:00Z", ownerId: null, dueAt: null, version: 1, sourceVersion: 1,
  acknowledgedAt: null, snoozedUntil: null,
  capabilities: { canAssign: true, canAcknowledge: true, canSnooze: true, canUnsnooze: true, disabledReason: null },
  activity: [],
  ...overrides
});

describe("attention items", () => {
  it("states the count rule and applies it: blocked and action items, excluding snoozed", () => {
    expect(ATTENTION_COUNT_RULE).toBe("blocked + action, excluding snoozed");
    expect(needsAttention({ severity: "blocked", snoozed: false })).toBe(true);
    expect(needsAttention({ severity: "action", snoozed: false })).toBe(true);
    expect(needsAttention({ severity: "action", snoozed: true })).toBe(false);
    expect(needsAttention({ severity: "info", snoozed: false })).toBe(false);
    expect(needsAttention({ severity: "clear", snoozed: false })).toBe(false);
  });

  it("gives every item a source, severity, owner and due date from either source", () => {
    const items = buildAttentionItems(
      [signal("break", "blocked")],
      [inbox("stale-feed", { ownerId: "operator-1", dueAt: "2026-05-15T00:00:00Z" })],
      NOW
    );
    expect(items.map(({ key, source, severity, owner, dueAt, workspaceKey }) => ({ key, source, severity, owner, dueAt, workspaceKey }))).toEqual([
      { key: "workspace:break", source: "workspace", severity: "blocked", owner: null, dueAt: null, workspaceKey: "accounting" },
      { key: "inbox:stale-feed", source: "inbox", severity: "action", owner: "operator-1", dueAt: "2026-05-15T00:00:00Z", workspaceKey: "data" }
    ]);
  });

  it("treats a running snooze as snoozed, and never snoozes a blocked inbox item", () => {
    const later = "2026-05-14T18:00:00Z";
    expect(attentionItemFromInbox(inbox("a", { snoozedUntil: later }), NOW).snoozed).toBe(true);
    expect(attentionItemFromInbox(inbox("b", { snoozedUntil: "2026-05-14T10:00:00Z" }), NOW).snoozed).toBe(false);
    expect(attentionItemFromInbox(inbox("c", { tone: "Critical", snoozedUntil: later }), NOW).snoozed).toBe(false);
  });

  it("counts one total whose per-source and per-workspace views add up to it", () => {
    const counts = countAttention(buildAttentionItems(
      [signal("break", "blocked"), signal("review", "review"), signal("loading", "pending"), signal("done", "ready"),
        signal("provider", "review", "/data/providers")],
      [inbox("stale-feed"), inbox("snoozed", { snoozedUntil: "2026-05-14T18:00:00Z" }), inbox("note", { tone: "Info" }),
        inbox("cash", { tone: "Critical", workspace: "Accounting", targetRoute: "/accounting/reconciliation" })],
      NOW
    ));

    expect(counts.total).toBe(5);
    expect(counts.bySource).toEqual({ workspace: 3, inbox: 2 });
    expect(counts.byWorkspace).toEqual({ accounting: 3, data: 2 });
    expect(counts.bySource.workspace + counts.bySource.inbox).toBe(counts.total);
    expect(Object.values(counts.byWorkspace).reduce((sum, count) => sum + count, 0)).toBe(counts.total);
    expect(counts.severityByWorkspace).toEqual({ accounting: "blocked", data: "action" });
  });

  it("is the workspace signals alone while the shared inbox is loading or unavailable", () => {
    expect(countAttention(buildAttentionItems([signal("break", "blocked")], null)).total).toBe(1);
  });
});

describe("one work item in both sources", () => {
  it("is listed and counted once, as the inbox item that carries its owner and snooze", () => {
    const items = buildAttentionItems(
      [signal("work-item:brokerage-sync", "review"), signal("break", "blocked")],
      [inbox("brokerage-sync", { ownerId: "operator-1" })],
      NOW
    );
    expect(items.map((item) => item.key)).toEqual(["workspace:break", "inbox:brokerage-sync"]);
    expect(countAttention(items).total).toBe(2);
  });

  it("keeps the workspace signal while the inbox is not loaded", () => {
    expect(buildAttentionItems([signal("work-item:brokerage-sync", "review")], null).map((item) => item.key))
      .toEqual(["workspace:work-item:brokerage-sync"]);
  });
});
