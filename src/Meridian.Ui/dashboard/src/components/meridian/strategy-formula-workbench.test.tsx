import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import {
  StrategyFormulaWorkbench,
  type StrategyFormulaCell,
  type StrategyFormulaField,
  type StrategyFormulaSuggestion
} from "@/components/meridian/strategy-formula-workbench";

const fields: StrategyFormulaField[] = [
  {
    fieldId: "PRICE",
    label: "Last price",
    category: "Market",
    typeName: "decimal",
    description: "Latest trusted price.",
    example: "100.25"
  },
  {
    fieldId: "RATING",
    label: "Credit rating",
    category: "Security Master",
    typeName: "string",
    description: "Normalized credit rating.",
    example: "A"
  }
];

const suggestions: StrategyFormulaSuggestion[] = [
  {
    suggestionId: "rating-filter",
    expression: "RATING >= \"BBB\"",
    description: "Investment grade filter.",
    tone: "filter",
    triggers: ["rating"]
  }
];

const cells: StrategyFormulaCell[] = [
  {
    cellId: "A1",
    label: "Universe filter",
    mode: "formula",
    source: "RATING >= \"BBB\"",
    status: "Ready"
  }
];

describe("StrategyFormulaWorkbench", () => {
  it("filters fields and inserts field tokens into the active formula", async () => {
    const user = userEvent.setup();
    render(
      <StrategyFormulaWorkbench
        title="Formula Workbench"
        description="Strategy authoring"
        fields={fields}
        suggestions={suggestions}
        initialCells={cells}
      />
    );

    await user.type(screen.getByRole("textbox", { name: "Search strategy fields" }), "rating");

    const fieldList = screen.getByRole("list", { name: "Strategy formula fields" });
    expect(within(fieldList).getByRole("button", { name: "Insert RATING" })).toBeInTheDocument();
    expect(within(fieldList).queryByRole("button", { name: "Insert PRICE" })).toBeNull();

    await user.click(within(fieldList).getByRole("button", { name: "Insert RATING" }));

    expect(screen.getByRole<HTMLTextAreaElement>("textbox", { name: "Strategy formula source" }).value).toContain("RATING");
    expect(screen.getByRole("region", { name: "Compiled strategy formula preview" })).toBeInTheDocument();
    expect(screen.getByTestId("strategy-formula-compiled-preview")).toHaveTextContent("fields: RATING");
  });

  it("switches authoring modes and inserts formula suggestions", async () => {
    const user = userEvent.setup();
    render(
      <StrategyFormulaWorkbench
        title="Formula Workbench"
        description="Strategy authoring"
        fields={fields}
        suggestions={suggestions}
        initialCells={cells}
      />
    );

    await user.click(screen.getByRole("button", { name: "Code" }));
    expect(screen.getByRole("button", { name: "Code" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByTestId("strategy-formula-compiled-preview")).toHaveTextContent("mode: code");

    await user.click(screen.getByRole("button", { name: "Insert formula suggestion RATING >= \"BBB\"" }));

    expect(screen.getByRole<HTMLTextAreaElement>("textbox", { name: "Strategy formula source" }).value).toContain("RATING >= \"BBB\"");
  });
});
