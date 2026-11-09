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

  describe("compactAfterSeen", () => {
    function memoryStorage(initial: Record<string, string> = {}): Storage {
      const values = new Map(Object.entries(initial));
      return {
        get length() { return values.size; },
        clear: () => values.clear(),
        getItem: (key) => values.get(key) ?? null,
        key: (index) => [...values.keys()][index] ?? null,
        removeItem: (key) => { values.delete(key); },
        setItem: (key, value) => { values.set(key, value); }
      };
    }
    const SEEN = "meridian.workstation.provenance-banner-seen";

    it("shows the full banner the first time a pairing appears and records it as seen", () => {
      const storage = memoryStorage();
      const { container } = render(<DataProvenanceBanner provenance="seeded" environment="paper" compactAfterSeen storage={storage} />);

      expect(container.querySelector("[data-compact]")).toBeNull();
      expect(screen.getByTestId("data-provenance-seeded")).toHaveTextContent("Seeded demo data");
      expect(storage.getItem(SEEN)).toBe("seeded:paper");
    });

    it("shrinks a seen pairing to a stripe that keeps the status role and full label", () => {
      const storage = memoryStorage({ [SEEN]: "seeded:paper" });
      const { container } = render(<DataProvenanceBanner provenance="seeded" environment="paper" compactAfterSeen storage={storage} />);

      expect(container.querySelector("[data-compact='true']")).not.toBeNull();
      const stripe = screen.getByTestId("data-provenance-seeded");
      expect(stripe).toHaveAttribute("role", "status");
      expect(stripe).toHaveTextContent("SEEDED");
      expect(stripe).toHaveTextContent("Seeded demo data");
      // Still non-dismissable: the stripe offers no control of any kind.
      expect(screen.queryByRole("button")).toBeNull();
    });

    it("brings the full banner back when the environment or provenance changes", () => {
      const storage = memoryStorage({ [SEEN]: "seeded:paper" });
      const { container } = render(<DataProvenanceBanner provenance="simulated" environment="paper" compactAfterSeen storage={storage} />);

      expect(container.querySelector("[data-compact]")).toBeNull();
      expect(storage.getItem(SEEN)).toBe("simulated:paper");
    });

    it.each([
      ["a LIVE session", { provenance: "seeded", environment: "live" }, "seeded:live"],
      ["an unresolved session", { provenance: "seeded", environment: null }, "seeded:paper"],
      ["unknown provenance, which carries the retry", { provenance: "unknown", environment: "paper" }, "unknown:paper"]
    ] as const)("keeps the full banner for %s", (_case, props, seen) => {
      const storage = memoryStorage({ [SEEN]: seen });
      const { container } = render(<DataProvenanceBanner {...props} compactAfterSeen storage={storage} onRetryLiveData={() => {}} />);

      expect(container.querySelector("[data-compact]")).toBeNull();
    });
  });

  it("disables the retry control while a retry is in flight", () => {
    render(<DataProvenanceBanner provenance="unknown" onRetryLiveData={() => {}} retryBusy />);

    const retry = screen.getByRole("button", { name: "Retrying live Meridian workspace data" });
    expect(retry).toBeDisabled();
    expect(retry).toHaveTextContent("Retrying live data");
  });
});
