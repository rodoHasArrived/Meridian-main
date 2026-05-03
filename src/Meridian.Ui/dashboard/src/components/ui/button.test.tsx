import { render, screen } from "@testing-library/react";
import { Button } from "@/components/ui/button";

describe("Button", () => {
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

  it("projects command state onto asChild links without adding invalid disabled attributes", () => {
    render(
      <Button asChild disabled disabledReason="Export is already running.">
        <a href="/api/export/analysis">Run export</a>
      </Button>
    );

    const link = screen.getByRole("link", { name: "Run export" });

    expect(link).toHaveAttribute("aria-disabled", "true");
    expect(link).toHaveAttribute("title", "Export is already running.");
    expect(link).not.toHaveAttribute("disabled");
  });
});
