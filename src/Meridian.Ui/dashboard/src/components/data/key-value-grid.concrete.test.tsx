import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { KeyValueGrid } from "./key-value-grid";
import { EntitySummary } from "./entity-summary";

describe("KeyValueGrid (Concrete)", () => {
  it("renders each label/value fact", () => {
    render(
      <KeyValueGrid
        columns={3}
        items={[
          { label: "CUSIP", value: "037833100" },
          { label: "Exchange", value: "NASDAQ" },
        ]}
      />,
    );
    expect(screen.getByText("CUSIP")).toBeInTheDocument();
    expect(screen.getByText("037833100")).toBeInTheDocument();
    expect(screen.getByText("NASDAQ")).toBeInTheDocument();
  });
});

describe("EntitySummary (Concrete)", () => {
  it("renders facts and applies per-item color overrides", () => {
    const { container } = render(
      <EntitySummary
        items={[
          { label: "Symbol", value: "AAPL" },
          { label: "Name", value: "Apple Inc.", mono: false },
          { label: "Status", value: "Active", color: "var(--severity-ready-fg)" },
        ]}
      />,
    );
    expect(screen.getByText("Apple Inc.")).toBeInTheDocument();
    const colored = Array.from(container.querySelectorAll<HTMLElement>("div")).find(
      (el) => el.textContent === "Active" && el.style.color !== "",
    );
    expect(colored?.style.color).toContain("severity-ready-fg");
  });
});
