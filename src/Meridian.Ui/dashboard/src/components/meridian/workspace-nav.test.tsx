import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { WorkspaceNav } from "@/components/meridian/workspace-nav";
import { renderWithRouter } from "@/test/render";

describe("WorkspaceNav", () => {
  it("renders the seven root workspaces through the design-system rail contract", () => {
    renderWithRouter(<WorkspaceNav />, { initialEntries: ["/trading"] });

    const rail = screen.getByLabelText("Meridian navigation");
    expect(rail).toHaveAttribute("data-design-system-component", "NavRail");
    expect(rail).toHaveClass("mds-nav-rail");
    expect(rail).toHaveClass("op-rail");
    expect(document.querySelectorAll(".operator-nav-item")).toHaveLength(7);
    [
      "Trading",
      "Portfolio",
      "Accounting",
      "Reporting",
      "Strategy",
      "Data",
      "Settings"
    ].forEach((label) => expect(screen.getByText(label)).toBeInTheDocument());
    expect(screen.getByText("Available · Current")).toBeInTheDocument();
  });

  it("announces the current workspace route", () => {
    renderWithRouter(<WorkspaceNav />, { initialEntries: ["/accounting/reconciliation"] });

    expect(screen.getByRole("navigation", { name: "Workspaces" })).toBeInTheDocument();
    expect(screen.queryByLabelText("Current workspace: Accounting, Available product maturity")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Accounting workspace, active section, Available product maturity")).not.toHaveAttribute("aria-current");
    expect(screen.getByLabelText("Casework, current page")).toHaveAttribute("aria-current", "page");
    expect(screen.getByText("Available · Current")).toBeInTheDocument();
    expect(screen.queryByText("Available product maturity")).not.toBeInTheDocument();
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
    expect(screen.getByLabelText("Open Trading workspace, Available product maturity, preserving Subject: AAPL / Provider: alpaca")).toHaveAttribute(
      "href",
      "/trading?symbol=AAPL&provider=alpaca"
    );
    expect(screen.getByLabelText("Market data, current page, preserving Subject: AAPL / Provider: alpaca")).toHaveAttribute(
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
    expect(screen.getByLabelText("Open Ingestion operations")).toHaveAttribute("href", "/data/operations");
    expect(screen.getByLabelText("Open Storage assurance")).toHaveAttribute("href", "/data/assurance");
  });

  it("renders exactly the seven root workspaces and shows maturity pills only in detailed mode", () => {
    const detailed = renderWithRouter(<WorkspaceNav />, { initialEntries: ["/trading"] });

    const detailedItems = detailed.container.querySelectorAll(".operator-nav-item");
    expect(detailedItems).toHaveLength(7);
    const labels = Array.from(detailedItems).map((item) => item.querySelector(".font-medium")?.textContent);
    expect(new Set(labels)).toEqual(
      new Set(["Trading", "Portfolio", "Accounting", "Reporting", "Strategy", "Data", "Settings"])
    );
    // Detailed rail surfaces the per-workspace product-maturity pill.
    expect(detailed.container.querySelectorAll(".operator-nav-status").length).toBeGreaterThan(0);

    detailed.unmount();

    const compact = renderWithRouter(<WorkspaceNav density="compact" />, { initialEntries: ["/trading"] });
    expect(compact.container.querySelectorAll(".operator-nav-item")).toHaveLength(7);
    // Compact rail keeps the same seven workspaces but drops the maturity pills.
    expect(compact.container.querySelectorAll(".operator-nav-status")).toHaveLength(0);
  });

  it("expands inactive workspace pages from the rail toggle", async () => {
    const user = userEvent.setup();
    renderWithRouter(<WorkspaceNav density="compact" />, { initialEntries: ["/portfolio"] });

    const accountingSections = document.getElementById("workspace-nav-compact-accounting-sections");
    expect(accountingSections).not.toBeNull();
    expect(accountingSections).toHaveAttribute("aria-hidden", "true");

    await user.click(screen.getByRole("button", { name: "Expand Accounting pages" }));

    expect(accountingSections).toHaveAttribute("aria-hidden", "false");
    ["Close", "Records", "Reconciliation", "Review", "Administration"].forEach((label) => {
      expect(within(accountingSections!).getByRole("group", { name: label })).toBeInTheDocument();
    });
    expect(within(accountingSections!).getByLabelText("Open Ledger explorer")).toHaveAttribute("href", "/accounting/ledger");
    expect(within(accountingSections!).getByLabelText("Open Close calendar")).toHaveAttribute("href", "/accounting/close-calendar");
    expect(within(accountingSections!).getByLabelText("Open Capital accounts")).toHaveAttribute("href", "/accounting/capital-accounts");
    expect(within(accountingSections!).getByLabelText("Open Approvals")).toHaveAttribute("href", "/accounting/approvals");
    expect(screen.getByRole("button", { name: "Collapse Accounting pages" })).toHaveAttribute("aria-expanded", "true");
  });
});
