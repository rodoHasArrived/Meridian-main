import { render, screen, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useState } from "react";
import { TableWorkbench, type TableWorkbenchColumn } from "@/components/meridian/table-workbench";
import { getJson, postJson } from "@/lib/api";
import { downloadCsv } from "@/lib/csv";
import { axe } from "jest-axe";

vi.mock("@/lib/api", () => ({ getJson: vi.fn(), postJson: vi.fn() }));
vi.mock("@/lib/csv", async importOriginal => ({ ...await importOriginal<typeof import("@/lib/csv")>(), downloadCsv: vi.fn(() => true) }));
interface Row { id: string; amount: number; }
const columns: TableWorkbenchColumn<Row>[] = [
  { id: "id", label: "Record", render: row => row.id, exportValue: row => row.id },
  { id: "amount", label: "Amount", render: row => row.amount, exportValue: row => row.amount }
];
beforeEach(() => { vi.clearAllMocks(); vi.mocked(getJson).mockResolvedValue([]); });

describe("TableWorkbench", () => {
  it("keeps search and filters exposed while compact table tools preserve column, density, export and save behavior", async () => {
    const user = userEvent.setup();
    vi.mocked(postJson).mockResolvedValue({ viewId: "personal", label: "Review A", searchText: "a", columnIds: ["id"], density: "comfortable" });
    const { container } = render(<TableWorkbench compactToolbar workbenchId="compact-tools" columns={columns}
      rows={[{ id: "a", amount: 1 }, { id: "b", amount: 2 }]} getRowId={row => row.id} getSearchText={row => row.id}
      filters={<label>Only owned<input type="checkbox" /></label>} filterState={{ owner: "self" }}
      completePopulation sourceDescription="Complete authorized snapshot" exportContext={{ Book: "Book A" }} emptyText="No matches" ariaLabel="Records" />);
    const tools = screen.getByText("Table tools", { selector: "summary" });
    const disclosure = tools.closest("details")!;
    expect(disclosure).not.toHaveAttribute("open");
    expect(screen.getByRole("searchbox", { name: "Search" })).toBeVisible();
    expect(screen.getByRole("checkbox", { name: "Only owned" })).toBeVisible();
    expect(screen.getByRole("button", { name: "Export CSV" })).not.toBeVisible();
    await user.type(screen.getByRole("searchbox"), "a");
    expect(screen.getByText("1 match", { exact: true })).toBeVisible();
    expect(within(screen.getByRole("table", { name: "Records" })).getAllByRole("row")).toHaveLength(2);
    await user.click(tools);
    expect(disclosure).toHaveAttribute("open");
    await user.selectOptions(screen.getByRole("combobox", { name: "Records density" }), "comfortable");
    expect(container.querySelector(".table-workbench")).toHaveClass("table-workbench--comfortable");
    await user.click(screen.getByText("Columns", { selector: "summary" }));
    await user.click(screen.getByRole("checkbox", { name: "Amount" }));
    expect(screen.queryByRole("columnheader", { name: "Amount" })).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Export CSV" }));
    expect(downloadCsv).toHaveBeenCalledWith(expect.stringMatching(/^compact-tools-/), expect.stringMatching(/^Record,Context/));
    expect(vi.mocked(downloadCsv).mock.lastCall?.[1]).toContain("Book A");
    await user.click(screen.getByText("Save view", { selector: "summary" }));
    await user.type(screen.getByLabelText("View name"), "Review A");
    await user.click(screen.getByRole("button", { name: "Save to my account" }));
    await waitFor(() => expect(postJson).toHaveBeenCalledWith("/api/workstation/tables/compact-tools/views", expect.objectContaining({
      label: "Review A", searchText: "a", columnIds: ["id"], density: "comfortable", filters: { owner: "self" }
    }), expect.objectContaining({ allowDevelopmentFallback: false })));
    expect((await axe(container)).violations).toEqual([]);
    tools.focus();
    await user.keyboard("{Escape}");
    expect(disclosure).not.toHaveAttribute("open");
    expect(tools).toHaveFocus();
    await user.click(tools);
    await user.click(screen.getByRole("searchbox"));
    expect(disclosure).not.toHaveAttribute("open");
  });

  it("retains incomplete-population export protection and closes compact tools on a source change", async () => {
    const user = userEvent.setup();
    const props = { compactToolbar: true, workbenchId: "scoped-tools", columns, rows: [{ id: "a", amount: 1 }],
      getRowId: (row: Row) => row.id, sourceDescription: "Paged source", emptyText: "No matches", ariaLabel: "Records" };
    const { rerender } = render(<TableWorkbench {...props} sourceKey="book-a" />);
    await user.click(screen.getByText("Table tools", { selector: "summary" }));
    expect(screen.getByRole("button", { name: "Export CSV" })).toBeDisabled();
    expect(downloadCsv).not.toHaveBeenCalled();
    rerender(<TableWorkbench {...props} sourceKey="book-b" />);
    expect(screen.getByText("Table tools", { selector: "summary" }).closest("details")).not.toHaveAttribute("open");
  });

  it("uses singular and plural result counts in the toolbar and complete export copy", async () => {
    const user = userEvent.setup();
    const props = { workbenchId: "counts", columns, getRowId: (row: Row) => row.id,
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
    await user.click(screen.getByText("Save view", { selector: "summary" }));
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
    await user.click(screen.getByText("Save view", { selector: "summary" }));
    await screen.findByRole("option", { name: "Smallest first" });
    await user.selectOptions(screen.getByLabelText("Saved views"), "sorted");
    expect(within(screen.getByRole("table", { name: "Records" })).getAllByRole("row").slice(1).map(row => row.textContent)).toEqual(["b2", "a10"]);
    await user.click(screen.getByRole("button", { name: "Export CSV" }));
    const csv = vi.mocked(downloadCsv).mock.calls[0][1];
    expect(csv.indexOf("b,2,")).toBeLessThan(csv.indexOf("a,10,"));
  });

  it("restores a legacy view then saves an explicit personal copy without claiming an owner", async () => {
    const user = userEvent.setup();
    vi.mocked(getJson).mockResolvedValue([{ viewId: "legacy", label: "Prior view", searchText: "a",
      columnIds: ["amount", "id"], density: "comfortable", isReadOnly: true }]);
    vi.mocked(postJson).mockResolvedValue({ viewId: "personal", label: "Prior view copy", searchText: "a", columnIds: ["amount", "id"], density: "comfortable" });
    render(<TableWorkbench workbenchId="test" columns={columns} rows={[{ id: "a", amount: 1 }]} getRowId={row => row.id}
      getSearchText={row => row.id} completePopulation sourceDescription="Complete snapshot" emptyText="No matches" ariaLabel="Records" />);
    await user.click(screen.getByText("Save view", { selector: "summary" }));
    await waitFor(() => expect(screen.getByRole("option", { name: /Prior view/ })).toBeInTheDocument());
    await user.selectOptions(screen.getByLabelText("Saved views"), "legacy");
    expect(screen.getByRole("searchbox")).toHaveValue("a");
    const headers = within(screen.getByRole("table", { name: "Records" })).getAllByRole("columnheader");
    expect(headers.map(header => header.textContent)).toEqual(["Amount", "Record"]);
    await user.click(screen.getByRole("button", { name: "Save to my account" }));
    await waitFor(() => expect(postJson).toHaveBeenCalledWith("/api/workstation/tables/test/views", expect.objectContaining({
      label: "Prior view copy", searchText: "a", columnIds: ["amount", "id"], density: "comfortable"
    }), expect.objectContaining({ allowDevelopmentFallback: false })));
    expect(screen.getByRole("status")).toHaveTextContent("Saved Prior view copy to your account");
  });
});
