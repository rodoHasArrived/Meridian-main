import { render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ProviderAccountingRegion } from "@/screens/data-screen.provider-accounting";
import type { ProviderAccountingServices } from "@/screens/data-screen.provider-accounting.view-model";
import {
  buildProviderAccountingCatalogFixture,
  buildProviderConnectionHealthFixture,
  buildProviderRateLimitsFixture
} from "@/screens/data-screen.provider-accounting.test-fixtures";

describe("ProviderAccountingRegion", () => {
  it("renders server-owned failures, current request state, and unavailable history", async () => {
    const rateLimits = buildProviderRateLimitsFixture();
    rateLimits.providers[0].resetAt = null;
    const services: ProviderAccountingServices = {
      getCatalog: vi.fn(async () => buildProviderAccountingCatalogFixture()),
      getRateLimits: vi.fn(async () => rateLimits),
      getConnectionHealth: vi.fn(async () => buildProviderConnectionHealthFixture(null))
    };

    render(<ProviderAccountingRegion services={services} />);

    await waitFor(() => expect(screen.getAllByText("1 provider registration failure")).toHaveLength(2));
    expect(screen.getByRole("table", { name: "Current provider rate-limit state" })).toBeInTheDocument();
    expect(screen.getByText("8 / 10")).toBeInTheDocument();
    expect(screen.getByText("Current rate-limit reason: provider response.")).toBeInTheDocument();
    expect(screen.getByText("Unknown — reachability unavailable; no runtime diagnostics.")).toBeInTheDocument();
    expect(screen.getByLabelText("Provider rate-limit history posture")).toHaveTextContent("not retained");
  });
});
