import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { FinancialValue } from "./financial-value";
import type { LedgerAmountSelection } from "@/types/ledger-amount-proof";
import { createAmountPassport } from "@/test/amount-passport-fixtures";
import { axe } from "jest-axe";

vi.mock("@/components/meridian/proof-drawer", () => ({
  LedgerAmountProofDrawer: ({ selection, onClose }: { selection: LedgerAmountSelection | null; onClose: () => void }) => selection
    ? <div role="dialog" aria-label="Amount passport"><button onClick={onClose}>Close</button>{selection.subjectId}</div> : null
}));

describe("FinancialValue", () => {
  it("uses a visible shared notice for unavailable values while retaining each value and reason accessibly", async () => {
    const { container } = render(<>
      <p id="unknown-balances">Data condition unavailable. Reconciliation evidence was not supplied for these balances.</p>
      <p id="balance-scope">Selected book and period.</p>
      <FinancialValue value="0" currency="USD" condition="unavailable" conditionContextId="unknown-balances" aria-describedby="balance-scope unknown-balances"
        reason="The source did not provide a condition for cash." />
      <FinancialValue value="125.50" currency="USD" condition="unavailable" conditionContextId="unknown-balances"
        reason="The source did not provide a condition for receivables." />
    </>);
    expect(screen.getAllByText(/Data condition unavailable\./)).toHaveLength(1);
    expect(screen.queryByText("Data condition unavailable", { exact: true })).not.toBeInTheDocument();
    const zero = screen.getByRole("group", { name: "0.00 USD · Data condition unavailable" });
    expect(zero).toHaveAccessibleDescription("Selected book and period. Data condition unavailable. Reconciliation evidence was not supplied for these balances. The source did not provide a condition for cash.");
    expect(zero.getAttribute("aria-describedby")?.split(" ").filter(id => id === "unknown-balances")).toHaveLength(1);
    expect(screen.getByRole("group", { name: "125.50 USD · Data condition unavailable" })).toHaveAccessibleDescription(/condition for receivables/);
    expect(screen.getByTitle("The source did not provide a condition for cash.")).toBeInTheDocument();
    expect((await axe(container)).violations).toEqual([]);
  });

  it.each(["scoped", "legacy"] as const)("retains the unavailable condition, scope and reason on the actual %s proof control", async (kind) => {
    const reference = createAmountPassport(undefined, "Cash", "25").reference;
    const passport: LedgerAmountSelection = { subjectId: "entry:line:debit", ledgerBookId: "book", periodId: "period", fundProfileId: "fund", amount: 25, currency: "USD", label: "Cash debit" };
    const binding = kind === "scoped" ? { amountPassport: reference } : { passport };
    render(<>
      <p id="proof-context">Data condition unavailable. These are retained posted balances.</p>
      <FinancialValue value="25.00" currency="USD" condition="unavailable" conditionContextId="proof-context" reason="Cash reconciliation was not supplied." {...binding} />
    </>);
    const control = screen.getByRole("button", { name: `Open Amount passport for ${kind === "scoped" ? "financial value" : "Cash debit"}: 25.00 USD · Data condition unavailable` });
    expect(control).toHaveAccessibleDescription("Data condition unavailable. These are retained posted balances. Cash reconciliation was not supplied.");
    expect(control.getAttribute("aria-describedby")?.split(" ")).toContain("proof-context");
    expect(screen.queryByText("Data condition unavailable", { exact: true })).not.toBeInTheDocument();
    if (kind === "legacy") {
      control.focus();
      await userEvent.keyboard("{Enter}");
      expect(screen.getByRole("dialog", { name: "Amount passport" })).toHaveTextContent(passport.subjectId);
    }
  });

  it("keeps other conditions inline and keeps an unavailable label when the shared context is blank", () => {
    const { rerender } = render(<FinancialValue value="25" currency="USD" condition="stale" conditionContextId="shared-condition" reason="Observation is old." />);
    expect(screen.getByText("Stale")).toBeInTheDocument();
    rerender(<FinancialValue value="25" currency="USD" condition="unavailable" conditionContextId="   " />);
    expect(screen.getByText("Data condition unavailable")).toBeInTheDocument();
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
    const { container } = render(<><FinancialValue value={null} reason="No retained source row" /><FinancialValue value="0" currency="USD" /></>);
    expect(screen.getByText("Not available")).toBeInTheDocument();
    expect(container.querySelector('[data-value-state="unavailable"]')).not.toBeNull();
    expect(container.querySelector('[data-value-state="zero"]')).not.toBeNull();
    expect(screen.getByText("No retained source row")).toBeInTheDocument();
    expect(screen.getByText("0.00 USD")).toBeInTheDocument();
    expect(screen.getByText("Data condition unavailable")).toBeInTheDocument();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("renders an unparseable value as Not available, never as 0", () => {
    const { container } = render(<FinancialValue value="n/a-from-source" currency="EUR" condition="confirmed" />);
    expect(screen.getByText("Not available")).toBeInTheDocument();
    expect(container).not.toHaveTextContent("0.00");
    expect(container.querySelector('[data-value-state="unavailable"]')).not.toBeNull();
  });

  it("shows n/a for a not-applicable figure with a spelled-out accessible name", () => {
    const { container } = render(<FinancialValue value={null} currency="EUR" condition="not-applicable" />);
    expect(container.querySelector('[data-value-state="not-applicable"]')).not.toBeNull();
    expect(screen.getByText("n/a")).toHaveAttribute("aria-hidden", "true");
    expect(screen.getByText("Not applicable")).toHaveClass("sr-only");
    expect(container).not.toHaveTextContent("Not available");
  });

  it("shows a lock and No access for a restricted figure and never leaks the value or a passport", () => {
    const reference = createAmountPassport(undefined, "Cash", "25").reference;
    const { container } = render(<FinancialValue value="1250.00" currency="EUR" condition="confirmed" amountPassport={reference}
      restricted restrictedReason="Fund Beta is outside your scope" />);
    const restricted = container.querySelector('[data-value-state="restricted"]');
    expect(restricted).not.toBeNull();
    expect(restricted).toHaveTextContent("No access");
    expect(restricted?.querySelector("svg")).toHaveAttribute("aria-hidden", "true");
    expect(restricted).toHaveAccessibleDescription("Fund Beta is outside your scope");
    expect(container).not.toHaveTextContent("1,250");
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
