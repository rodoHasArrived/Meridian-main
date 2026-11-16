import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { BackfillProviderEditorRegion } from "@/screens/data-screen.backfill-provider-editor";
import { renderWithRouter } from "@/test/render";
import { apiGetJson, apiPutJson } from "@/lib/api";
import { useState, type ReactNode } from "react";
import { GlobalRefreshProvider } from "@/lib/global-refresh";

function HeaderRefresh({ children }: { children: ReactNode }) {
  const [sequence, setSequence] = useState(0);
  return <GlobalRefreshProvider sequence={sequence}><button type="button" onClick={() => setSequence(value => value + 1)}>Header refresh</button>{children}</GlobalRefreshProvider>;
}

vi.mock("@/lib/api", () => ({ apiGetJson: vi.fn(), apiPutJson: vi.fn() }));
const snapshot = [{ metadata: { providerId: "polygon", displayName: "Polygon", tiers: [
  { id: "free", label: "Basic", rateLimitPerMinute: 5, rateLimitPerHour: null },
  { id: "starter", label: "Starter", rateLimitPerMinute: 100, rateLimitPerHour: null }
] }, options: { enabled: true, priority: 12, tierId: "free", rateLimitPerMinute: 5, rateLimitPerHour: null } }];

describe("Backfill provider editor", () => {
  beforeEach(() => { vi.clearAllMocks(); vi.mocked(apiGetJson).mockResolvedValue(structuredClone(snapshot)); });

  it("loads authoritative configuration only after opening and saves a tier preset", async () => {
    const user = userEvent.setup();
    vi.mocked(apiPutJson).mockResolvedValue({ changeImpact: { impact: "ReconnectProvider", requiresReconnect: true } });
    renderWithRouter(<BackfillProviderEditorRegion />);
    expect(apiGetJson).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Edit provider priorities and limits" }));
    await user.click(await screen.findByRole("button", { name: "Polygon" }));
    expect(apiGetJson).toHaveBeenCalledWith("/api/backfill/providers/statuses", expect.objectContaining({ allowDevelopmentFallback: false }));
    await user.selectOptions(screen.getByLabelText("Vendor tier"), "starter");
    expect(screen.getByLabelText("Requests per minute (ceiling 100)")).toHaveValue(100);
    await user.click(screen.getByRole("button", { name: "Save provider" }));
    expect(apiPutJson).toHaveBeenCalledWith("/api/backfill/providers/polygon", expect.objectContaining({ tierId: "starter", rateLimitPerMinute: 100 }), expect.objectContaining({ signal: expect.any(AbortSignal) }));
    expect(await screen.findByText(/Saved. Pending provider reconnect/)).toBeVisible();
  });

  it("keeps an unsaved draft on reselect and refuses an excessive limit", async () => {
    const user = userEvent.setup();
    renderWithRouter(<BackfillProviderEditorRegion />);
    await user.click(screen.getByRole("button", { name: "Edit provider priorities and limits" }));
    await user.click(await screen.findByRole("button", { name: "Polygon" }));
    const limit = screen.getByLabelText("Requests per minute (ceiling 5)");
    await user.clear(limit); await user.type(limit, "6");
    await user.click(screen.getByRole("button", { name: "Polygon" }));
    expect(limit).toHaveValue(6);
    expect(screen.getByRole("button", { name: "Save provider" })).toBeDisabled();
    expect(apiPutJson).not.toHaveBeenCalled();
  });

  it("shows a save failure and preserves the draft for retry", async () => {
    const user = userEvent.setup();
    vi.mocked(apiPutJson).mockRejectedValue(new Error("Host configuration validation refused the save."));
    renderWithRouter(<BackfillProviderEditorRegion />);
    await user.click(screen.getByRole("button", { name: "Edit provider priorities and limits" }));
    await user.click(await screen.findByRole("button", { name: "Polygon" }));
    await user.selectOptions(screen.getByLabelText("Vendor tier"), "starter");
    await user.click(screen.getByRole("button", { name: "Save provider" }));
    expect(await screen.findByText("Host configuration validation refused the save.")).toBeVisible();
    await waitFor(() => expect(screen.getByRole("button", { name: "Save provider" })).toBeEnabled());
    expect(screen.getByLabelText("Vendor tier")).toHaveValue("starter");
  });

  it("reloads with the header refresh, never over an unsaved draft, and offers Retry only after a failed load", async () => {
    const user = userEvent.setup();
    renderWithRouter(<HeaderRefresh><BackfillProviderEditorRegion /></HeaderRefresh>);
    await user.click(screen.getByRole("button", { name: "Edit provider priorities and limits" }));
    await user.click(await screen.findByRole("button", { name: "Polygon" }));
    expect(screen.queryByRole("button", { name: /Refresh/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Retry loading provider settings" })).not.toBeInTheDocument();
    expect(apiGetJson).toHaveBeenCalledTimes(1);
    await user.click(screen.getByRole("button", { name: "Header refresh" }));
    await waitFor(() => expect(apiGetJson).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.queryByText("Loading provider configuration…")).not.toBeInTheDocument());

    await user.selectOptions(screen.getByLabelText("Vendor tier"), "starter");
    await user.click(screen.getByRole("button", { name: "Header refresh" }));
    expect(apiGetJson).toHaveBeenCalledTimes(2);
    expect(screen.getByLabelText("Vendor tier")).toHaveValue("starter");

    await user.click(screen.getByRole("button", { name: "Discard changes" }));
    vi.mocked(apiGetJson).mockRejectedValueOnce(new Error("Provider host offline."));
    await user.click(screen.getByRole("button", { name: "Header refresh" }));
    expect(await screen.findByText("Provider host offline.")).toBeVisible();
    await user.click(screen.getByRole("button", { name: "Retry loading provider settings" }));
    await waitFor(() => expect(apiGetJson).toHaveBeenCalledTimes(4));
    await waitFor(() => expect(screen.queryByRole("button", { name: "Retry loading provider settings" })).not.toBeInTheDocument());
  });
});
