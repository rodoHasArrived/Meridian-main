import { render, screen } from "@testing-library/react";
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
});
