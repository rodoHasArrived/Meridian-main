// @vitest-environment node
import { describe, expect, it } from "vitest";
import { closeHubTabFromSearch, closeWorkspaceRedirect, updateWorkstationSearch } from "./workstation-close-routing";

describe("Close and Today URL state", () => {
  it("retains all operating context and the evidence anchor on legacy redirects", () => {
    const query = "?workflowId=workflow-1&periodId=2026-05&fundAccountId=fund-1&closeTab=evidence";
    expect(closeWorkspaceRedirect("/accounting/operations-continuity", query, "#retained-evidence"))
      .toBe(`/accounting/close${query}#retained-evidence`);
    expect(closeWorkspaceRedirect("/trading/readiness", "?symbol=AAPL&runId=run-1", "#provider"))
      .toBe("/settings/diagnostics?symbol=AAPL&runId=run-1#provider");
    expect(closeWorkspaceRedirect("/accounting/reconciliation")).toBeNull();
  });

  it("changes a tab or source without discarding workflow, period, or unrelated state", () => {
    const next = updateWorkstationSearch("?workflowId=w1&periodId=2026-05&source=Data", "source", "Accounting");
    expect(new URLSearchParams(next).get("workflowId")).toBe("w1");
    expect(new URLSearchParams(next).get("periodId")).toBe("2026-05");
    expect(new URLSearchParams(next).get("source")).toBe("Accounting");
    expect(updateWorkstationSearch("?source=Data", "source", null)).toBe("");
    expect(closeHubTabFromSearch("?closeTab=approvals")).toBe("approvals");
    expect(closeHubTabFromSearch("?closeTab=invalid")).toBe("board");
  });
});
