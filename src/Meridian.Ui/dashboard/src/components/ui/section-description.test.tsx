import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { axe } from "jest-axe";
import { SectionDescription, SectionDescriptionPreferences } from "./section-description";

function Help({ operator = "Operator A", section = "queue" }: { operator?: string; section?: string }) {
  return <SectionDescriptionPreferences operatorKey={operator}>
    <SectionDescription descriptionKey={section} label="About the queue">Inspect source evidence before acting.</SectionDescription>
  </SectionDescriptionPreferences>;
}

describe("SectionDescription", () => {
  beforeEach(() => { localStorage.clear(); });

  it("shows help on first visit, remembers the operator, and keeps help keyboard-accessible", async () => {
    const user = userEvent.setup();
    const first = render(<Help />);
    expect(screen.getByText("Inspect source evidence before acting.")).toBeVisible();
    expect(screen.getByRole("button", { name: "Hide description: About the queue" })).toBeVisible();
    first.unmount();

    const next = render(<Help />);
    const summary = next.container.querySelector("summary")!;
    expect(screen.getByText("Inspect source evidence before acting.")).not.toBeVisible();
    await user.tab();
    expect(summary).toHaveFocus();
    // JSDOM does not implement native summary keyboard activation; Chromium QA covers Enter.
    await user.click(summary);
    expect(screen.getByText("Inspect source evidence before acting.")).toBeVisible();
    await user.keyboard("{Escape}");
    expect(summary).toHaveFocus();
    expect(summary.parentElement).not.toHaveAttribute("open");
    expect((await axe(next.container)).violations).toEqual([]);
  });

  it("does not share reading preferences between operators or sections", () => {
    const view = render(<Help />);
    fireEvent.click(screen.getByRole("button", { name: "Hide description: About the queue" }));
    expect(view.container.querySelector("summary")).toBeInTheDocument();
    expect(view.container.querySelector("summary")).toHaveFocus();
    view.rerender(<Help operator="Operator B" />);
    expect(screen.getByRole("button", { name: "Hide description: About the queue" })).toBeVisible();
    view.rerender(<Help section="journal" />);
    expect(screen.getByRole("button", { name: "Hide description: About the queue" })).toBeVisible();
  });

  it("does not remember mounted help until it enters the viewport", () => {
    let notify: IntersectionObserverCallback = () => {};
    const observer = { observe: vi.fn(), disconnect: vi.fn() };
    vi.stubGlobal("IntersectionObserver", class {
      constructor(callback: IntersectionObserverCallback) { notify = callback; }
      observe = observer.observe;
      disconnect = observer.disconnect;
    });
    try {
      const view = render(<Help />);
      expect(localStorage.length).toBe(0);
      notify([{ isIntersecting: false, intersectionRatio: 0 }] as IntersectionObserverEntry[], observer as unknown as IntersectionObserver);
      expect(localStorage.length).toBe(0);
      notify([{ isIntersecting: true, intersectionRatio: 1 }] as IntersectionObserverEntry[], observer as unknown as IntersectionObserver);
      expect(localStorage.length).toBe(1);
      expect(observer.disconnect).toHaveBeenCalled();
      // Observation records the next visit without shifting the currently visible layout.
      expect(screen.getByRole("button", { name: "Hide description: About the queue" })).toBeVisible();
      view.unmount();
    } finally { vi.unstubAllGlobals(); }
  });

  it("remains usable when local storage is unavailable", async () => {
    const get = vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => { throw new Error("blocked"); });
    const set = vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("blocked"); });
    try {
      const view = render(<Help />);
      fireEvent.click(screen.getByRole("button", { name: "Hide description: About the queue" }));
      fireEvent.click(view.container.querySelector("summary")!);
      expect(screen.getByText("Inspect source evidence before acting.")).toBeVisible();
    } finally { get.mockRestore(); set.mockRestore(); }
  });
});
