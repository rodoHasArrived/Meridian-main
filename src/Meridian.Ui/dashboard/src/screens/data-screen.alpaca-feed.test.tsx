import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AlpacaFeedRegion } from "./data-screen.alpaca-feed";

const api = vi.hoisted(() => ({ apiGetJson: vi.fn(), apiPutJson: vi.fn() }));
vi.mock("@/lib/api", () => api);

describe("Alpaca equity feed", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.apiGetJson.mockResolvedValue({ feed: "iex" });
  });

  it("sends a feed-only SIP update and reports pending restart", async () => {
    api.apiPutJson.mockResolvedValue({ requiresRestart: true });
    render(<AlpacaFeedRegion />);
    const feed = await screen.findByRole("combobox", { name: "Equity feed" });
    expect(feed).toHaveValue("iex");
    await userEvent.selectOptions(feed, "sip");
    await userEvent.click(screen.getByRole("button", { name: "Verify SIP and save" }));
    await screen.findByText("Feed saved. Restart the host to activate it.");
    expect(api.apiPutJson).toHaveBeenCalledWith("/api/config/alpaca/feed", { feed: "sip" },
      expect.objectContaining({ allowDevelopmentFallback: false }));
  });

  it("keeps the draft available after refusal", async () => {
    api.apiPutJson.mockRejectedValue(new Error("Alpaca refused SIP access."));
    render(<AlpacaFeedRegion />);
    const feed = await screen.findByRole("combobox", { name: "Equity feed" });
    await userEvent.selectOptions(feed, "sip");
    await userEvent.click(screen.getByRole("button", { name: "Verify SIP and save" }));
    await screen.findByText("Feed change refused");
    expect(feed).toHaveValue("sip");
    expect(screen.getByRole("button", { name: "Verify SIP and save" })).toBeEnabled();
  });
});
