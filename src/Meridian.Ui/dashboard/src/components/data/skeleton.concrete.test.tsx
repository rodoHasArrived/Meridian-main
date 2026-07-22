import { render } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { Skeleton, SkeletonTable } from "./skeleton";

describe("Skeleton (Concrete)", () => {
  it("renders a single block placeholder by default", () => {
    const { container } = render(<Skeleton width={120} height={16} />);
    const el = container.querySelector(".mds-skel") as HTMLElement;
    expect(el).not.toBeNull();
    expect(el.style.width).toBe("120px");
    expect(el.style.height).toBe("16px");
  });

  it("renders one line per `lines` for the text variant", () => {
    const { container } = render(<Skeleton variant="text" lines={4} />);
    expect(container.querySelectorAll(".mds-skel--text")).toHaveLength(4);
  });

  it("defaults circle height to its width", () => {
    const { container } = render(<Skeleton variant="circle" width={40} />);
    const el = container.querySelector(".mds-skel--circle") as HTMLElement;
    expect(el.style.width).toBe("40px");
    expect(el.style.height).toBe("40px");
  });
});

describe("SkeletonTable (Concrete)", () => {
  it("lays out rows × columns placeholder cells", () => {
    const { container } = render(<SkeletonTable rows={3} columns={5} />);
    expect(container.querySelectorAll("tr")).toHaveLength(3);
    expect(container.querySelectorAll("td")).toHaveLength(15);
  });
});
