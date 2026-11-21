import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { FinancialValue } from "./financial-value";
import type { LedgerAmountSelection } from "@/types/ledger-amount-proof";

vi.mock("@/components/meridian/proof-drawer", () => ({
  LedgerAmountProofDrawer: ({ selection, onClose }: { selection: LedgerAmountSelection | null; onClose: () => void }) => selection
    ? <div role="dialog" aria-label="Amount passport"><button onClick={onClose}>Close</button>{selection.subjectId}</div> : null
}));

describe("FinancialValue", () => {
  it("shows missing and zero distinctly and never infers confirmation", () => {
    render(<><FinancialValue value={null} reason="No retained source row" /><FinancialValue value="0" currency="USD" /></>);
    expect(screen.getByText("Not recorded")).toBeInTheDocument();
    expect(screen.getByText("No retained source row")).toBeInTheDocument();
    expect(screen.getByText("0.00 USD")).toBeInTheDocument();
    expect(screen.getByText("Data condition unavailable")).toBeInTheDocument();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("retains exact display, currency precision, and non-color condition text", () => {
    render(<FinancialValue value="9007199254740993.125" currency="KWD" condition="stale" reason="Mark observation is old" />);
    expect(screen.getByText("9,007,199,254,740,993.125 KWD")).toBeInTheDocument();
    expect(screen.getByText("Stale")).toBeInTheDocument();
    expect(screen.getByTitle("Mark observation is old")).toBeInTheDocument();
  });

  it("opens only matching amount and currency proof subjects", async () => {
    const passport: LedgerAmountSelection = { subjectId: "entry:line:debit", ledgerBookId: "book", periodId: "period", fundProfileId: "fund", amount: 25, currency: "USD", label: "Cash debit" };
    const { rerender } = render(<FinancialValue value="25.00" currency="USD" condition="confirmed" passport={passport} />);
    await userEvent.tab();
    await userEvent.keyboard("{Enter}");
    expect(screen.getByRole("dialog", { name: "Amount passport" })).toHaveTextContent(passport.subjectId);
    await userEvent.click(screen.getByRole("button", { name: "Close" }));
    rerender(<FinancialValue value="26.00" currency="USD" condition="confirmed" passport={passport} />);
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
    expect(screen.getByText("Amount passport unavailable")).toBeInTheDocument();
  });
});
