import { describe, expect, it } from "vitest";
import { buildRouteFocusState } from "@/app-shell.route-focus";
import { workspaceForPath } from "@/lib/workspace";

function focusState(search: string, pathname = "/reporting", hash = "") {
  return buildRouteFocusState(pathname, search, hash, workspaceForPath(pathname));
}

describe("reporting queue route focus", () => {
  it("keeps focus in the workbench when only its selected work item changes", () => {
    const initial = focusState("");
    const first = focusState("?reportingWork=approval-needed%3Aboard");
    const second = focusState("?reportingWork=evidence-gap%3Acapital");
    expect(first.routeKey).toBe(initial.routeKey);
    expect(second.routeKey).toBe(initial.routeKey);
    expect(second.fallbackElementId).toBe("workbench-content");
    expect(second.announcement).toBe("Reporting Workstation loaded.");
  });

  it("preserves financial scope and unrelated query values in the route focus key", () => {
    const initial = focusState("?fundAccountId=fund-a&asOf=2026-06-30&filter=due");
    const selected = focusState("?fundAccountId=fund-a&asOf=2026-06-30&filter=due&reportingWork=board");
    expect(selected.routeKey).toBe(initial.routeKey);
    expect(selected.routeKey).toBe("/reporting?fundAccountId=fund-a&asOf=2026-06-30&filter=due");
    expect(focusState("?fundAccountId=fund-b&asOf=2026-06-30&filter=due&reportingWork=board").routeKey).not.toBe(selected.routeKey);
    expect(focusState("?fundAccountId=fund-a&asOf=2026-07-31&filter=due&reportingWork=board").routeKey).not.toBe(selected.routeKey);
    expect(focusState("?fundAccountId=fund-a&asOf=2026-06-30&filter=all&reportingWork=board").routeKey).not.toBe(selected.routeKey);
  });

  it.each(["from", "to", "date"])("still focuses a new %s financial date scope", (key) => {
    expect(focusState(`?${key}=2026-07-31&reportingWork=board`).routeKey)
      .not.toBe(focusState(`?${key}=2026-06-30&reportingWork=board`).routeKey);
  });

  it("keeps requested hash targets and hash changes actionable", () => {
    const initial = focusState("?fundAccountId=fund-a", "/reporting", "#reporting-work-detail");
    const selected = focusState("?fundAccountId=fund-a&reportingWork=board", "/reporting", "#reporting-work-detail");
    expect(selected.routeKey).toBe(initial.routeKey);
    expect(selected.targetElementId).toBe("reporting-work-detail");
    expect(focusState("?fundAccountId=fund-a&reportingWork=board", "/reporting", "#reporting-production").routeKey).not.toBe(selected.routeKey);
  });

  it("canonicalizes existing encoding so adding selection does not look like navigation", () => {
    expect(focusState("?entity=Fund%20A").routeKey)
      .toBe(focusState("?entity=Fund+A&reportingWork=board").routeKey);
  });

  it.each(["/reporting/", "/REPORTING", "/REPORTING/"])("keeps selection local on the accepted %s landing path", (pathname) => {
    expect(focusState("?fundAccountId=fund-a&reportingWork=board", pathname).routeKey)
      .toBe(focusState("?fundAccountId=fund-a", pathname).routeKey);
    expect(focusState("?fundAccountId=fund-a&reportingWork=capital", pathname).routeKey)
      .toBe(focusState("?fundAccountId=fund-a&reportingWork=board", pathname).routeKey);
    expect(focusState("?fundAccountId=fund-b&reportingWork=board", pathname).routeKey)
      .not.toBe(focusState("?fundAccountId=fund-a&reportingWork=board", pathname).routeKey);
  });

  it.each(["/reporting/run-status", "/reporting/run-status/", "/REPORTING/run-status", "/portfolio"])("preserves query navigation semantics on %s", (pathname) => {
    expect(focusState("?reportingWork=board", pathname).routeKey).toBe(`${pathname}?reportingWork=board`);
    expect(focusState("?reportingWork=capital", pathname).routeKey).not.toBe(focusState("?reportingWork=board", pathname).routeKey);
  });
});
