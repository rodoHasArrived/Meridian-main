import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { WorkspaceNav } from "@/components/meridian/workspace-nav";
import { renderWithRouter } from "@/test/render";

describe("WorkspaceNav", () => {
  it("announces the current workspace route", () => {
    renderWithRouter(<WorkspaceNav />, { initialEntries: ["/accounting/reconciliation"] });

    expect(screen.getByRole("navigation", { name: "Workspaces" })).toBeInTheDocument();
    expect(screen.queryByLabelText("Current workspace: Accounting, Review posture")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Accounting workspace, active section, Review")).not.toHaveAttribute("aria-current");
    expect(screen.getByLabelText("Reconciliation, current page")).toHaveAttribute("aria-current", "page");
    expect(screen.getByText("Review · Current")).toBeInTheDocument();
    expect(screen.queryByText("Review posture")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Canonical route /accounting")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Open command palette with Control K")).not.toBeInTheDocument();
  });

  it("shows preserved operating context and carries it into navigation links", () => {
    renderWithRouter(
      <WorkspaceNav operatingContextScope={{ symbol: "AAPL", provider: "alpaca" }} />,
      { initialEntries: ["/data/quotes"] }
    );

    expect(screen.getByLabelText("Navigation preserves operating scope: Subject: AAPL / Provider: alpaca")).toHaveTextContent(
      "Subject: AAPL / Provider: alpaca"
    );
    expect(screen.getByLabelText("Open Trading workspace, Review, preserving Subject: AAPL / Provider: alpaca")).toHaveAttribute(
      "href",
      "/trading?symbol=AAPL&provider=alpaca"
    );
    expect(screen.getByLabelText("Live quotes, current page, preserving Subject: AAPL / Provider: alpaca")).toHaveAttribute(
      "href",
      "/data/quotes?symbol=AAPL&provider=alpaca"
    );
  });

  it("auto-expands the active workspace pages in compact rail mode", () => {
    renderWithRouter(<WorkspaceNav density="compact" />, { initialEntries: ["/data/providers"] });

    expect(screen.getByRole("navigation", { name: "Workspaces" })).toBeInTheDocument();
    const dataSections = screen.getByRole("group", { name: "Data pages" });

    expect(dataSections).toHaveAttribute("aria-hidden", "false");
    expect(screen.getByLabelText("Providers, current page")).toHaveAttribute("aria-current", "page");
    expect(screen.getByLabelText("Open Backfill queues")).toHaveAttribute("href", "/data/backfills");
  });

  it("expands inactive workspace pages from the rail toggle", async () => {
    const user = userEvent.setup();
    renderWithRouter(<WorkspaceNav density="compact" />, { initialEntries: ["/portfolio"] });

    const accountingSections = document.getElementById("workspace-nav-compact-accounting-sections");
    expect(accountingSections).not.toBeNull();
    expect(accountingSections).toHaveAttribute("aria-hidden", "true");

    await user.click(screen.getByRole("button", { name: "Expand Accounting pages" }));

    expect(accountingSections).toHaveAttribute("aria-hidden", "false");
    expect(within(accountingSections!).getByLabelText("Open Ledger")).toHaveAttribute("href", "/accounting/ledger");
    expect(within(accountingSections!).getByLabelText("Open Approvals")).toHaveAttribute("href", "/accounting/approvals");
    expect(screen.getByRole("button", { name: "Collapse Accounting pages" })).toHaveAttribute("aria-expanded", "true");
  });
});
