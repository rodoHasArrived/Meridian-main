import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { apiGetJson, apiPostJson, apiPutJson } from "@/lib/api";
import { BackfillSchedulesRegion } from "@/screens/data-screen.backfill-schedules";
import { renderWithRouter } from "@/test/render";

vi.mock("@/lib/api", () => ({ apiGetJson: vi.fn(), apiPostJson: vi.fn(), apiPutJson: vi.fn() }));

const schedule = { scheduleId: "nightly", name: "Nightly", enabled: true, cronExpression: "0 2 * * *", timeZoneId: "UTC", preferredProviders: ["tiingo"] };
const providers = [
  { providerId: "tiingo", displayName: "Tiingo", priority: 1, isSimulated: false },
  { providerId: "polygon", displayName: "Polygon", priority: 2, isSimulated: false },
  { providerId: "synthetic", displayName: "Simulated", priority: 3, isSimulated: true }
];

describe("Backfill schedules", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(apiGetJson).mockImplementation(async path => {
      if (path.endsWith("effective-order")) return { providers, fallbackEnabled: true, rotationEnabled: false };
      if (path.includes("history")) return { executions: [{
        executionId: "execution", status: "Completed", startedAt: "2026-11-19T02:00:00Z", scheduledAt: "2026-11-19T02:00:00Z",
        symbolResults: { AAPL: { symbol: "AAPL", servedBy: [{ providerId: "tiingo", bars: 5 }] } }
      }] };
      return { schedules: [schedule] };
    });
  });

  it("edits ordered preferences and displays retained schedule evidence", async () => {
    const user = userEvent.setup();
    vi.mocked(apiPutJson).mockResolvedValue({ ...schedule, preferredProviders: ["polygon", "tiingo"] });
    renderWithRouter(<BackfillSchedulesRegion />);
    expect(apiGetJson).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Manage schedules" }));
    await user.click(await screen.findByRole("button", { name: "Nightly" }));
    const add = screen.getByLabelText("Add a provider");
    expect(within(add).queryByRole("option", { name: "Simulated" })).not.toBeInTheDocument();
    await user.selectOptions(add, "polygon");
    await user.click(screen.getByRole("button", { name: "Move Polygon earlier" }));
    await user.click(screen.getByRole("button", { name: "Save preferences" }));
    expect(await screen.findByText("Saved. Provider preferences apply to the next job.")).toBeVisible();
    expect(apiPutJson).toHaveBeenCalledWith("/api/backfill/schedules/nightly/preferred-providers",
      { preferredProviders: ["polygon", "tiingo"] }, expect.objectContaining({ signal: expect.any(AbortSignal) }));
    await user.click(await screen.findByText(/UTC · Completed/));
    expect(screen.getByRole("row", { name: "AAPL tiingo 5" })).toBeVisible();
  });

  it("creates a schedule using global order by default", async () => {
    const user = userEvent.setup();
    vi.mocked(apiPostJson).mockResolvedValue({ ...schedule, scheduleId: "created", name: "EUR equities", preferredProviders: [] });
    renderWithRouter(<BackfillSchedulesRegion />);
    await user.click(screen.getByRole("button", { name: "Manage schedules" }));
    await screen.findByRole("button", { name: "Nightly" });
    await user.click(screen.getByRole("button", { name: "New gap-fill schedule" }));
    await user.type(screen.getByLabelText("Schedule name"), "EUR equities");
    await user.click(screen.getByRole("button", { name: "Create schedule" }));
    expect(await screen.findByText("Schedule created.")).toBeVisible();
    expect(apiPostJson).toHaveBeenCalledWith("/api/backfill/schedules",
      expect.objectContaining({ preferredProviders: [], name: "EUR equities", timeZoneId: "UTC" }),
      expect.objectContaining({ signal: expect.any(AbortSignal) }));
  });
});
