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
      { key: "sync", status: "err", label: "Sync", value: "attention required" },
      { key: "workspace", label: "Workspace", value: "Reporting", push: true }
    ]);
  });

  it("keeps the footer focused on current sync and workspace context", () => {
    expect(buildWorkstationStatusItems({
      workspaceLabel: "Portfolio",
      refreshing: false,
      hasError: false
    })).toEqual([
      { key: "sync", status: "ok", label: "Sync", value: "up to date" },
      { key: "workspace", label: "Workspace", value: "Portfolio", push: true }
    ]);
  });

  it("repeats a provider-trust warning so a green sync is never the whole answer", () => {
    const build = (providerTrust: Parameters<typeof buildWorkstationStatusItems>[0]["providerTrust"]) =>
      buildWorkstationStatusItems({ workspaceLabel: "Accounting", refreshing: false, hasError: false, providerTrust });

    expect(build({ value: "1 warning", tone: "review" })).toEqual([
      { key: "sync", status: "ok", label: "Sync", value: "up to date" },
      { key: "providers", status: "warn", label: "Providers", value: "1 warning" },
      { key: "workspace", label: "Workspace", value: "Accounting", push: true }
    ]);
    expect(build({ value: "2 blocked", tone: "blocked" })[1])
      .toEqual({ key: "providers", status: "err", label: "Providers", value: "2 blocked" });
    // Healthy and still-loading postures leave the footer on sync and workspace alone.
    expect(build({ value: "4/4 healthy", tone: "ready" }).map((item) => item.key)).toEqual(["sync", "workspace"]);
    expect(build({ value: "Pending", tone: "pending" }).map((item) => item.key)).toEqual(["sync", "workspace"]);
    expect(build(null).map((item) => item.key)).toEqual(["sync", "workspace"]);
  });
  it("never says up to date while the page's own freshness needs review", () => {
    const build = (pageFreshness: Parameters<typeof buildWorkstationStatusItems>[0]["pageFreshness"]) =>
      buildWorkstationStatusItems({ workspaceLabel: "Portfolio", refreshing: false, hasError: false, pageFreshness });

    // The Portfolio posture strip says "Freshness: Stale · 187d ago"; the footer repeats it.
    expect(build({ tone: "review", value: "Stale · 187d ago" })).toEqual([
      { key: "sync", status: "ok", label: "Sync", value: "connected" },
      { key: "freshness", status: "warn", label: "Freshness", value: "Stale · 187d ago" },
      { key: "workspace", label: "Workspace", value: "Portfolio", push: true }
    ]);
    expect(build({ tone: "blocked", value: null })[1])
      .toEqual({ key: "freshness", status: "err", label: "Freshness", value: "blocked" });
    for (const items of [build({ tone: "review", value: "Stale" }), build({ tone: "blocked", value: "Unavailable" })]) {
      expect(items.map((item) => item.value)).not.toContain("up to date");
    }

    // A fresh page, a page with no posture strip, and an unreported tone leave Sync as it was.
    expect(build({ tone: "ready", value: "2s ago" })[0].value).toBe("up to date");
    expect(build({ tone: "unknown", value: "Not available" })[0].value).toBe("up to date");
    expect(build(null).map((item) => item.key)).toEqual(["sync", "workspace"]);
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
