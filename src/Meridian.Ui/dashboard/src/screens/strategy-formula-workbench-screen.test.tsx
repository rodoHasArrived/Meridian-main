import { screen } from "@testing-library/react";
import { axe } from "jest-axe";
import { describe, expect, it } from "vitest";
import { StrategyFormulaWorkbenchScreen } from "@/screens/strategy-formula-workbench-screen";
import { renderWithRouter } from "@/test/render";

describe("StrategyFormulaWorkbenchScreen", () => {
  it("renders an honest not-connected formula workbench route", () => {
    renderWithRouter(<StrategyFormulaWorkbenchScreen />);

    expect(screen.getByRole("heading", { name: "Formula Workbench" })).toBeInTheDocument();
    expect(screen.getByText("Formula catalog is not connected")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Review provider connections required by Formula Workbench" }))
      .toHaveAttribute("href", "/settings/providers");
    expect(screen.getByRole("link", { name: "Open Strategy workspace" })).toHaveAttribute("href", "/strategy");
    expect(screen.queryByRole("button", { name: "Run local strategy formula preview" })).not.toBeInTheDocument();
  });

  it("has no basic accessibility violations", async () => {
    const { container } = renderWithRouter(<StrategyFormulaWorkbenchScreen />);

    const results = await axe(container);
    expect(results.violations).toHaveLength(0);
  });
});
