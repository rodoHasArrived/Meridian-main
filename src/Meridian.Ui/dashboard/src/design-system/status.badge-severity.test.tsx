import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { StatusBadge, badgeSeverity } from "@/design-system/status";

describe("badgeSeverity", () => {
  it("lets a status-word label decide its colour, so one word has one colour everywhere", () => {
    expect(badgeSeverity("action", "Blocked")).toBe("blocked");
    expect(badgeSeverity("blocked", "Review")).toBe("review");
    expect(badgeSeverity("blocked", "3 open")).toBe("action");
    expect(badgeSeverity("info", "Running")).toBe("review");
  });

  it("keeps the caller's status for a custom label", () => {
    expect(badgeSeverity("blocked", "3 breaks")).toBe("blocked");
    expect(badgeSeverity("ready", undefined)).toBe("ready");
  });

  it("renders the label's severity on the badge", () => {
    render(<StatusBadge status="action" label="Blocked" />);
    expect(screen.getByText("Blocked").closest("[data-severity]")).toHaveAttribute("data-severity", "blocked");
  });
});
