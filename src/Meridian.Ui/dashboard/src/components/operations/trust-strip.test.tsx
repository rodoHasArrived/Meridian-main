import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { TrustStrip } from "./trust-strip";

const items = [
  { label: "Readiness", value: "86 / 100", state: "review" },
  { label: "Recon", value: "3 breaks", state: "blocked" },
  { label: "Providers", value: "4 live", state: "ready" },
  { label: "Approval", value: "Pending", state: "pending" },
];

describe("TrustStrip", () => {
  it("renders a label/value item per entry", () => {
    const { container } = render(<TrustStrip items={items} />);
    expect(container.querySelectorAll(".mds-trust__item")).toHaveLength(4);
    expect(screen.getByText("Readiness")).toBeInTheDocument();
    expect(screen.getByText("86 / 100")).toBeInTheDocument();
  });

  it("maps trust states to their tint classes (review reads as attention/ochre)", () => {
    const { container } = render(<TrustStrip items={items} />);
    expect(container.querySelector(".mds-trust__item--review")).not.toBeNull();
    expect(container.querySelector(".mds-trust__item--blocked")).not.toBeNull();
    expect(container.querySelector(".mds-trust__item--ready")).not.toBeNull();
    expect(container.querySelector(".mds-trust__item--pending")).not.toBeNull();
  });

  it("falls back to a muted item for an unknown state", () => {
    const { container } = render(<TrustStrip items={[{ label: "X", value: "y" }]} />);
    expect(container.querySelector(".mds-trust__item--muted")).not.toBeNull();
  });
});
