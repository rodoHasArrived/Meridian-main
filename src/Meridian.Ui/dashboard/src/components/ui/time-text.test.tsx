import { act, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { TimeText } from "@/components/ui/time-text";
import { setDisplayTimeZone } from "@/lib/format";

const NOW = "2026-06-30T18:10:00.000Z";

afterEach(() => {
  setDisplayTimeZone("UTC");
});

describe("TimeText", () => {
  it("renders the absolute timestamp with the age on hover", () => {
    render(<TimeText value="2026-06-30T18:05:00Z" now={NOW} />);

    const time = screen.getByText("Jun 30, 2026, 18:05 UTC");
    expect(time.tagName).toBe("TIME");
    expect(time).toHaveAttribute("datetime", "2026-06-30T18:05:00Z");
    expect(time).toHaveAttribute("title", "5m ago");
  });

  it("renders the relative age with the absolute timestamp on hover", () => {
    render(<TimeText value="2026-06-30T18:05:00Z" display="relative" now={NOW} />);

    const time = screen.getByText("5m ago");
    expect(time).toHaveAttribute("title", "Jun 30, 2026, 18:05 UTC");
    expect(time).toHaveAttribute("datetime", "2026-06-30T18:05:00Z");
  });

  it("re-renders when the operator changes the display zone", () => {
    render(<TimeText value="2026-06-30T18:05:00Z" now={NOW} />);
    act(() => {
      setDisplayTimeZone("America/New_York");
    });

    expect(screen.getByText("Jun 30, 2026, 14:05 EDT")).toHaveAttribute("datetime", "2026-06-30T18:05:00Z");
  });

  it("renders the fallback without a dateTime for an absent or invalid instant", () => {
    const { rerender } = render(<TimeText value={null} fallback="Never" />);
    expect(screen.getByText("Never").tagName).toBe("SPAN");

    rerender(<TimeText value="not a time" />);
    expect(screen.getByText("—")).not.toHaveAttribute("datetime");
  });
});
