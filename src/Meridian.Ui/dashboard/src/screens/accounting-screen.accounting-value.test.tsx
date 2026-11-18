import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { AccountingValue } from "@/screens/accounting-screen.accounting-value";

describe("AccountingValue", () => {
  it("keeps figures on one line in the data face and prose in the body face", () => {
    render(<>
      <AccountingValue label="SLA breach count" value="12" />
      <AccountingValue label="Close readiness" value="Ready to close after approvals" />
    </>);
    const figure = screen.getByText("12");
    expect(figure).toHaveAttribute("data-detail-value", "number");
    expect(figure).toHaveClass("whitespace-nowrap", "font-mono");
    expect(screen.getByText("Ready to close after approvals")).not.toHaveClass("font-mono");
  });

  it("prints a date-only or midnight-UTC value as a date, without an invented time", () => {
    render(<AccountingValue label="Period start" value="2026-01-01T00:00:00Z" />);
    const value = screen.getByText("Jan 1, 2026");
    expect(value).toHaveAttribute("data-detail-value", "date");
    expect(value).toHaveClass("whitespace-nowrap");
    expect(value).not.toHaveClass("font-mono");
  });
});
