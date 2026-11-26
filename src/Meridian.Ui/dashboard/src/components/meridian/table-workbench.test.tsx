import { act, render, screen, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useState } from "react";
import { TABLE_SEARCH_MIN_ROWS, TableWorkbench, type TableWorkbenchColumn } from "@/components/meridian/table-workbench";
import { getJson, postJson } from "@/lib/api";
import { ApiError } from "@/lib/api-errors";
import { downloadCsv } from "@/lib/csv";

vi.mock("@/lib/api", () => ({ getJson: vi.fn(), postJson: vi.fn() }));
vi.mock("@/lib/csv", async importOriginal => ({ ...await importOriginal<typeof import("@/lib/csv")>(), downloadCsv: vi.fn(() => true) }));
const { xlsxToFile, writeXlsxFile } = vi.hoisted(() => {
  const toFile = vi.fn(async (_name: string) => undefined);
  return { xlsxToFile: toFile, writeXlsxFile: vi.fn((_sheets: unknown) => ({ toFile })) };
});
vi.mock("write-excel-file/browser", () => ({ default: writeXlsxFile }));
interface Row { id: string; amount: number; }
const columns: TableWorkbenchColumn<Row>[] = [
  { id: "id", label: "Record", render: row => row.id, exportValue: row => row.id },
  { id: "amount", label: "Amount", render: row => row.amount, exportValue: row => row.amount }
];
beforeEach(() => { vi.clearAllMocks(); vi.mocked(getJson).mockResolvedValue([]); });

const longRows: Row[] = "abcdefghijkl".split("").map((id, index) => ({ id, amount: index + 1 }));

describe("TableWorkbench export as shown", () => {
  const shownColumns: TableWorkbenchColumn<Row>[] = [
    { id: "id", label: "Record", render: row => row.id, exportValue: row => row.id },
    { id: "amount", label: "Amount", render: row => row.amount.toFixed(2), exportValue: row => row.amount, exportDisplay: row => row.amount.toFixed(2), exportNumberFormat: "#,##0.00" },
    { id: "note", label: "Note", render: row => `Note ${row.id}`, exportDisplay: row => `Note ${row.id}` }
  ];
  const rows: Row[] = [{ id: "a", amount: 1 }, { id: "b", amount: 2.5 }];

  it("exports the visible columns in order with the shown text and the totals row, as CSV and XLSX", async () => {
    const user = userEvent.setup();
    render(<TableWorkbench workbenchId="shown" columns={shownColumns} rows={rows} getRowId={row => row.id}
      completePopulation sourceDescription="Complete snapshot" emptyText="None" ariaLabel="Records"
      footerLabel="Totals" footerCells={{ amount: "3.50" }} footerExport={{ amount: { value: 3.5, display: "3.50" } }} />);
    await act(async () => { await Promise.resolve(); });
    // Hiding a column leaves it out of the export.
    await user.click(screen.getByRole("checkbox", { name: "Note" }));
    await user.click(screen.getByRole("button", { name: "Export as shown · CSV" }));
    const [fileName, csv] = vi.mocked(downloadCsv).mock.calls.at(-1)!;
    expect(fileName).toMatch(/^shown-.*-as-shown\.csv$/);
    expect(csv.split("\n")[0]).toMatch(/^Record,Amount,Context · Source/);
    expect(csv).toContain("b,2.50,");
    expect(csv).toContain("Totals,3.50,");
    expect(csv).not.toContain("Note a");
    await user.click(screen.getByRole("button", { name: "Export as shown · XLSX" }));
    await waitFor(() => expect(xlsxToFile).toHaveBeenCalledWith(expect.stringMatching(/^shown-.*\.xlsx$/)));
    const sheets = writeXlsxFile.mock.calls.at(-1)![0] as Array<{ sheet: string; data: unknown[][] }>;
    expect(sheets[0]!.data[0]).toEqual([{ value: "Record", type: String, fontWeight: "bold" }, { value: "Amount", type: String, fontWeight: "bold" }]);
    expect(sheets[0]!.data[2]).toEqual([{ value: "b", type: String }, { value: 2.5, type: Number, format: "#,##0.00" }]);
    expect(sheets[0]!.data[3]![1]).toMatchObject({ value: 3.5, fontWeight: "bold" });
    expect(sheets[1]!.sheet).toBe("About");
    expect(await screen.findByText("Exported 2 rows as shown with totals (XLSX).")).toBeInTheDocument();
  });

  it("is unavailable for a table the server pages", async () => {
    render(<TableWorkbench workbenchId="paged" columns={shownColumns} rows={rows} getRowId={row => row.id}
      onExportAll={vi.fn(async () => undefined)} sourceDescription="Server page" emptyText="None" ariaLabel="Records" />);
    await act(async () => { await Promise.resolve(); });
    expect(screen.getByRole("button", { name: "Export as shown · CSV" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Export as shown · XLSX" })).toBeDisabled();
  });
});

describe("TableWorkbench", () => {
  it("keeps compact queries visible while table options retain columns, export and saving, with density from the global setting", async () => {
    const user = userEvent.setup();
    // Density is the operator's global preference; the table has no density control of its own.
    document.body.setAttribute("data-theme-density", "spacious");
    vi.mocked(postJson).mockResolvedValue({ viewId: "personal", label: "Review", searchText: "a", columnIds: ["id"], density: "comfortable" });
    const { container } = render(<TableWorkbench workbenchId="compact-review" compactToolbar columns={columns}
      rows={longRows} getRowId={row => row.id} getSearchText={row => row.id}
      filters={<label>Status<select aria-label="Status filter" defaultValue="open"><option value="open">Open</option></select></label>}
      filterState={{ status: "open" }} completePopulation sourceDescription="Complete authorized snapshot"
      emptyText="No matches" ariaLabel="Records" />);
    await act(async () => { await Promise.resolve(); });
    expect(screen.getByRole("searchbox")).toBeVisible();
    expect(screen.getByRole("combobox", { name: "Status filter" })).toBeVisible();
    expect(screen.getByText("12 matches", { exact: true })).toBeVisible();
    expect(screen.getByRole("button", { name: "Export CSV" })).not.toBeVisible();
    await user.type(screen.getByRole("searchbox"), "a");
    expect(screen.getByText("1 match", { exact: true })).toBeVisible();
    const summary = container.querySelector("summary")!;
    await user.click(summary);
    expect(screen.getByRole("button", { name: "Export CSV" })).toBeVisible();
    expect(screen.queryByRole("combobox", { name: "Records density" })).not.toBeInTheDocument();
    expect(container.querySelector(".table-workbench--comfortable")).not.toBeNull();
    await user.click(screen.getByRole("checkbox", { name: "Amount" }));
    expect(screen.queryByRole("columnheader", { name: "Amount" })).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Export CSV" }));
    expect(downloadCsv).toHaveBeenCalledWith(expect.any(String), expect.stringMatching(/^Record,Context/));
    await user.type(screen.getByLabelText("View name"), "Review");
    await user.click(screen.getByRole("button", { name: "Save to my account" }));
    await waitFor(() => expect(postJson).toHaveBeenCalledWith("/api/workstation/tables/compact-review/views", expect.objectContaining({
      searchText: "a", columnIds: ["id"], density: "comfortable", filters: { status: "open" }
    }), expect.any(Object)));
    await user.keyboard("{Escape}");
    expect(summary.parentElement).not.toHaveAttribute("open");
    expect(summary).toHaveFocus();
    expect(screen.getByRole("searchbox")).toHaveValue("a");
    document.body.removeAttribute("data-theme-density");
  });

  it("sizes the toolbar to the table: no search for a short table, export and saved views in one menu", async () => {
    const user = userEvent.setup();
    const props = { workbenchId: "short", columns, getRowId: (row: Row) => row.id, getSearchText: (row: Row) => row.id,
      completePopulation: true, sourceDescription: "Complete snapshot", emptyText: "No matches", ariaLabel: "Records" };
    const { rerender } = render(<TableWorkbench {...props} rows={[{ id: "a", amount: 1 }]} />);
    expect(screen.queryByRole("searchbox")).not.toBeInTheDocument();
    expect(screen.queryByText("1 match", { exact: true })).not.toBeInTheDocument();
    expect(screen.queryByRole("combobox", { name: "Records density" })).not.toBeInTheDocument();
    const more = screen.getByLabelText("Records more table actions");
    expect(screen.getByRole("button", { name: "Export CSV" })).not.toBeVisible();
    await user.click(more);
    expect(screen.getByRole("button", { name: "Export CSV" })).toBeVisible();
    expect(screen.getByLabelText("View name")).toBeVisible();
    rerender(<TableWorkbench {...props} rows={longRows.slice(0, TABLE_SEARCH_MIN_ROWS)} />);
    expect(screen.queryByRole("searchbox")).not.toBeInTheDocument();
    rerender(<TableWorkbench {...props} rows={longRows} />);
    expect(screen.getByRole("searchbox")).toBeInTheDocument();
    expect(screen.getByText("12 matches", { exact: true })).toBeInTheDocument();
  });

  it("drops low-priority columns in a narrow frame instead of clipping, and keeps them in exports", async () => {
    const user = userEvent.setup();
    const width = vi.spyOn(HTMLElement.prototype, "clientWidth", "get").mockReturnValue(600);
    // The table needs 900px until its low-priority column is dropped.
    const content = vi.spyOn(HTMLElement.prototype, "scrollWidth", "get").mockImplementation(() =>
      document.querySelector('th[scope="col"]:nth-of-type(2)') ? 900 : 560);
    const prioritized: TableWorkbenchColumn<Row>[] = [
      { id: "id", label: "Record", render: row => row.id, exportValue: row => row.id },
      { id: "amount", label: "Amount", priority: "low", render: row => row.amount, exportValue: row => row.amount }
    ];
    const props = { workbenchId: "priority", columns: prioritized, rows: [{ id: "a", amount: 1 }], getRowId: (row: Row) => row.id,
      completePopulation: true, sourceDescription: "Complete snapshot", emptyText: "No matches", ariaLabel: "Records" };
    try {
      const { unmount } = render(<TableWorkbench {...props} />);
      await waitFor(() => expect(screen.queryByRole("columnheader", { name: "Amount" })).not.toBeInTheDocument());
      expect(screen.getByRole("columnheader", { name: "Record" })).toBeInTheDocument();
      expect(screen.getByText("1 column hidden to fit")).toBeInTheDocument();
      await user.click(screen.getByRole("button", { name: "Export CSV" }));
      expect(vi.mocked(downloadCsv).mock.calls.at(-1)?.[1]).toMatch(/^Record,Amount,Context/);
      unmount();
      width.mockReturnValue(1200);
      render(<TableWorkbench {...props} />);
      expect(screen.getByRole("columnheader", { name: "Amount" })).toBeInTheDocument();
      expect(screen.queryByText(/hidden to fit/)).not.toBeInTheDocument();
    } finally {
      width.mockRestore();
      content.mockRestore();
    }
  });

  it("says once that a column has no values instead of a placeholder on every row", async () => {
    interface DueRow { id: string; due: string | null }
    const dueColumns: TableWorkbenchColumn<DueRow>[] = [
      { id: "id", label: "Record", render: row => row.id },
      { id: "due", label: "Due", hideWhenEmpty: row => !row.due, emptyNote: "Due: no item has a due time", render: row => row.due ?? "No due time" }
    ];
    const { rerender } = render(<TableWorkbench workbenchId="due-empty" columns={dueColumns}
      rows={[{ id: "a", due: null }, { id: "b", due: null }]} getRowId={row => row.id} emptyText="None" ariaLabel="Due records" sourceDescription="Test rows" completePopulation />);
    await act(async () => { await Promise.resolve(); });
    expect(screen.queryByRole("columnheader", { name: "Due" })).not.toBeInTheDocument();
    expect(screen.queryByText("No due time")).not.toBeInTheDocument();
    expect(screen.getByText("Due: no item has a due time")).toBeInTheDocument();
    rerender(<TableWorkbench workbenchId="due-empty" columns={dueColumns}
      rows={[{ id: "a", due: null }, { id: "b", due: "Jul 14" }]} getRowId={row => row.id} emptyText="None" ariaLabel="Due records" sourceDescription="Test rows" completePopulation />);
    expect(screen.getByRole("columnheader", { name: "Due" })).toBeInTheDocument();
    expect(screen.getByText("No due time")).toBeInTheDocument();
    expect(screen.queryByText("Due: no item has a due time")).not.toBeInTheDocument();
  });

  it("shows a selection bar only while rows are selected", async () => {
    const user = userEvent.setup();
    const onClear = vi.fn();
    const props = { workbenchId: "selection", columns, rows: [{ id: "a", amount: 1 }], getRowId: (row: Row) => row.id,
      completePopulation: true, sourceDescription: "Complete snapshot", emptyText: "No matches", ariaLabel: "Records" };
    const selection = (count: number) => ({ count, noun: "record", onClear, actions: <button type="button">Assign selected</button> });
    const { rerender } = render(<TableWorkbench {...props} selection={selection(0)} />);
    expect(screen.queryByRole("region", { name: "Records selection actions" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Assign selected" })).not.toBeInTheDocument();
    rerender(<TableWorkbench {...props} selection={selection(2)} />);
    const bar = screen.getByRole("region", { name: "Records selection actions" });
    expect(bar).toHaveTextContent("2 records selected");
    expect(within(bar).getByRole("button", { name: "Assign selected" })).toBeInTheDocument();
    await user.click(within(bar).getByRole("button", { name: "Clear selection" }));
    expect(onClear).toHaveBeenCalledOnce();
  });

  it("uses singular and plural result counts in the toolbar and complete export copy", async () => {
    const user = userEvent.setup();
    const props = { workbenchId: "counts", columns, getRowId: (row: Row) => row.id, filters: <span>Open only</span>,
      completePopulation: true, sourceDescription: "Complete snapshot", emptyText: "No matches", ariaLabel: "Records" };
    const { rerender } = render(<TableWorkbench {...props} rows={[{ id: "a", amount: 1 }]} />);
    expect(screen.getByText("1 match", { exact: true })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Export CSV" })).toHaveAttribute("title", "Export all 1 filtered match. Complete snapshot");
    await user.click(screen.getByRole("button", { name: "Export CSV" }));
    expect(screen.getByRole("status")).toHaveTextContent("Exported all 1 filtered match. Complete snapshot");
    rerender(<TableWorkbench {...props} rows={[]} />);
    expect(screen.getByText("0 matches", { exact: true })).toBeInTheDocument();
    rerender(<TableWorkbench {...props} rows={[{ id: "a", amount: 1 }, { id: "b", amount: 2 }]} />);
    expect(screen.getByText("2 matches", { exact: true })).toBeInTheDocument();
  });

  it("exports every filtered source row, including rows outside the DOM cap", async () => {
    const user = userEvent.setup();
    const rows = Array.from({ length: 105 }, (_, index) => ({ id: `record-${index}`, amount: -index }));
    render(<TableWorkbench workbenchId="test" columns={columns} rows={rows} getRowId={row => row.id}
      completePopulation sourceDescription="Complete authorized snapshot" emptyText="No matches" ariaLabel="Records" />);
    expect(screen.queryByText("record-104")).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Export CSV" }));
    expect(downloadCsv).toHaveBeenCalledWith(expect.stringMatching(/^test-/), expect.stringContaining("record-104,-104"));
    expect(screen.getByRole("status")).toHaveTextContent("Exported all 105 filtered matches");
  });

  it("does not present a partial population as a full export", async () => {
    render(<TableWorkbench workbenchId="test" columns={columns} rows={[{ id: "a", amount: 1 }]} getRowId={row => row.id}
      sourceDescription="Paged source" emptyText="No matches" ariaLabel="Records" />);
    expect(screen.getByRole("button", { name: "Export CSV" })).toBeDisabled();
    expect(downloadCsv).not.toHaveBeenCalled();
  });

  it("shows and exports a replacement result schema while the workbench remains mounted", async () => {
    const user = userEvent.setup();
    const props = { workbenchId: "sql", rows: [{ id: "query-row", amount: 2 }], getRowId: (row: Row) => row.id,
      completePopulation: true, sourceDescription: "Retained query result", emptyText: "No matches", ariaLabel: "Query results" };
    const { rerender } = render(<TableWorkbench {...props} sourceKey="execution-1" columns={[
      { id: "first", label: "first", render: row => row.amount, exportValue: row => row.amount }
    ]} />);
    expect(screen.getByRole("columnheader", { name: "first" })).toBeInTheDocument();
    rerender(<TableWorkbench {...props} sourceKey="execution-2" columns={[
      { id: "second", label: "second", render: row => row.amount, exportValue: row => row.amount }
    ]} />);
    expect(screen.getByRole("columnheader", { name: "second" })).toBeInTheDocument();
    expect(screen.queryByRole("columnheader", { name: "first" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Export CSV" })).toBeEnabled();
    await user.click(screen.getByRole("button", { name: "Export CSV" }));
    expect(downloadCsv).toHaveBeenCalledWith(expect.stringMatching(/^sql-/), expect.stringMatching(/^second,Context/));
  });

  it("preserves hidden surviving columns and reveals new fields when the schema changes", async () => {
    const user = userEvent.setup();
    const props = { workbenchId: "test", rows: [{ id: "a", amount: 1 }], getRowId: (row: Row) => row.id,
      completePopulation: true, sourceDescription: "Complete snapshot", emptyText: "No matches", ariaLabel: "Records" };
    const { rerender } = render(<TableWorkbench {...props} columns={columns} />);
    await user.click(screen.getByText("Columns", { selector: "summary" }));
    await user.click(screen.getByRole("checkbox", { name: "Amount" }));
    rerender(<TableWorkbench {...props} columns={[...columns,
      { id: "new", label: "New field", render: row => row.id, exportValue: row => row.id }
    ]} />);
    const headers = within(screen.getByRole("table", { name: "Records" })).getAllByRole("columnheader");
    expect(headers.map(header => header.textContent)).toEqual(["Record", "New field"]);
    expect(screen.getByRole("checkbox", { name: "Amount" })).not.toBeChecked();
    expect(screen.getByRole("checkbox", { name: "New field" })).toBeChecked();
  });

  it("uses one Columns control for URL-owned visibility and exports exactly its displayed fields", async () => {
    const user = userEvent.setup();
    const changed = vi.fn();
    function ControlledWorkbench() {
      const [hidden, setHidden] = useState(["amount"]);
      return <TableWorkbench workbenchId="controlled" columns={columns.map(column => ({ ...column, hideable: column.id === "amount" }))}
        rows={[{ id: "a", amount: 9 }]} getRowId={row => row.id} hiddenColumnIds={hidden}
        onHiddenColumnIdsChange={next => { changed(next); setHidden(next); }} completePopulation
        sourceDescription="Complete snapshot" emptyText="No matches" ariaLabel="Records" />;
    }
    render(<ControlledWorkbench />);
    expect(screen.getAllByText("Columns", { selector: "summary" })).toHaveLength(1);
    expect(screen.queryByRole("columnheader", { name: "Amount" })).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Export CSV" }));
    expect(downloadCsv).toHaveBeenLastCalledWith(expect.any(String), expect.stringMatching(/^Record,Context/));
    await user.click(screen.getByText("Columns", { selector: "summary" }));
    await user.click(screen.getByRole("checkbox", { name: "Amount" }));
    expect(changed).toHaveBeenLastCalledWith([]);
    expect(screen.getByRole("columnheader", { name: "Amount" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Export CSV" }));
    expect(downloadCsv).toHaveBeenLastCalledWith(expect.any(String), expect.stringMatching(/^Record,Amount,Context/));
  });

  it("honors a default-hidden column without creating an independent inner menu", async () => {
    const user = userEvent.setup();
    render(<TableWorkbench workbenchId="defaults" columns={columns.map(column => ({ ...column, defaultHidden: column.id === "amount" }))}
      rows={[{ id: "a", amount: 1 }]} getRowId={row => row.id} completePopulation
      sourceDescription="Complete snapshot" emptyText="No matches" ariaLabel="Records" />);
    expect(screen.getAllByText("Columns", { selector: "summary" })).toHaveLength(1);
    expect(screen.queryByRole("columnheader", { name: "Amount" })).not.toBeInTheDocument();
    await user.click(screen.getByText("Columns", { selector: "summary" }));
    await user.click(screen.getByRole("checkbox", { name: "Amount" }));
    expect(screen.getByRole("columnheader", { name: "Amount" })).toBeInTheDocument();
  });

  it("uses the displayed built-in sort for complete CSV, server export and saved views", async () => {
    const user = userEvent.setup();
    const sortedColumns: TableWorkbenchColumn<Row>[] = columns.map(column => ({ ...column,
      sortValue: row => column.id === "amount" ? row.amount : row.id, numeric: column.id === "amount" }));
    const props = { workbenchId: "sorted", columns: sortedColumns, rows: [{ id: "b", amount: 2 }, { id: "a", amount: 10 }],
      getRowId: (row: Row) => row.id, completePopulation: true, sourceDescription: "Complete snapshot", emptyText: "No matches", ariaLabel: "Records" };
    vi.mocked(postJson).mockResolvedValue({ viewId: "sorted-view", label: "Largest first", searchText: "",
      columnIds: ["id", "amount"], density: "compact", sortColumnId: "amount", sortDirection: "desc" });
    const { rerender } = render(<TableWorkbench {...props} />);
    await user.click(screen.getByRole("button", { name: "Sort by Amount" }));
    expect(screen.getByRole("columnheader", { name: "Amount" })).toHaveAttribute("aria-sort", "descending");
    const table = screen.getByRole("table", { name: "Records" });
    expect(within(table).getAllByRole("row").slice(1).map(row => row.textContent)).toEqual(["a10", "b2"]);
    await user.click(screen.getByRole("button", { name: "Export CSV" }));
    const csv = vi.mocked(downloadCsv).mock.calls[0][1];
    expect(csv.indexOf("a,10,")).toBeLessThan(csv.indexOf("b,2,"));
    await user.click(screen.getByLabelText("Records more table actions"));
    await user.type(screen.getByLabelText("View name"), "Largest first");
    await user.click(screen.getByRole("button", { name: "Save to my account" }));
    await waitFor(() => expect(postJson).toHaveBeenCalledWith("/api/workstation/tables/sorted/views", expect.objectContaining({
      sortColumnId: "amount", sortDirection: "desc"
    }), expect.any(Object)));
    const onExportAll = vi.fn(async () => {});
    rerender(<TableWorkbench {...props} onExportAll={onExportAll} />);
    await user.click(screen.getByRole("button", { name: "Export CSV" }));
    expect(onExportAll).toHaveBeenCalledWith(["id", "amount"], "", { columnId: "amount", direction: "desc" }, expect.any(AbortSignal));
  });

  it("restores a saved built-in sort to both displayed and exported rows", async () => {
    const user = userEvent.setup();
    vi.mocked(getJson).mockResolvedValue([{ viewId: "sorted", label: "Smallest first", searchText: "",
      columnIds: ["id", "amount"], density: "compact", sortColumnId: "amount", sortDirection: "asc" }]);
    render(<TableWorkbench workbenchId="sorted" columns={columns.map(column => ({ ...column,
      ...(column.id === "amount" ? { sortValue: (row: Row) => row.amount, numeric: true } : {}) }))}
      rows={[{ id: "a", amount: 10 }, { id: "b", amount: 2 }]} getRowId={row => row.id} completePopulation
      sourceDescription="Complete snapshot" emptyText="No matches" ariaLabel="Records" />);
    await user.click(screen.getByLabelText("Records more table actions"));
    await screen.findByRole("option", { name: "Smallest first" });
    await user.selectOptions(screen.getByLabelText("Saved views"), "sorted");
    expect(within(screen.getByRole("table", { name: "Records" })).getAllByRole("row").slice(1).map(row => row.textContent)).toEqual(["b2", "a10"]);
    await user.click(screen.getByRole("button", { name: "Export CSV" }));
    const csv = vi.mocked(downloadCsv).mock.calls[0][1];
    expect(csv.indexOf("b,2,")).toBeLessThan(csv.indexOf("a,10,"));
  });

  function renderSavedViewTable() {
    render(<TableWorkbench workbenchId="test" columns={columns} rows={[{ id: "a", amount: 1 }]} getRowId={row => row.id}
      getSearchText={row => row.id} completePopulation sourceDescription="Complete snapshot" emptyText="No matches" ariaLabel="Records" />);
  }

  it("says which saved columns were skipped when restoring a view whose columns partly no longer exist", async () => {
    const user = userEvent.setup();
    vi.mocked(getJson).mockResolvedValue([{ viewId: "older", label: "Older view", searchText: "",
      columnIds: ["amount", "retired-a", "retired-b"], density: "compact" }]);
    renderSavedViewTable();
    await user.click(screen.getByLabelText("Records more table actions"));
    await screen.findByRole("option", { name: "Older view" });
    await user.selectOptions(screen.getByLabelText("Saved views"), "older");
    expect(within(screen.getByRole("table", { name: "Records" })).getAllByRole("columnheader").map(header => header.textContent)).toEqual(["Amount"]);
    expect(screen.getByRole("status")).toHaveTextContent("Restored Older view. 2 saved columns no longer exist and were skipped.");
  });

  it("refuses to apply a view none of whose columns exist, and leaves the current view in place", async () => {
    const user = userEvent.setup();
    vi.mocked(getJson).mockResolvedValue([{ viewId: "gone", label: "Gone view", searchText: "zzz",
      columnIds: ["retired"], density: "compact" }]);
    renderSavedViewTable();
    await user.click(screen.getByLabelText("Records more table actions"));
    await screen.findByRole("option", { name: "Gone view" });
    await user.selectOptions(screen.getByLabelText("Saved views"), "gone");
    expect(screen.getByLabelText("Saved views")).toHaveValue("");
    expect(within(screen.getByRole("table", { name: "Records" })).getAllByRole("columnheader").map(header => header.textContent)).toEqual(["Record", "Amount"]);
    expect(screen.getByRole("status")).toHaveTextContent("Gone view couldn't be applied: its columns were removed");
    expect(screen.queryByText(/Restored Gone view/)).not.toBeInTheDocument();
  });

  it("offers to replace a personal view of the same name and keeps one entry after saving", async () => {
    const user = userEvent.setup();
    vi.mocked(getJson).mockResolvedValue([{ viewId: "breaks", label: "Open breaks", searchText: "", columnIds: ["id"], density: "compact" }]);
    vi.mocked(postJson).mockResolvedValue({ viewId: "breaks", label: "open breaks", searchText: "", columnIds: ["id", "amount"], density: "compact" });
    renderSavedViewTable();
    await user.click(screen.getByLabelText("Records more table actions"));
    await screen.findByRole("option", { name: "Open breaks" });
    await user.type(screen.getByLabelText("View name"), "open breaks");
    await user.click(screen.getByRole("button", { name: "Replace “Open breaks”" }));
    await waitFor(() => expect(screen.getByRole("status")).toHaveTextContent("Updated open breaks in your account."));
    expect(within(screen.getByLabelText("Saved views")).getAllByRole("option").map(option => option.textContent)).toEqual(["Current view", "open breaks"]);
  });

  it("disables saved views with the reason when the host refuses them for this table", async () => {
    const user = userEvent.setup();
    vi.mocked(getJson).mockRejectedValue(new ApiError({ path: "/api/workstation/tables/test/views", status: 403 }));
    renderSavedViewTable();
    await user.click(screen.getByLabelText("Records more table actions"));
    await waitFor(() => expect(screen.getByRole("button", { name: "Save to my account" })).toBeDisabled());
    expect(screen.getByLabelText("Saved views")).toBeDisabled();
    expect(screen.getAllByText("Saved views aren't available for this table with your current permissions.").length).toBeGreaterThan(0);
    expect(postJson).not.toHaveBeenCalled();
  });

  it("restores a legacy view then saves an explicit personal copy without claiming an owner", async () => {
    const user = userEvent.setup();
    vi.mocked(getJson).mockResolvedValue([{ viewId: "legacy", label: "Prior view", searchText: "a",
      columnIds: ["amount", "id"], density: "comfortable", isReadOnly: true }]);
    vi.mocked(postJson).mockResolvedValue({ viewId: "personal", label: "Prior view copy", searchText: "a", columnIds: ["amount", "id"], density: "comfortable" });
    render(<TableWorkbench workbenchId="test" columns={columns} rows={[{ id: "a", amount: 1 }]} getRowId={row => row.id}
      getSearchText={row => row.id} completePopulation sourceDescription="Complete snapshot" emptyText="No matches" ariaLabel="Records" />);
    await user.click(screen.getByLabelText("Records more table actions"));
    await waitFor(() => expect(screen.getByRole("option", { name: /Prior view/ })).toBeInTheDocument());
    await user.selectOptions(screen.getByLabelText("Saved views"), "legacy");
    expect(screen.getByRole("searchbox")).toHaveValue("a");
    const headers = within(screen.getByRole("table", { name: "Records" })).getAllByRole("columnheader");
    expect(headers.map(header => header.textContent)).toEqual(["Amount", "Record"]);
    await user.click(screen.getByRole("button", { name: "Save to my account" }));
    await waitFor(() => expect(postJson).toHaveBeenCalledWith("/api/workstation/tables/test/views", expect.objectContaining({
      // A restored view no longer overrides density: the saved copy records the global setting.
      label: "Prior view copy", searchText: "a", columnIds: ["amount", "id"], density: "compact"
    }), expect.objectContaining({ allowDevelopmentFallback: false })));
    expect(screen.getByRole("status")).toHaveTextContent("Saved Prior view copy to your account");
  });
});
