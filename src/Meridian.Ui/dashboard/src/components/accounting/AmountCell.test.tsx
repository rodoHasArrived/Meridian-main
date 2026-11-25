import { render, screen } from "@testing-library/react";
import { AmountCell } from "./AmountCell";

describe("AmountCell", () => {
  it("renders a plain positive amount", () => {
    render(<AmountCell value={1234.5} currency="USD" />);
    expect(screen.getByText("$1,234.50")).toBeInTheDocument();
  });

  it("renders negatives in accounting parentheses when parens is set", () => {
    render(<AmountCell value={-1234} currency="USD" parens />);
    expect(screen.getByText("($1,234.00)")).toBeInTheDocument();
  });

  it("renders exact zero as an em dash when zeroDash is set", () => {
    render(<AmountCell value={0} zeroDash />);
    expect(screen.getByText("—")).toBeInTheDocument();
  });

  it("colors P&L mode: negative red, positive green", () => {
    // Assert the declared design token, not the computed color: jsdom 31+ resolves var()
    // fallbacks in getComputedStyle, which would make this depend on the test theme.
    const { rerender } = render(<AmountCell value={-5} mode="pnl" />);
    expect(screen.getByText("−5.00").style.color).toBe("var(--red-dim, #883832)");

    rerender(<AmountCell value={5} mode="pnl" signed />);
    expect(screen.getByText("+5.00").style.color).toBe("var(--green-dim, #225840)");
  });

  it("renders through an alternate element tag", () => {
    render(<AmountCell as="td" value={10} data-testid="amt" />);
    const cell = screen.getByTestId("amt");
    expect(cell.tagName).toBe("TD");
    expect(cell).toHaveTextContent("10.00");
  });
});
