import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { InlineFundAccountPicker } from "@/components/meridian/inline-fund-account-picker";
import {
  FundAccountScopeSelectionProvider,
  type FundAccountScopeSelection
} from "@/lib/operating-scope/scope-picker-context";
import { collectScopeStructureOptions, UNAVAILABLE_SCOPE_STRUCTURE } from "@/lib/operating-scope/structure-options";

const STRUCTURE = collectScopeStructureOptions({
  entities: [{ entityId: "E-A", name: "Entity A" }],
  funds: [
    { fundId: "F-1", name: "Fund One", entityIds: ["E-A"] },
    { fundId: "F-2", name: "Fund Two", entityIds: ["E-A"] }
  ],
  accounts: [
    { accountId: "A-1", displayName: "One brokerage", fundId: "F-1", entityId: "E-A" },
    { accountId: "A-2", displayName: "Two brokerage", fundId: "F-2", entityId: "E-A" }
  ]
});
const LOADED = [
  { id: "A-1", label: "One brokerage" },
  { id: "A-2", label: "Two brokerage" },
  { id: "H-9", label: "Household only" }
];

function renderPicker(selection: Partial<FundAccountScopeSelection>) {
  const select = vi.fn();
  render(
    <FundAccountScopeSelectionProvider value={{ options: LOADED, select, ...selection }}>
      <InlineFundAccountPicker title="Choose the fund account" fallback={<p>No shell</p>} />
    </FundAccountScopeSelectionProvider>
  );
  return { select };
}

const suggestedIds = () =>
  Array.from(screen.getByTestId("inline-fund-account-options").querySelectorAll("option")).map((option) => option.value);

describe("InlineFundAccountPicker fund narrowing", () => {
  it("offers every loaded account when no entity or fund is chosen", () => {
    renderPicker({ structure: STRUCTURE, scope: {} });
    expect(suggestedIds()).toEqual(["H-9", "A-1", "A-2"]);
    expect(screen.queryByText(/under the chosen/)).toBeNull();
  });

  it("narrows suggestions to the chosen fund and counts accounts with no recorded fund", () => {
    renderPicker({ structure: STRUCTURE, scope: { fundId: "f-1" } });
    expect(suggestedIds()).toEqual(["A-1"]);
    expect(screen.getByText("Showing 1 account under the chosen fund. 1 with no recorded fund not shown.")).toBeInTheDocument();
    expect(screen.getByLabelText("Fund account")).toHaveAccessibleDescription(
      "Showing 1 account under the chosen fund. 1 with no recorded fund not shown."
    );
  });

  it("flags an account the graph places outside the chosen fund, but still lets the operator use it", async () => {
    const user = userEvent.setup();
    const { select } = renderPicker({ structure: STRUCTURE, scope: { fundId: "F-1" } });

    await user.type(screen.getByLabelText("Fund account"), "A-2");
    const flag = screen.getByRole("status");
    expect(flag).toHaveTextContent("Two brokerage is not under the chosen fund.");

    await user.click(screen.getByRole("button", { name: "Use account" }));
    expect(select).toHaveBeenCalledWith("A-2");
  });

  it("does not flag an account whose fund is unknown", async () => {
    const user = userEvent.setup();
    renderPicker({ structure: STRUCTURE, scope: { fundId: "F-1" } });
    await user.type(screen.getByLabelText("Fund account"), "H-9");
    expect(screen.queryByRole("status")).toBeNull();
  });

  it("flags the scope's current account when it does not fit the chosen fund", () => {
    renderPicker({ structure: STRUCTURE, scope: { fundId: "F-1", fundAccountId: "A-2" } });
    expect(screen.getByRole("status")).toHaveTextContent("Two brokerage is not under the chosen fund.");
  });

  it("does not narrow without the organization graph, and says why", () => {
    renderPicker({ structure: UNAVAILABLE_SCOPE_STRUCTURE, scope: { fundId: "F-1" } });
    expect(suggestedIds()).toHaveLength(3);
    const form = screen.getByRole("form", { name: "Choose the fund account form" });
    expect(within(form).getByText("Entity and fund lists could not be loaded, so accounts are not narrowed by entity or fund.")).toBeInTheDocument();
  });

  it("says the structure is loading while it loads", () => {
    renderPicker({ structureLoading: true, scope: { entityId: "E-A" } });
    expect(screen.getByText("Loading entities and funds…")).toBeInTheDocument();
  });
});
