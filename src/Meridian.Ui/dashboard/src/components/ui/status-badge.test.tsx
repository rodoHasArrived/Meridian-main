import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { StatusBadge } from "./status-badge";

describe("StatusBadge", () => {
  it("renders the label text", () => {
    render(<StatusBadge tone="warning">Review required</StatusBadge>);
    expect(screen.getByText("Review required")).toBeTruthy();
  });

  it("maps danger tone to the danger design-system variant", () => {
    render(<StatusBadge tone="danger">Blocked</StatusBadge>);
    expect(screen.getByText("Blocked").className).toContain("danger");
  });

  it("maps warning tone to the warning variant", () => {
    render(<StatusBadge tone="warning">Stale</StatusBadge>);
    expect(screen.getByText("Stale").className).toContain("warning");
  });

  it("does not use the danger styling for info or neutral tones", () => {
    const { unmount } = render(<StatusBadge tone="info">Selected</StatusBadge>);
    expect(screen.getByText("Selected").className).not.toContain("danger");
    unmount();

    render(<StatusBadge>Note</StatusBadge>);
    expect(screen.getByText("Note").className).not.toContain("danger");
  });

  it("uses the solid default variant only for a neutral solid emphasis", () => {
    render(<StatusBadge emphasis="solid">Current</StatusBadge>);
    expect(screen.getByText("Current").className).not.toContain("danger");
  });

  it("renders the dot indicator when requested", () => {
    const { container } = render(<StatusBadge tone="success" dot>Ready</StatusBadge>);
    expect(container.querySelector(".mds-badge__dot")).not.toBeNull();
  });

  it.each([
    ["success", "bg-success", "text-background"],
    ["warning", "bg-warning", "text-background"],
    ["danger", "bg-danger", "text-background"],
    ["info", "bg-primary", "text-primary-foreground"]
  ] as const)("renders %s solid as a filled span with one contrasting foreground", (tone, fill, foreground) => {
    render(<StatusBadge tone={tone} emphasis="solid">Label</StatusBadge>);
    const className = screen.getByText("Label").className;

    expect(className).toContain(fill);
    expect(className).toContain(foreground);
    // No competing semantic or outline colour classes that would override the fill.
    expect(className).not.toContain(`text-${tone}`);
    expect(className).not.toContain("bg-secondary");
  });

  it("renders info subtle without an outline background that would fight the tint", () => {
    render(<StatusBadge tone="info">Label</StatusBadge>);
    expect(screen.getByText("Label").className).not.toContain("bg-secondary");
  });
});
