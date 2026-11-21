import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { FinancialValue, FinancialValueConditionGroup } from "./financial-value";
import type { LedgerAmountSelection } from "@/types/ledger-amount-proof";
import { createAmountPassport } from "@/test/amount-passport-fixtures";
import { axe } from "jest-axe";

vi.mock("@/components/meridian/proof-drawer", () => ({
  LedgerAmountProofDrawer: ({ selection, onClose }: { selection: LedgerAmountSelection | null; onClose: () => void }) => selection
    ? <div role="dialog" aria-label="Amount passport"><button onClick={onClose}>Close</button>{selection.subjectId}</div> : null
}));

describe("FinancialValue", () => {
  it("groups only unknown conditions while preserving each exact value, accessible reason and passport", async () => {
    const reference = createAmountPassport(undefined, "Cash", "25").reference;
    const { container } = render(<FinancialValueConditionGroup description="The selected source has not recorded these conditions.">
      <FinancialValue value="25.00" currency="USD" amountPassport={reference} reason="Cash condition was not retained." />
      <FinancialValue value="0" currency="USD" reason="Fee condition was not retained." />
      <FinancialValue value="3.125" currency="KWD" condition="stale" reason="The observation is old." />
      <FinancialValue value="10" currency="USD" condition="confirmed" />
    </FinancialValueConditionGroup>);
    expect(screen.getAllByText(/Data condition unavailable:/)).toHaveLength(1);
    const cash = screen.getByRole("group", { name: "25.00 USD · Data condition unavailable" });
    expect(cash).toHaveAccessibleDescription("Data condition unavailable: The selected source has not recorded these conditions.");
    expect(cash).toHaveAttribute("title", "Cash condition was not retained.");
    expect(screen.getByRole("group", { name: "0.00 USD · Data condition unavailable" })).toBeInTheDocument();
    expect(screen.getByText("Stale")).toBeInTheDocument();
    expect(screen.getByText("10.00 USD")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Open Amount passport for financial value: 25.00 USD · Data condition unavailable" })).toBeInTheDocument();
    expect((await axe(container, { runOnly: { type: "rule", values: ["aria-allowed-attr", "label-content-name-mismatch"] } })).violations).toEqual([]);
  });

  it("does not add a missing-condition claim for an inactive group", () => {
    render(<FinancialValueConditionGroup active={false} description="No retained condition."><FinancialValue value="12" condition="confirmed" /></FinancialValueConditionGroup>);
    expect(screen.queryByText(/Data condition unavailable/)).not.toBeInTheDocument();
  });
  it.each(["scoped", "legacy"] as const)("includes the visible condition in the %s passport name while confirmed values remain concise", async (kind) => {
    const reference = createAmountPassport(undefined, "Cash", "25").reference;
    const passport: LedgerAmountSelection = { subjectId: "entry:line:debit", ledgerBookId: "book", periodId: "period", fundProfileId: "fund", amount: 25, currency: "USD", label: "Cash debit" };
    const binding = kind === "scoped" ? { amountPassport: reference } : { passport };
    const { container, rerender } = render(<FinancialValue value="25.00" currency="USD" condition="stale" reason="Price observation is old" {...binding} />);
    expect(screen.getByRole("button")).toHaveAccessibleName(`Open Amount passport for ${kind === "scoped" ? "financial value" : "Cash debit"}: 25.00 USD · Stale`);
    expect(screen.getByTitle("Price observation is old")).toBeInTheDocument();
    expect((await axe(container, { runOnly: { type: "rule", values: ["label-content-name-mismatch"] } })).violations).toEqual([]);
    rerender(<FinancialValue value="25.00" currency="USD" condition="confirmed" {...binding} />);
    expect(screen.getByRole("button")).toHaveAccessibleName(`Open Amount passport for ${kind === "scoped" ? "financial value" : "Cash debit"}: 25.00 USD`);
    expect(screen.queryByText("Stale")).not.toBeInTheDocument();
  });
  it("uses the existing scoped passport trigger without the unavailable-origin question glyph", () => {
    const reference = createAmountPassport(undefined, "Cash", "25").reference;
    const { rerender } = render(<FinancialValue value="25.00" currency="USD" condition="confirmed" amountPassport={reference} />);
    expect(screen.getByRole("button", { name: "Open Amount passport for financial value: 25.00 USD" })).toBeInTheDocument();
    expect(screen.queryByText("?")).not.toBeInTheDocument();
    rerender(<FinancialValue value="26.00" currency="USD" condition="confirmed" amountPassport={reference} />);
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
    expect(screen.getByText("Amount passport unavailable")).toBeInTheDocument();
  });
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
