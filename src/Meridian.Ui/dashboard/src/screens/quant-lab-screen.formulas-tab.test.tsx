import { screen } from "@testing-library/react";
import { axe } from "jest-axe";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { QuantLabScreen } from "@/screens/quant-lab-screen";
import { renderWithRouter, waitForAsyncEffects } from "@/test/render";
import * as api from "@/lib/api";

describe("QuantLabScreen formulas tab", () => {
  beforeEach(() => {
    vi.spyOn(api, "getQuantTemplates").mockResolvedValue({ templates: [] });
    vi.spyOn(api, "extractQuantParameters").mockResolvedValue({ parameters: [] });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("renders an honest not-connected formula workbench view on the formulas tab", async () => {
    renderWithRouter(<QuantLabScreen />, { initialEntries: ["/strategy/quant-lab?view=formulas"] });
    await waitForAsyncEffects();

    expect(screen.getByRole("tab", { name: "Formulas" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByText("Formula catalog is not connected")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Review provider connections required by the formula workbench" }))
      .toHaveAttribute("href", "/settings/providers");
    expect(screen.getByRole("link", { name: "Open Strategy workspace" })).toHaveAttribute("href", "/strategy");
    expect(screen.queryByRole("button", { name: "Run local strategy formula preview" })).not.toBeInTheDocument();
  });

  it("keeps the script lab as the default tab", async () => {
    renderWithRouter(<QuantLabScreen />, { initialEntries: ["/strategy/quant-lab"] });
    await waitForAsyncEffects();

    expect(screen.getByRole("tab", { name: "Script lab" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByLabelText("Script source")).toBeInTheDocument();
    expect(screen.queryByText("Formula catalog is not connected")).not.toBeInTheDocument();
  });

  it("has no basic accessibility violations", async () => {
    const { container } = renderWithRouter(<QuantLabScreen />, { initialEntries: ["/strategy/quant-lab?view=formulas"] });
    await waitForAsyncEffects();

    const results = await axe(container);
    expect(results.violations).toHaveLength(0);
  });
});
