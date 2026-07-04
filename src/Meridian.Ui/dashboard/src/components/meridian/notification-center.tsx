import { useState } from "react";
import { Link } from "react-router-dom";
import { Bell, Check, ExternalLink, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Sheet,
  SheetBody,
  SheetCloseButton,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle
} from "@/components/ui/sheet";
import { useNotificationCenter } from "@/hooks/use-notification-center";
import { formatRelativeAge } from "@/lib/time";
import { cn } from "@/lib/utils";
import type { NotificationEnvelope, NotificationSeverity } from "@/lib/notification-center/types";
import type { SystemOverviewResponse } from "@/types";

const SEVERITY_ORDER: NotificationSeverity[] = ["critical", "warning", "info", "success"];
const SEVERITY_LABELS: Record<NotificationSeverity, string> = {
  critical: "Critical",
  warning: "Warning",
  info: "Info",
  success: "Resolved"
};
const SEVERITY_DOT: Record<NotificationSeverity, string> = {
  critical: "bg-danger",
  warning: "bg-warning",
  info: "bg-primary",
  success: "bg-success"
};
const SOURCE_LABELS: Record<NotificationEnvelope["source"], string> = {
  inbox: "Operator inbox",
  system: "System",
  "price-alert": "Price alert"
};

export interface NotificationCenterProps {
  overview: SystemOverviewResponse | null;
  fundAccountId?: string | null;
}

/**
 * Masthead notification bell + drawer. Merges the operator inbox, system events,
 * and price-alert triggers into one severity-grouped feed with per-item deep links
 * and read/dismiss actions. Replaces the standalone price-alerts bell.
 */
export function NotificationCenter({ overview, fundAccountId }: NotificationCenterProps) {
  const [open, setOpen] = useState(false);
  const center = useNotificationCenter({ overview, fundAccountId });
  const { notifications, unreadCount } = center;

  const bellLabel = unreadCount > 0
    ? `Notifications — ${unreadCount} unread`
    : "Notifications — none unread";

  return (
    <>
      <button
        type="button"
        aria-label={bellLabel}
        title={bellLabel}
        onClick={() => setOpen(true)}
        className="relative inline-flex h-9 w-9 items-center justify-center rounded-md border border-border/60 bg-transparent text-muted-foreground transition-colors hover:bg-secondary/60 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
      >
        <Bell className="h-4 w-4" aria-hidden="true" />
        {unreadCount > 0 ? (
          <span
            aria-hidden="true"
            className="absolute -right-1 -top-1 inline-flex h-4 min-w-4 items-center justify-center rounded-full border border-background bg-warning px-1 font-mono text-[10px] font-semibold leading-none text-background"
          >
            {unreadCount > 99 ? "99+" : unreadCount}
          </span>
        ) : null}
      </button>

      <Sheet open={open} onOpenChange={setOpen}>
        <SheetContent aria-labelledby="notification-center-title" aria-describedby="notification-center-summary" side="right">
          <SheetHeader>
            <div className="flex items-start justify-between gap-3">
              <div className="min-w-0">
                <SheetTitle id="notification-center-title">Notifications</SheetTitle>
                <SheetDescription id="notification-center-summary">
                  {notifications.length === 0
                    ? "You're all caught up."
                    : `${unreadCount} unread of ${notifications.length}.`}
                </SheetDescription>
              </div>
              <div className="flex flex-shrink-0 items-center gap-2">
                {unreadCount > 0 ? (
                  <Button size="sm" variant="outline" type="button" onClick={() => center.markAllRead()}>
                    <Check className="h-3.5 w-3.5" aria-hidden="true" />
                    Mark all read
                  </Button>
                ) : null}
                <SheetCloseButton onClick={() => setOpen(false)} />
              </div>
            </div>
          </SheetHeader>
          <SheetBody>
            {notifications.length === 0 ? (
              <p className="text-sm text-muted-foreground">No operator, system, or price-alert notifications right now.</p>
            ) : (
              SEVERITY_ORDER.map((severity) => {
                const rows = notifications.filter((entry) => entry.severity === severity);
                if (rows.length === 0) {
                  return null;
                }
                return (
                  <section key={severity} aria-label={`${SEVERITY_LABELS[severity]} notifications`} className="space-y-2">
                    <div className="eyebrow-label">{SEVERITY_LABELS[severity]}</div>
                    <ul className="space-y-2">
                      {rows.map((entry) => (
                        <NotificationRow
                          key={entry.id}
                          entry={entry}
                          onMarkRead={() => center.markRead(entry.id)}
                          onDismiss={() => center.dismiss(entry.id)}
                          onOpenRoute={() => {
                            center.markRead(entry.id);
                            setOpen(false);
                          }}
                        />
                      ))}
                    </ul>
                  </section>
                );
              })
            )}
          </SheetBody>
        </SheetContent>
      </Sheet>
    </>
  );
}

interface NotificationRowProps {
  entry: NotificationEnvelope;
  onMarkRead: () => void;
  onDismiss: () => void;
  onOpenRoute: () => void;
}

function NotificationRow({ entry, onMarkRead, onDismiss, onOpenRoute }: NotificationRowProps) {
  return (
    <li
      className={cn(
        "rounded-md border px-3 py-2.5 text-sm transition-colors",
        entry.read ? "border-border/50 bg-transparent" : "border-primary/30 bg-primary/5"
      )}
    >
      <div className="flex items-start gap-2.5">
        <span
          aria-hidden="true"
          className={cn("mt-1.5 h-1.5 w-1.5 flex-shrink-0 rounded-full", entry.read ? "bg-muted-foreground/40" : SEVERITY_DOT[entry.severity])}
        />
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <span className={cn("truncate", entry.read ? "font-medium" : "font-semibold")}>{entry.title}</span>
            {!entry.read ? <span className="sr-only">(unread)</span> : null}
          </div>
          <p className="mt-0.5 text-xs leading-5 text-muted-foreground">{entry.detail}</p>
          <div className="mt-1 flex flex-wrap items-center gap-x-2 gap-y-1 font-mono text-[10px] uppercase tracking-[0.1em] text-muted-foreground">
            <span>{SOURCE_LABELS[entry.source]}</span>
            <span aria-hidden="true">·</span>
            <span>{formatRelativeAge(entry.timestamp)}</span>
          </div>
        </div>
        <div className="flex flex-shrink-0 flex-col items-end gap-1.5">
          {entry.route ? (
            <Button asChild size="sm" variant="ghost">
              <Link to={entry.route} onClick={onOpenRoute} aria-label={`Open ${entry.title}`}>
                <ExternalLink className="h-3.5 w-3.5" aria-hidden="true" />
                <span className="ml-1">Open</span>
              </Link>
            </Button>
          ) : null}
          {!entry.read ? (
            <Button size="sm" variant="ghost" type="button" onClick={onMarkRead} aria-label={`Mark ${entry.title} as read`}>
              <Check className="h-3.5 w-3.5" aria-hidden="true" />
            </Button>
          ) : null}
          {entry.dismissible ? (
            <Button size="sm" variant="ghost" type="button" onClick={onDismiss} aria-label={`Dismiss ${entry.title}`}>
              <X className="h-3.5 w-3.5" aria-hidden="true" />
            </Button>
          ) : null}
        </div>
      </div>
    </li>
  );
}
