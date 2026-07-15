/**
 * NotificationCenter — persistent notification rail: a bell button with an unread count and
 * an anchored panel (tone dot, title, detail, relative time). The durable complement to Toast:
 * toasts confirm actions and vanish; this answers "what fired while I was away". Controlled —
 * you own the items array.
 *
 * @example
 * <NotificationCenter
 *   items={[{ id: "n1", tone: "error", title: "Drawdown breach", detail: "momentum.v4 · -8.4%", time: firedAt, read: false }]}
 *   onMarkAllRead={() => setAll(read)}
 *   onSelect={(n) => nav(n.href)} />
 */
export interface NotificationItem {
  id: string;
  /** @default "info" */
  tone?: "info" | "success" | "warning" | "error";
  title: React.ReactNode;
  detail?: React.ReactNode;
  /** Date, epoch ms, or ISO string — renders as relative time. */
  time?: Date | number | string;
  read?: boolean;
}
export interface NotificationCenterProps {
  items?: NotificationItem[];
  onMarkAllRead?: () => void;
  /** Row click handler (rows become clickable when set). */
  onSelect?: (item: NotificationItem) => void;
  /** Bell button label. @default "Alerts" */
  label?: React.ReactNode;
}
export declare function NotificationCenter(props: NotificationCenterProps): JSX.Element;
