import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { RateLimitHistoryPanel, type RateLimitHistoryResponse } from "./data-screen.rate-limit-history";
import { renderWithRouter } from "@/test/render";

const snapshot: RateLimitHistoryResponse = {
  provider: "polygon", periodHours: 24, timestamp: "2026-11-13T10:00:00Z", isAvailable: true,
  message: "Process-lifetime history; cleared on host restart.", history: [{
    timestamp: "2026-11-13T02:14:00Z", requestsUsed: 5, usageRatio: 1, wasRateLimited: true,
    resetAt: "2026-11-13T02:15:00Z", eventType: "rotated", nextProvider: "stooq", httpStatus: 429
  }]
};
describe("RateLimitHistoryPanel", () => {
  it("loads retained throttling and actual rotation evidence only on request", async () => {
    const user = userEvent.setup(); const fetchHistory = vi.fn().mockResolvedValue(snapshot);
    renderWithRouter(<RateLimitHistoryPanel fetchHistory={fetchHistory} />);
    expect(fetchHistory).not.toHaveBeenCalled();
    await user.click(screen.getByText("Rate-limit and quota history"));
    await user.click(screen.getByRole("button", { name: "Load quota history" }));
    expect(await screen.findByRole("cell", { name: "Rotated (HTTP 429)" })).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "stooq" })).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "100%" })).toBeInTheDocument();
    expect(fetchHistory).toHaveBeenCalledWith("polygon", 24, expect.any(AbortSignal));
  });

  it("distinguishes empty retained history from unavailable history", async () => {
    const user = userEvent.setup();
    const fetchHistory = vi.fn().mockResolvedValueOnce({ ...snapshot, history: [] }).mockResolvedValueOnce({ ...snapshot, isAvailable: false, history: [] });
    renderWithRouter(<RateLimitHistoryPanel fetchHistory={fetchHistory} />);
    await user.click(screen.getByText("Rate-limit and quota history"));
    await user.click(screen.getByRole("button", { name: "Load quota history" }));
    expect(await screen.findByRole("status")).toHaveTextContent("No retained rate-limit events");
    await user.click(screen.getByRole("button", { name: "Load quota history" }));
    expect(await screen.findByRole("status")).toHaveTextContent("Quota history is unavailable");
  });

  it("reports a failed history read without pretending there were no events", async () => {
    const user = userEvent.setup();
    renderWithRouter(<RateLimitHistoryPanel fetchHistory={vi.fn().mockRejectedValue(new Error("Service unavailable"))} />);
    await user.click(screen.getByText("Rate-limit and quota history"));
    await user.click(screen.getByRole("button", { name: "Load quota history" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Service unavailable");
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });
});
