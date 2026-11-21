import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { ProviderQuotaEditor, type ProviderQuotaResponse } from "./data-screen.provider-quota-editor";

const basic: ProviderQuotaResponse = {
  providerId: "twelvedata",
  displayName: "Twelve Data",
  unit: "credits",
  dailyResetPolicy: "utc-midnight",
  canEdit: true,
  policy: "account-quota",
  saved: { tierId: "free", unitsPerMinute: 8, dailyLimit: 800, requestsPerMinute: 8 },
  running: { maximumPerWindow: 8, windowSeconds: 60, dailyLimit: 800, minimumDelaySeconds: 7.5 },
  pendingRestart: false,
  tiers: [
    { id: "free", label: "Basic", minuteCeiling: 8, dailyCeiling: 800, defaultDailyLimit: 800 },
    { id: "grow", label: "Grow", minuteCeiling: 55, dailyCeiling: 79200, defaultDailyLimit: null }
  ]
};

describe("ProviderQuotaEditor", () => {
  it("requires a finite paid cap and keeps the running limits visible after save", async () => {
    const user = userEvent.setup();
    const services = {
      read: vi.fn().mockResolvedValue(basic),
      save: vi.fn().mockResolvedValue({
        ...basic, pendingRestart: true,
        saved: { tierId: "grow", unitsPerMinute: 55, dailyLimit: 3500, requestsPerMinute: 30 }
      })
    };
    render(<ProviderQuotaEditor services={services} />);
    await user.click(screen.getByText("Quota settings"));
    await user.click(screen.getByRole("button", { name: "Load limits" }));
    await user.selectOptions(await screen.findByLabelText("Vendor tier"), "grow");
    const daily = screen.getByLabelText("Daily limit (credits)");
    expect(daily).toHaveValue(null);
    expect(daily).toBeRequired();
    await user.click(screen.getByRole("button", { name: "Save limits" }));
    expect(services.save).not.toHaveBeenCalled();
    await user.type(daily, "3500");
    const pace = screen.getByLabelText("Request pacing (requests/minute)");
    await user.clear(pace);
    await user.type(pace, "30");
    await user.click(screen.getByRole("button", { name: "Save limits" }));
    expect(services.save).toHaveBeenCalledWith("twelvedata",
      { tierId: "grow", unitsPerMinute: 55, dailyLimit: 3500, requestsPerMinute: 30 }, expect.any(AbortSignal));
    expect(await screen.findByText("Saved changes take effect after host restart.")).toBeVisible();
    expect(screen.getByText("8 credits per 60s · 800 credits/day")).toBeVisible();
    expect(screen.getByText("55 credits/minute · 3500 credits/day")).toBeVisible();
  });

  it("shows an adapter-fixed limit without edition controls", async () => {
    const user = userEvent.setup();
    render(<ProviderQuotaEditor services={{
      read: vi.fn().mockResolvedValue({
        ...basic, providerId: "fred", displayName: "FRED", policy: "adapter-fixed", saved: null, canEdit: false,
        unit: "requests", dailyResetPolicy: null, tiers: [],
        running: { maximumPerWindow: 120, windowSeconds: 60, dailyLimit: null, minimumDelaySeconds: 0.5 }
      }),
      save: vi.fn()
    }} />);
    await user.click(screen.getByText("Quota settings"));
    await user.selectOptions(screen.getByLabelText("Quota provider"), "fred");
    await user.click(screen.getByRole("button", { name: "Load limits" }));
    expect(await screen.findByText("The adapter controls these limits.")).toBeVisible();
    expect(screen.getByText("120 requests per 60s · No daily cap reported")).toBeVisible();
    expect(screen.queryByRole("button", { name: "Save limits" })).toBeNull();
  });
});
