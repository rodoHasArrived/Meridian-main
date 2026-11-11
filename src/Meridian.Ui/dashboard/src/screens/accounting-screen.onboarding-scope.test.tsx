import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, expect, it, vi } from "vitest";
import { apiGetJson } from "@/lib/api";
import { OnboardingScopeFields } from "./accounting-screen.onboarding-scope";
vi.mock("@/lib/api", () => ({ apiGetJson: vi.fn() }));
afterEach(() => vi.resetAllMocks());
it("discovers dependent authorized scope choices and clears incompatible selections", async () => {
  const account = { accountId: "account-a", entityId: "entity-a", fundId: "fund-a", sleeveId: null, vehicleId: null, displayName: "Operating cash", baseCurrency: "EUR", isActive: true };
  vi.mocked(apiGetJson).mockResolvedValueOnce({ entities: [{ entityId: "entity-a", name: "North entity", isActive: true }, { entityId: "entity-b", name: "South entity", isActive: true }], accounts: [account] })
    .mockResolvedValueOnce([{ ledgerBookId: "book-a", fundProfileId: "profile-a", fundStructureNodeKind: "Account", fundStructureNodeId: "account-a", displayName: "Operating book", baseCurrency: "EUR" },
      { ledgerBookId: "book-other", fundProfileId: "profile-other", fundStructureNodeKind: "Account", fundStructureNodeId: "other-account", displayName: "Other book", baseCurrency: "USD" }]);
  const user = userEvent.setup();
  const { container } = render(<form><OnboardingScopeFields /></form>);
  await waitFor(() => expect(screen.getByLabelText("Entity")).toBeEnabled());
  await user.selectOptions(screen.getByLabelText("Entity"), "entity-a");
  expect(screen.queryByRole("option", { name: /Other book/ })).not.toBeInTheDocument();
  await user.selectOptions(screen.getByLabelText("Ledger book"), "book-a");
  await user.click(screen.getByLabelText("Operating cash · EUR"));
  const data = new FormData(container.querySelector("form")!);
  expect(data.get("entityId")).toBe("entity-a");
  expect(data.get("fundProfileId")).toBe("profile-a");
  expect(data.get("accountIds")).toBe("account-a");
  expect(vi.mocked(apiGetJson).mock.calls.every(([, options]) => options?.allowDevelopmentFallback === false)).toBe(true);
  await user.selectOptions(screen.getByLabelText("Entity"), "entity-b");
  expect(screen.getByLabelText("Ledger book")).toHaveValue("");
  expect(new FormData(container.querySelector("form")!).get("accountIds")).toBe("");
});
it("reports failed discovery without inventing selectable scope", async () => {
  vi.mocked(apiGetJson).mockRejectedValue(new Error("Access denied"));
  render(<OnboardingScopeFields />);
  await screen.findByText(/Authorized scope discovery unavailable: Access denied/);
  expect(screen.getByLabelText("Entity")).toBeDisabled();
  expect(screen.getByRole("button", { name: "Retry scope discovery" })).toBeEnabled();
});
