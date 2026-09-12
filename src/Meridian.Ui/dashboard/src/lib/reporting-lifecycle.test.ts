import { describe, expect, it } from "vitest";
import {
  admitsUpstreamChange,
  describeReportBlockState,
  describeReportClass,
  describeReportingControlState,
  describeReportingDataState,
  describeReportingFreezeState,
  describeReportingWorkflowState,
  isPublishedWorkflowState,
  normalizeReportBlockState,
  normalizeReportClass,
  normalizeReportingControlState,
  normalizeReportingDataState,
  normalizeReportingFreezeState,
  normalizeReportingWorkflowState,
  reportingStateSeverity,
  reportingWorkflowOrdinal,
  requiresPreparerAction,
  REPORTING_WORKFLOW_STATES
} from "@/lib/reporting-lifecycle";

describe("reporting workflow vocabulary", () => {
  it("maps server run statuses onto the controlled workflow vocabulary", () => {
    expect(normalizeReportingWorkflowState("Released")).toBe("Published");
    expect(normalizeReportingWorkflowState("Draft")).toBe("Preparing");
    expect(normalizeReportingWorkflowState("Validated")).toBe("ReadyForReview");
    expect(normalizeReportingWorkflowState("InReview")).toBe("InReview");
    expect(normalizeReportingWorkflowState("in review")).toBe("InReview");
    expect(normalizeReportingWorkflowState("Rejected")).toBe("ChangesRequested");
    expect(normalizeReportingWorkflowState("Failed")).toBe("Blocked");
  });

  it("resolves unknown workflow strings to Preparing so nothing reads as publishable", () => {
    expect(normalizeReportingWorkflowState("wat")).toBe("Preparing");
    expect(normalizeReportingWorkflowState(null)).toBe("Preparing");
    expect(normalizeReportingWorkflowState(undefined)).toBe("Preparing");
    expect(normalizeReportingWorkflowState("")).toBe("Preparing");
  });

  it("orders states along the production lifecycle", () => {
    expect(reportingWorkflowOrdinal("NotStarted")).toBeLessThan(reportingWorkflowOrdinal("InReview"));
    expect(reportingWorkflowOrdinal("InReview")).toBeLessThan(reportingWorkflowOrdinal("Approved"));
    expect(reportingWorkflowOrdinal("Approved")).toBeLessThan(reportingWorkflowOrdinal("Published"));
    expect(REPORTING_WORKFLOW_STATES.every((state) => reportingWorkflowOrdinal(state) >= 0)).toBe(true);
  });

  it("classifies terminal and preparer-owned states", () => {
    expect(isPublishedWorkflowState("Released")).toBe(true);
    expect(isPublishedWorkflowState("Restated")).toBe(true);
    expect(isPublishedWorkflowState("Approved")).toBe(false);
    expect(requiresPreparerAction("ChangesRequested")).toBe(true);
    expect(requiresPreparerAction("Blocked")).toBe(true);
    expect(requiresPreparerAction("InReview")).toBe(false);
  });

  it("describes workflow states with a label and severity", () => {
    expect(describeReportingWorkflowState("Blocked")).toMatchObject({ label: "Blocked", severity: "blocked" });
    expect(describeReportingWorkflowState("Released")).toMatchObject({ label: "Published", severity: "ready" });
    expect(describeReportingWorkflowState("ChangesRequested").severity).toBe("action");
  });
});

describe("reporting data and control vocabularies", () => {
  it("keeps data state separate from workflow state", () => {
    expect(normalizeReportingDataState("Current")).toBe("Confirmed");
    expect(normalizeReportingDataState("Accrued")).toBe("Estimated");
    expect(normalizeReportingDataState("Manual")).toBe("Overridden");
    expect(normalizeReportingDataState("Break")).toBe("Exception");
  });

  it("resolves unknown data states to Provisional rather than Confirmed", () => {
    expect(normalizeReportingDataState("mystery")).toBe("Provisional");
    expect(describeReportingDataState("mystery").severity).toBe("review");
  });

  it("treats a waived control as operator attention, not a pass", () => {
    expect(normalizeReportingControlState("Waiver")).toBe("Waived");
    expect(describeReportingControlState("Waived").severity).toBe("action");
    expect(describeReportingControlState("WithinTolerance").severity).toBe("ready");
    expect(describeReportingControlState("Failed").severity).toBe("blocked");
  });

  it("resolves unknown control states to NotTested rather than Passed", () => {
    expect(normalizeReportingControlState("probably fine")).toBe("NotTested");
  });
});

describe("freeze and block vocabularies", () => {
  it("distinguishes the four freeze states", () => {
    expect(normalizeReportingFreezeState("soft")).toBe("SoftFrozen");
    expect(normalizeReportingFreezeState("Locked")).toBe("HardFrozen");
    expect(normalizeReportingFreezeState("Released")).toBe("Published");
    expect(normalizeReportingFreezeState(null)).toBe("Open");
    expect(describeReportingFreezeState("SoftFrozen").severity).toBe("review");
  });

  it("admits silent upstream change only while the report is open", () => {
    expect(admitsUpstreamChange("Open")).toBe(true);
    expect(admitsUpstreamChange("SoftFrozen")).toBe(false);
    expect(admitsUpstreamChange("HardFrozen")).toBe(false);
    expect(admitsUpstreamChange("Published")).toBe(false);
  });

  it("resolves unknown block states to Stale rather than Live", () => {
    expect(normalizeReportBlockState("Pinned")).toBe("Snapshot");
    expect(normalizeReportBlockState("Modified")).toBe("Changed");
    expect(normalizeReportBlockState("nonsense")).toBe("Stale");
    expect(describeReportBlockState("Live").severity).toBe("ready");
    expect(describeReportBlockState("Missing").severity).toBe("blocked");
  });
});

describe("report classes", () => {
  it("maps template families onto report classes", () => {
    expect(normalizeReportClass("Performance")).toBe("Portfolio");
    expect(normalizeReportClass("Ledger")).toBe("Accounting");
    expect(normalizeReportClass("Statutory")).toBe("Regulatory");
    expect(normalizeReportClass("Committee")).toBe("Management");
    expect(normalizeReportClass("anything else")).toBe("Analytical");
  });

  it("varies governance requirements by class", () => {
    expect(describeReportClass("Regulatory")).toMatchObject({
      requiresReconciliation: true,
      requiresApproval: true,
      structureLocked: true
    });
    expect(describeReportClass("Analytical")).toMatchObject({
      requiresReconciliation: false,
      requiresApproval: false,
      structureLocked: false
    });
    expect(describeReportClass("Accounting").requiresReconciliation).toBe(true);
  });
});

describe("cross-vocabulary severity", () => {
  it("resolves severity for states drawn from any vocabulary", () => {
    expect(reportingStateSeverity("ChangesRequested")).toBe("action");
    expect(reportingStateSeverity("Estimated")).toBe("action");
    expect(reportingStateSeverity("WithinTolerance")).toBe("ready");
    expect(reportingStateSeverity("SoftFrozen")).toBe("review");
  });

  it("falls back to the shared design-system normalizer for foreign strings", () => {
    expect(reportingStateSeverity("healthy")).toBe("ready");
    expect(reportingStateSeverity("unknown")).toBe("info");
  });
});
