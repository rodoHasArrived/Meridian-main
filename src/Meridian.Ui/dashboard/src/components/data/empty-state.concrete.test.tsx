import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { EmptyState } from "./empty-state";

describe("EmptyState (Concrete)", () => {
  it("renders the title and detail", () => {
    render(<EmptyState title="No matching rows" detail="Adjust the filters and try again." />);
    expect(screen.getByText("No matching rows")).toBeInTheDocument();
    expect(screen.getByText("Adjust the filters and try again.")).toBeInTheDocument();
  });

  it("renders an action button only when both action and onAction are set", async () => {
    const onAction = vi.fn();
    const { rerender } = render(<EmptyState title="Empty" action="Add row" />);
    expect(screen.queryByRole("button")).toBeNull();

    rerender(<EmptyState title="Empty" action="Add row" onAction={onAction} />);
    await userEvent.click(screen.getByRole("button", { name: "Add row" }));
    expect(onAction).toHaveBeenCalledOnce();
  });

  it("paints the action with full-color structural roles and blue focus", () => {
    render(<EmptyState title="Empty" action="Add row" onAction={() => undefined} />);
    const css = [...document.head.querySelectorAll("style[data-mds]")].map((style) => style.textContent ?? "").join("\n");
    const action = css.match(/\.mds-empty__action\{[^}]*\}/)?.[0] ?? "";
    expect(action).toContain("background:var(--brand-axis,");
    expect(action).toContain("color:var(--ink-inverse,");
    expect(css).toContain("background:var(--ws-primary-hover,");
    expect(css).toContain("outline:2px solid var(--interaction-focus,");
    // --accent remains a Tailwind HSL channel triple, so native color declarations
    // must resolve full colors instead of rendering as invalid CSS.
    expect(css).not.toMatch(/(background|border|color):var\(--accent[,)]/);
  });

  it("can be named as a status region for screen placeholders", () => {
    render(<EmptyState title="No rows" role="status" ariaLabel="Provider rows empty state" compact />);
    expect(screen.getByRole("status", { name: "Provider rows empty state" })).toHaveClass("mds-empty--compact");
  });
});
