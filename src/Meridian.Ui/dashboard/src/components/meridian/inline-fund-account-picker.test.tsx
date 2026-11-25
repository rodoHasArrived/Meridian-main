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
  const view = render(
    <FundAccountScopeSelectionProvider value={{ options: LOADED, select, ...selection }}>
      <InlineFundAccountPicker title="Choose the fund account" fallback={<p>No shell</p>} />
    </FundAccountScopeSelectionProvider>
  );
  return { select, ...view };
}

const openPicker = async () => {
  const user = userEvent.setup();
  await user.click(screen.getByRole("combobox", { name: "Fund account" }));
  return user;
};

describe("InlineFundAccountPicker", () => {
  it("offers loaded account names without requiring raw IDs", async () => {
    const { select } = renderPicker({ structure: STRUCTURE, scope: {} });
    const user = await openPicker();
    expect(screen.getAllByRole("option").map((option) => option.textContent)).toEqual(["Household only", "One brokerage", "Two brokerage"]);
    await user.type(screen.getByRole("combobox"), "One brokerage");
    expect(select).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "Use account" })).toBeDisabled();
    await user.keyboard("{Enter}");
    expect(select).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Use account" }));
    expect(select).toHaveBeenCalledWith("A-1");
  });

  it("narrows choices to the chosen fund and explains omitted accounts", async () => {
    renderPicker({ structure: STRUCTURE, scope: { fundId: "f-1" } });
    await openPicker();
    expect(screen.getAllByRole("option").map((option) => option.textContent)).toEqual(["One brokerage"]);
    expect(screen.getByRole("combobox", { name: "Fund account" })).toHaveAccessibleDescription(
      "Showing 1 account under the chosen fund. 1 with no recorded fund not shown."
    );
  });

  it("keeps a current outside-fund account visible and warns without silently replacing it", async () => {
    const { select } = renderPicker({ structure: STRUCTURE, scope: { fundId: "F-1", fundAccountId: "A-2" } });
    expect(screen.getByRole("combobox")).toHaveValue("Two brokerage");
    expect(screen.getByRole("status")).toHaveTextContent("Two brokerage is not under the chosen fund.");
    await userEvent.click(screen.getByRole("button", { name: "Use account" }));
    expect(select).toHaveBeenCalledWith("A-2");
  });

  it("does not submit arbitrary search text", async () => {
    const { select } = renderPicker({ structure: STRUCTURE, scope: {} });
    const user = await openPicker();
    await user.type(screen.getByRole("combobox"), "unknown-id");
    expect(screen.getByText("No matching accounts.")).toBeInTheDocument();
    await user.keyboard("{Enter}");
    expect(screen.getByRole("button", { name: "Use account" })).toBeDisabled();
    expect(select).not.toHaveBeenCalled();
  });

  it("preselects the last-used account as a draft without changing scope", () => {
    const { select } = renderPicker({ lastUsedAccountId: "A-2" });
    expect(screen.getByRole("combobox")).toHaveValue("Two brokerage");
    expect(screen.getByRole("button", { name: "Use account" })).toBeEnabled();
    expect(select).not.toHaveBeenCalled();
  });

  it("does not preselect a last-used account outside the chosen fund", () => {
    renderPicker({ structure: STRUCTURE, scope: { fundId: "F-1" }, lastUsedAccountId: "A-2" });
    expect(screen.getByRole("combobox")).toHaveValue("");
    expect(screen.getByRole("button", { name: "Use account" })).toBeDisabled();
  });

  it("prefers explicit scope over the last-used account", () => {
    renderPicker({ scope: { fundAccountId: "A-1" }, lastUsedAccountId: "A-2" });
    expect(screen.getByRole("combobox")).toHaveValue("One brokerage");
  });

  it("keeps empty scope recoverable and explains unloaded relationships", async () => {
    renderPicker({ options: [], structure: UNAVAILABLE_SCOPE_STRUCTURE, scope: { fundId: "F-1" } });
    await openPicker();
    expect(screen.getByText(/No accounts available in this scope/)).toBeInTheDocument();
    const form = screen.getByRole("form", { name: "Choose the fund account form" });
    expect(within(form).getByText("Entity and fund lists could not be loaded, so accounts are not narrowed by entity or fund.")).toBeInTheDocument();
  });

  it("says the structure is loading while it loads", () => {
    renderPicker({ structureLoading: true, scope: { entityId: "E-A" } });
    expect(screen.getByText("Loading entities and funds…")).toBeInTheDocument();
  });
});
