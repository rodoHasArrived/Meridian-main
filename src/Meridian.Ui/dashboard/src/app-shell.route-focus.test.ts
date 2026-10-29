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


describe("portfolio table route focus", () => {
  it.each(["/portfolio", "/portfolio/", "/PORTFOLIO", "/PORTFOLIO/"])("keeps table view updates local on %s", (pathname) => {
    const initial = focusState("?fundAccountId=fund-a", pathname);
    expect(focusState("?fundAccountId=fund-a&view=first", pathname).routeKey).toBe(initial.routeKey);
    expect(focusState("?fundAccountId=fund-a&view=second", pathname).routeKey).toBe(initial.routeKey);
    expect(focusState("?view=first", pathname).routeKey).toBe(focusState("", pathname).routeKey);
  });

  it("preserves operating scope and unrelated filters as navigation", () => {
    const initial = focusState("?fundAccountId=fund-a&asOf=2026-06-30&filter=due&view=first", "/portfolio");
    expect(initial.routeKey).toBe("/portfolio?fundAccountId=fund-a&asOf=2026-06-30&filter=due");
    expect(focusState("?fundAccountId=fund-b&asOf=2026-06-30&filter=due&view=second", "/portfolio").routeKey).not.toBe(initial.routeKey);
    expect(focusState("?fundAccountId=fund-a&asOf=2026-07-31&filter=due&view=second", "/portfolio").routeKey).not.toBe(initial.routeKey);
    expect(focusState("?fundAccountId=fund-a&asOf=2026-06-30&filter=all&view=second", "/portfolio").routeKey).not.toBe(initial.routeKey);
  });

  it("preserves explicit evidence hash navigation", () => {
    const initial = focusState("?view=first", "/portfolio", "#portfolio-position-detail");
    const updated = focusState("?view=second", "/portfolio", "#portfolio-position-detail");
    expect(updated.routeKey).toBe(initial.routeKey);
    expect(updated.targetElementId).toBe("portfolio-position-detail");
    expect(focusState("?view=second", "/portfolio", "#portfolio-runs").routeKey).not.toBe(initial.routeKey);
  });

  it.each(["/portfolio/attribution", "/portfolio/brokerage-sync", "/trading", "/reporting"])("preserves view token navigation on %s", (pathname) => {
    expect(focusState("?view=first", pathname).routeKey).not.toBe(focusState("?view=second", pathname).routeKey);
  });
});


describe("reconciliation queue route focus", () => {
  const paths = ["/accounting/reconciliation", "/accounting/exceptions", "/accounting/reconciliation/", "/ACCOUNTING/EXCEPTIONS/"];
  const localQuery = "reconciliationRun=run-a&reconciliationBreak=break-a&reconciliationSearch=cash&reconciliationStatus=Open&reconciliationSort=oldest";

  it.each(paths)("keeps row selection and local controls focused on %s", (pathname) => {
    const initial = focusState("", pathname);
    expect(focusState(`?${localQuery}`, pathname).routeKey).toBe(initial.routeKey);
    for (const key of ["reconciliationRun", "reconciliationBreak", "reconciliationSearch", "reconciliationStatus", "reconciliationSort"]) {
      expect(focusState(`?${key}=first`, pathname).routeKey).toBe(initial.routeKey);
      expect(focusState(`?${key}=second`, pathname).routeKey).toBe(initial.routeKey);
    }
  });

  it.each(paths)("preserves shared scope, unrelated parameters, and hash targets on %s", (pathname) => {
    const scope = "fundAccountId=fund-a&runId=ledger-a&provider=source-a&from=2026-06-01&to=2026-06-30&asOf=2026-06-30&symbol=MSFT&view=review";
    const initial = focusState(`?${scope}`, pathname, "#reconciliation-break-queue");
    const selected = focusState(`?${scope}&${localQuery}`, pathname, "#reconciliation-break-queue");
    expect(selected.routeKey).toBe(initial.routeKey);
    expect(selected.routeKey).toBe(`${pathname}?${scope}#reconciliation-break-queue`);
    expect(selected.targetElementId).toBe("reconciliation-break-queue");
    expect(focusState(`?${scope}&${localQuery}`, pathname, "#other-evidence").routeKey).not.toBe(initial.routeKey);
    for (const key of ["fundAccountId", "runId", "provider", "from", "to", "date", "asOf", "symbol", "view", "unrelated"]) {
      const changed = new URLSearchParams(`${scope}&${localQuery}`);
      changed.set(key, "changed");
      expect(focusState(`?${changed}`, pathname, "#reconciliation-break-queue").routeKey).not.toBe(initial.routeKey);
    }
  });

  it.each(["/accounting", "/accounting/ledger", "/accounting/reconciliation/match", "/reporting"])("does not suppress reconciliation query navigation on %s", (pathname) => {
    expect(focusState("?reconciliationBreak=first", pathname).routeKey)
      .not.toBe(focusState("?reconciliationBreak=second", pathname).routeKey);
  });
});


describe("task workbench route focus", () => {
  it.each([
    ["/strategy", "strategyRun"], ["/strategy", "strategyTask"],
    ["/data", "dataJob"], ["/data", "dataTask"], ["/settings", "settingsTask"]
  ])("keeps %s selection/search local while preserving scope navigation", (path, key) => {
    const initial = focusState("?companyId=a&asOf=2026-10-29&filter=all", path, "#evidence");
    const selected = focusState(`?companyId=a&asOf=2026-10-29&filter=all&${key}=two&${key}Search=retained`, path, "#evidence");
    expect(selected.routeKey).toBe(initial.routeKey);
    expect(selected.targetElementId).toBe("evidence");
    expect(focusState(`?companyId=b&asOf=2026-10-29&filter=all&${key}=two`, path, "#evidence").routeKey).not.toBe(initial.routeKey);
    expect(focusState(`?companyId=a&asOf=2026-10-30&filter=all&${key}=two`, path, "#evidence").routeKey).not.toBe(initial.routeKey);
  });
  it("does not suppress drill-through navigation on dedicated routes", () => {
    expect(focusState("?runId=one", "/strategy/promotions").routeKey).not.toBe(focusState("?runId=two", "/strategy/promotions").routeKey);
  });
});
