import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { vi } from "vitest";
import { AccountTree, type AccountNode } from "./AccountTree";

const nodes: AccountNode[] = [
  {
    code: "1000",
    name: "Assets",
    children: [
      { code: "1010", name: "Cash", balance: 4000 },
      { code: "1020", name: "Receivables", balance: 1500 }
    ]
  }
];

describe("AccountTree", () => {
  it("rolls up child balances onto a parent with no explicit balance", () => {
    render(<AccountTree nodes={nodes} currency="USD" defaultExpandedDepth={1} />);
    // 4000 + 1500 = 5500 rolled up to Assets
    expect(screen.getByText("$5,500.00")).toBeInTheDocument();
  });

  it("renders leaf balances", () => {
    render(<AccountTree nodes={nodes} currency="USD" defaultExpandedDepth={1} />);
    expect(screen.getByText("$4,000.00")).toBeInTheDocument();
    expect(screen.getByText("$1,500.00")).toBeInTheDocument();
  });

  it("exposes a tree role", () => {
    render(<AccountTree nodes={nodes} />);
    expect(screen.getByRole("tree", { name: "Chart of accounts" })).toBeInTheDocument();
  });

  it("keeps selectable rows as treeitems and supports keyboard selection", async () => {
    const onSelect = vi.fn();
    render(<AccountTree nodes={nodes} selectedCode="1000" onSelect={onSelect} />);
    const assets = screen.getByRole("treeitem", { name: /1000 Assets/ });
    expect(screen.queryByRole("button", { name: /1000 Assets/ })).toBeNull();
    assets.focus();
    await userEvent.keyboard("{Enter}");
    expect(onSelect).toHaveBeenCalledWith(nodes[0]);
  });

  it("expands and collapses branches through a disclosure button", async () => {
    render(<AccountTree nodes={nodes} defaultExpandedDepth={0} />);
    expect(screen.queryByText("Cash")).toBeNull();
    await userEvent.click(screen.getByRole("button", { name: "Expand Assets" }));
    expect(screen.getByText("Cash")).toBeInTheDocument();
  });

  it("reaches a child row with the arrow keys through a single roving tab stop", async () => {
    render(<AccountTree nodes={nodes} />);
    const items = screen.getAllByRole("treeitem");
    expect(items.filter((item) => item.tabIndex === 0)).toHaveLength(1);
    const assets = screen.getByRole("treeitem", { name: /1000 Assets/ });
    expect(assets).toHaveAttribute("tabindex", "0");

    assets.focus();
    await userEvent.keyboard("{ArrowDown}");
    const cash = screen.getByRole("treeitem", { name: /1010 Cash/ });
    expect(cash).toHaveFocus();
    expect(cash).toHaveAttribute("tabindex", "0");
    expect(assets).toHaveAttribute("tabindex", "-1");

    await userEvent.keyboard("{End}");
    expect(screen.getByRole("treeitem", { name: /1020 Receivables/ })).toHaveFocus();
    await userEvent.keyboard("{ArrowUp}");
    expect(cash).toHaveFocus();
    await userEvent.keyboard("{ArrowLeft}");
    expect(assets).toHaveFocus();
    await userEvent.keyboard("{ArrowRight}");
    expect(cash).toHaveFocus();
    await userEvent.keyboard("{Home}");
    expect(assets).toHaveFocus();
  });

  it("expands and collapses from the keyboard without onSelect", async () => {
    render(<AccountTree nodes={nodes} defaultExpandedDepth={0} />);
    const assets = screen.getByRole("treeitem", { name: /1000 Assets/ });
    expect(assets).toHaveAttribute("aria-expanded", "false");

    assets.focus();
    await userEvent.keyboard("{ArrowRight}");
    expect(assets).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByText("Cash")).toBeInTheDocument();

    await userEvent.keyboard("{ArrowLeft}");
    expect(assets).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByText("Cash")).toBeNull();

    await userEvent.keyboard("{Enter}");
    expect(screen.getByText("Cash")).toBeInTheDocument();
  });

  it("returns the tab stop to the parent when the focused child is collapsed away", async () => {
    // Two roots, and the collapsed branch is the second: a fallback to the first row of the
    // tree would put the tab stop on Equity and fail this.
    const twoRoots: AccountNode[] = [{ code: "3000", name: "Equity", balance: 100 }, ...nodes];
    render(<AccountTree nodes={twoRoots} />);
    await userEvent.click(screen.getByRole("treeitem", { name: /1010 Cash/ }));
    await userEvent.click(screen.getByRole("button", { name: "Collapse Assets" }));
    expect(screen.getByRole("treeitem", { name: /1000 Assets/ })).toHaveAttribute("tabindex", "0");
    expect(screen.getByRole("treeitem", { name: /3000 Equity/ })).toHaveAttribute("tabindex", "-1");
  });

  it("exposes aria-level and keeps the column header outside the tree", () => {
    render(<AccountTree nodes={nodes} />);
    expect(screen.getByRole("treeitem", { name: /1000 Assets/ })).toHaveAttribute("aria-level", "1");
    expect(screen.getByRole("treeitem", { name: /1010 Cash/ })).toHaveAttribute("aria-level", "2");
    const tree = screen.getByRole("tree", { name: "Chart of accounts" });
    expect(Array.from(tree.children).every((child) => child.getAttribute("role") === "treeitem")).toBe(true);
    expect(tree).not.toHaveTextContent("Balance");
  });

  it("renders an empty state when there are no accounts", () => {
    render(<AccountTree nodes={[]} />);
    expect(screen.getByText("No accounts")).toBeInTheDocument();
    expect(screen.queryAllByRole("treeitem")).toHaveLength(0);
  });
});
