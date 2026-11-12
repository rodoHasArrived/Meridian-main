import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { axe } from "jest-axe";
import { ProviderIdentityEvidence } from "./provider-identity-evidence";
import type { ProviderIntegrationStagingIdentityResolutionPreview } from "@/types";

const resolve = vi.hoisted(() => vi.fn());
vi.mock("@/lib/api", () => ({ resolveProviderIntegrationIdentity: resolve }));
const first = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const second = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
const preview: ProviderIntegrationStagingIdentityResolutionPreview = {
  connectionId: "connection", syncRunIds: ["run"], totalRows: 1,
  accountReviewRequiredCount: 0, missingAccountIdentifierCount: 0,
  securityResolvedCount: 0, securityReviewRequiredCount: 1, missingSecurityIdentifierCount: 0,
  rows: [{ stagingRecordId: "row", syncRunId: "run", capability: "Holdings", accountStatus: "Resolved",
    securityStatus: "Conflict", hasIdentifierConflict: true, evidenceHash: "matching-evidence-hash", issues: [],
    securityCandidates: [
      { identifierKind: "Cusip", identifierValue: "123456789", priority: 1, status: "Resolved", internalSecurityId: first, displayName: "Bond A" },
      { identifierKind: "Isin", identifierValue: "US1234567890", priority: 2, status: "Resolved", internalSecurityId: second, displayName: "Bond B" }
    ] }]
};

describe("provider identifier agreement", () => {
  it("shows every identifier and retains the explicit reviewed choice with its matching hash", async () => {
    resolve.mockResolvedValue({ decisionId: "decision" });
    const onResolved = vi.fn();
    const { container } = render(<ProviderIdentityEvidence preview={preview} onResolved={onResolved} />);
    expect(screen.getByRole("table")).toHaveTextContent("123456789");
    expect(screen.getByRole("table")).toHaveTextContent("US1234567890");
    expect(screen.getByRole("table")).toHaveTextContent("Bond A");
    expect(screen.getByRole("table")).toHaveTextContent("Bond B");
    const button = screen.getByRole("button", { name: "Resolve source conflict" });
    expect(button).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Chosen security"), { target: { value: second } });
    fireEvent.change(screen.getByLabelText("Resolution reason"), { target: { value: "Verified original source" } });
    expect((await axe(container)).violations).toEqual([]);
    fireEvent.click(button);
    await waitFor(() => expect(resolve).toHaveBeenCalledWith("connection", {
      syncRunId: "run", stagingRecordId: "row", selectedSecurityId: second,
      evidenceHash: "matching-evidence-hash", reason: "Verified original source"
    }, expect.objectContaining({ signal: expect.any(AbortSignal) })));
    await waitFor(() => expect(onResolved).toHaveBeenCalledOnce());
  });

  it("retains the context and reason after a failed save", async () => {
    resolve.mockRejectedValue(new Error("Matching evidence changed"));
    const onResolved = vi.fn();
    render(<ProviderIdentityEvidence preview={preview} onResolved={onResolved} />);
    fireEvent.change(screen.getByLabelText("Chosen security"), { target: { value: second } });
    fireEvent.change(screen.getByLabelText("Resolution reason"), { target: { value: "Reviewed" } });
    fireEvent.click(screen.getByRole("button", { name: "Resolve source conflict" }));
    await waitFor(() => expect(screen.getByRole("status")).toHaveTextContent("Matching evidence changed"));
    expect(screen.getByLabelText("Resolution reason")).toHaveValue("Reviewed");
    expect(onResolved).not.toHaveBeenCalled();
  });
});
