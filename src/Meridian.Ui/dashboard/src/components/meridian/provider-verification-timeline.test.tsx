import { fireEvent, screen, waitFor } from "@testing-library/react";
import { vi } from "vitest";
import { renderWithRouter } from "@/test/render";
import { ProviderVerificationTimeline } from "./provider-verification-timeline";
import { getProviderVerificationHistory, type ProviderVerificationHistory } from "@/lib/api/provider-verification-history.api";
import type { ProviderRoutingConnection } from "@/types";

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
