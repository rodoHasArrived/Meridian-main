import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { StatementImportRows, StatementImportOutcomes } from "./statement-import-rows";
import type { StatementImportCommitResult, StatementImportPreview } from "@/types";

const preview = {
  reviewId: "review-1", recordCount: 1, totalRows: 1,
  rows: [{ rowId: "row-1", normalizedIndex: 0, sourceRowNumber: null, issues: [], canExclude: true, canAcceptAsBreak: false,
    record: { account: "A-1", symbol: "AAPL", quantity: 1, quantityExact: "9007199254740993", cashAmount: 10, cashAmountExact: "10.01", currency: "USD" } }]
} as unknown as StatementImportPreview;

it("offers only server-permitted row decisions and retains source identity uncertainty", async () => {
  const user = userEvent.setup(); const onChange = vi.fn();
  const { rerender, container } = render(<StatementImportRows preview={preview} decisions={{}} onChange={onChange} disabled={false} />);
  expect(screen.getByText("Source line unavailable")).toBeInTheDocument();
  expect(screen.getByText("9,007,199,254,740,993.000000 units")).toBeInTheDocument();
  expect(screen.getByRole("option", { name: "Accept as a break" })).toBeDisabled();
  await user.selectOptions(screen.getByRole("combobox", { name: "Decision for record 1" }), "Exclude");
  expect(onChange).toHaveBeenLastCalledWith({ rowId: "row-1", disposition: "Exclude", reason: "" }, "row-1");
  rerender(<StatementImportRows preview={preview} decisions={{ "row-1": { rowId: "row-1", disposition: "Exclude", reason: "" } }} onChange={onChange} disabled={false} />);
  expect(screen.getByRole("textbox", { name: "Reason for record 1" })).toBeRequired();
  expect((await axe(container)).violations).toEqual([]);
});

it("shows excluded and failed row outcomes directly", () => {
  const result = { runId: "run-1", totalRows: 2, rowOutcomes: [
    { rowId: "one", normalizedIndex: 0, disposition: "Exclude", reason: "Duplicate source", status: "Excluded" },
    { rowId: "two", normalizedIndex: 1, disposition: "Import", status: "Failed", caseIds: ["case-2"] }
  ] } as unknown as StatementImportCommitResult;
  render(<StatementImportOutcomes result={result} />);
  const rows = screen.getAllByRole("row");
  expect(rows[1]).toHaveTextContent("Failed");
  expect(screen.getByText("Exclude · Duplicate source")).toBeInTheDocument();
  expect(screen.getByText("case-2")).toBeInTheDocument();
});
