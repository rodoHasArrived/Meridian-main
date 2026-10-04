import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { RecurringJournalQueue } from "./RecurringJournalQueue";
import { getRecurringJournalQueue, type RecurringJournalQueue as Queue } from "@/lib/api/recurring-journals.api";

vi.mock("@/lib/api/recurring-journals.api", () => ({ getRecurringJournalQueue: vi.fn() }));
const scope = { fundProfileId: "fund-a", ledgerBookId: "book-a", entityId: "entity-a" };
const queue: Queue = { ...scope, occurrences: [{
  ...scope, occurrenceId: "occurrence-1", scheduleId: "rent", scheduleVersion: 3,
  templateId: "rent-template", templateVersion: 7, effectiveDate: "2026-10-01",
  periodId: "2026-10", state: "Drafted", journalEntryId: "draft-1", approvalStatus: "Draft",
  blockers: [], sourceEvidenceReferences: ["evidence://rent/2026-10"], periodLockOwner: null, governedReopenPath: null
}] };

beforeEach(() => vi.resetAllMocks());

it("shows exact versions, retained draft and evidence, and selects that draft for human review", async () => {
  vi.mocked(getRecurringJournalQueue).mockResolvedValue(queue);
  const select = vi.fn();
  render(<RecurringJournalQueue scope={scope} availableDraftIds={["draft-1"]} onSelectDraft={select} />);
  expect(await screen.findByText("rent · v3")).toBeInTheDocument();
  expect(screen.getByText("Template rent-template · v7")).toBeInTheDocument();
  expect(screen.getByText("Drafted · Approval: Draft")).toBeInTheDocument();
  await userEvent.click(screen.getByText("Source evidence (1)"));
  expect(screen.getByText("evidence://rent/2026-10")).toBeVisible();
  await userEvent.click(screen.getByRole("button", { name: "Review retained draft" }));
  expect(select).toHaveBeenCalledExactlyOnceWith("draft-1");
});

it("shows definition changes and locked period ownership and governed reopen without creating a draft", async () => {
  vi.mocked(getRecurringJournalQueue).mockResolvedValue({ ...queue, occurrences: [{ ...queue.occurrences[0],
    state: "DefinitionChanged", journalEntryId: null, approvalStatus: null,
    blockers: ["Template version no longer matches; explicitly supersede this occurrence.", "Period is locked."],
    periodLockOwner: "controller-a", governedReopenPath: "Accounting > Close > Request governed reopen"
  }] });
  render(<RecurringJournalQueue scope={scope} availableDraftIds={[]} onSelectDraft={vi.fn()} />);
  expect(await screen.findByText(/DefinitionChanged · Approval: Not submitted/)).toBeInTheDocument();
  expect(screen.getByText(/Template version no longer matches/)).toBeInTheDocument();
  expect(screen.getByText("Period lock owner: controller-a")).toBeInTheDocument();
  expect(screen.getByText(/Governed reopen: Accounting/)).toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "Review retained draft" })).not.toBeInTheDocument();
});

it("refuses unavailable evidence as an empty queue and retries the shared read", async () => {
  vi.mocked(getRecurringJournalQueue).mockRejectedValueOnce(new Error("Durable occurrence store unavailable")).mockResolvedValueOnce(queue);
  render(<RecurringJournalQueue scope={scope} availableDraftIds={[]} onSelectDraft={vi.fn()} />);
  expect(await screen.findByRole("alert")).toHaveTextContent("Durable occurrence store unavailable");
  expect(screen.queryByText(/No retained recurring occurrences/)).not.toBeInTheDocument();
  await userEvent.click(screen.getByRole("button", { name: "Retry recurring queue" }));
  expect(await screen.findByText("rent · v3")).toBeInTheDocument();
});

it("hides old scope rows and ignores a late response after entity selection changes", async () => {
  let resolveFirst!: (result: Queue) => void;
  vi.mocked(getRecurringJournalQueue).mockImplementationOnce(() => new Promise((resolve) => { resolveFirst = resolve; }))
    .mockResolvedValueOnce({ ...scope, entityId: "entity-b", occurrences: [] });
  const { rerender } = render(<RecurringJournalQueue scope={scope} availableDraftIds={[]} onSelectDraft={vi.fn()} />);
  rerender(<RecurringJournalQueue scope={{ ...scope, entityId: "entity-b" }} availableDraftIds={[]} onSelectDraft={vi.fn()} />);
  await screen.findByText("No retained recurring occurrences for this scope.");
  await act(async () => resolveFirst(queue));
  expect(screen.queryByText("rent · v3")).not.toBeInTheDocument();
});

it("does not request unscoped data", async () => {
  render(<RecurringJournalQueue scope={null} availableDraftIds={[]} onSelectDraft={vi.fn()} />);
  await waitFor(() => expect(screen.getByText(/Select a fund, ledger book and entity/)).toBeInTheDocument());
  expect(getRecurringJournalQueue).not.toHaveBeenCalled();
});
