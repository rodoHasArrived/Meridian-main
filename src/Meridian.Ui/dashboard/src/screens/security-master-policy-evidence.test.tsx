import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { SecuritySchedulesPanel } from "./accounting-screen.security-master-panels";
import { buildSecuritySchedulesViewState } from "./accounting-screen.view-model";

const base = { securityId: "test-bond", displayName: "Test bond", assetClass: "Bond", schedules: [], selectedRowId: null };

describe("Security Master schedule and governance evidence", () => {
  it("shows a typed cash-flow blocker separately from a resolved exercise schedule", () => {
    const view = buildSecuritySchedulesViewState({ ...base,
      cashFlowBlocker: { code: "NegativeInterestUnsupported", reason: "Signed interest is unsupported." },
      declaredExercise: { status: "Resolved", asOf: "2026-11-11", securityVersion: 7, windowCount: 1, blockers: [], diagnostics: [] }
    });
    render(<MemoryRouter><SecuritySchedulesPanel view={view} onSelect={vi.fn()} /></MemoryRouter>);
    expect(screen.getByRole("heading", { name: "Cash-flow calculation: blocked" })).toBeTruthy();
    expect(screen.getByText("NegativeInterestUnsupported: Signed interest is unsupported.")).toBeTruthy();
    expect(screen.getByRole("heading", { name: "Declared call/put schedule: resolved" })).toBeTruthy();
    expect(view.statusAnnouncement).toContain("NegativeInterestUnsupported");
  });

  it("shows typed blockers and effective latched governance even when the schedule has no rows", () => {
    const view = buildSecuritySchedulesViewState({ ...base,
      declaredExercise: { status: "Blocked", asOf: "2026-11-11", securityVersion: 7, windowCount: 0,
        blockers: [{ code: "CalendarUnavailable", reason: "The declared calendar is unavailable." }], diagnostics: ["Refresh calendar evidence."] },
      governance: { profile: "Institutional", profileSource: "Latched", isProfileLatched: true,
        requireGovernedTermAmendments: true, requireIndependentReviewer: true, requireIndependentProfileApprover: true,
        contradictedKeys: ["SecurityMasterWorkbench:GovernanceProfile"] }
    });
    render(<MemoryRouter><SecuritySchedulesPanel view={view} onSelect={vi.fn()} /></MemoryRouter>);
    expect(screen.getByRole("heading", { name: "Declared call/put schedule: blocked" })).toBeTruthy();
    expect(screen.getByText("CalendarUnavailable: The declared calendar is unavailable.")).toBeTruthy();
    expect(screen.getByText("Refresh calendar evidence.")).toBeTruthy();
    expect(screen.getByText("2026-11-11")).toBeTruthy();
    expect(screen.getByRole("heading", { name: "Effective governance: Institutional" })).toBeTruthy();
    expect(screen.getByText(/Institutional requirements remain in force until the host restarts/)).toBeTruthy();
    expect(screen.getByText(/Ignored configuration: SecurityMasterWorkbench:GovernanceProfile/)).toBeTruthy();
    expect(view.statusAnnouncement).toContain("CalendarUnavailable");
  });

  it("keeps undeclared and unavailable evidence distinct from a resolved schedule and Standard profile", () => {
    const view = buildSecuritySchedulesViewState(base);
    expect(view.declaredExercise).toBeNull();
    expect(view.governance.title).toBe("Effective governance unavailable");
    expect(view.governance.fields).toEqual([]);
  });

  it("shows resolved window count, retained version and diagnostics without granting exercise authority", () => {
    const view = buildSecuritySchedulesViewState({ ...base,
      declaredExercise: { status: "Resolved", asOf: "2026-11-11", securityVersion: 8, windowCount: 2,
        blockers: [], diagnostics: ["The notice deadline has elapsed."] }
    });
    expect(view.declaredExercise).toMatchObject({ title: "Declared call/put schedule: resolved", blocked: false,
      messages: ["The notice deadline has elapsed."] });
    expect(view.declaredExercise!.summary).toContain("2 exercise windows resolved");
    expect(view.declaredExercise!.summary).toContain("no exercise authority");
    expect(view.declaredExercise!.fields).toContainEqual({ label: "Security version", value: "8" });
  });
});
