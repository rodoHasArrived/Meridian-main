import { routeForOperatorWorkItem } from "@/app-shell.workflow-routing";
import type { OperatorWorkItem, OperatorWorkItemTone, SystemEventRecord } from "@/types";
import type { PriceAlertTrigger } from "@/lib/price-alerts/index";
import type { NotificationEnvelope, NotificationSeverity, NotificationSeverityCounts } from "@/lib/notification-center/types";
import type { NotificationReadState } from "@/lib/notification-center/read-state";

const SEVERITY_RANK: Record<NotificationSeverity, number> = {
  critical: 3,
  warning: 2,
  info: 1,
  success: 0
};

const PRICE_ALERTS_ROUTE = "/data/alerts";

export interface NotificationFeeds {
  inbox: readonly OperatorWorkItem[];
  systemEvents: readonly SystemEventRecord[];
  priceTriggers: readonly PriceAlertTrigger[];
  readState: NotificationReadState;
}

export interface NotificationCenterModel {
  notifications: NotificationEnvelope[];
  unreadCount: number;
  severityCounts: NotificationSeverityCounts;
}

/**
 * Merges the operator inbox, system events, and price-alert triggers into one
 * ordered feed. Inbox/system read state comes from `readState`; price-alert read
 * state is the trigger's own `acknowledged` flag. Dismissed inbox/system items are
 * filtered out entirely.
 */
export function buildNotificationCenterModel(feeds: NotificationFeeds): NotificationCenterModel {
  const dismissed = new Set(feeds.readState.dismissedIds);
  const read = new Set(feeds.readState.readIds);
  const envelopes: NotificationEnvelope[] = [];

  for (const item of feeds.inbox) {
    const id = `inbox:${item.workItemId}`;
    if (dismissed.has(id)) {
      continue;
    }
    envelopes.push({
      id,
      source: "inbox",
      sourceId: item.workItemId,
      severity: severityFromInboxTone(item.tone),
      title: item.label,
      detail: item.detail,
      timestamp: item.createdAt,
      route: safeRoute(() => routeForOperatorWorkItem(item)),
      read: read.has(id),
      dismissible: true
    });
  }

  for (const event of feeds.systemEvents) {
    const id = `system:${event.id}`;
    if (dismissed.has(id)) {
      continue;
    }
    envelopes.push({
      id,
      source: "system",
      sourceId: event.id,
      severity: severityFromSystemType(event.type),
      title: event.source,
      detail: event.message,
      timestamp: event.timestamp,
      route: null,
      read: read.has(id),
      dismissible: true
    });
  }

  for (const trigger of feeds.priceTriggers) {
    const id = `price-alert:${trigger.id}`;
    envelopes.push({
      id,
      source: "price-alert",
      sourceId: trigger.id,
      severity: "warning",
      title: `${trigger.symbol} price alert`,
      detail: describePriceTrigger(trigger),
      timestamp: trigger.triggeredAt,
      route: PRICE_ALERTS_ROUTE,
      read: trigger.acknowledged,
      dismissible: false
    });
  }

  envelopes.sort(compareEnvelopes);

  const severityCounts: NotificationSeverityCounts = { critical: 0, warning: 0, info: 0, success: 0 };
  let unreadCount = 0;
  for (const envelope of envelopes) {
    if (!envelope.read) {
      unreadCount += 1;
      severityCounts[envelope.severity] += 1;
    }
  }

  return { notifications: envelopes, unreadCount, severityCounts };
}

function compareEnvelopes(a: NotificationEnvelope, b: NotificationEnvelope): number {
  // Unread first, then by severity, then newest.
  if (a.read !== b.read) {
    return a.read ? 1 : -1;
  }
  const severity = SEVERITY_RANK[b.severity] - SEVERITY_RANK[a.severity];
  if (severity !== 0) {
    return severity;
  }
  return compareTimestampsDesc(a.timestamp, b.timestamp);
}

function compareTimestampsDesc(a: string, b: string): number {
  const left = Date.parse(a);
  const right = Date.parse(b);
  const leftValid = Number.isFinite(left);
  const rightValid = Number.isFinite(right);
  if (!leftValid && !rightValid) {
    return 0;
  }
  if (!leftValid) {
    return 1;
  }
  if (!rightValid) {
    return -1;
  }
  return right - left;
}

function severityFromInboxTone(tone: OperatorWorkItemTone): NotificationSeverity {
  switch (tone) {
    case "Critical":
      return "critical";
    case "Warning":
      return "warning";
    case "Success":
      return "success";
    default:
      return "info";
  }
}

function severityFromSystemType(type: SystemEventRecord["type"]): NotificationSeverity {
  switch (type) {
    case "error":
      return "critical";
    case "warning":
      return "warning";
    default:
      return "info";
  }
}

function describePriceTrigger(trigger: PriceAlertTrigger): string {
  const direction = trigger.condition.replace("-", " ");
  return `${trigger.field} ${direction} ${trigger.threshold} — triggered at ${trigger.triggeredPrice}`;
}

function safeRoute(build: () => string): string | null {
  try {
    const route = build();
    return route && route.trim() ? route : null;
  } catch {
    return null;
  }
}
