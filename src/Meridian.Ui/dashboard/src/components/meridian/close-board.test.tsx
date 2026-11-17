import { render, screen, within } from "@testing-library/react";
import { axe } from "jest-axe";
import { describe, expect, it } from "vitest";
import { CloseBoard } from "@/components/meridian/close-board";
import { buildCloseBoard } from "@/lib/close-board";
import type { CloseTask } from "@/types";

const tasks: CloseTask[] = [
  { taskId: "bank-rec", displayName: "Bank reconciliation", status: "SignedOff", owner: "ops", dueDate: "2026-11-02", dependencies: [], signOffs: [], evidenceLinks: [] },
  { taskId: "accruals", displayName: "Accruals", status: "InProgress", owner: "controller", dueDate: "2026-11-03",
    dependencies: [{ dependencyId: "d1", dependsOnTaskId: "bank-rec", reason: "" }], signOffs: [], evidenceLinks: [] },
  { taskId: "nav", displayName: "NAV review", status: "Blocked", owner: "", dueDate: "2026-11-05", blockerReason: "Pricing source stale",
    dependencies: [{ dependencyId: "d2", dependsOnTaskId: "accruals", reason: "" }], signOffs: [], evidenceLinks: [] }
];

describe("CloseBoard", () => {
  it("lays tasks out by due day with status, owner, blocker and named dependencies", async () => {
    const board = buildCloseBoard({ periodEnd: "2026-10-31", closeDueDate: "2026-11-05", tasks }, "2026-11-03")!;
    const { container } = render(<CloseBoard board={board} />);

    expect(screen.getByRole("status")).toHaveTextContent("1 of 3 signed off · 1 blocked · T+0 is the period end, Oct 31, 2026 · close due T+5 · Nov 5, 2026");
    const days = within(screen.getByRole("list", { name: "Close tasks by due day" })).getAllByRole("listitem", { name: /^T\+/ });
    expect(days.map((day) => day.getAttribute("aria-label"))).toEqual([
      "T+2, Nov 2, 2026: 1 task", "T+3, Nov 3, 2026: 1 task", "T+5, Nov 5, 2026: 1 task"
    ]);
    expect(days[1]).toHaveTextContent("Today");
    expect(days[1]).toHaveTextContent("Accruals");
    expect(days[1]).toHaveTextContent("After Bank reconciliation (signed off)");
    expect(days[2]).toHaveTextContent("Pricing source stale");
    expect(days[2]).toHaveTextContent("Unassigned");
    expect(days[2]).toHaveTextContent("After Accruals (not yet signed off)");
    expect((await axe(container)).violations).toEqual([]);
  });
});
