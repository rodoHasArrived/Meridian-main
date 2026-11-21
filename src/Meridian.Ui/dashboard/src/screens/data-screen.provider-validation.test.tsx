import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { ProviderValidationPanel, profileLoosens, type ProviderValidationResponse } from "./data-screen.provider-validation";
import { renderWithRouter } from "@/test/render";

const snapshot: ProviderValidationResponse = {
  provider: "polygon", impact: "RestartHost", audit: [],
  profile: { staleToleranceDays: 10, futureSkewSeconds: 300, staleThresholdSeconds: null, maxLatencyMs: 0 }
};

describe("ProviderValidationPanel", () => {
  it("loads on request and records a reason before loosening validation", async () => {
    const user = userEvent.setup();
    const fetchProfile = vi.fn().mockResolvedValue(snapshot);
    const saveProfile = vi.fn().mockImplementation(async (_provider, profile) => ({ saved: true, profile, impact: "RestartHost", pendingRestart: true,
      audit: { id: "change-1", changedAt: "2026-11-13T10:00:00Z", actor: "operator", reason: "Thinly traded symbols", loosened: true } }));
    const event = vi.fn(); window.addEventListener("meridian:configuration-saved", event);
    renderWithRouter(<ProviderValidationPanel fetchProfile={fetchProfile} saveProfile={saveProfile} />);
    expect(fetchProfile).not.toHaveBeenCalled();
    await user.click(screen.getByText("Provider validation profiles"));
    await user.click(screen.getByRole("button", { name: "Load validation profile" }));
    const tolerance = await screen.findByRole("spinbutton", { name: "Stale tolerance (calendar days)" });
    await user.clear(tolerance); await user.type(tolerance, "30");
    expect(screen.getByRole("button", { name: "Save validation profile" })).toBeDisabled();
    await user.type(screen.getByRole("textbox", { name: "Change reason (required)" }), "Thinly traded symbols");
    await user.click(screen.getByRole("button", { name: "Save validation profile" }));
    expect(await screen.findByRole("status")).toHaveTextContent("pending host restart");
    expect(saveProfile).toHaveBeenCalledWith("polygon", expect.objectContaining({ staleToleranceDays: 30 }), "Thinly traded symbols", expect.any(AbortSignal));
    expect(event).toHaveBeenCalledTimes(1);
    window.removeEventListener("meridian:configuration-saved", event);
  });

  it("surfaces save failures and keeps unsaved edits", async () => {
    const user = userEvent.setup();
    renderWithRouter(<ProviderValidationPanel fetchProfile={vi.fn().mockResolvedValue(snapshot)} saveProfile={vi.fn().mockRejectedValue(new Error("Permission denied"))} />);
    await user.click(screen.getByText("Provider validation profiles"));
    await user.click(screen.getByRole("button", { name: "Load validation profile" }));
    const skew = await screen.findByRole("spinbutton", { name: "Future skew (seconds)" });
    await user.clear(skew); await user.type(skew, "100");
    await user.click(screen.getByRole("button", { name: "Save validation profile" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Permission denied");
    expect(skew).toHaveValue(100);
  });

  it("aborts a pending profile read on unmount", async () => {
    const user = userEvent.setup(); let signal: AbortSignal | undefined;
    const fetchProfile = vi.fn((_provider: string, requestSignal?: AbortSignal) => {
      signal = requestSignal; return new Promise<ProviderValidationResponse>(() => {});
    });
    const view = renderWithRouter(<ProviderValidationPanel fetchProfile={fetchProfile} />);
    await user.click(screen.getByText("Provider validation profiles"));
    await user.click(screen.getByRole("button", { name: "Load validation profile" }));
    view.unmount(); expect(signal?.aborted).toBe(true);
  });

  it("treats disabling an active threshold as loosening", () => {
    expect(profileLoosens(snapshot.profile, { ...snapshot.profile, staleThresholdSeconds: 5 })).toBe(true);
    expect(profileLoosens(snapshot.profile, { ...snapshot.profile, maxLatencyMs: 5 })).toBe(true);
    expect(profileLoosens({ ...snapshot.profile, staleThresholdSeconds: 5 }, snapshot.profile)).toBe(false);
  });
});
