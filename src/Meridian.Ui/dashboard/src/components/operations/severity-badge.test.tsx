import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { SeverityBadge } from "./severity-badge";

describe("SeverityBadge", () => {
  it("renders the canonical label for a normalized status", () => {
    render(<SeverityBadge status="ReviewRequired" />);
    expect(screen.getByText("Review")).toBeInTheDocument();
  });

  it("applies the severity class for the resolved severity", () => {
    const { container } = render(<SeverityBadge status="Blocked" />);
    expect(container.querySelector(".mds-sev--blocked")).not.toBeNull();
  });

  it("honours a custom label and defaults to info", () => {
    render(<SeverityBadge label="3 breaks" />);
    expect(screen.getByText("3 breaks")).toBeInTheDocument();
  });

  it("omits the status dot when dot is false", () => {
    const { container } = render(<SeverityBadge status="Ready" dot={false} />);
    expect(container.querySelector(".mds-sev__dot")).toBeNull();
  });
});
