import { StatusBadge } from "@/design-system/status";
import { formatTimestamp } from "@/lib/format";
import { describeDue, type DueUrgency } from "@/lib/due-urgency";

const SEVERITY: Record<DueUrgency["state"], string> = { overdue: "blocked", today: "action", soon: "info", later: "info", none: "info" };

/**
 * A due date as urgency (UX Phase E): "Overdue 3 days" in the blocked colour, "Due today" as action,
 * otherwise a quiet "Due in 2 days" or date. The exact due time is in the tooltip. Renders the fallback
 * when there is no due date.
 */
export function DueChip({ dueAt, nowMs, done, fallback = "—" }: { dueAt: string | null | undefined; nowMs?: number; done?: boolean; fallback?: React.ReactNode }) {
  const due = describeDue(dueAt, nowMs, { done });
  if (due.state === "none") return <>{fallback}</>;
  const exact = due.dueMs === null ? "" : /^\d{4}-\d{2}-\d{2}$/.test(dueAt?.trim() ?? "") ? dueAt!.trim() : formatTimestamp(due.dueMs);
  return (
    <span title={exact ? `Due ${exact}` : undefined} data-due-state={due.state} className="inline-flex whitespace-nowrap">
      {due.state === "later" || due.state === "soon"
        ? <span className="text-xs tabular-nums text-muted-foreground">{due.label}</span>
        : <StatusBadge status={SEVERITY[due.state]} label={due.label} />}
    </span>
  );
}
