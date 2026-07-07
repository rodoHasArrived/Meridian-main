import { fireEvent, render, screen } from "@testing-library/react";
import { vi } from "vitest";
import { Button } from "@/components/ui/button";
import { DesignSystemButton } from "@/design-system/primitives";

describe("Button", () => {
  it("re-exports the design-system button adapter so behavior is not forked", () => {
    expect(Button).toBe(DesignSystemButton);
  });

  it("preserves busy and disabled semantics when rendered through the design-system adapter", () => {
    render(
      <DesignSystemButton busy busyLabel="Saving..." disabledReason="Save is already running." aria-label="Save mapping">
        Save
      </DesignSystemButton>
    );

    const button = screen.getByRole("button", { name: "Save mapping" });

    expect(button).toBeDisabled();
    expect(button).toHaveAttribute("aria-busy", "true");
    expect(button).toHaveAttribute("title", "Save is already running.");
    expect(button).toHaveTextContent("Saving...");
  });

  it("renders busy commands with disabled and busy semantics", () => {
    render(
      <Button busy busyLabel="Previewing..." aria-label="Previewing backfill request">
        Preview
      </Button>
    );

    const button = screen.getByRole("button", { name: "Previewing backfill request" });

    expect(button).toBeDisabled();
    expect(button).toHaveAttribute("aria-busy", "true");
    expect(button).toHaveAttribute("title", "Previewing...");
    expect(button).toHaveTextContent("Previewing...");
  });

  it("exposes disabled reasons as button titles", () => {
    render(
      <Button disabled disabledReason="Preview the request before running.">
        Run backfill
      </Button>
    );

    const button = screen.getByRole("button", { name: "Run backfill" });

    expect(button).toBeDisabled();
    expect(button).toHaveAttribute("title", "Preview the request before running.");
  });

  it("keeps busy labels visible while exposing disabled reasons as titles", () => {
    render(
      <Button busy busyLabel="Running..." disabledReason="Export is already running." aria-label="Run export">
        Run export
      </Button>
    );

    const button = screen.getByRole("button", { name: "Run export" });

    expect(button).toBeDisabled();
    expect(button).toHaveAttribute("aria-busy", "true");
    expect(button).toHaveAttribute("title", "Export is already running.");
    expect(button).toHaveTextContent("Running...");
  });

  it("projects command state onto asChild links without adding invalid disabled attributes", () => {
    render(
      <Button asChild disabled disabledReason="Export is already running.">
        <a href="/api/export/analysis">Run export</a>
      </Button>
    );

    const link = screen.getByRole("link", { name: "Run export" });

    expect(link).toHaveAttribute("aria-disabled", "true");
    expect(link).toHaveAttribute("title", "Export is already running.");
    expect(link).toHaveAttribute("tabindex", "-1");
    expect(link).not.toHaveAttribute("disabled");
  });

  it("forwards accessibility props to asChild links", () => {
    render(
      <Button asChild aria-label="Preview Excel export payload" aria-describedby="export-action-detail">
        <a href="/api/export/preview?profile=excel">Preview payload</a>
      </Button>
    );

    const link = screen.getByRole("link", { name: "Preview Excel export payload" });

    expect(link).toHaveAttribute("aria-describedby", "export-action-detail");
  });

  it("routes asChild click handlers through the command surface", () => {
    const onClick = vi.fn((event: React.MouseEvent<HTMLButtonElement>) => {
      event.preventDefault();
    });
    render(
      <Button asChild onClick={onClick}>
        <a href="/api/export/analysis">Run export</a>
      </Button>
    );

    fireEvent.click(screen.getByRole("link", { name: "Run export" }));

    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it("blocks disabled asChild activation", () => {
    const onClick = vi.fn();
    render(
      <Button asChild disabled disabledReason="Export is already running." onClick={onClick}>
        <a href="/api/export/analysis">Run export</a>
      </Button>
    );

    const cancelled = !fireEvent.click(screen.getByRole("link", { name: "Run export" }));

    expect(cancelled).toBe(true);
    expect(onClick).not.toHaveBeenCalled();
  });
});
