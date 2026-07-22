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

  it("can be named as a status region for screen placeholders", () => {
    render(<EmptyState title="No rows" role="status" ariaLabel="Provider rows empty state" compact />);
    expect(screen.getByRole("status", { name: "Provider rows empty state" })).toHaveClass("mds-empty--compact");
  });
});
