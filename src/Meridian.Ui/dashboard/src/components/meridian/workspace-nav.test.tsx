import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { useNavigate } from "react-router-dom";
import { WorkspaceNav } from "@/components/meridian/workspace-nav";
import { renderWithRouter } from "@/test/render";

/** Workspace rows only: the pinned "Today" entry shares the item styling but is not a workspace. */
const WORKSPACE_ITEM = ".operator-nav-item:not(.operator-nav-today)";

describe("WorkspaceNav", () => {
  it("keeps the Daily Control Tower outside the seven-workspace active state", () => {
    renderWithRouter(<WorkspaceNav density="compact" />, { initialEntries: ["/"] });

    // The Control Tower is the pinned "Today" entry: a home page, not an eighth workspace.
    expect(document.querySelectorAll(WORKSPACE_ITEM)).toHaveLength(7);
    expect(document.querySelectorAll(`${WORKSPACE_ITEM}.active`)).toHaveLength(0);
    const today = screen.getByRole("link", { name: /^Today, Daily Control Tower, current route/ });
    expect(today).toHaveAttribute("aria-current", "page");
    expect(today).toHaveAttribute("href", "/");
    expect(screen.queryByText("Available · Current")).not.toBeInTheDocument();
    expect(screen.queryByRole("group", { name: "Trading pages" })).not.toBeInTheDocument();
  });

  it("renders the seven root workspaces through the design-system rail contract", () => {
    renderWithRouter(<WorkspaceNav />, { initialEntries: ["/trading"] });

    const rail = screen.getByLabelText("Meridian navigation");
    expect(rail).toHaveAttribute("data-design-system-component", "NavRail");
    expect(rail).toHaveClass("mds-nav-rail");
    expect(rail).toHaveClass("op-rail");
    expect(document.querySelectorAll(WORKSPACE_ITEM)).toHaveLength(7);
    expect(document.querySelectorAll(".operator-nav-today")).toHaveLength(1);
    [
      "Trading",
      "Portfolio",
      "Accounting",
      "Reporting",
      "Strategy",
      "Data",
      "Settings"
    ].forEach((label) => expect(screen.getByText(label)).toBeInTheDocument());
    // Product maturity is a tooltip now, not a rail pill.
    expect(screen.queryByText("Available · Current")).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: /^Trading workspace, current route/ }))
      .toHaveAttribute("title", "Trading · Available product maturity");
  });

  it("announces the current workspace route", () => {
    renderWithRouter(<WorkspaceNav />, { initialEntries: ["/accounting/reconciliation"] });

    expect(screen.getByRole("navigation", { name: "Workspaces" })).toBeInTheDocument();
    expect(screen.queryByLabelText("Current workspace: Accounting, Available product maturity")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Accounting workspace, active section, Available product maturity")).not.toHaveAttribute("aria-current");
    expect(screen.getByLabelText("Casework, current page")).toHaveAttribute("aria-current", "page");
    expect(screen.queryByText("Available · Current")).not.toBeInTheDocument();
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

  it("renders exactly the seven root workspaces with product maturity in tooltips, not pills", () => {
    const detailed = renderWithRouter(<WorkspaceNav />, { initialEntries: ["/trading"] });

    const detailedItems = detailed.container.querySelectorAll(WORKSPACE_ITEM);
    expect(detailedItems).toHaveLength(7);
    const labels = Array.from(detailedItems).map((item) => item.querySelector(".font-medium")?.textContent);
    expect(new Set(labels)).toEqual(
      new Set(["Trading", "Portfolio", "Accounting", "Reporting", "Strategy", "Data", "Settings"])
    );
    // Neither rail shows maturity pills: maturity is in each link's tooltip and accessible name.
    expect(detailed.container.querySelectorAll(".operator-nav-status")).toHaveLength(0);

    detailed.unmount();

    const compact = renderWithRouter(<WorkspaceNav density="compact" />, { initialEntries: ["/trading"] });
    expect(compact.container.querySelectorAll(WORKSPACE_ITEM)).toHaveLength(7);
    // Compact rail keeps the same seven workspaces but drops the maturity pills.
    expect(compact.container.querySelectorAll(".operator-nav-status")).toHaveLength(0);
  });

  it("shows Control Tower attention counts on Today and the owning workspaces", () => {
    renderWithRouter(
      <WorkspaceNav density="compact" attention={{ total: 3, byWorkspace: { accounting: 2, data: 1 } }} />,
      { initialEntries: ["/trading"] }
    );

    const badgeFor = (link: HTMLElement) => link.parentElement?.querySelector(".operator-nav-count");
    const today = screen.getByRole("link", { name: /^Open Today, the Daily Control Tower, 3 items need attention/ });
    const accounting = screen.getByRole("link", { name: /^Open Accounting workspace.*, 2 items need attention/ });
    const data = screen.getByRole("link", { name: /^Open Data workspace.*, 1 item needs attention/ });
    expect(badgeFor(today)).toHaveTextContent("3");
    expect(badgeFor(accounting)).toHaveTextContent("2");
    expect(badgeFor(data)).toHaveTextContent("1");
    // Label in Name (WCAG 2.5.3): the badge is a sibling, so each link's visible text is exactly
    // its label and stays contained in the accessible name.
    expect(today).toHaveTextContent(/^Today$/);
    expect(accounting).toHaveTextContent(/^Accounting$/);
    expect(badgeFor(accounting)).toHaveAttribute("aria-hidden", "true");
    // Workspaces with nothing queued carry no badge and no attention suffix.
    const portfolio = screen.getByRole("link", { name: /^Open Portfolio workspace/ });
    expect(badgeFor(portfolio)).toBeNull();
    expect(portfolio).not.toHaveAccessibleName(/attention/);
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

  it("reveals a new active page in the same workspace while preserving other expansion choices", async () => {
    const user = userEvent.setup();
    function RouteControls() {
      const navigate = useNavigate();
      return <button onClick={() => navigate("/accounting/approvals?fundAccountId=account-1")}>Open approval destination</button>;
    }
    renderWithRouter(<><WorkspaceNav density="compact" /><RouteControls /></>, {
      initialEntries: ["/accounting/reconciliation?fundAccountId=account-1"]
    });

    await user.click(screen.getByRole("button", { name: "Expand Data pages" }));
    await user.click(screen.getByRole("button", { name: "Collapse Accounting pages" }));
    expect(screen.queryByLabelText("Casework, current page, preserving Account: account-1")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Expand Accounting pages" })).toHaveAttribute("aria-expanded", "false");

    await user.click(screen.getByRole("button", { name: "Open approval destination" }));

    expect(screen.getByRole("button", { name: "Collapse Accounting pages" })).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("button", { name: "Collapse Data pages" })).toHaveAttribute("aria-expanded", "true");
    const current = screen.getByRole("link", { name: /Approvals, current page/ });
    expect(current).toHaveAttribute("aria-current", "page");
    expect(current).toHaveAttribute("href", "/accounting/approvals?fundAccountId=account-1");

    // A deliberate collapse remains closed until the destination changes again.
    await user.click(screen.getByRole("button", { name: "Collapse Accounting pages" }));
    expect(screen.getByRole("button", { name: "Expand Accounting pages" })).toHaveAttribute("aria-expanded", "false");
  });

  it("makes complete destination names available for pointer and keyboard users", async () => {
    const user = userEvent.setup();
    renderWithRouter(<WorkspaceNav density="compact" />, { initialEntries: ["/data/providers"] });
    const destination = screen.getByRole("link", { name: "Open Ingestion operations" });
    expect(destination).toHaveAttribute("title", "Ingestion operations");
    expect(destination.querySelector(".operator-nav-label")).toHaveTextContent("Ingestion operations");
    destination.focus();
    expect(destination).toHaveFocus();
    await user.keyboard("{Enter}");
    expect(screen.getByRole("link", { name: "Ingestion operations, current page" })).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("link", { name: /Open Trading workspace/ })).toHaveAttribute("title", "Trading · Available product maturity");
  });

});
