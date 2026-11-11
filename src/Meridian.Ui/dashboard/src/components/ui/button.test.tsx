import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { fireEvent, render, screen } from "@testing-library/react";
import { vi } from "vitest";
import { Button } from "@/components/ui/button";
import { DesignSystemButton } from "@/design-system/primitives";

describe("Button", () => {
  it("re-exports the design-system button adapter so behavior is not forked", () => {
    expect(Button).toBe(DesignSystemButton);
  });

  it("keeps primary interaction states on structural action roles", () => {
    render(<Button>Save</Button>);
    const button = screen.getByRole("button", { name: "Save" });
    expect(button).toHaveClass("hover:bg-[var(--ws-primary-hover)]", "active:bg-[var(--ws-primary-pressed)]");
    expect(button.className).not.toContain("ws-accent");
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
    expect(button).toHaveAttribute("data-design-system-component", "Button");
    expect(button).toHaveClass("mds-btn");
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
    expect(link).toHaveAttribute("data-design-system-component", "Button");
    expect(link).toHaveClass("mds-btn");
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
  it("keeps a disabled button at full opacity and carries its reason", () => {
    render(
      <>
        <Button disabled disabledReason="Validate before submitting.">Submit approval</Button>
        <Button variant="destructive" disabled>Cancel order</Button>
      </>
    );

    const submit = screen.getByRole("button", { name: "Submit approval" });
    expect(submit).toBeDisabled();
    // The reason is the tooltip and, because the name comes from the label, the description.
    expect(submit).toHaveAttribute("title", "Validate before submitting.");
    expect(submit).toHaveAccessibleDescription("Validate before submitting.");
    // No variant halves its opacity any more: the shared rule in styles/index.css owns the look.
    for (const button of screen.getAllByRole("button")) {
      expect(button.className).not.toMatch(/opacity-50/);
      expect(button).toHaveClass("mds-btn");
    }
  });

  it("defines one disabled look with a visible edge for every variant", () => {
    const css = readFileSync(resolve(process.cwd(), "src/styles/index.css"), "utf8");
    const rule = css.slice(css.indexOf(".mds-btn:disabled,"));

    expect(rule).toMatch(/background-color: var\(--ws-disabled-bg\)/);
    expect(rule).toMatch(/border-color: var\(--ws-disabled-border\)/);
    expect(rule).toMatch(/color: var\(--ws-disabled-fg\)/);
    expect(rule).toMatch(/opacity: 1/);
    // Each theme block defines the three tokens.
    for (const token of ["--ws-disabled-bg:", "--ws-disabled-fg:", "--ws-disabled-border:"]) {
      expect(css.split(token).length - 1).toBe(4);
    }
  });

  it.each([
    ["light", "#EDEAE4", "#5E666F", "#85807A", ["#F2F0EB", "#F8F7F3", "#EBE8E1", "#EDEAE4", "#FCFBF8"]],
    ["dark", "#222B34", "#929CA6", "#77828D", ["#11171D", "#171E25", "#1D252D", "#1F2831", "#222B34"]]
  ])("meets contrast for the %s disabled tokens", (_theme, fill, label, edge, surfaces) => {
    const css = readFileSync(resolve(process.cwd(), "src/styles/index.css"), "utf8");
    for (const value of [fill, label, edge]) {
      expect(css).toContain(value);
    }
    // Label on the disabled fill reads as text (4.5:1); the edge is a 3:1 boundary on every surface.
    expect(contrastRatio(label, fill)).toBeGreaterThanOrEqual(4.5);
    for (const surface of surfaces) {
      expect(contrastRatio(edge, surface)).toBeGreaterThanOrEqual(3);
    }
  });
});

function relativeLuminance(hex: string): number {
  const [red, green, blue] = [0, 2, 4].map((offset) => {
    const channel = Number.parseInt(hex.replace("#", "").slice(offset, offset + 2), 16) / 255;
    return channel <= 0.03928 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * red + 0.7152 * green + 0.0722 * blue;
}

function contrastRatio(first: string, second: string): number {
  const [lighter, darker] = [relativeLuminance(first), relativeLuminance(second)].sort((a, b) => b - a);
  return (lighter + 0.05) / (darker + 0.05);
}
