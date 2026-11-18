import { formatUtcDate } from "@/lib/format";
import type { CloseTask, CloseTaskStatus } from "@/types";

/**
 * A day-by-day view of one close plan: tasks grouped by their due date relative to the period end
 * (T+0 is the period end). Every value comes from the retained plan; the board derives only the
 * grouping, dependency names and the overdue flag, and never infers a date or a dependency.
 */
export interface CloseBoardTaskCard {
  taskId: string;
  name: string;
  owner: string;
  statusLabel: string;
  tone: "done" | "blocked" | "overdue" | "waiting" | "open";
  /** Plain-language reason for the tone, shown beside the status. */
  toneDetail: string | null;
  dependencies: { taskId: string; name: string; met: boolean }[];
}

export interface CloseBoardColumn {
  /** Calendar days after the period end; negative before it. */
  offset: number;
  label: string;
  dateLabel: string;
  dueDate: string;
  isToday: boolean;
  tasks: CloseBoardTaskCard[];
}

export interface CloseBoardViewModel {
  columns: CloseBoardColumn[];
  periodEndLabel: string;
  closeDueLabel: string | null;
  doneCount: number;
  taskCount: number;
  overdueCount: number;
  blockedCount: number;
  /** Tasks whose due date is not a calendar date; listed, never placed on a guessed day. */
  undatedTasks: CloseBoardTaskCard[];
}

const STATUS_LABELS: Record<CloseTaskStatus, string> = {
  NotStarted: "Not started",
  WaitingOnDependency: "Waiting on dependency",
  InProgress: "In progress",
  ReadyForSignOff: "Ready for sign-off",
  SignedOff: "Signed off",
  Blocked: "Blocked"
};

const DAY_MS = 24 * 60 * 60 * 1000;

function parseDate(value: string | null | undefined): number | null {
  if (!value || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return null;
  const time = Date.parse(`${value}T00:00:00Z`);
  return Number.isFinite(time) ? time : null;
}

/** "T+3", "T+0", "T-2": calendar days relative to the period end. */
export function closeOffsetLabel(offset: number): string {
  return offset < 0 ? `T${offset}` : `T+${offset}`;
}

export function buildCloseBoard(
  plan: { periodEnd: string; closeDueDate?: string | null; tasks: readonly CloseTask[] } | null | undefined,
  today: string
): CloseBoardViewModel | null {
  const periodEnd = parseDate(plan?.periodEnd);
  if (!plan || periodEnd === null || plan.tasks.length === 0) return null;
  const todayTime = parseDate(today);
  const names = new Map(plan.tasks.map((task) => [task.taskId, task.displayName || task.taskId]));
  const statusById = new Map(plan.tasks.map((task) => [task.taskId, task.status]));

  const card = (task: CloseTask): CloseBoardTaskCard => {
    const due = parseDate(task.dueDate);
    const done = task.status === "SignedOff";
    const dependencies = task.dependencies.map((dependency) => ({
      taskId: dependency.dependsOnTaskId,
      name: names.get(dependency.dependsOnTaskId) ?? `${dependency.dependsOnTaskId} (not in this plan)`,
      met: statusById.get(dependency.dependsOnTaskId) === "SignedOff"
    }));
    const unmet = dependencies.filter((dependency) => !dependency.met);
    const overdue = !done && due !== null && todayTime !== null && due < todayTime;
    const tone: CloseBoardTaskCard["tone"] = done ? "done"
      : task.status === "Blocked" || task.blockerReason ? "blocked"
      : overdue ? "overdue"
      : task.status === "WaitingOnDependency" || unmet.length > 0 ? "waiting"
      : "open";
    const toneDetail = tone === "blocked" ? task.blockerReason ?? "Blocked"
      : tone === "overdue" ? `Overdue since ${formatUtcDate(new Date(due!))}`
      : tone === "waiting" && unmet.length > 0 ? `Waiting on ${unmet.map((dependency) => dependency.name).join(", ")}`
      : null;
    return {
      taskId: task.taskId,
      name: task.displayName || task.taskId,
      owner: task.owner?.trim() || "Unassigned",
      statusLabel: STATUS_LABELS[task.status] ?? task.status,
      tone,
      toneDetail,
      dependencies
    };
  };

  const byOffset = new Map<number, CloseBoardTaskCard[]>();
  const undatedTasks: CloseBoardTaskCard[] = [];
  for (const task of plan.tasks) {
    const due = parseDate(task.dueDate);
    if (due === null) {
      undatedTasks.push(card(task));
      continue;
    }
    const offset = Math.round((due - periodEnd) / DAY_MS);
    byOffset.set(offset, [...(byOffset.get(offset) ?? []), card(task)]);
  }

  const columns = [...byOffset.entries()].sort(([left], [right]) => left - right).map(([offset, tasks]) => {
    const date = new Date(periodEnd + offset * DAY_MS);
    const dueDate = date.toISOString().slice(0, 10);
    return {
      offset,
      label: closeOffsetLabel(offset),
      dateLabel: formatUtcDate(date),
      dueDate,
      isToday: dueDate === today,
      tasks
    };
  });
  const all = [...columns.flatMap((column) => column.tasks), ...undatedTasks];
  const closeDue = parseDate(plan.closeDueDate);
  return {
    columns,
    periodEndLabel: formatUtcDate(new Date(periodEnd)),
    closeDueLabel: closeDue === null ? null : `${closeOffsetLabel(Math.round((closeDue - periodEnd) / DAY_MS))} · ${formatUtcDate(new Date(closeDue))}`,
    doneCount: all.filter((task) => task.tone === "done").length,
    taskCount: all.length,
    overdueCount: all.filter((task) => task.tone === "overdue").length,
    blockedCount: all.filter((task) => task.tone === "blocked").length,
    undatedTasks
  };
}
