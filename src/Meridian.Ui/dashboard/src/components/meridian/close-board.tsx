import { DesignSystemStatus } from "@/design-system/status";
import type { CloseBoardTaskCard, CloseBoardViewModel } from "@/lib/close-board";
import { cn } from "@/lib/utils";

const TONE_STATUS: Record<CloseBoardTaskCard["tone"], "ready" | "blocked" | "action" | "info"> = {
  done: "ready",
  blocked: "blocked",
  overdue: "action",
  waiting: "info",
  open: "info"
};

/**
 * Close tasks laid out by due day relative to the period end. A view of the retained close plan:
 * it changes no task, and every date, owner, status and dependency comes from the plan.
 */
export function CloseBoard({ board }: { board: CloseBoardViewModel }) {
  return (
    <section aria-labelledby="close-board-title" className="space-y-2">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h3 id="close-board-title" className="text-sm font-semibold">Close board</h3>
        <p className="text-xs text-muted-foreground" role="status">
          {board.doneCount} of {board.taskCount} signed off
          {board.overdueCount > 0 ? ` · ${board.overdueCount} overdue` : ""}
          {board.blockedCount > 0 ? ` · ${board.blockedCount} blocked` : ""}
          {` · T+0 is the period end, ${board.periodEndLabel}`}
          {board.closeDueLabel ? ` · close due ${board.closeDueLabel}` : ""}
        </p>
      </div>
      <p className="text-xs text-muted-foreground">Calendar days after the period end, from each task's retained due date. Business-day calendars are not applied here.</p>
      <ol className="flex gap-3 overflow-x-auto pb-2" aria-label="Close tasks by due day">
        {board.columns.map((column) => (
          <li key={column.dueDate} className="w-60 shrink-0 space-y-2" aria-label={`${column.label}, ${column.dateLabel}: ${column.tasks.length} ${column.tasks.length === 1 ? "task" : "tasks"}`}>
            <div className={cn("flex items-baseline justify-between border-b border-border pb-1", column.isToday && "border-b-2 border-primary")}>
              <span className="text-sm font-semibold">{column.label}</span>
              <span className="text-xs text-muted-foreground">{column.isToday ? "Today · " : ""}{column.dateLabel}</span>
            </div>
            <ul className="space-y-2">
              {column.tasks.map((card) => <CloseBoardCard key={card.taskId} card={card} />)}
            </ul>
          </li>
        ))}
      </ol>
      {board.undatedTasks.length > 0 ? (
        <div className="space-y-2">
          <h4 className="text-xs font-semibold">Without a calendar due date</h4>
          <ul className="flex flex-wrap gap-2">
            {board.undatedTasks.map((card) => <CloseBoardCard key={card.taskId} card={card} />)}
          </ul>
        </div>
      ) : null}
    </section>
  );
}

function CloseBoardCard({ card }: { card: CloseBoardTaskCard }) {
  return (
    <li className="w-60 space-y-1 rounded-[var(--radius-card,2px)] border border-border bg-card p-2 text-xs" data-close-board-tone={card.tone}>
      {/* Name first at full card width; a long status ("Waiting on dependency") gets its own line. */}
      <span className="block text-sm font-medium text-foreground">{card.name}</span>
      <DesignSystemStatus status={TONE_STATUS[card.tone]}>{card.statusLabel}</DesignSystemStatus>
      <p className="text-muted-foreground">{card.owner}</p>
      {card.toneDetail ? <p className={card.tone === "blocked" ? "text-danger" : "text-foreground"}>{card.toneDetail}</p> : null}
      {card.dependencies.length > 0 ? (
        <p className="text-muted-foreground">
          After{" "}
          {card.dependencies.map((dependency, index) => (
            <span key={dependency.taskId}>
              {index > 0 ? ", " : ""}
              <span className={dependency.met ? "line-through" : "text-foreground"} title={dependency.met ? "Signed off" : "Not yet signed off"}>{dependency.name}</span>
              <span className="sr-only">{dependency.met ? " (signed off)" : " (not yet signed off)"}</span>
            </span>
          ))}
        </p>
      ) : null}
    </li>
  );
}
