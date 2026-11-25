import { fireEvent, screen, waitFor } from "@testing-library/react";
import { vi } from "vitest";
import { renderWithRouter } from "@/test/render";
import { ProviderVerificationTimeline } from "./provider-verification-timeline";
import { getProviderVerificationHistory, type ProviderVerificationHistory } from "@/lib/api/provider-verification-history.api";
import type { ProviderRoutingConnection } from "@/types";
import { providerHistoryHref } from "@/lib/provider-history-link";

vi.mock("@/lib/api/provider-verification-history.api", () => ({ getProviderVerificationHistory: vi.fn() }));

it("discards delayed history after the selected connection changes", async () => {
  let completeFirst!: (value: ProviderVerificationHistory) => void;
  vi.mocked(getProviderVerificationHistory).mockImplementation((id) => id === "c1"
    ? new Promise((resolve) => { completeFirst = resolve; })
    : Promise.resolve({ connectionId: id, providerId: "alpaca", asOfUtc: "2026-11-11T00:00:00Z", isTruncated: false, observations: [] }));
  renderWithRouter(<ProviderVerificationTimeline connections={[
    { connectionId: "c1", displayName: "First" }, { connectionId: "c2", displayName: "Second" }
  ] as ProviderRoutingConnection[]} />);
  fireEvent.change(screen.getByRole("combobox", { name: "Verification history connection" }), { target: { value: "c2" } });
  await screen.findByText("No retained verification events exist for this exact connection and environment.");
  completeFirst({ connectionId: "c1", providerId: "alpaca", asOfUtc: "2026-11-11T00:00:00Z", isTruncated: false,
    observations: [{ recordedAtUtc: "2026-11-10T00:00:00Z", succeeded: true }] });
  await waitFor(() => expect(screen.queryByRole("img")).not.toBeInTheDocument());
});

it("opens a provider family's connection from a deep link and keeps the choice in the URL", async () => {
  vi.mocked(getProviderVerificationHistory).mockImplementation(async (id) => ({ connectionId: id, providerId: "x", asOfUtc: "2026-11-11T00:00:00Z", isTruncated: false, observations: [] }));
  renderWithRouter(<ProviderVerificationTimeline connections={[
    { connectionId: "c1", displayName: "Polygon feed", providerFamilyId: "polygon" },
    { connectionId: "c2", displayName: "Alpaca paper", providerFamilyId: "alpaca" }
  ] as ProviderRoutingConnection[]} />, { initialEntries: [providerHistoryHref("ALPACA")] });

  const select = screen.getByRole("combobox", { name: "Verification history connection" });
  // Matched on the exact family id (case-insensitive), not the first connection or a display name.
  expect(select).toHaveValue("c2");
  await waitFor(() => expect(getProviderVerificationHistory).toHaveBeenLastCalledWith("c2", expect.anything()));
  expect(document.getElementById("provider-verification-history")).not.toBeNull();

  fireEvent.change(select, { target: { value: "c1" } });
  expect(select).toHaveValue("c1");
  await waitFor(() => expect(getProviderVerificationHistory).toHaveBeenLastCalledWith("c1", expect.anything()));
});

it("falls back to the first connection when no loaded connection belongs to the linked family", () => {
  vi.mocked(getProviderVerificationHistory).mockReturnValue(new Promise(() => {}));
  renderWithRouter(<ProviderVerificationTimeline connections={[
    { connectionId: "c1", displayName: "Polygon feed", providerFamilyId: "polygon" }
  ] as ProviderRoutingConnection[]} />, { initialEntries: [providerHistoryHref("ibkr")] });
  expect(screen.getByRole("combobox", { name: "Verification history connection" })).toHaveValue("c1");
});
