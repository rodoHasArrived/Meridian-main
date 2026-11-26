// @vitest-environment node
import { describe, expect, it } from "vitest";
import { buildCloseBoard, closeOffsetLabel } from "@/lib/close-board";
import type { CloseTask } from "@/types";

function task(taskId: string, dueDate: string, status: CloseTask["status"], extra: Partial<CloseTask> = {}): CloseTask {
  return { taskId, displayName: taskId.replaceAll("-", " "), status, owner: "controller", dueDate, dependencies: [], signOffs: [], evidenceLinks: [], ...extra };
}

const plan = {
  periodEnd: "2026-10-31",
  closeDueDate: "2026-11-05",
  tasks: [
    task("bank-rec", "2026-11-02", "SignedOff"),
    task("accruals", "2026-11-02", "InProgress", { dependencies: [{ dependencyId: "d1", dependsOnTaskId: "bank-rec", reason: "" }] }),
    task("fx-reval", "2026-11-03", "NotStarted", { dependencies: [{ dependencyId: "d2", dependsOnTaskId: "accruals", reason: "" }] }),
    task("nav-review", "2026-11-05", "Blocked", { blockerReason: "Pricing source stale", owner: "" }),
    task("cutoff", "2026-10-31", "SignedOff")
  ]
};

describe("buildCloseBoard", () => {
  it("groups tasks by calendar days after the period end, in order", () => {
    const board = buildCloseBoard(plan, "2026-11-03")!;
    expect(board.columns.map((column) => [column.label, column.tasks.map((card) => card.taskId)])).toEqual([
      ["T+0", ["cutoff"]],
      ["T+2", ["bank-rec", "accruals"]],
      ["T+3", ["fx-reval"]],
      ["T+5", ["nav-review"]]
    ]);
    expect(board.columns[2].isToday).toBe(true);
    expect(board.closeDueLabel).toBe("T+5 · Nov 5, 2026");
    expect(board).toMatchObject({ taskCount: 5, doneCount: 2, blockedCount: 1 });
  });

  it("names dependencies and says which are unmet, from the plan's own task statuses", () => {
    const board = buildCloseBoard(plan, "2026-11-01")!;
    const cards = board.columns.flatMap((column) => column.tasks);
    const accruals = cards.find((card) => card.taskId === "accruals")!;
    expect(accruals.dependencies).toEqual([{ taskId: "bank-rec", name: "bank rec", met: true }]);
    expect(accruals.tone).toBe("open");
    const fx = cards.find((card) => card.taskId === "fx-reval")!;
    expect(fx.tone).toBe("waiting");
    expect(fx.toneDetail).toBe("Waiting on accruals");
  });

  it("flags overdue only for unfinished tasks past their due date, and keeps blockers as blocked", () => {
    const cards = buildCloseBoard(plan, "2026-11-04")!.columns.flatMap((column) => column.tasks);
    expect(cards.find((card) => card.taskId === "accruals")).toMatchObject({ tone: "overdue", toneDetail: "Overdue since Nov 2, 2026" });
    expect(cards.find((card) => card.taskId === "bank-rec")!.tone).toBe("done");
    expect(cards.find((card) => card.taskId === "nav-review")).toMatchObject({ tone: "blocked", toneDetail: "Pricing source stale", owner: "Unassigned" });
  });

  it("lists a task without a calendar due date instead of placing it on a guessed day, and names a missing dependency", () => {
    const board = buildCloseBoard({ periodEnd: "2026-10-31", tasks: [
      task("orphan", "", "NotStarted", { dependencies: [{ dependencyId: "d", dependsOnTaskId: "gone", reason: "" }] })
    ] }, "2026-11-01")!;
    expect(board.columns).toEqual([]);
    expect(board.undatedTasks[0].dependencies[0]).toEqual({ taskId: "gone", name: "gone (not in this plan)", met: false });
  });

  it("returns nothing without a plan, a valid period end or any tasks", () => {
    expect(buildCloseBoard(null, "2026-11-01")).toBeNull();
    expect(buildCloseBoard({ ...plan, periodEnd: "not-a-date" }, "2026-11-01")).toBeNull();
    expect(buildCloseBoard({ ...plan, tasks: [] }, "2026-11-01")).toBeNull();
  });

  it("labels offsets before the period end with a minus sign", () => {
    expect(closeOffsetLabel(-2)).toBe("T-2");
    expect(closeOffsetLabel(0)).toBe("T+0");
  });
});
