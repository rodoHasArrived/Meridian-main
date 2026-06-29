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

  it("accepts a source conflict with the policy winner and no deviation acknowledgement", async () => {
    const user = userEvent.setup();
    const resolveConflict = vi.fn().mockResolvedValue({
      conflictId: "k1",
      policyWinnerSource: "golden-copy",
      chosenWinnerSource: "golden-copy",
      isPolicyDeviation: false,
      reason: "Accepted the recommended policy winner.",
      newVersion: 8
    });
    render(
      <SecurityPassportEditor
        securityId={SECURITY_ID}
        symbol="ACME"
        version={7}
        service={{ resolveConflict }}
        conflicts={[{ conflictId: "k1", fieldLabel: "Coupon", policyWinnerSource: "golden-copy", challengerSource: "vendor-a", severity: "High" }]}
      />
    );

    await user.click(screen.getByRole("button", { name: /^accept$/i }));

    await waitFor(() => expect(resolveConflict).toHaveBeenCalledTimes(1));
    const [, input] = resolveConflict.mock.calls[0];
    expect(input).toMatchObject({ conflictId: "k1", chosenWinnerSource: "golden-copy", acknowledgePolicyDeviation: false, expectedVersion: 7 });
  });

  it("requires acknowledgement before an override deviation can resolve", async () => {
    const user = userEvent.setup();
    const resolveConflict = vi.fn().mockResolvedValue({
      conflictId: "k1",
      policyWinnerSource: "golden-copy",
      chosenWinnerSource: "vendor-a",
      isPolicyDeviation: true,
      reason: "Vendor is correct.",
      newVersion: 8
    });
    render(
      <SecurityPassportEditor
        securityId={SECURITY_ID}
        symbol="ACME"
        version={7}
        service={{ resolveConflict }}
        conflicts={[{ conflictId: "k1", fieldLabel: "Coupon", policyWinnerSource: "golden-copy", challengerSource: "vendor-a", severity: "High" }]}
      />
    );

    await user.click(screen.getByRole("button", { name: /override/i }));
    await user.type(screen.getByLabelText(/reason \(required\)/i), "Vendor is correct.");

    // Deviation (challenger != policy winner) without acknowledgement keeps Resolve disabled.
    expect(screen.getByRole("button", { name: /^resolve$/i })).toBeDisabled();

    await user.click(screen.getByRole("checkbox", { name: /acknowledge deviation/i }));
    expect(screen.getByRole("button", { name: /^resolve$/i })).toBeEnabled();

    await user.click(screen.getByRole("button", { name: /^resolve$/i }));
    await waitFor(() => expect(resolveConflict).toHaveBeenCalledTimes(1));
    expect(resolveConflict.mock.calls[0][1]).toMatchObject({ chosenWinnerSource: "vendor-a", acknowledgePolicyDeviation: true });
  });
});
