import { useState } from "react";
import { Link } from "react-router-dom";
import { CheckCircle2, ExternalLink, History, RotateCcw, Trash2, Undo2, X } from "lucide-react";
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
import { useActivityLog } from "@/lib/activity-log/store";
import type { ActivityEntry, ActivityOperationStatus, ActivityTone } from "@/lib/activity-log/types";
import { formatRelativeAge } from "@/lib/time";
import { cn } from "@/lib/utils";

const TONE_DOT: Record<ActivityTone, string> = {
  default: "bg-primary",
  success: "bg-success",
  warning: "bg-warning",
  danger: "bg-danger"
};

const OPERATION_LABEL: Record<ActivityOperationStatus, string> = {
  pending: "Pending confirmation",
  completed: "Completed",
  failed: "Failed",
  uncertain: "Outcome unconfirmed"
};

/**
 * Masthead activity button + drawer: a running, scannable ledger of the mutating
 * actions taken this session — sibling to the notification center. Reversible
 * actions carry an Undo; irreversible ones link to their compensating context.
 */
export function ActivityCenter() {
  const [open, setOpen] = useState(false);
  const log = useActivityLog();
  const { entries, unseenCount } = log;

  const buttonLabel = unseenCount > 0
    ? `Activity — ${unseenCount} new`
    : "Activity history";

  const openDrawer = () => {
    setOpen(true);
    log.markAllSeen();
  };

  return (
    <>
      <button
        type="button"
        aria-label={buttonLabel}
        title={buttonLabel}
        onClick={openDrawer}
        className="relative inline-flex h-9 w-9 items-center justify-center rounded-md border border-border/60 bg-transparent text-muted-foreground transition-colors hover:bg-secondary/60 hover:text-foreground focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-primary/40"
      >
        <History className="h-4 w-4" aria-hidden="true" />
        {unseenCount > 0 ? (
          <span
            aria-hidden="true"
            className="absolute -right-1 -top-1 inline-flex h-4 min-w-4 items-center justify-center rounded-full border border-background bg-primary px-1 font-mono text-[10px] font-semibold leading-none text-background"
          >
            {unseenCount > 99 ? "99+" : unseenCount}
          </span>
        ) : null}
      </button>

      <Sheet open={open} onOpenChange={setOpen}>
        <SheetContent aria-labelledby="activity-center-title" aria-describedby="activity-center-summary" side="right">
          <SheetHeader>
            <div className="flex items-start justify-between gap-3">
              <div className="min-w-0">
                <SheetTitle id="activity-center-title">Recent operations</SheetTitle>
                <SheetDescription id="activity-center-summary">
                  {entries.length === 0
                    ? "Actions you take will show up here."
                    : `${entries.length} recorded action${entries.length === 1 ? "" : "s"} in this browser session.`}
                  {" "}Local history is a navigation aid. Open retained records to verify server status and authority.
                </SheetDescription>
              </div>
              <div className="flex shrink-0 items-center gap-2 pr-9">
                {entries.length > 0 ? (
                  <Button size="sm" variant="outline" type="button" onClick={() => log.clear()}>
                    <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
                    Clear local history
                  </Button>
                ) : null}
                <SheetCloseButton onClick={() => setOpen(false)} />
              </div>
            </div>
          </SheetHeader>
          <SheetBody>
            {entries.length === 0 ? (
              <p className="text-sm text-muted-foreground">
                No activity yet. Supported actions appear here with their outcome and record link.
                Undo appears only when a real compensating operation is available in this session.
              </p>
            ) : (
              <ul className="space-y-2" aria-label="Recorded actions">
                {entries.map((entry) => (
                  <ActivityRow
                    key={entry.id}
                    entry={entry}
                    onUndo={() => void log.undo(entry.id)}
                    onOpenRoute={() => setOpen(false)}
                  />
                ))}
              </ul>
            )}
          </SheetBody>
        </SheetContent>
      </Sheet>
    </>
  );
}

interface ActivityRowProps {
  entry: ActivityEntry;
  onUndo: () => void;
  onOpenRoute: () => void;
}

function ActivityRow({ entry, onUndo, onOpenRoute }: ActivityRowProps) {
  return (
    <li className="rounded-md border border-border/50 px-3 py-2.5 text-sm">
      <div className="flex items-start gap-2.5">
        <span aria-hidden="true" className={cn("mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full", TONE_DOT[entry.tone])} />
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <span className="truncate font-medium">{entry.title}</span>
          </div>
          {entry.operation ? <p className={cn("mt-1 text-xs font-semibold", entry.operation.status === "uncertain" ? "text-warning" : entry.operation.status === "failed" ? "text-danger" : undefined)}>{OPERATION_LABEL[entry.operation.status]}</p> : null}
          {entry.detail ? <p className="mt-0.5 text-xs leading-5 text-muted-foreground">{entry.detail}</p> : null}
          {entry.operation ? (
            <dl className="mt-1.5 grid gap-1 text-xs leading-5">
              {entry.operation.scope ? <div><dt className="inline text-muted-foreground">Scope: </dt><dd className="inline break-words">{entry.operation.scope}</dd></div> : null}
              {entry.operation.actor ? <div><dt className="inline text-muted-foreground">Actor: </dt><dd className="inline break-words">{entry.operation.actor}</dd></div> : null}
              {entry.operation.reference ? <div><dt className="inline text-muted-foreground">Reference: </dt><dd className="inline break-all font-mono">{entry.operation.reference}</dd></div> : null}
              <div><dt className="inline text-muted-foreground">Updated: </dt><dd className="inline"><time dateTime={entry.operation.updatedAt}>{formatRelativeAge(entry.operation.updatedAt)}</time></dd></div>
            </dl>
          ) : null}
          {entry.operation?.status === "uncertain" ? <p className="mt-1 text-xs leading-5 text-warning">Inspect the retained record before taking another action. This history does not retry commands.</p> : null}
          <div className="mt-1 flex flex-wrap items-center gap-x-2 gap-y-1 font-mono text-[10px] uppercase tracking-[0.1em] text-muted-foreground">
            <span>{entry.kind}</span>
            <span aria-hidden="true">·</span>
            <span>{formatRelativeAge(entry.timestamp)}</span>
          </div>
          {entry.undoStatus === "failed" && entry.undoError ? (
            <p className="mt-1 text-xs text-danger" role="alert">{entry.undoError}</p>
          ) : null}
        </div>
        <div className="flex shrink-0 flex-col items-end gap-1.5">
          <UndoControl status={entry.undoStatus} onUndo={onUndo} />
          {entry.route ? (
            <Button asChild size="sm" variant="ghost">
              <Link to={entry.route} onClick={onOpenRoute} aria-label={`Open ${entry.title}`}>
                <ExternalLink className="h-3.5 w-3.5" aria-hidden="true" />
                <span className="ml-1">{entry.routeLabel ?? "Open"}</span>
              </Link>
            </Button>
          ) : null}
        </div>
      </div>
    </li>
  );
}

function UndoControl({ status, onUndo }: { status: ActivityEntry["undoStatus"]; onUndo: () => void }) {
  switch (status) {
    case "idle":
      return (
        <Button size="sm" variant="outline" type="button" onClick={onUndo}>
          <Undo2 className="h-3.5 w-3.5" aria-hidden="true" />
          <span className="ml-1">Undo</span>
        </Button>
      );
    case "running":
      return (
        <Button size="sm" variant="outline" type="button" disabled busy busyLabel="Undoing…">
          Undoing…
        </Button>
      );
    case "failed":
      return (
        <Button size="sm" variant="outline" type="button" onClick={onUndo}>
          <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
          <span className="ml-1">Retry undo</span>
        </Button>
      );
    case "undone":
      return (
        <span className="inline-flex items-center gap-1 font-mono text-[10px] uppercase tracking-[0.12em] text-success">
          <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
          Undone
        </span>
      );
    case "expired":
      return (
        <span className="inline-flex items-center gap-1 font-mono text-[10px] uppercase tracking-[0.12em] text-muted-foreground">
          <X className="h-3.5 w-3.5" aria-hidden="true" />
          Undo expired
        </span>
      );
    default:
      return null;
  }
}
