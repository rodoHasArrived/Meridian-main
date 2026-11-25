import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api-errors";
import { renderWithRouter } from "@/test/render";
import { ProviderValidationPanel, profileLoosens, type ProviderValidationResponse } from "./data-screen.provider-validation";

const deploymentDefault = { staleToleranceDays: 10, futureSkewSeconds: 300, staleThresholdSeconds: null, maxLatencyMs: 0 };
const snapshot: ProviderValidationResponse = {
  provider: "polygon", profile: deploymentDefault, defaultProfile: deploymentDefault, audit: [],
  isOverride: false, canEdit: true, revision: "default", supportsStreamingRules: true,
  startupStreamingProfile: deploymentDefault, pendingRestart: false
};

describe("ProviderValidationPanel", () => {
  it("saves a tightening, requires a reason and confirmation for a relaxation, and renders server audit", async () => {
    const user = userEvent.setup();
    const fetchProfile = vi.fn().mockResolvedValue(snapshot);
    const tightened = { ...snapshot.profile, staleToleranceDays: 4 };
    const relaxed = { ...tightened, staleToleranceDays: 14 };
    const saveProfile = vi.fn()
      .mockResolvedValueOnce({ saved: true, snapshot: { ...snapshot, profile: tightened, isOverride: true, revision: "tightened" } })
      .mockResolvedValueOnce({ saved: true, snapshot: { ...snapshot, profile: relaxed, isOverride: true, audit: [{
        id: "change-1", changedAt: "2026-11-20T19:00:00Z", actor: "operator", reason: "Delayed close",
        before: tightened, after: relaxed, resetToDefault: false
      }] } });
    renderWithRouter(<ProviderValidationPanel providerId="polygon" providerName="Polygon.io" fetchProfile={fetchProfile} saveProfile={saveProfile} />);
    await user.click(await screen.findByRole("button", { name: "Edit profile" }));
    expect(fetchProfile).toHaveBeenCalledWith("polygon", expect.any(AbortSignal));
    const tolerance = screen.getByRole("spinbutton", { name: /staleness tolerance/i });
    await user.clear(tolerance);
    await user.type(tolerance, "4");
    await user.click(screen.getByRole("button", { name: "Save changes" }));
    expect(saveProfile).toHaveBeenCalledWith("polygon", expect.objectContaining({
      profile: tightened, expectedRevision: "default", confirmLoosening: false
    }), expect.any(AbortSignal));

    await user.click(await screen.findByRole("button", { name: "Edit profile" }));
    await user.clear(screen.getByRole("spinbutton", { name: /staleness tolerance/i }));
    await user.type(screen.getByRole("spinbutton", { name: /staleness tolerance/i }), "14");
    await user.click(screen.getByRole("button", { name: "Review relaxation" }));
    expect(screen.getByRole("alert")).toHaveTextContent("Enter a reason");
    expect(saveProfile).toHaveBeenCalledTimes(1);
    await user.type(screen.getByRole("textbox", { name: /change reason/i }), "Delayed close");
    await user.click(screen.getByRole("button", { name: "Review relaxation" }));
    const confirmation = screen.getByRole("dialog", { name: "Confirm relaxation" });
    expect(confirmation).toHaveTextContent(/historical backfills and gap repair that use Polygon.io/);
    expect(confirmation).toHaveTextContent(/including jobs waiting in the queue/);
    expect(saveProfile).toHaveBeenCalledTimes(1);
    await user.click(screen.getByRole("button", { name: "Confirm relaxation" }));
    expect(saveProfile).toHaveBeenLastCalledWith("polygon", expect.objectContaining({
      profile: relaxed, expectedRevision: "tightened", reason: "Delayed close", confirmLoosening: true
    }), expect.any(AbortSignal));
    const details = within(screen.getByRole("region", { name: "Last profile change" }));
    expect(await details.findByText("Delayed close")).toBeInTheDocument();
    expect(details.getByText("operator")).toBeInTheDocument();
    expect(details.getByText("Before").nextElementSibling).toHaveTextContent("4 calendar days");
    expect(details.getByText("After").nextElementSibling).toHaveTextContent("14 calendar days");
  });

  it("keeps a viewer read only and distinguishes saved streaming values from the active profile", async () => {
    const active = { ...deploymentDefault, futureSkewSeconds: 120 };
    renderWithRouter(<ProviderValidationPanel providerId="polygon" providerName="Polygon.io"
      fetchProfile={vi.fn().mockResolvedValue({
        ...snapshot, canEdit: false, pendingRestart: true, startupStreamingProfile: active,
        profile: { ...snapshot.profile, futureSkewSeconds: 60 }
      })} />);
    expect(await screen.findByText(/read only/i)).toBeInTheDocument();
    expect(screen.getByText("120 seconds")).toBeInTheDocument();
    expect(screen.getByText("60 seconds")).toBeInTheDocument();
    expect(screen.getByText("Live validation changes pending host restart")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Edit profile" })).not.toBeInTheDocument();
  });

  it("cancels a draft and leaves the verified profile visible when a reset is refused", async () => {
    const user = userEvent.setup();
    const saveProfile = vi.fn().mockRejectedValue(new ApiError({ path: "/validation-profile", status: 403 }));
    renderWithRouter(<ProviderValidationPanel providerId="polygon" providerName="Polygon.io"
      fetchProfile={vi.fn().mockResolvedValue({ ...snapshot, isOverride: true, profile: { ...snapshot.profile, staleToleranceDays: 14 } })}
      saveProfile={saveProfile} />);
    const reset = await screen.findByRole("button", { name: "Reset to deployment default" });
    await user.click(reset);
    await user.click(screen.getByRole("button", { name: "Cancel" }));
    expect(saveProfile).not.toHaveBeenCalled();
    await user.click(reset);
    await user.click(screen.getByRole("button", { name: "Reset profile" }));
    expect(saveProfile).toHaveBeenCalledWith("polygon", expect.objectContaining({ profile: null, resetToDefault: true }), expect.any(AbortSignal));
    expect(await screen.findByRole("alert")).toHaveTextContent("No change was applied");
    expect(screen.getByText("14 calendar days")).toBeInTheDocument();
  });

  it("aborts a pending profile read on unmount", () => {
    let signal: AbortSignal | undefined;
    const fetchProfile = vi.fn((_provider: string, requestSignal?: AbortSignal) => {
      signal = requestSignal;
      return new Promise<ProviderValidationResponse>(() => {});
    });
    const view = renderWithRouter(<ProviderValidationPanel providerId="polygon" providerName="Polygon.io" fetchProfile={fetchProfile} />);
    view.unmount();
    expect(signal?.aborted).toBe(true);
  });

  it("treats disabling an active warning or check as loosening", () => {
    expect(profileLoosens(deploymentDefault, { ...deploymentDefault, staleThresholdSeconds: 5 })).toBe(true);
    expect(profileLoosens(deploymentDefault, { ...deploymentDefault, maxLatencyMs: 5 })).toBe(true);
    expect(profileLoosens({ ...deploymentDefault, staleThresholdSeconds: 5 }, deploymentDefault)).toBe(false);
  });
});
