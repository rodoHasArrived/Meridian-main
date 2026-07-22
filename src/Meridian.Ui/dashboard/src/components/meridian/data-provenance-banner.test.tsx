import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { DataProvenanceBanner } from "@/components/meridian/data-provenance-banner";

describe("DataProvenanceBanner", () => {
  it("renders nothing for real data", () => {
    const { container } = render(<DataProvenanceBanner provenance="real" />);

    expect(container).toBeEmptyDOMElement();
  });

  it("labels seeded demo data with a persistent status region", () => {
    render(<DataProvenanceBanner provenance="seeded" />);

    const badge = screen.getByTestId("data-provenance-seeded");
    expect(badge).toHaveAttribute("role", "status");
    expect(badge).toHaveTextContent("SEEDED");
    expect(badge).toHaveTextContent("Seeded demo data");
  });

  it("labels simulated data and exposes no dismiss control", () => {
    render(<DataProvenanceBanner provenance="simulated" />);

    const badge = screen.getByTestId("data-provenance-simulated");
    expect(badge).toHaveTextContent("SIMULATED");
    // A non-dismissable badge has no interactive close affordance anywhere in the region.
    expect(screen.queryByRole("button")).toBeNull();
  });
});
