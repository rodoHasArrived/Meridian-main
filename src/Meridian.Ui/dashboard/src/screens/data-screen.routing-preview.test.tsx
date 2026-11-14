import { describe, expect, it, vi, beforeEach } from "vitest";
import { screen, waitFor, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { renderWithRouter } from "@/test/render";
import { DataRoutingPreviewPanel } from "./data-screen.routing-preview";
import { apiPostJson } from "@/lib/api";

vi.mock("@/lib/api", () => ({ apiPostJson: vi.fn() }));
const post = vi.mocked(apiPostJson);
const result = { selectedProviderFamilyId: "synthetic", isProposed: true, candidates: [], warnings: ["FABRICATED DATA WOULD WIN: Synthetic would serve this query."] };

beforeEach(() => vi.clearAllMocks());

describe("historical routing preview", () => {
  it("is lazy, sends only proposed operational fields and shows fabricated-data warning", async () => {
    const user = userEvent.setup();
    post.mockResolvedValue(result);
    renderWithRouter(<DataRoutingPreviewPanel />);
    expect(post).not.toHaveBeenCalled();
    await user.click(screen.getByText("Preview historical routing before saving"));
    await user.type(screen.getByLabelText("Proposed routing provider"), "tiingo");
    await user.type(screen.getByLabelText("Proposed routing priority"), "1");
    await user.click(screen.getByRole("button", { name: "Preview route" }));
    expect(await screen.findByText(/FABRICATED DATA WOULD WIN/)).toBeInTheDocument();
    expect(post).toHaveBeenCalledWith("/api/provider-routing/preview", expect.objectContaining({
      previewBackfill: true, symbol: "AAPL", from: "2019-01-01", to: "2024-12-31",
      proposedConfig: { backfillProviders: [{ providerId: "tiingo", priority: 1 }] }
    }), expect.objectContaining({ allowDevelopmentFallback: false }));
    await user.type(screen.getByLabelText("Routing symbol"), "B");
    expect(screen.queryByText(/FABRICATED DATA WOULD WIN/)).not.toBeInTheDocument();
  });

  it("discards stale previews when the query changes", async () => {
    let resolve!: (value: unknown) => void;
    post.mockImplementation(() => new Promise((complete) => { resolve = complete; }));
    const user = userEvent.setup();
    renderWithRouter(<DataRoutingPreviewPanel />);
    await user.click(screen.getByText("Preview historical routing before saving"));
    await user.click(screen.getByRole("button", { name: "Preview route" }));
    fireEvent.change(screen.getByLabelText("Routing symbol"), { target: { value: "MSFT" } });
    resolve(result);
    await waitFor(() => expect(screen.getByRole("button", { name: "Preview route" })).toBeEnabled());
    expect(screen.queryByText(/FABRICATED DATA WOULD WIN/)).not.toBeInTheDocument();
  });

  it("reports unavailable preview without fabricating a success and has accessible forms", async () => {
    const user = userEvent.setup();
    post.mockRejectedValue(new Error("Provider preview unavailable"));
    const { container } = renderWithRouter(<DataRoutingPreviewPanel />);
    await user.click(screen.getByText("Preview historical routing before saving"));
    await user.click(screen.getByRole("button", { name: "Preview route" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Provider preview unavailable");
    expect((await axe(container)).violations).toEqual([]);
  });
});
