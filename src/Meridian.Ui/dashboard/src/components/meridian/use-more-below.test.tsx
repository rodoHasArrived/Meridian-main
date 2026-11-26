import { act, render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { expectNarrowWorkbenchViewport } from "@/test/narrow-workbench";
import { WorkspaceWorkbenchShell } from "@/components/meridian/workspace-workbench-shell";
import { hasMoreBelow } from "@/components/meridian/use-more-below";

describe("hasMoreBelow", () => {
  it("is true only while content remains beneath the visible area", () => {
    expect(hasMoreBelow({ scrollHeight: 900, clientHeight: 500, scrollTop: 0 })).toBe(true);
    expect(hasMoreBelow({ scrollHeight: 900, clientHeight: 500, scrollTop: 398 })).toBe(false);
    expect(hasMoreBelow({ scrollHeight: 500, clientHeight: 500, scrollTop: 0 })).toBe(false);
  });
});

describe("WorkspaceWorkbenchShell inspector overflow cue", () => {
  // The test viewport is the constrained layout, where the inspector stacks under the queue
  // and the sticky More below control is hidden by CSS; the overflow marker still tracks.
  it("marks a clipped inspector and renders a More below control, then clears it at the bottom", () => {
    expectNarrowWorkbenchViewport();
    const { container } = render(<WorkspaceWorkbenchShell label="Holdings" inspector={<p>Detail</p>}><p>List</p></WorkspaceWorkbenchShell>);
    const inspector = container.querySelector<HTMLElement>('[data-workbench-slot="inspector"]')!;
    expect(inspector).not.toHaveAttribute("data-more-below");

    Object.defineProperty(inspector, "scrollHeight", { configurable: true, value: 1200 });
    Object.defineProperty(inspector, "clientHeight", { configurable: true, value: 600 });
    act(() => { inspector.dispatchEvent(new Event("scroll")); });
    expect(inspector).toHaveAttribute("data-more-below", "true");
    const moreBelow = screen.getByRole("button", { name: "More below", hidden: true });
    expect(moreBelow).toBeInTheDocument();
    expect(moreBelow).not.toBeVisible();

    inspector.scrollTop = 600;
    act(() => { inspector.dispatchEvent(new Event("scroll")); });
    expect(inspector).not.toHaveAttribute("data-more-below");
    expect(screen.queryByRole("button", { name: "More below", hidden: true })).not.toBeInTheDocument();
  });
});
