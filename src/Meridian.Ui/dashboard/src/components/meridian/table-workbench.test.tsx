import { render, screen, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { TableWorkbench, type TableWorkbenchColumn } from "@/components/meridian/table-workbench";
import { getJson, postJson } from "@/lib/api";
import { downloadCsv } from "@/lib/csv";

vi.mock("@/lib/api", () => ({ getJson: vi.fn(), postJson: vi.fn() }));
vi.mock("@/lib/csv", async importOriginal => ({ ...await importOriginal<typeof import("@/lib/csv")>(), downloadCsv: vi.fn(() => true) }));
interface Row { id: string; amount: number; }
const columns: TableWorkbenchColumn<Row>[] = [
  { id: "id", label: "Record", render: row => row.id, exportValue: row => row.id },
  { id: "amount", label: "Amount", render: row => row.amount, exportValue: row => row.amount }
];
beforeEach(() => { vi.clearAllMocks(); vi.mocked(getJson).mockResolvedValue([]); });

describe("TableWorkbench", () => {
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
