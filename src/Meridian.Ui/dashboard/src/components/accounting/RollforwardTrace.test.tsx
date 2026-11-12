import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { vi } from "vitest";
import { RollforwardTrace } from "./RollforwardTrace";

describe("RollforwardTrace", () => {
  it("prints opening, movements and closing, and proves they agree", () => {
    const { container } = render(
      <RollforwardTrace
        opening={9_875_000}
        closing={9_878_812.4}
        currency="USD"
        movements={[
          { id: "accretion", label: "Discount accretion", value: 3_812.4, note: "effective interest" },
          { id: "principal", label: "Principal movement", value: 0 }
        ]}
      />
    );

    expect(screen.getByText("Opening book value")).toBeInTheDocument();
    expect(screen.getByText("$9,875,000.00")).toBeInTheDocument();
    expect(screen.getByText("Discount accretion")).toBeInTheDocument();
    expect(screen.getByText("effective interest")).toBeInTheDocument();
    expect(screen.getByText("$3,812.40")).toBeInTheDocument();
    expect(screen.getByText("Closing book value")).toBeInTheDocument();
    expect(screen.getByText("$9,878,812.40")).toBeInTheDocument();
    expect(screen.getByText("Opening + movements = closing · 2 movements")).toBeInTheDocument();
    expect(container.firstElementChild).toHaveAttribute("data-rollforward-verdict", "tied");
  });

  it("says how far out the stated closing is instead of printing both numbers quietly", () => {
    const { container } = render(
      <RollforwardTrace
        opening={100}
        closing={135.5}
        currency="USD"
        movements={[
          { id: "call", label: "Capital call", value: 50 },
          { id: "dist", label: "Distribution", value: -20 }
        ]}
      />
    );

    expect(screen.getByText("Out by $5.50 — movements do not explain the closing balance")).toBeInTheDocument();
    expect(screen.queryByText(/Opening \+ movements = closing/)).not.toBeInTheDocument();
    expect(container.firstElementChild).toHaveAttribute("data-rollforward-verdict", "out");
  });

  it("carries the sign in the operator column and speaks it to assistive technology", () => {
    render(
      <RollforwardTrace
        opening={100}
        movements={[
          { id: "call", label: "Capital call", value: 50 },
          { id: "dist", label: "Distribution", value: -20 },
          { id: "flat", label: "Other adjustment", value: 0 }
        ]}
      />
    );

    const call = screen.getByText("Capital call").closest("div") as HTMLElement;
    const distribution = screen.getByText("Distribution").closest("div") as HTMLElement;
    const flat = screen.getByText("Other adjustment").closest("div") as HTMLElement;

    expect(within(call).getByText("+")).toHaveAttribute("aria-hidden", "true");
    expect(within(call).getByText("plus")).toBeInTheDocument();
    // The amount prints as an absolute value; the minus lives in the operator column.
    expect(within(distribution).getByText("−")).toHaveAttribute("aria-hidden", "true");
    expect(within(distribution).getByText("less")).toBeInTheDocument();
    expect(within(distribution).getByText("20.00")).toBeInTheDocument();
    expect(within(flat).getByText("±")).toBeInTheDocument();
    expect(within(flat).getByText("—")).toBeInTheDocument();
  });

  it("prints a missing amount in words so it cannot be mistaken for the zero dash", () => {
    render(
      <RollforwardTrace
        openingLabel="Opening net activity"
        opening={0}
        closingLabel="Ending net activity"
        closing={null}
        movements={[
          { id: "flat", label: "Other adjustment", value: 0 },
          { id: "call", label: "Capital call", value: null }
        ]}
      />
    );

    const opening = screen.getByText("Opening net activity").closest("div") as HTMLElement;
    const flat = screen.getByText("Other adjustment").closest("div") as HTMLElement;
    const call = screen.getByText("Capital call").closest("div") as HTMLElement;
    const ending = screen.getByText("Ending net activity").closest("div") as HTMLElement;

    // Zero keeps the package's dash for sighted users and is spoken as "zero".
    expect(within(opening).getByText("—")).toHaveAttribute("aria-hidden", "true");
    expect(within(opening).getByText("zero")).toBeInTheDocument();
    expect(within(flat).getByText("—")).toHaveAttribute("aria-hidden", "true");
    expect(within(flat).getByText("zero")).toBeInTheDocument();
    // Missing is never a dash.
    expect(within(ending).getByText("Not available")).toBeVisible();
    expect(within(ending).queryByText("—")).not.toBeInTheDocument();
    expect(within(call).getByText("No amount")).toBeVisible();
    expect(within(call).queryByText("—")).not.toBeInTheDocument();
    expect(screen.getAllByText("—")).toHaveLength(2);
  });

  it("does not speak a zero that is printed as 0.00", () => {
    render(
      <RollforwardTrace opening={10} movements={[{ id: "flat", label: "Other adjustment", value: 0, emptyDash: false }]} />
    );

    expect(screen.queryByText("zero")).not.toBeInTheDocument();
  });

  it("prints a zero movement as 0.00 when the dash is switched off", () => {
    render(
      <RollforwardTrace opening={10} movements={[{ id: "flat", label: "Other adjustment", value: 0, emptyDash: false }]} />
    );

    const flat = screen.getByText("Other adjustment").closest("div") as HTMLElement;
    expect(within(flat).getByText("0.00")).toBeInTheDocument();
  });

  it("uses opening plus movements as the closing when no closing is stated", () => {
    render(<RollforwardTrace opening={10} movements={[{ id: "call", label: "Capital call", value: 2.5 }]} />);

    expect(screen.getByText("12.50")).toBeInTheDocument();
    expect(screen.getByText("Opening + movements = closing · 1 movement")).toBeInTheDocument();
  });

  it("labels a derived opening and states the derivation", () => {
    render(
      <RollforwardTrace
        openingLabel="Opening net activity"
        opening={0}
        closing={125}
        movements={[{ id: "net", label: "Net capital activity", value: 125 }]}
        openingDerivation="Opening is derived from the subledger running balance; it is not a separately stated opening balance."
      />
    );

    expect(screen.getByText("(derived)")).toBeInTheDocument();
    expect(screen.getByText("Opening is derived from the subledger running balance; it is not a separately stated opening balance.")).toBeInTheDocument();
  });

  it("reports figures that agree by construction as not proved, never as a passed proof", () => {
    const { container } = render(
      <RollforwardTrace
        openingLabel="Opening net activity"
        opening={0}
        closingLabel="Ending net activity"
        closing={125}
        currency="USD"
        movements={[{ id: "net", label: "Net capital activity", value: 125 }]}
        unsupportedReason="no subledger entries are loaded, so the one net movement restates the ending."
      />
    );

    expect(screen.getByText("Not proved — no subledger entries are loaded, so the one net movement restates the ending.")).toBeInTheDocument();
    expect(screen.queryByText(/Opening \+ movements = closing/)).not.toBeInTheDocument();
    expect(screen.queryByText("✓")).not.toBeInTheDocument();
    expect(container.firstElementChild).toHaveAttribute("data-rollforward-verdict", "unsupported");
    // The figures still print: the trace withholds the verdict, not the numbers.
    expect(screen.getByText("Net capital activity")).toBeInTheDocument();
    expect(screen.getAllByText("$125.00")).toHaveLength(2);
  });

  it("still prints an out-by or a missing amount when the figures are not independent", () => {
    const { container, rerender } = render(
      <RollforwardTrace
        opening={0}
        closing={200}
        currency="USD"
        movements={[{ id: "net", label: "Net capital activity", value: 125 }]}
        unsupportedReason="no subledger entries are loaded."
      />
    );

    expect(screen.getByText("Out by $75.00 — movements do not explain the closing balance")).toBeInTheDocument();
    expect(screen.queryByText(/Not proved/)).not.toBeInTheDocument();
    expect(container.firstElementChild).toHaveAttribute("data-rollforward-verdict", "out");

    rerender(
      <RollforwardTrace
        opening={0}
        closing={null}
        movements={[{ id: "net", label: "Net capital activity", value: 125 }]}
        unsupportedReason="no subledger entries are loaded."
      />
    );

    expect(screen.getByText(/Cannot prove the rollforward — Closing book value has no value\./)).toBeInTheDocument();
    expect(screen.queryByText(/Not proved/)).not.toBeInTheDocument();
    expect(container.firstElementChild).toHaveAttribute("data-rollforward-verdict", "indeterminate");
  });

  it("does not mark the opening as derived unless told so", () => {
    render(<RollforwardTrace opening={0} movements={[]} />);

    expect(screen.queryByText("(derived)")).not.toBeInTheDocument();
  });

  it("refuses to prove a rollforward with a missing amount rather than reading it as zero", () => {
    const { container } = render(
      <RollforwardTrace
        openingLabel="Opening net activity"
        opening={null}
        closing={125}
        movements={[{ id: "call", label: "Capital call", value: null }]}
      />
    );

    expect(screen.getByText(/Cannot prove the rollforward — Opening net activity has no value · Movement "Capital call" has no amount\. A missing amount is not a zero\./)).toBeInTheDocument();
    expect(screen.queryByText(/Opening \+ movements = closing/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Out by/)).not.toBeInTheDocument();
    expect(container.firstElementChild).toHaveAttribute("data-rollforward-verdict", "indeterminate");
  });

  it("does not agree with itself when the stated closing is missing", () => {
    render(
      <RollforwardTrace
        opening={100}
        closingLabel="Ending net activity"
        closing={null}
        movements={[{ id: "call", label: "Capital call", value: 25 }]}
      />
    );

    expect(screen.getByText(/Cannot prove the rollforward — Ending net activity has no value\./)).toBeInTheDocument();
    expect(screen.queryByText(/Opening \+ movements = closing/)).not.toBeInTheDocument();
  });

  it("renders a resolve-to link only when it can resolve", async () => {
    const onOpen = vi.fn();
    render(
      <RollforwardTrace
        opening={0}
        movements={[
          { id: "a", label: "Discount accretion", value: 5, linkLabel: "Journal", onOpen },
          { id: "b", label: "Principal movement", value: 5, linkLabel: "Journal" }
        ]}
      />
    );

    const links = screen.getAllByRole("button");
    expect(links).toHaveLength(1);
    expect(links[0]).toHaveAccessibleName("Journal: Discount accretion");
    await userEvent.click(links[0]);
    expect(onOpen).toHaveBeenCalledTimes(1);
  });

  it("can hide the proof strip", () => {
    render(<RollforwardTrace opening={1} closing={5} movements={[]} showProof={false} />);

    expect(screen.queryByText(/Out by/)).not.toBeInTheDocument();
  });

  it("has no accessibility violations when tied, out, indeterminate, and not proved", async () => {
    const { container } = render(
      <div>
        <RollforwardTrace
          opening={100}
          closing={150}
          currency="USD"
          movements={[{ id: "call", label: "Capital call", value: 50, linkLabel: "Journal", onOpen: () => undefined }]}
          openingDerivation="Opening is derived from the subledger running balance."
        />
        <RollforwardTrace opening={100} closing={10} movements={[{ id: "dist", label: "Distribution", value: -20 }]} />
        <RollforwardTrace opening={null} movements={[{ id: "call", label: "Capital call", value: null }]} />
        <RollforwardTrace
          opening={0}
          closing={125}
          movements={[{ id: "net", label: "Net capital activity", value: 125 }]}
          unsupportedReason="no subledger entries are loaded."
        />
      </div>
    );

    expect((await axe(container)).violations).toEqual([]);
  });
});
