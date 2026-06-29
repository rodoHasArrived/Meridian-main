import { describe, expect, it } from "vitest";
import {
  buildAccountingTaskModes,
  resolveAccountingWorkstream,
  resolveGovernanceWorkstream,
  type AccountingTaskModeCloseCommandCenter,
  type AccountingTaskModeInput,
  type AccountingTaskModeTone,
  type AccountingTaskModeWorkflowLaunch
} from "@/lib/accounting-task-modes";

describe("accounting task modes", () => {
  it("resolves active accounting task modes from canonical routes", () => {
    expect(activeModeId("/accounting")).toBe("close-cockpit");
    expect(activeModeId("/accounting?fundAccountId=fund-alpha")).toBe("close-cockpit");
    expect(activeModeId("/accounting/operations-continuity")).toBe("close-cockpit");
    expect(activeModeId("/accounting/reconciliation")).toBe("reconciliation-casework");
    expect(activeModeId("/accounting/exceptions")).toBe("reconciliation-casework");
    expect(activeModeId("/accounting/ledger")).toBe("ledger-explorer");
    expect(activeModeId("/accounting/ledger?journalEntryId=je-cash-1")).toBe("ledger-explorer");
    expect(activeModeId("/accounting/journal-entries")).toBe("journal-entry");
    expect(activeModeId("/accounting/capital-accounts")).toBe("capital-accounts");
    expect(activeModeId("/accounting/configure")).toBe("rules");
    expect(activeModeId("/accounting/entity-setup")).toBe("rules");
    expect(activeModeId("/accounting/security-master")).toBe("security-master");
    expect(activeModeId("/accounting/evidence")).toBe("evidence");
  });

  it("resolves accounting and governance workstreams from canonical routes", () => {
    expect(resolveAccountingWorkstream("/accounting/security-master")).toBe("security-master");
    expect(resolveAccountingWorkstream("/accounting/reconciliation")).toBe("reconciliation");
    expect(resolveAccountingWorkstream("/accounting/exceptions")).toBe("exceptions");
    expect(resolveAccountingWorkstream("/accounting/approvals")).toBe("approvals");
    expect(resolveAccountingWorkstream("/accounting/capital-accounts")).toBe("capital-accounts");
    expect(resolveAccountingWorkstream("/accounting")).toBe("close-cockpit");
    expect(resolveAccountingWorkstream("/accounting/operations-continuity")).toBe("close-cockpit");
    expect(resolveAccountingWorkstream("/accounting/ledger")).toBe("ledger");
    expect(resolveAccountingWorkstream("/accounting/evidence")).toBe("evidence");
    expect(resolveAccountingWorkstream("/reporting")).toBe("reporting");
    expect(resolveGovernanceWorkstream("/governance/security-master")).toBe("security-master");
    expect(resolveGovernanceWorkstream("/governance/reconciliation")).toBe("reconciliation");
    expect(resolveGovernanceWorkstream("/governance")).toBe("close-cockpit");
  });

  it("keeps close, reconciliation, and evidence guidance in the focused mode model", () => {
    const modes = buildAccountingTaskModes(baseInput({
      pathname: "/accounting/reconciliation",
      closeCommandCenter: closeCommandCenter("danger", "Close blocked", [
        { id: "source-files", value: "2", tone: "warning" },
        { id: "report-pack", value: "1", tone: "success" }
      ]),
      workflowLaunch: workflowLaunch([
        { id: "reconciliation", statusLabel: "Breaks open", metricValue: "7", tone: "warning" },
        { id: "exceptions", statusLabel: "Escalated", metricValue: "2", tone: "danger" },
        { id: "ledger", statusLabel: "Ledger ready", metricValue: "4", tone: "success" },
        { id: "journal-entries", statusLabel: "Drafts", metricValue: "3", tone: "warning" }
      ])
    }));

    expect(mode(modes, "close-cockpit")).toMatchObject({
      statusLabel: "Close blocked",
      countLabel: "2 blockers",
      badgeVariant: "danger",
      recommended: true
    });
    expect(mode(modes, "reconciliation-casework")).toMatchObject({
      statusLabel: "Breaks open",
      countLabel: "7 open breaks",
      badgeVariant: "danger",
      recommended: true,
      active: true
    });
    expect(mode(modes, "ledger-explorer")).toMatchObject({
      statusLabel: "Ledger ready",
      badgeVariant: "success",
      recommended: false
    });
    expect(mode(modes, "journal-entry")).toMatchObject({
      statusLabel: "Drafts",
      countLabel: "3 pending",
      badgeVariant: "warning",
      recommended: true
    });
    expect(mode(modes, "evidence")).toMatchObject({
      statusLabel: "Ready",
      countLabel: "2 source gaps",
      badgeVariant: "warning",
      recommended: true
    });
  });
});

function activeModeId(pathname: string) {
  return buildAccountingTaskModes(baseInput({ pathname })).find((item) => item.active)?.id;
}

function baseInput(overrides: Partial<AccountingTaskModeInput> = {}): AccountingTaskModeInput {
  return {
    pathname: "/accounting",
    data: {
      breakQueue: [{ id: "break-1" }],
      reconciliationQueue: [{ id: "run-1" }, { id: "run-2" }],
      reporting: { profileCount: 3 }
    },
    closeCommandCenter: closeCommandCenter("success", "Close ready", []),
    workflowLaunch: workflowLaunch([]),
    ...overrides
  };
}

function closeCommandCenter(
  statusTone: AccountingTaskModeTone,
  statusLabel: string,
  metricRows: AccountingTaskModeCloseCommandCenter["metricRows"]
): AccountingTaskModeCloseCommandCenter {
  return {
    blockerRows: [{ id: "blocker-1" }, { id: "blocker-2" }],
    statusTone,
    statusLabel,
    metricRows
  };
}

function workflowLaunch(steps: AccountingTaskModeWorkflowLaunch["steps"]): AccountingTaskModeWorkflowLaunch {
  return { steps };
}

function mode<T extends { id: string }>(modes: T[], id: T["id"]): T {
  const found = modes.find((item) => item.id === id);
  if (!found) {
    throw new Error(`Expected accounting mode ${id}`);
  }

  return found;
}
