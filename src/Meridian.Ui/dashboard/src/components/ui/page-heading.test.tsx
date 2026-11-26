import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { PageHeading } from "./page-heading";

describe("PageHeading", () => {
  it("renders the page name as a level-two heading with its purpose on its own line", () => {
    render(<PageHeading title="Trading overview" purpose="See your positions and open orders." actions={<button type="button">Import file</button>} />);

    const heading = screen.getByRole("heading", { level: 2, name: "Trading overview" });
    const purpose = screen.getByText("See your positions and open orders.");
    // The purpose is below the title, never inline with it, and there is no per-page Hide link.
    expect(heading.parentElement).toContainElement(purpose);
    expect(heading).not.toContainElement(purpose);
    expect(screen.queryByRole("button", { name: /hide/i })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Import file" })).toBeInTheDocument();
  });

  it("renders the title alone when the page states no purpose", () => {
    const { container } = render(<PageHeading title="Holdings" />);
    expect(screen.getByRole("heading", { level: 2, name: "Holdings" })).toBeInTheDocument();
    expect(container.querySelector("[data-page-heading]")?.textContent).toBe("Holdings");
  });
});
