import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { StrategyFormulaWorkbenchScreen } from "@/screens/strategy-formula-workbench-screen";

describe("StrategyFormulaWorkbenchScreen", () => {
  it("renders the Meridian formula workbench page", () => {
    render(<StrategyFormulaWorkbenchScreen />);

    expect(screen.getByRole("heading", { name: "Formula Workbench" })).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "Strategy formula fields" })).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "Formula suggestions" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Run local strategy formula preview" })).toBeInTheDocument();
  });
});
