import { act, cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { SectionHeading, SectionHelpProvider } from "./section-heading";

function Heading({ user = "operator-a", description = "Select a row to review its evidence." }: { user?: string | null; description?: string }) {
  return <SectionHelpProvider userKey={user}><SectionHeading title="Review queue" description={description} helpId="review-queue" /></SectionHelpProvider>;
}

beforeEach(() => localStorage.clear());
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks(); });

describe("SectionHeading", () => {
  it("keeps the first introduction stable, then remembers it on a return visit", async () => {
    const first = render(<Heading />);
    expect(screen.getByText("Select a row to review its evidence.")).toBeVisible();
    expect(screen.queryByRole("button", { name: "About Review queue" })).not.toBeInTheDocument();
    first.unmount();
    const { container } = render(<Heading />);
    expect(screen.queryByText("Select a row to review its evidence.")).not.toBeInTheDocument();
    const help = screen.getByRole("button", { name: "About Review queue" });
    const user = userEvent.setup();
    help.focus();
    await user.keyboard("{Enter}");
    expect(screen.getByRole("tooltip")).toHaveTextContent("Select a row to review its evidence.");
    expect(help).toHaveAttribute("aria-expanded", "true");
    expect(help).toHaveAccessibleDescription("Select a row to review its evidence.");
    expect((await axe(container)).violations).toEqual([]);
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("tooltip")).not.toBeInTheDocument();
    expect(help).toHaveFocus();
    await user.click(help);
    expect(screen.getByRole("tooltip")).toBeVisible();
    await user.click(document.body);
    expect(screen.queryByRole("tooltip")).not.toBeInTheDocument();
  });

  it("keeps introductions separate between users and shows changed copy again", () => {
    const { rerender } = render(<Heading />);
    rerender(<Heading user="operator-b" />);
    expect(screen.getByText("Select a row to review its evidence.")).toBeVisible();
    rerender(<Heading />);
    expect(screen.getByRole("button", { name: "About Review queue" })).toBeInTheDocument();
    rerender(<Heading description="New review instructions." />);
    expect(screen.getByText("New review instructions.")).toBeVisible();
  });

  it("does not persist unscoped operators and tolerates unavailable browser storage", () => {
    const first = render(<Heading user={null} />);
    expect(localStorage.length).toBe(0);
    first.unmount();
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => { throw new Error("Storage blocked"); });
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("Storage blocked"); });
    render(<Heading />);
    expect(screen.getByText("Select a row to review its evidence.")).toBeVisible();
  });

  it("remembers only introductions that enter the visible viewport", () => {
    let observe: ((entries: { isIntersecting: boolean }[]) => void) | undefined;
    const disconnect = vi.fn();
    vi.stubGlobal("IntersectionObserver", class {
      constructor(callback: typeof observe) { observe = callback; }
      observe() {}
      disconnect = disconnect;
    });
    render(<Heading />);
    expect(localStorage.length).toBe(0);
    act(() => observe?.([{ isIntersecting: false }]));
    expect(localStorage.length).toBe(0);
    act(() => observe?.([{ isIntersecting: true }]));
    expect(localStorage.length).toBe(1);
    expect(disconnect).toHaveBeenCalledOnce();
    expect(screen.getByText("Select a row to review its evidence.")).toBeVisible();
  });
});
