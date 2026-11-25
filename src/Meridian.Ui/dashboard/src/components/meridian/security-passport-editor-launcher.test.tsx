import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { defaultLoadPassport, SecurityPassportEditorLauncher } from "@/components/meridian/security-passport-editor-launcher";

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

  it("shows a conflict that already waits on a Use B draft after reload, and continues that draft", async () => {
    const user = userEvent.setup();
    const loadPassport = vi.fn().mockResolvedValue({
      version: 7,
      conflicts: [
        {
          conflictId: "k1",
          fieldLabel: "EconomicDefinition.Coupon",
          policyWinnerSource: "golden-copy",
          challengerSource: "vendor-b",
          severity: "High",
          pendingRevisionId: "rev-staged-1",
          pendingRevisionState: "Submitted"
        }
      ]
    });
    const approve = vi.fn().mockResolvedValue({ securityId: SECURITY_ID, revisionId: "rev-staged-1", newVersion: 2, state: "Approved", changeEntry: {} });

    render(<SecurityPassportEditorLauncher securityId={SECURITY_ID} symbol="ACME" loadPassport={loadPassport} service={{ approve }} />);

    expect(await screen.findByText("Awaiting draft rev-staged-1 — resolves when it is published")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^accept$/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /override/i })).toBeDisabled();

    await user.click(screen.getByRole("button", { name: "Continue draft" }));
    expect(screen.queryByRole("button", { name: "Continue draft" })).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /^approve$/i }));
    await waitFor(() => expect(approve).toHaveBeenCalledTimes(1));
    expect(approve.mock.calls[0][1]).toMatchObject({ revisionId: "rev-staged-1" });
  });

  describe("defaultLoadPassport", () => {
    afterEach(() => vi.unstubAllGlobals());

    const snapshot = {
      securityId: SECURITY_ID,
      retrievedAtUtc: "2026-11-25T00:00:00Z",
      economicDefinition: { version: 9 },
      conflictAssessments: [
        {
          conflict: { conflictId: "k1", securityId: SECURITY_ID, conflictKind: "FieldValue", fieldPath: "EconomicDefinition.Coupon",
            providerA: "golden-copy", valueA: "5.00", providerB: "vendor-b", valueB: "5.25", detectedAt: "2026-11-24T00:00:00Z", status: "Open" },
          currentWinningValue: "5.00", challengerValue: "5.25", currentWinningSource: "golden-copy", challengerSource: "vendor-b",
          recommendation: "PreserveWinner", recommendedResolution: "Keep", recommendedWinner: "golden-copy", impactSeverity: "High",
          impactSummary: "", impactDetail: "", isBulkEligible: false
        }
      ]
    };

    it("reads the version and conflicts from one snapshot, then this security's pending drafts", async () => {
      const fetch = vi
        .fn()
        .mockResolvedValueOnce(new Response(JSON.stringify(snapshot), { status: 200 }))
        .mockResolvedValueOnce(
          new Response(JSON.stringify([{ revisionId: "rev-staged-1", securityId: SECURITY_ID, state: "Draft", actor: "alice", createdAt: "",
            updatedAt: "", fieldPath: "EconomicDefinition.Coupon", fieldValue: "5.25", justification: null, fundProfileId: null, baseVersion: 9,
            ageHours: 1, conflictId: "k1", conflictCandidateSource: "vendor-b" }]), { status: 200 })
        );
      vi.stubGlobal("fetch", fetch);

      const passport = await defaultLoadPassport(SECURITY_ID);

      expect(passport.version).toBe(9);
      expect(passport.conflicts).toEqual([expect.objectContaining({ conflictId: "k1", pendingRevisionId: "rev-staged-1", pendingRevisionState: "Draft" })]);
      expect(passport.notice ?? null).toBeNull();
      expect(fetch).toHaveBeenCalledTimes(2);
      expect(String(fetch.mock.calls[1][0])).toBe(
        `/api/security-master/workbench/revisions?state=Draft%2CSubmitted%2CApproved&securityId=${SECURITY_ID}`
      );
    });

    it("still shows the conflicts, with a notice, when pending drafts cannot be loaded", async () => {
      const fetch = vi
        .fn()
        .mockResolvedValueOnce(new Response(JSON.stringify(snapshot), { status: 200 }))
        .mockResolvedValueOnce(new Response('{"detail":"store offline"}', { status: 503 }));
      vi.stubGlobal("fetch", fetch);

      const passport = await defaultLoadPassport(SECURITY_ID);

      expect(passport.conflicts).toHaveLength(1);
      expect(passport.conflicts[0].pendingRevisionId).toBeUndefined();
      expect(passport.notice).toMatch(/pending drafts could not be loaded/i);
    });

    it("skips the drafts request when there are no open conflicts", async () => {
      const fetch = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify({ ...snapshot, conflictAssessments: [] }), { status: 200 }));
      vi.stubGlobal("fetch", fetch);

      expect(await defaultLoadPassport(SECURITY_ID)).toEqual({ version: 9, conflicts: [] });
      expect(fetch).toHaveBeenCalledTimes(1);
    });
  });
});

