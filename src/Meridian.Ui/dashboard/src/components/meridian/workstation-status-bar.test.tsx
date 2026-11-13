import { render, screen } from "@testing-library/react";
import { WorkstationStatusBar, buildWorkstationStatusItems } from "./workstation-status-bar";

describe("WorkstationStatusBar", () => {
  it("renders status items with pushed workspace layout marker", () => {
    render(
      <WorkstationStatusBar
        items={[
          { key: "session", label: "Session", value: "paper", status: "ok" },
          { key: "data", label: "Data", value: "demo fixtures", status: "warn" },
          { key: "sync", label: "Sync", value: "failed", status: "err" },
          { key: "workspace", label: "Workspace", value: "Accounting", push: true }
        ]}
      />
    );

    expect(screen.getByRole("contentinfo", { name: "Workstation status" })).toBeInTheDocument();
    expect(screen.getByText("paper")).toBeInTheDocument();
    expect(screen.getByText("demo fixtures")).toBeInTheDocument();
    expect(screen.getByText("failed")).toBeInTheDocument();
    expect(screen.getByText("Accounting").closest(".workstation-statusbar-item"))
      .toHaveClass("workstation-statusbar-item-push");
    expect(document.querySelector(".workstation-statusbar-dot-ok")).toBeInTheDocument();
    expect(document.querySelector(".workstation-statusbar-dot-warn")).toBeInTheDocument();
    expect(document.querySelector(".workstation-statusbar-dot-err")).toBeInTheDocument();
  });

  it("builds ok, warn, and error status text from shell state", () => {
    const items = buildWorkstationStatusItems({
      workspaceLabel: "Reporting",
      refreshing: true,
      hasError: true
    });

    expect(items).toEqual([
      { key: "sync", status: "err", label: "Sync", value: "attention required" }
    ]);
  });

  it("keeps the normal footer focused on sync without repeating route or provider inventory", () => {
    expect(buildWorkstationStatusItems({
      workspaceLabel: "Portfolio",
      refreshing: false,
      hasError: false
    })).toEqual([
      { key: "sync", status: "ok", label: "Sync", value: "up to date" }
    ]);
  });

  it("repeats the masthead attention state so a green sync is never the whole answer", () => {
    const build = (attention: Parameters<typeof buildWorkstationStatusItems>[0]["attention"]) =>
      buildWorkstationStatusItems({ workspaceLabel: "Accounting", refreshing: false, hasError: false, attention });

    expect(build({ tone: "review", title: "Providers: 1 warning", moreCount: 0 })).toEqual([
      { key: "sync", status: "ok", label: "Sync", value: "up to date" },
      { key: "attention", status: "warn", label: "Attention", value: "Providers: 1 warning" }
    ]);
    expect(build({ tone: "blocked", title: "Cash variance needs review", moreCount: 2 })[1])
      .toEqual({ key: "attention", status: "err", label: "Attention", value: "Cash variance needs review +2 more issues" });
    // Clear and still-loading states leave the footer on sync alone.
    expect(build({ tone: "ready", title: "No open issues", moreCount: 0 }).map((item) => item.key)).toEqual(["sync"]);
    expect(build({ tone: "pending", title: "Workspace data loading", moreCount: 0 }).map((item) => item.key)).toEqual(["sync"]);
    expect(build(null).map((item) => item.key)).toEqual(["sync"]);
  });
  it("never says up to date while the page's own freshness needs review", () => {
    const build = (pageFreshness: Parameters<typeof buildWorkstationStatusItems>[0]["pageFreshness"]) =>
      buildWorkstationStatusItems({ workspaceLabel: "Portfolio", refreshing: false, hasError: false, pageFreshness });

    // The Portfolio posture strip says "Freshness: Stale · 187d ago"; the footer repeats it.
    expect(build({ tone: "review", value: "Stale · 187d ago" })).toEqual([
      { key: "sync", status: "ok", label: "Sync", value: "connected" },
      { key: "freshness", status: "warn", label: "Freshness", value: "Stale · 187d ago" }
    ]);
    expect(build({ tone: "blocked", value: null })[1])
      .toEqual({ key: "freshness", status: "err", label: "Freshness", value: "blocked" });
    for (const items of [build({ tone: "review", value: "Stale" }), build({ tone: "blocked", value: "Unavailable" })]) {
      expect(items.map((item) => item.value)).not.toContain("up to date");
    }

    // A fresh page, a page with no posture strip, and an unreported tone leave Sync as it was.
    expect(build({ tone: "ready", value: "2s ago" })[0].value).toBe("up to date");
    expect(build({ tone: "unknown", value: "Not available" })[0].value).toBe("up to date");
    expect(build(null).map((item) => item.key)).toEqual(["sync"]);
  });

  it("retains the shared blocked issue and page freshness together without repeating navigation", () => {
    expect(buildWorkstationStatusItems({
      workspaceLabel: "Portfolio",
      refreshing: false,
      hasError: false,
      attention: { tone: "blocked", title: "Brokerage sync failed", moreCount: 1 },
      pageFreshness: { tone: "review", value: "Stale · 187d ago" }
    })).toEqual([
      { key: "sync", status: "ok", label: "Sync", value: "connected" },
      { key: "freshness", status: "warn", label: "Freshness", value: "Stale · 187d ago" },
      { key: "attention", status: "err", label: "Attention", value: "Brokerage sync failed +1 more issue" }
    ]);
  });

  it("keeps a long freshness sentence to one footer line", () => {
    const [, freshness] = buildWorkstationStatusItems({
      workspaceLabel: "Reporting",
      refreshing: false,
      hasError: false,
      pageFreshness: {
        tone: "review",
        value: "Loaded runs do not retain an as-of date; confirm the reporting period before release"
      }
    });

    expect(freshness.value.length).toBeLessThanOrEqual(56);
    expect(freshness.value.endsWith("…")).toBe(true);
  });
});
