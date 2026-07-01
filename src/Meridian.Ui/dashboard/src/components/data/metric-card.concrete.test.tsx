import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { MetricCard } from "./metric-card";

describe("MetricCard (Concrete)", () => {
  it("renders the label, value, and delta", () => {
    render(<MetricCard label="Net liquidation" value="$1,284,002.18" delta="+1.84% today" tone="success" />);
    expect(screen.getByText("Net liquidation")).toBeInTheDocument();
    expect(screen.getByText("$1,284,002.18")).toBeInTheDocument();
    expect(screen.getByText("+1.84% today")).toBeInTheDocument();
  });

  it("applies the tone class for the left-accent border", () => {
    const { container } = render(<MetricCard label="Day P&L" value="-$4,118.22" tone="danger" />);
    expect(container.querySelector(".mds-metric--danger")).not.toBeNull();
  });

  it("infers delta trend from its leading sign", () => {
    const { container, rerender } = render(<MetricCard label="X" value="1" delta="+2%" />);
    expect(container.querySelector(".mds-delta--up")).not.toBeNull();
    rerender(<MetricCard label="X" value="1" delta="-2%" />);
    expect(container.querySelector(".mds-delta--down")).not.toBeNull();
  });

  it("honours an explicit trend override", () => {
    const { container } = render(<MetricCard label="X" value="1" delta="2%" trend="flat" />);
    expect(container.querySelector(".mds-delta--flat")).not.toBeNull();
  });

  it("composes change and status into the accessible group name", () => {
    render(<MetricCard label="Day P&L" value="-$4,118.22" delta="-0.32%" tone="danger" />);
    expect(
      screen.getByRole("group", { name: "Day P&L, -$4,118.22, change -0.32%, status critical" })
    ).toBeInTheDocument();
  });
});
