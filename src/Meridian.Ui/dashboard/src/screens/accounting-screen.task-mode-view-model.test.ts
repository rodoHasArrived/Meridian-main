import { describe, expect, it } from "vitest";
import {
  buildAccountingSectionVisibility,
  buildAccountingTaskMode
} from "@/screens/accounting-screen.task-mode-view-model";

function visibilityFor(pathname: string, hash = "") {
  return buildAccountingSectionVisibility(buildAccountingTaskMode(pathname), hash);
}

describe("buildAccountingSectionVisibility", () => {
  it.each([
    ["/accounting/exceptions", "exceptions", "Exceptions"],
    ["/accounting/security-master", "security-master", "Security Master"],
    ["/accounting/approvals", "approvals", "Approvals"],
    ["/accounting/configure", "configure", "Configure"]
  ] as const)("gives %s an exact route-owned task identity", (pathname, id, label) => {
    const taskMode = buildAccountingTaskMode(pathname);
    expect(taskMode.id).toBe(id);
    expect(taskMode.label).toBe(label);
    expect(taskMode.routeLabel).toBe(label);
  });

  it("keeps the bare accounting route on the close-cockpit landing", () => {
    const visibility = visibilityFor("/accounting");
    expect(visibility.showCloseCockpitLanding).toBe(true);
    expect(visibility.showWorkflowDetails).toBe(false);
    expect(visibility.showPosture).toBe(false);
    expect(visibility.showReporting).toBe(false);
    expect(visibility.showReconciliation).toBe(false);
  });

  it.each([
    ["/accounting/reconciliation", "showReconciliation"],
    ["/accounting/reconciliation/external-gl", "showExternalGl"],
    ["/accounting/journal-entries", "showJournalEntries"],
    ["/accounting/capital-accounts", "showCapitalAccounts"],
    ["/accounting/exceptions", "showExceptionWorkbench"],
    ["/accounting/security-master", "showSecurityMaster"],
    ["/accounting/approvals", "showApprovals"],
    ["/accounting/configure", "showConfiguration"],
    ["/accounting/reporting", "showReporting"]
  ] as const)("scopes %s to its own section flag", (pathname, flag) => {
    const visibility = visibilityFor(pathname);
    expect(visibility[flag]).toBe(true);
    expect(visibility.showCloseCockpitLanding).toBe(false);

    const primaryFlags = [
      "showExternalGl",
      "showConfiguration",
      "showJournalEntries",
      "showCapitalAccounts",
      "showApprovals",
      "showExceptionWorkbench",
      "showReconciliation",
      "showLedgerExplorer",
      "showSecurityMaster",
      "showReporting"
    ] as const;
    expect(primaryFlags.filter((candidate) => visibility[candidate])).toEqual([flag]);
    expect(visibility.showWorkflowDetails).toBe(false);
    expect(visibility.showMultiAssetCoverage).toBe(false);
    expect(visibility.showPosture).toBe(false);
  });

  it("keeps the reporting band off every non-reporting workstream", () => {
    for (const pathname of [
      "/accounting",
      "/accounting/reconciliation",
      "/accounting/reconciliation/external-gl",
      "/accounting/configure",
      "/accounting/journal-entries",
      "/accounting/capital-accounts",
      "/accounting/exceptions",
      "/accounting/security-master",
      "/accounting/approvals"
    ]) {
      expect(visibilityFor(pathname).showReporting).toBe(false);
    }
  });

  it("forces the reporting band visible for its hash deep link", () => {
    const visibility = visibilityFor("/accounting", "#accounting-reporting");
    expect(visibility.showReporting).toBe(true);
    expect(visibility.showCloseCockpitLanding).toBe(false);
    expect(visibility.showWorkflowDetails).toBe(true);
  });

  it("keeps existing hash overrides working", () => {
    const visibility = visibilityFor("/accounting", "#accounting-exceptions");
    expect(visibility.showReconciliation).toBe(true);
    expect(visibility.showCloseCockpitLanding).toBe(false);
  });
});
