import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
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
    render(<DataProvenanceBanner provenance="simulated" onRetryLiveData={() => {}} />);

    const badge = screen.getByTestId("data-provenance-simulated");
    expect(badge).toHaveTextContent("SIMULATED");
    // A confirmed non-real badge has no interactive affordance anywhere in the region:
    // no dismiss, and no retry — retrying cannot change a confirmed simulated tape.
    expect(screen.queryByRole("button")).toBeNull();
  });

  it("renders unknown provenance as a warning with a retry control, not a simulated brand", () => {
    const onRetry = vi.fn();
    render(<DataProvenanceBanner provenance="unknown" onRetryLiveData={onRetry} />);

    const badge = screen.getByTestId("data-provenance-unknown");
    expect(badge).toHaveTextContent("UNKNOWN");
    expect(badge).toHaveTextContent("Data source unverified");
    expect(screen.queryByTestId("data-provenance-simulated")).toBeNull();

    const retry = screen.getByRole("button", { name: "Retry live Meridian workspace data" });
    fireEvent.click(retry);
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it("disables the retry control while a retry is in flight", () => {
    render(<DataProvenanceBanner provenance="unknown" onRetryLiveData={() => {}} retryBusy />);

    const retry = screen.getByRole("button", { name: "Retrying live Meridian workspace data" });
    expect(retry).toBeDisabled();
    expect(retry).toHaveTextContent("Retrying live data");
  });
});
