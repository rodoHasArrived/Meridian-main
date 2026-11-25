import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { ScopeBarAccountPicker } from "@/components/meridian/scope-bar-account-picker";
import { FundAccountScopeSelectionProvider, type FundAccountScopeSelection } from "@/lib/operating-scope/scope-picker-context";

const accountId = "11111111-2222-3333-4444-555555555555";

function renderPicker(selection: Partial<FundAccountScopeSelection>, normalize?: (value: string) => string | undefined) {
  const select = vi.fn();
  render(
    <FundAccountScopeSelectionProvider value={{ options: [{ id: accountId, label: "Main fund" }], select, ...selection }}>
      <ScopeBarAccountPicker normalize={normalize} invalidMessage="Trading needs a fund account with a GUID id." />
    </FundAccountScopeSelectionProvider>
  );
  return select;
}

describe("ScopeBarAccountPicker", () => {
  it("sets the fund account of the operating scope as soon as one is chosen", async () => {
    const user = userEvent.setup();
    const select = renderPicker({});
    await user.type(screen.getByRole("combobox", { name: "Account for this workspace" }), "Main");
    await user.keyboard("{Enter}");
    expect(select).toHaveBeenCalledWith(accountId);
  });

  it("renders nothing once an account is in scope, or outside the shell", () => {
    renderPicker({ scope: { entityId: null, fundId: null, fundAccountId: accountId } });
    expect(screen.queryByRole("combobox")).not.toBeInTheDocument();
    render(<ScopeBarAccountPicker />);
    expect(screen.queryByRole("combobox")).not.toBeInTheDocument();
  });

  it("does not apply an account the workspace cannot use, and says why", async () => {
    const user = userEvent.setup();
    const select = renderPicker({}, () => undefined);
    await user.type(screen.getByRole("combobox", { name: "Account for this workspace" }), "Main");
    await user.keyboard("{Enter}");
    expect(select).not.toHaveBeenCalled();
    expect(screen.getByRole("alert")).toHaveTextContent("Trading needs a fund account with a GUID id.");
  });
});
