import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import * as cutoverApi from "@/lib/api/bond-cutover.api";
import { ApiError } from "@/lib/api-errors";
import { BondCutoverConsolePanel } from "@/screens/accounting-screen.bond-cutover";
import type { BondMigrationInventory } from "@/types/bond-cutover.types";

vi.mock("@/lib/api/bond-cutover.api", () => ({
  getBondCutoverReadiness: vi.fn(),
  enableBondCalculationCutover: vi.fn()
}));

const api = vi.mocked(cutoverApi);

afterEach(() => {
  vi.resetAllMocks();
});

const inventory: BondMigrationInventory = {
  inventorySha256: "inv-hash-000000000000",
  bonds: [
    { securityId: "11111111-1111-1111-1111-111111111111", version: 3, securityContentSha256: "aaa111", displayName: "ACME 5 2030", hasDeclaredConventions: true, blockedReason: null },
    { securityId: "22222222-2222-2222-2222-222222222222", version: 7, securityContentSha256: "bbb222", displayName: "BETA FRN 2029", hasDeclaredConventions: false, blockedReason: "Bond cutover requires reviewed explicit schedule conventions; legacy live calculation is retired." }
  ],
  cutoverEnabled: false,
  requiresLegacyCalculations: true,
  currentReceiptSha256: null
};

describe("BondCutoverConsolePanel", () => {
  it("reads nothing until asked because the inventory is a full store read", () => {
    render(<BondCutoverConsolePanel />);
    expect(api.getBondCutoverReadiness).not.toHaveBeenCalled();
  });

  it("requires a reviewed block for every undeclared bond and binds blocks to version and hash", async () => {
    api.getBondCutoverReadiness.mockResolvedValueOnce(inventory).mockResolvedValueOnce({ ...inventory, cutoverEnabled: true, requiresLegacyCalculations: false, currentReceiptSha256: "receipt-1" });
    api.enableBondCalculationCutover.mockResolvedValue({
      inventorySha256: inventory.inventorySha256, reviewer: "reviewed-operator", recordedAt: "2026-10-31T00:00:00Z",
      explicitBlocks: [{ securityId: inventory.bonds[1].securityId, version: 7, securityContentSha256: "bbb222", reason: "Awaiting confirmation" }],
      modelVersion: "bond-migration-v1", previousReceiptSha256: null
    });
    render(<BondCutoverConsolePanel />);

    fireEvent.click(screen.getByRole("button", { name: "Load the complete active bond inventory" }));
    await screen.findByText("BETA FRN 2029");
    const submit = screen.getByRole("button", { name: "Enable bond calculation cutover" });
    fireEvent.click(screen.getByRole("checkbox"));
    expect(submit).toBeDisabled();
    expect(screen.getByRole("status")).toHaveTextContent("1 undeclared bond still needs a reviewed block reason.");

    fireEvent.change(screen.getByLabelText("Block reason for BETA FRN 2029"), { target: { value: "Awaiting confirmation" } });
    expect(submit).toBeEnabled();
    fireEvent.click(submit);

    await waitFor(() => expect(api.enableBondCalculationCutover).toHaveBeenCalledTimes(1));
    const [request] = api.enableBondCalculationCutover.mock.calls[0];
    expect(request).toMatchObject({
      expectedInventorySha256: inventory.inventorySha256,
      expectedPreviousReceiptSha256: null,
      explicitBlocks: [{ securityId: inventory.bonds[1].securityId, version: 7, securityContentSha256: "bbb222", reason: "Awaiting confirmation" }]
    });
    expect(await screen.findByLabelText("Bond cutover receipt")).toHaveTextContent(/reviewed-operator.*1 explicit block.*first cutover/);
    await screen.findByText("Cutover enabled");
    expect(screen.getByRole("button", { name: "Amend bond calculation cutover" })).toBeInTheDocument();
  });

  it("explains refusals without inventing a verdict", async () => {
    api.getBondCutoverReadiness.mockRejectedValueOnce(new ApiError({ path: "/api/reference-data/bonds/cutover-readiness", status: 403 }));
    render(<BondCutoverConsolePanel />);
    fireEvent.click(screen.getByRole("button", { name: "Load the complete active bond inventory" }));
    expect(await screen.findByText("Requires maintenance authority")).toBeInTheDocument();

    api.getBondCutoverReadiness.mockResolvedValueOnce({ ...inventory, bonds: [inventory.bonds[0]], requiresLegacyCalculations: false });
    api.enableBondCalculationCutover.mockRejectedValueOnce(new ApiError({ path: "/api/reference-data/bonds/cutover", status: 409, detail: "Active bond inventory changed; rebuild and review the cutover inventory." }));
    fireEvent.click(screen.getByRole("button", { name: "Load the complete active bond inventory" }));
    await screen.findByText("ACME 5 2030");
    fireEvent.click(screen.getByRole("checkbox"));
    fireEvent.click(screen.getByRole("button", { name: "Enable bond calculation cutover" }));
    expect(await screen.findByText("Inventory or receipt changed")).toBeInTheDocument();
    expect(screen.queryByLabelText("Bond cutover receipt")).not.toBeInTheDocument();
  });
});
