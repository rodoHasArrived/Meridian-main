import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { exportLedgerPeriodJournalCsv } from "@/lib/ledger-reports-api";
import { PostedJournalCsvButton } from "./PostedJournalCsvButton";

vi.mock("@/lib/ledger-reports-api", () => ({ exportLedgerPeriodJournalCsv: vi.fn() }));
beforeEach(() => {
  vi.clearAllMocks();
  vi.stubGlobal("URL", Object.assign(URL, { createObjectURL: vi.fn().mockReturnValue("blob:journal"), revokeObjectURL: vi.fn() }));
});
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); });

it("downloads server CSV bytes for the complete selected period", async () => {
  const bytes = new Blob(["date,debit,credit\n2026-11-20,0.123456789012,0"]);
  vi.mocked(exportLedgerPeriodJournalCsv).mockResolvedValue(bytes);
  vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => undefined);
  render(<PostedJournalCsvButton periodId="period-a" bookId="book-a" />);
  await userEvent.click(screen.getByRole("button", { name: "Download journal CSV" }));
  expect(await screen.findByRole("status")).toHaveTextContent("Complete period journal downloaded");
  expect(exportLedgerPeriodJournalCsv).toHaveBeenCalledWith("period-a", expect.objectContaining({ signal: expect.any(AbortSignal) }));
  expect(URL.createObjectURL).toHaveBeenCalledWith(bytes);
});

it("aborts and discards late bytes when the book or period changes", async () => {
  let resolve!: (bytes: Blob) => void;
  vi.mocked(exportLedgerPeriodJournalCsv).mockImplementation(() => new Promise((done) => { resolve = done; }));
  const click = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => undefined);
  const { rerender } = render(<PostedJournalCsvButton periodId="period-a" bookId="book-a" />);
  await userEvent.click(screen.getByRole("button", { name: "Download journal CSV" }));
  const signal = vi.mocked(exportLedgerPeriodJournalCsv).mock.calls[0][1]?.signal;
  rerender(<PostedJournalCsvButton periodId="period-b" bookId="book-b" />);
  expect(signal?.aborted).toBe(true);
  await act(async () => resolve(new Blob(["old scope"])));
  expect(click).not.toHaveBeenCalled();
  expect(screen.queryByRole("status")).not.toBeInTheDocument();
});

it("reports a refused export and allows an explicit retry", async () => {
  vi.mocked(exportLedgerPeriodJournalCsv).mockRejectedValue(new Error("Export permission required"));
  render(<PostedJournalCsvButton periodId="period-a" bookId="book-a" />);
  await userEvent.click(screen.getByRole("button", { name: "Download journal CSV" }));
  expect(await screen.findByRole("status")).toHaveTextContent("Export permission required");
  expect(screen.getByRole("button", { name: "Download journal CSV" })).toBeEnabled();
  expect(URL.createObjectURL).not.toHaveBeenCalled();
});
