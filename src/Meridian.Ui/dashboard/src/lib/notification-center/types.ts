// Notification center — shared envelope shape merging the operator inbox,
// system events, and price-alert triggers into one client-side feed.
export type NotificationSource = "inbox" | "system" | "price-alert";
export type NotificationSeverity = "critical" | "warning" | "info" | "success";

export interface NotificationEnvelope {
  /** Stable id: `${source}:${sourceId}`. Read-state and actions key off this. */
  id: string;
  source: NotificationSource;
  /** The originating feed's own id (WorkItemId, system event id, trigger id). */
  sourceId: string;
  severity: NotificationSeverity;
  title: string;
  detail: string;
  /** ISO timestamp used for ordering and the relative-age label. */
  timestamp: string;
  /** Deep link into the owning surface, or null when the row isn't navigable. */
  route: string | null;
  read: boolean;
  /** Inbox/system rows can be dismissed; price alerts are managed via acknowledge. */
  dismissible: boolean;
}

export interface NotificationSeverityCounts {
  critical: number;
  warning: number;
  info: number;
  success: number;
}
