import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { SecurityPassportEditorLauncher } from "@/components/meridian/security-passport-editor-launcher";
import { createSecurityImpactPreview, getSecurityImpactPreview, type SecurityImpactPreviewReceipt } from "@/lib/api/security-impact-preview.api";

vi.mock("@/lib/api/security-impact-preview.api", () => ({ createSecurityImpactPreview: vi.fn(), getSecurityImpactPreview: vi.fn() }));

const SECURITY_ID = "11111111-1111-1111-1111-111111111111";

describe("SecurityPassportEditorLauncher", () => {
  it("shows a loading state, then mounts the editor with the fetched version", async () => {
    let resolve: (v: number) => void = () => {};
    const loadVersion = vi.fn().mockReturnValue(new Promise<number>((r) => { resolve = r; }));

    render(<SecurityPassportEditorLauncher securityId={SECURITY_ID} symbol="ACME" assetClass="Equity" loadVersion={loadVersion} />);

    expect(screen.getByRole("status")).toHaveTextContent(/loading passport for acme/i);
    resolve(7);

    await waitFor(() => expect(screen.getByTestId("security-passport-editor")).toBeInTheDocument());
    expect(loadVersion).toHaveBeenCalledWith(SECURITY_ID);
    // The version chip reflects the fetched concurrency token.
    expect(screen.getByText("v7")).toBeInTheDocument();
  });

  it("surfaces a load error without mounting the editor", async () => {
    const loadVersion = vi.fn().mockRejectedValue(new Error("snapshot unavailable"));

    render(<SecurityPassportEditorLauncher securityId={SECURITY_ID} symbol="ACME" loadVersion={loadVersion} />);

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(/snapshot unavailable/i);
    expect(screen.queryByTestId("security-passport-editor")).not.toBeInTheDocument();
  });

  it("lets the default unscoped launcher choose a fund, freezes it, and approves only its inspected receipt", async () => {
    const user = userEvent.setup();
    const hash = "a".repeat(64);
    const result = (state: "Draft" | "Submitted" | "Approved") => ({ securityId: SECURITY_ID, revisionId: "revision-1", newVersion: 7, state, changeEntry: {} });
    const updateField = vi.fn().mockResolvedValue(result("Draft"));
    const submit = vi.fn().mockResolvedValue(result("Submitted"));
    const approve = vi.fn().mockResolvedValue(result("Approved"));
    const receipt: SecurityImpactPreviewReceipt = {
      contentSha256: hash, dependencyFingerprint: "b".repeat(64), securityId: SECURITY_ID, revisionId: "revision-1", canonicalVersion: 7,
      fundProfileId: "reviewed-fund", asOf: "2026-11-14T00:00:00Z", capturedAt: "2026-11-14T00:00:00Z",
      books: { completeness: "Partial", bookIds: [], basis: "Conservative reviewed fund books", issues: [] }, periods: [],
      reports: { completeness: "Unavailable", runIds: [], issues: [] }, before: null, after: null, issues: []
    };
    vi.mocked(createSecurityImpactPreview).mockResolvedValue(receipt);
    vi.mocked(getSecurityImpactPreview).mockRejectedValueOnce(new Error("retained evidence unavailable"));
    render(<SecurityPassportEditorLauncher securityId={SECURITY_ID} symbol="ACME" loadVersion={async () => 7}
      service={{ updateField, submit, approve }} />);

    await user.type(await screen.findByLabelText("Field path"), "assetSpecificTerms.rateUnit");
    await user.type(screen.getByLabelText("New value"), "Percent");
    await user.type(screen.getByLabelText("Justification (required)"), "Approved vendor field definition");
    expect(screen.getByRole("button", { name: /save draft/i })).toBeDisabled();
    const fund = screen.getByLabelText("Fund profile id (required)");
    await user.type(fund, "reviewed-fund");
    await user.click(screen.getByRole("button", { name: /save draft/i }));
    await waitFor(() => expect(updateField).toHaveBeenCalledWith(SECURITY_ID, expect.objectContaining({ fundProfileId: "reviewed-fund" })));
    expect(fund).toBeDisabled();
    expect(fund).toHaveValue("reviewed-fund");
    await user.click(screen.getByRole("button", { name: /submit for approval/i }));
    await waitFor(() => expect(submit).toHaveBeenCalledWith(SECURITY_ID, expect.objectContaining({ fundProfileId: "reviewed-fund" })));
    expect(screen.getByRole("button", { name: /^approve/i })).toBeDisabled();
    await user.click(screen.getByRole("button", { name: "Refresh impact preview" }));
    await screen.findByRole("alert");
    expect(createSecurityImpactPreview).toHaveBeenCalledWith(SECURITY_ID, "revision-1", expect.any(String), "reviewed-fund");
    expect(screen.getByRole("button", { name: /^approve/i })).toBeDisabled();

    // A distinct refreshed immutable receipt must be loaded successfully before approval.
    const refreshed = { ...receipt, contentSha256: "c".repeat(64) };
    vi.mocked(createSecurityImpactPreview).mockResolvedValue(refreshed);
    vi.mocked(getSecurityImpactPreview).mockResolvedValue(refreshed);
    await user.click(screen.getByRole("button", { name: "Refresh impact preview" }));
    await screen.findByText("Retained fund profile: reviewed-fund.");
    await waitFor(() => expect(screen.getByRole("button", { name: /^approve/i })).toBeEnabled());
    await user.click(screen.getByRole("button", { name: /^approve/i }));
    await waitFor(() => expect(approve).toHaveBeenCalledWith(SECURITY_ID, expect.objectContaining({ impactReceiptId: refreshed.contentSha256 })));
    expect(fund).toBeDisabled();
    expect(screen.getByRole("button", { name: "Refresh impact preview" })).toBeDisabled();
  });
});
