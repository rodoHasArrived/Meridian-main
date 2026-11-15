import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SettingsAssetProfilePromotion } from "./settings-screen.asset-profile-promotion";
import { downloadProfileCandidate, getProfilePromotionAssessments, type ProfilePromotionAssessments } from "@/lib/api/security-asset-profile-promotion.api";

vi.mock("@/lib/api/security-asset-profile-promotion.api", () => ({
  getProfilePromotionAssessments: vi.fn(), downloadProfileCandidate: vi.fn()
}));

const assessment: ProfilePromotionAssessments = {
  scope: "Current reference catalog; records, not holdings.", observedAtUtc: "2026-11-14T12:00:00Z", canExport: true,
  profiles: [{ profileId: "royalty", version: 2, name: "Royalty", profileFingerprint: "approved-hash",
    usage: { profileId: "royalty", version: 2, activeRecordCount: 37, inactiveRecordCount: 6, unknownStatusRecordCount: 0, unresolvedReason: null,
      completeActiveRecordCount: 30, incompleteActiveRecordCount: 6, unknownActiveRecordCount: 1, missingRequiredFieldCounts: { royaltyRate: 6, notional: 2 } }, definitionAssessment: null }],
  unresolvedUsage: [{ profileId: "royalty", version: 1, activeRecordCount: 3, inactiveRecordCount: 0, unknownStatusRecordCount: 0, unresolvedReason: "Profile version is not currently approved" }]
};

beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(getProfilePromotionAssessments).mockResolvedValue(assessment);
  vi.mocked(downloadProfileCandidate).mockResolvedValue();
});

describe("profile usage and developer export", () => {
  it("shows exact version usage and explicitly exports only the chosen approved fingerprint", async () => {
    const user = userEvent.setup();
    render(<SettingsAssetProfilePromotion />);
    expect(await screen.findByText("Royalty v2")).toBeInTheDocument();
    expect(screen.getByRole("table")).toHaveTextContent("37");
    expect(screen.getByText(/records, not holdings/)).toBeInTheDocument();
    expect(screen.getByText("Complete 30 · Incomplete 6 · Unknown 1")).toBeInTheDocument();
    await user.click(screen.getByText("Missing fields · assessed active records"));
    expect(screen.getByText("royaltyRate: 6")).toBeInTheDocument();
    expect(screen.getByText(/presence does not establish type or economic validity/)).toBeInTheDocument();
    await user.click(screen.getByText("Unresolved profile references (3 records)"));
    expect(screen.getByText(/royalty · v1/)).toHaveTextContent("not currently approved");
    await user.click(screen.getByRole("button", { name: "Download developer draft for Royalty v2" }));
    await waitFor(() => expect(downloadProfileCandidate).toHaveBeenCalledWith(assessment.profiles[0]));
    expect(await screen.findByRole("status")).toHaveTextContent("No class was registered");
  });

  it("retains read access while export is disabled for a viewer", async () => {
    vi.mocked(getProfilePromotionAssessments).mockResolvedValue({ ...assessment, canExport: false });
    render(<SettingsAssetProfilePromotion />);
    expect(await screen.findByRole("button", { name: /Download developer draft for/ })).toBeDisabled();
    expect(screen.getByText(/Modify Security Master permission is required/)).toBeInTheDocument();
  });

  it("surfaces stale export refusal and refreshes the authoritative assessment", async () => {
    const user = userEvent.setup();
    vi.mocked(downloadProfileCandidate).mockRejectedValue(new Error("The profile changed. Refresh the assessment."));
    render(<SettingsAssetProfilePromotion />);
    await user.click(await screen.findByRole("button", { name: /Download developer draft for/ }));
    expect(await screen.findByRole("alert")).toHaveTextContent("profile changed");
    await user.click(screen.getByRole("button", { name: "Refresh usage" }));
    await waitFor(() => expect(getProfilePromotionAssessments).toHaveBeenCalledTimes(2));
  });
});
