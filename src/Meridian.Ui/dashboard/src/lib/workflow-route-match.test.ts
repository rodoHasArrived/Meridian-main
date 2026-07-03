import { describe, expect, it } from "vitest";
import { findWorkflowActionForRoute } from "@/lib/workflow-route-match";
import type { WorkflowAction, WorkflowLibrary } from "@/types";

function action(actionId: string, routePrefixes: string[], routeContains: string[] = []): WorkflowAction {
  return {
    actionId,
    label: actionId,
    detail: `Detail for ${actionId}`,
    targetPageTag: "Tag",
    tone: "ready",
    workItemKind: null,
    routePrefixes,
    routeContains,
    aliases: []
  };
}

const library: WorkflowLibrary = {
  generatedAt: "2026-07-01T00:00:00Z",
  workflows: [
    {
      workflowId: "reporting-delivery",
      title: "Reporting Delivery",
      summary: "Reporting workflows",
      workspaceId: "reporting",
      workspaceTitle: "Reporting",
      entryPageTag: "Reporting",
      tone: "ready",
      actions: [
        action("reporting.open", ["/reporting"]),
        action("reporting.exports", ["/reporting/report-builder", "/reporting/exports"])
      ],
      evidenceTags: [],
      marketPatternTags: []
    },
    {
      workflowId: "accounting-close",
      title: "Accounting Close",
      summary: "Close workflows",
      workspaceId: "accounting",
      workspaceTitle: "Accounting",
      entryPageTag: "Accounting",
      tone: "ready",
      actions: [action("accounting.recon", [], ["reconciliation"])],
      evidenceTags: [],
      marketPatternTags: []
    }
  ]
} as WorkflowLibrary;

describe("findWorkflowActionForRoute", () => {
  it("picks the longest matching route prefix", () => {
    const match = findWorkflowActionForRoute(library, "/reporting/report-builder");
    expect(match?.action.actionId).toBe("reporting.exports");
    expect(match?.workflow.workflowId).toBe("reporting-delivery");
  });

  it("falls back to shorter prefixes and contains matches", () => {
    expect(findWorkflowActionForRoute(library, "/reporting/evidence")?.action.actionId).toBe("reporting.open");
    expect(findWorkflowActionForRoute(library, "/accounting/reconciliation/match")?.action.actionId).toBe(
      "accounting.recon"
    );
  });

  it("does not match sibling routes that merely share a string prefix", () => {
    expect(findWorkflowActionForRoute(library, "/reporting-sibling")).toBeNull();
    expect(findWorkflowActionForRoute(library, "/reporting/report-builder-archive")?.action.actionId).not.toBe(
      "reporting.exports"
    );
  });

  it("returns null without a library or a match", () => {
    expect(findWorkflowActionForRoute(null, "/reporting")).toBeNull();
    expect(findWorkflowActionForRoute(library, "/settings")).toBeNull();
  });
});
