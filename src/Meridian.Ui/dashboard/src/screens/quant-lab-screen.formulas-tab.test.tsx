import { screen } from "@testing-library/react";
import { axe } from "jest-axe";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { QuantLabScreen } from "@/screens/quant-lab-screen";
import { renderWithRouter, waitForAsyncEffects } from "@/test/render";
import * as api from "@/lib/api";

// The Formulas tab used to render an "honest not-connected" card. Honest is not the same as
// useful: `/strategy/quant-lab?view=formulas` is registered in UNWIRED_WORKSTATION_ROUTES, and
// filtering it only out of the command palette left the tab itself fully navigable — the more
// likely way an operator would reach it. The tab is withdrawn until the built
// strategy-formula-workbench component is mounted against a real formula-catalog endpoint.
describe("QuantLabScreen formulas tab (withdrawn)", () => {
  beforeEach(() => {
    vi.spyOn(api, "getQuantTemplates").mockResolvedValue({ templates: [] });
    vi.spyOn(api, "extractQuantParameters").mockResolvedValue({ parameters: [] });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("does not offer a Formulas tab", async () => {
    renderWithRouter(<QuantLabScreen />, { initialEntries: ["/strategy/quant-lab"] });
    await waitForAsyncEffects();

    expect(screen.queryByRole("tab", { name: "Formulas" })).not.toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Script lab" })).toHaveAttribute("aria-selected", "true");
  });

  it("degrades a stale ?view=formulas deep link to the script lab instead of a dead end", async () => {
    renderWithRouter(<QuantLabScreen />, { initialEntries: ["/strategy/quant-lab?view=formulas"] });
    await waitForAsyncEffects();

    expect(screen.getByRole("tab", { name: "Script lab" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByLabelText("Script source")).toBeInTheDocument();
    expect(screen.queryByText("Formula catalog is not connected")).not.toBeInTheDocument();
  });

  it("canonicalizes the address so a shared link matches what is on screen", async () => {
    renderWithRouter(<QuantLabScreen />, { initialEntries: ["/strategy/quant-lab?view=formulas"] });
    await waitForAsyncEffects();

    // Otherwise Copy Link would share a URL claiming to be the Formula Workbench while the
    // operator is looking at the Script Lab.
    expect(window.location.search).not.toContain("view=formulas");
  });

  it("keeps the script lab as the default tab", async () => {
    renderWithRouter(<QuantLabScreen />, { initialEntries: ["/strategy/quant-lab"] });
    await waitForAsyncEffects();

    expect(screen.getByRole("tab", { name: "Script lab" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByLabelText("Script source")).toBeInTheDocument();
  });

  it("has no basic accessibility violations", async () => {
    const { container } = renderWithRouter(<QuantLabScreen />, {
      initialEntries: ["/strategy/quant-lab?view=formulas"]
    });
    await waitForAsyncEffects();

    const results = await axe(container);
    expect(results.violations).toHaveLength(0);
  });
});
