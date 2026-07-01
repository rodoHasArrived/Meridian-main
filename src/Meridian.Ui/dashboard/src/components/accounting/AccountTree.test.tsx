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
});
