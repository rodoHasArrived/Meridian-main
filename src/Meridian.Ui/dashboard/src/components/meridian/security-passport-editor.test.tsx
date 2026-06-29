import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { SecurityPassportEditor, type SecurityPassportWorkbenchService } from "@/components/meridian/security-passport-editor";
import { ApiError } from "@/lib/api-errors";
import type { SecurityMasterEditResult } from "@/lib/api/security-master-workbench.api";

const SECURITY_ID = "11111111-1111-1111-1111-111111111111";

function editResult(overrides: Partial<SecurityMasterEditResult> = {}): SecurityMasterEditResult {
  return {
    securityId: SECURITY_ID,
    revisionId: "rev-1",
    newVersion: 8,
    state: "Draft",
    changeEntry: {
      changeId: "c1",
      streamVersion: 8,
      eventType: "OperatorFieldAnnotation",
      changedAtUtc: "2026-03-15T00:00:00Z",
      effectiveAtUtc: "2026-03-15T00:00:00Z",
      actor: "session.actor",
      origin: "Operator",
      sourceSystem: "operator",
      sourceRecordId: null,
      reason: "x",
      summary: "x",
      changedFields: ["EconomicDefinition.Coupon"],
      changedFieldsSummary: "EconomicDefinition.Coupon"
    },
    ...overrides
  };
}

function renderEditor(service: Partial<SecurityPassportWorkbenchService>) {
  return render(
    <SecurityPassportEditor securityId={SECURITY_ID} symbol="ACME" assetClass="Equity" version={7} trustPosture="Review" service={service} />
  );
}

describe("SecurityPassportEditor", () => {
  it("saves a draft through the workbench client with the loaded version and advances lifecycle state", async () => {
    const user = userEvent.setup();
    const updateField = vi.fn().mockResolvedValue(editResult());
    renderEditor({ updateField });

    await user.type(screen.getByPlaceholderText("EconomicDefinition.Coupon"), "EconomicDefinition.Coupon");
    await user.type(screen.getByPlaceholderText("Vendor confirmation #…"), "Vendor confirmation 4821");
    await user.click(screen.getByRole("button", { name: /save draft/i }));

    await waitFor(() => expect(updateField).toHaveBeenCalledTimes(1));
    const [calledSecurityId, input] = updateField.mock.calls[0];
    expect(calledSecurityId).toBe(SECURITY_ID);
    expect(input).toMatchObject({ expectedVersion: 7, fieldPath: "EconomicDefinition.Coupon", justification: "Vendor confirmation 4821" });

    // Draft now exists → Submit becomes enabled.
    await waitFor(() => expect(screen.getByRole("button", { name: /submit for approval/i })).toBeEnabled());
  });

  it("shows a non-destructive reload banner on a version conflict", async () => {
    const user = userEvent.setup();
    const updateField = vi.fn().mockRejectedValue(
      new ApiError({ path: "/x", status: 409, responseBody: JSON.stringify({ error: "version-conflict", currentVersion: 9 }) })
    );
    const onReloadRequested = vi.fn();
    render(
      <SecurityPassportEditor
        securityId={SECURITY_ID}
        symbol="ACME"
        version={7}
        service={{ updateField }}
        onReloadRequested={onReloadRequested}
      />
    );

    await user.type(screen.getByPlaceholderText("EconomicDefinition.Coupon"), "EconomicDefinition.Coupon");
    await user.type(screen.getByPlaceholderText("Vendor confirmation #…"), "reason");
    await user.click(screen.getByRole("button", { name: /save draft/i }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(/version 9/i);
    await user.click(screen.getByRole("button", { name: /reload passport/i }));
    expect(onReloadRequested).toHaveBeenCalledWith(9);
  });

  it("keeps publish disabled until the revision is approved", () => {
    renderEditor({});
    expect(screen.getByRole("button", { name: /publish/i })).toBeDisabled();
  });
});
