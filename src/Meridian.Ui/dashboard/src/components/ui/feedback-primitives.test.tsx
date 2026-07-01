import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { Callout } from "@/components/ui/callout";
import { Kbd } from "@/components/ui/kbd";
import { Spinner } from "@/components/ui/spinner";

describe("Callout", () => {
  it("renders a titled note with tone-driven left rule", () => {
    render(
      <Callout tone="warning" title="Live environment">
        Orders execute against real capital.
      </Callout>
    );
    const note = screen.getByRole("note");
    expect(note).toHaveClass("border-l-warning");
    expect(screen.getByText("Live environment")).toBeInTheDocument();
    expect(screen.getByText("Orders execute against real capital.")).toBeInTheDocument();
  });
});

describe("Kbd", () => {
  it("splits a combo string into separate key caps", () => {
    render(<Kbd keys="Ctrl+Enter" />);
    expect(screen.getByText("Ctrl")).toBeInTheDocument();
    expect(screen.getByText("Enter")).toBeInTheDocument();
  });

  it("renders children when no keys are provided", () => {
    render(<Kbd>Esc</Kbd>);
    expect(screen.getByText("Esc")).toBeInTheDocument();
  });
});

describe("Spinner", () => {
  it("exposes a status role with the accessible label", () => {
    render(<Spinner label="Loading positions" />);
    expect(screen.getByRole("status", { name: "Loading positions" })).toBeInTheDocument();
    expect(screen.getByText("Loading positions")).toBeInTheDocument();
  });

  it("defaults the accessible label to Loading", () => {
    render(<Spinner />);
    expect(screen.getByRole("status", { name: "Loading" })).toBeInTheDocument();
  });
});
