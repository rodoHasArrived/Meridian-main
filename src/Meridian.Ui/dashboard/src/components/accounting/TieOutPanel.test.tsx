import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { vi } from "vitest";
import { TieOutPanel } from "./TieOutPanel";

describe("TieOutPanel", () => {
  it("ties out when the explained items account for the whole difference", () => {
    render(
      <TieOutPanel
        title="Custodian cash tie-out"
        sourceA={{ label: "Ledger balance", value: 100, condition: "confirmed" }}
        sourceB={{ label: "Custodian statement", value: 90, condition: "confirmed", count: 12 }}
        items={[{ id: "wire", label: "In-transit wire", value: -10, evidence: "Wire advice 4471" }]}
      />
    );

    const panel = screen.getByRole("region", { name: "Custodian cash tie-out" });
    expect(panel).toHaveAttribute("data-tie-out-status", "tied");
    expect(within(panel).getByText("Tied out")).toBeInTheDocument();
    expect(within(panel).getByText("One-to-one")).toBeInTheDocument();
    expect(within(panel).getByText("12 records")).toBeInTheDocument();
    expect(within(panel).getByText("+$10.00")).toBeInTheDocument();
    expect(within(panel).getByText("−$10.00")).toBeInTheDocument();
    expect(within(panel).getByText("Difference fully explained by the reconciling items above.")).toBeInTheDocument();
    expect(within(panel).getByText("Agrees exactly")).toBeInTheDocument();
  });

  it("is indeterminate when a source has no value — a missing input is not a zero", () => {
    render(
      <TieOutPanel
        sourceA={{ label: "Ledger balance", value: 100 }}
        sourceB={{ label: "Custodian statement", condition: "missing" }}
        tolerance={500}
      />
    );

    const panel = screen.getByRole("region", { name: "Tie-out proof" });
    expect(panel).toHaveAttribute("data-tie-out-status", "indeterminate");
    expect(panel).not.toHaveTextContent("Tied out");
    expect(panel).not.toHaveTextContent("Within tolerance");
    expect(within(panel).getByText("Not tied — input missing")).toBeInTheDocument();
    expect(panel).toHaveTextContent("Cannot tie out.");
    expect(panel).toHaveTextContent("Custodian statement has no value");
    expect(panel).toHaveTextContent("An unavailable input is not a zero.");
    expect(within(panel).getByText("Residual not computable — an input is missing")).toBeInTheDocument();
    // No acceptance or resolution affordance exists in the read-only port.
    expect(within(panel).queryAllByRole("button")).toHaveLength(0);
  });

  it("does not let a declared status outrank an unusable source condition", () => {
    render(
      <TieOutPanel
        sourceA={{ label: "Ledger", value: 100, condition: "confirmed" }}
        sourceB={{ label: "Custodian", value: 100, condition: "stale" }}
        status="tied"
        statusNote="Signed off by the controller."
      />
    );

    const panel = screen.getByRole("region", { name: "Tie-out proof" });
    expect(panel).not.toHaveTextContent("Tied out");
    expect(panel).toHaveTextContent("Custodian is stale");
    expect(panel).not.toHaveTextContent("Signed off by the controller.");
  });

  it("reports an exception with the unexplained residual", () => {
    render(
      <TieOutPanel
        sourceA={{ label: "Custodian statement", value: 800 }}
        sourceB={{ label: "Internal ledger", value: 600 }}
      />
    );

    const panel = screen.getByRole("region", { name: "Tie-out proof" });
    expect(panel).toHaveAttribute("data-tie-out-status", "exception");
    expect(within(panel).getByText("Exception")).toBeInTheDocument();
    expect(within(panel).getByText("+$200.00")).toBeInTheDocument();
    expect(within(panel).getByText("Residual exceeds tolerance and is unexplained.")).toBeInTheDocument();
    expect(within(panel).getByText("Outside tolerance")).toBeInTheDocument();
  });

  it("reads within tolerance as a disposition still owed, not as tied", () => {
    render(
      <TieOutPanel
        sourceA={{ label: "Ledger", value: 100 }}
        sourceB={{ label: "Custodian", value: 97 }}
        tolerance={5}
      />
    );

    const panel = screen.getByRole("region", { name: "Tie-out proof" });
    expect(panel).toHaveAttribute("data-tie-out-status", "tolerance");
    expect(panel).not.toHaveTextContent("Tied out");
    expect(within(panel).getByText("Within tolerance")).toBeInTheDocument();
    expect(within(panel).getByText("Within $5.00 tolerance")).toBeInTheDocument();
    expect(within(panel).getByText("Residual is below tolerance but is not explained. Classify it or accept it with a reason.")).toBeInTheDocument();
  });

  it("keeps a declared exception when offsetting breaks net to zero, and says why", () => {
    render(
      <TieOutPanel
        sourceA={{ label: "Custodian statement", value: 0 }}
        sourceB={{ label: "Internal ledger", value: 0 }}
        items={[
          { id: "cash", label: "Cash", value: 20, explained: false, conditionLabel: "Variance" },
          { id: "payables", label: "Payables", value: -20, explained: false, conditionLabel: "Variance" }
        ]}
        status="exception"
        statusNote="2 open breaks remain although the totals net to zero."
      />
    );

    const panel = screen.getByRole("region", { name: "Tie-out proof" });
    expect(panel).toHaveAttribute("data-tie-out-status", "exception");
    expect(panel).not.toHaveTextContent("Tied out");
    expect(panel).not.toHaveTextContent("Agrees exactly");
    expect(within(panel).getByText("2 open breaks remain although the totals net to zero.")).toBeInTheDocument();
    expect(within(panel).getByText("Not tied")).toBeInTheDocument();
    expect(within(panel).getByRole("button", { name: "Explained · 0 reconciling items · 2 identified, not explained" })).toBeInTheDocument();
  });

  it("ignores a declared tied status when the arithmetic leaves a residual", () => {
    render(
      <TieOutPanel
        sourceA={{ label: "Custodian statement", value: 800 }}
        sourceB={{ label: "Internal ledger", value: 600 }}
        status="tied"
        statusNote="Looks fine."
      />
    );

    const panel = screen.getByRole("region", { name: "Tie-out proof" });
    expect(panel).toHaveAttribute("data-tie-out-status", "exception");
    expect(panel).not.toHaveTextContent("Tied out");
    expect(panel).not.toHaveTextContent("Looks fine.");
  });

  it("discloses reconciling items with their condition and evidence", async () => {
    const onToggleDetail = vi.fn();
    render(
      <TieOutPanel
        sourceA={{ label: "Ledger", value: 100 }}
        sourceB={{ label: "Custodian", value: 90 }}
        items={[
          { id: "wire", label: "In-transit wire", value: -10, evidence: "Wire advice 4471" },
          { id: "fee", label: "Suspected fee", value: -3, explained: false }
        ]}
        onToggleDetail={onToggleDetail}
      />
    );

    const toggle = screen.getByRole("button", { name: "Explained · 1 reconciling item · 1 identified, not explained" });
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByRole("table")).not.toBeInTheDocument();

    await userEvent.click(toggle);

    expect(onToggleDetail).toHaveBeenCalledWith(true);
    expect(toggle).toHaveAttribute("aria-expanded", "true");
    const items = screen.getByRole("region", { name: "Reconciling items" });
    expect(items).toHaveAttribute("id", toggle.getAttribute("aria-controls"));
    const rows = within(items).getAllByRole("row");
    expect(rows).toHaveLength(3);
    expect(rows[1]).toHaveTextContent("In-transit wire");
    expect(rows[1]).toHaveTextContent("Confirmed");
    expect(rows[1]).toHaveTextContent("Wire advice 4471");
    expect(rows[2]).toHaveTextContent("Suspected fee");
    expect(rows[2]).toHaveTextContent("Exception");
    expect(rows[2]).toHaveTextContent("No evidence");

    await userEvent.click(toggle);
    expect(onToggleDetail).toHaveBeenLastCalledWith(false);
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("supports controlled disclosure and a host heading level", () => {
    render(
      <TieOutPanel
        headingLevel={4}
        expanded
        sourceA={{ label: "Ledger", value: 100 }}
        sourceB={{ label: "Custodian", value: 90 }}
        items={[{ id: "wire", label: "In-transit wire", value: -10 }]}
      />
    );

    expect(screen.getByRole("heading", { level: 4, name: "Tie-out proof" })).toBeInTheDocument();
    expect(screen.getByRole("table")).toBeInTheDocument();
  });

  it("prints the explained total as a dash when an explained item has no amount", () => {
    render(
      <TieOutPanel
        defaultExpanded
        sourceA={{ label: "Ledger", value: 100 }}
        sourceB={{ label: "Custodian", value: 100 }}
        items={[{ id: "fee", label: "Fee accrual", value: null }]}
      />
    );

    const panel = screen.getByRole("region", { name: "Tie-out proof" });
    expect(panel).toHaveAttribute("data-tie-out-status", "indeterminate");
    expect(panel).toHaveTextContent('Reconciling item "Fee accrual" has no amount');
    expect(within(panel).getAllByText("not computable").length).toBeGreaterThanOrEqual(2);
  });

  it("has no accessibility violations across statuses", async () => {
    const { container } = render(
      <main>
        <TieOutPanel
          title="Tied"
          defaultExpanded
          sourceA={{ label: "Ledger balance", value: 100, condition: "confirmed", count: 4 }}
          sourceB={{ label: "Custodian statement", value: 90, condition: "confirmed", count: 3 }}
          items={[
            { id: "wire", label: "In-transit wire", value: -10, evidence: "Wire advice 4471" },
            { id: "fee", label: "Suspected fee", value: null, explained: false }
          ]}
        />
        <TieOutPanel
          title="Exception"
          sourceA={{ label: "Custodian statement", value: 800 }}
          sourceB={{ label: "Internal ledger", value: 600 }}
        />
        <TieOutPanel
          title="Indeterminate"
          sourceA={{ label: "Ledger balance", value: 100 }}
          sourceB={{ label: "Custodian statement", value: null }}
        />
      </main>
    );

    expect((await axe(container)).violations).toEqual([]);
  });
});
