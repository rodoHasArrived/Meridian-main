import { render } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import {
  Skeleton,
  SkeletonCardRow,
  SkeletonForm,
  SkeletonGrid,
  SkeletonText,
  SkeletonTimeline
} from "@/components/ui/skeleton";

describe("Skeleton", () => {
  it("renders a decorative, animated block", () => {
    const { container } = render(<Skeleton className="h-4 w-10" />);
    const block = container.firstElementChild as HTMLElement;

    expect(block).toHaveAttribute("aria-hidden", "true");
    expect(block.className).toContain("animate-pulse");
    expect(block.className).toContain("motion-reduce:animate-none");
    expect(block.className).toContain("h-4");
  });

  it("stays out of the accessibility tree so regions own the loading announcement", () => {
    const { container } = render(<SkeletonCardRow />);
    expect(container.querySelector("[aria-hidden='true']")).not.toBeNull();
  });
});

describe("Skeleton presets", () => {
  it("renders the requested number of text lines", () => {
    const { container } = render(<SkeletonText lines={4} />);
    expect(container.querySelectorAll("[aria-hidden='true'] > div")).toHaveLength(4);
  });

  it("renders one card per requested metric", () => {
    const { container } = render(<SkeletonCardRow cards={3} />);
    // Each card is a bordered wrapper containing skeleton blocks.
    expect(container.querySelectorAll(".border.bg-card")).toHaveLength(3);
  });

  it("renders header plus body rows for a grid", () => {
    const { container } = render(<SkeletonGrid rows={5} columns={3} showHeader />);
    const grids = container.querySelectorAll("[style*='grid-template-columns']");
    // One header row + five body rows.
    expect(grids).toHaveLength(6);
  });

  it("omits the header when asked", () => {
    const { container } = render(<SkeletonGrid rows={2} columns={2} showHeader={false} />);
    expect(container.querySelectorAll("[style*='grid-template-columns']")).toHaveLength(2);
  });

  it("renders a timeline item per event", () => {
    const { container } = render(<SkeletonTimeline items={4} />);
    expect(container.querySelectorAll("li")).toHaveLength(4);
  });

  it("renders a field per form row plus an action", () => {
    const { container } = render(<SkeletonForm fields={3} />);
    const root = container.firstElementChild as HTMLElement;
    // Three field groups + one action block.
    expect(root.children).toHaveLength(4);
  });

  it("guards against zero or negative counts", () => {
    const { container } = render(<SkeletonText lines={0} />);
    expect(container.querySelectorAll("[aria-hidden='true'] > div")).toHaveLength(1);
  });
});
