import { describe, expect, it } from "vitest";
import {
  evidenceWorkbenchPath,
  legacyWorkspaceRedirect,
  normalizeWorkspacePath,
  WORKSPACES,
  workflowTargetPath,
  workspaceForKey,
  workspacePath
} from "@/lib/workspace";

describe("workspace metadata", () => {
  it("defines the canonical workstation navigation order", () => {
    expect(WORKSPACES.map((workspace) => workspace.label)).toEqual([
      "Trading",
      "Portfolio",
      "Accounting",
      "Reporting",
      "Strategy",
      "Data",
      "Settings"
    ]);
  });

  it("routes every canonical workspace by key", () => {
    expect(WORKSPACES.map((workspace) => workspacePath(workspace.key))).toEqual([
      "/trading",
      "/portfolio",
      "/accounting",
      "/reporting",
      "/strategy",
      "/data",
      "/settings"
    ]);
  });

  it("normalizes legacy workspace URLs to canonical roots", () => {
    expect(normalizeWorkspacePath("/")).toBe("trading");
    expect(normalizeWorkspacePath("/overview")).toBe("trading");
    expect(normalizeWorkspacePath("/research/run-library")).toBe("strategy");
    expect(normalizeWorkspacePath("/data-operations/backfills")).toBe("data");
    expect(normalizeWorkspacePath("/governance/security-master")).toBe("accounting");
  });

  it("preserves legacy suffix, query, and hash when building redirects", () => {
    expect(legacyWorkspaceRedirect("/data-operations/backfills", "?provider=alpaca", "#queue")).toBe(
      "/data/backfills?provider=alpaca#queue"
    );
    expect(legacyWorkspaceRedirect("/governance/reconciliation")).toBe("/accounting/reconciliation");
    expect(legacyWorkspaceRedirect("/trading")).toBeNull();
  });

  it("returns workspace summaries for canonical keys", () => {
    expect(workspaceForKey("reporting")).toMatchObject({
      label: "Reporting",
      status: "Review"
    });
  });

  it("builds encoded Evidence Workbench subject routes", () => {
    expect(evidenceWorkbenchPath("strategy-run", "run 1/A")).toBe(
      "/reporting/evidence?subjectKind=strategy-run&subjectId=run%201%2FA"
    );
    expect(evidenceWorkbenchPath("report pack", "current")).toBe(
      "/reporting/evidence?subjectKind=report%20pack&subjectId=current"
    );
  });

  it("maps backend workflow targets to browser workstation routes", () => {
    expect(workflowTargetPath("EvidenceWorkbench", "strategy")).toBe("/reporting/evidence");
    expect(workflowTargetPath("FundReportPack", "reporting")).toBe("/reporting/report-packs");
    expect(workflowTargetPath("ProviderTrust", "data")).toBe("/data/providers");
    expect(workflowTargetPath("UnknownTag", "data")).toBe("/data");
    expect(workflowTargetPath(null, null)).toBe("/trading");
  });
});
