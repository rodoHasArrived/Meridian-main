import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AccrualScheduleWorkbench } from "./accrual-schedule-workbench";
import { ApiError } from "@/lib/api-errors";
import * as api from "@/lib/api/accrual-schedules.api";
import * as recurring from "@/lib/api/recurring-journals.api";
vi.mock("@/lib/api/accrual-schedules.api", () => ({ getAccrualSchedules: vi.fn(), previewAccrualSchedule: vi.fn(), configureAccrualSchedule: vi.fn(), runAccrualSchedule: vi.fn() }));
vi.mock("@/lib/api/recurring-journals.api", () => ({ getRecurringJournalQueue: vi.fn() }));
const scope = { fundProfileId: "fund", ledgerBookId: "00000000-0000-0000-0000-000000000001", entityId: "entity" };
const props = { scope, availableDraftIds: ["draft"], onSelectDraft: vi.fn(), onDraftsGenerated: vi.fn() };
const evidence = [{ evidenceId: "ev", uri: "retained://ev", kind: "Accrual", sourceSystem: "Source", retainedBy: "custodian", retainedAtUtc: "2026-10-01T00:00:00Z", evidenceVersion: 1 }];
const preview = { fingerprint: "a".repeat(64), currency: "USD", occurrences: [{ effectiveDate: "2026-10-01", periodId: "period", version: 2, isOpen: true, blocker: null, lockOwner: null, reopenPath: null, amount: "123.123456789123456789", debitAccount: "Expense:Fees", creditAccount: "Liability:Accruals" }] };
function expectDisabledPrimaryReasons() {
  const disabled = Array.from(document.querySelectorAll<HTMLButtonElement>("button.mds-btn--primary"))
    .filter(button => button.matches(":disabled"));
  expect(disabled.length).toBeGreaterThan(0);
  for (const button of disabled) {
    expect(button).toHaveAttribute("disabled");
    expect(button.title.trim()).not.toBe("");
  }
}
async function fill() {
  await waitFor(() => expect(screen.getByRole("button", { name: "Preview accrual schedule", hidden: true })).toBeEnabled());
  fireEvent.click(screen.getByText("Configure a new accrual schedule"));
  for (const [label, value] of [["Schedule identity", "fees"], ["Accrual description", "Reviewed fee"], ["Book currency", "USD"], ["Amount per occurrence", "123.123456789123456789"], ["First accrual date", "2026-10-01"], ["Last accrual date", "2026-11-01"], ["Debit account path", "Expense:Fees"], ["Credit account path", "Liability:Accruals"], ["Evidence identity", "ev"], ["Retained evidence location", "retained://ev"], ["Evidence source system", "Source"], ["Evidence retained at (UTC)", "2026-10-01T00:00:00Z"], ["Evidence custodian", "custodian"], ["Evidence version (or supply SHA-256)", "1"]]) fireEvent.change(screen.getByLabelText(label!), { target: { value } });
  fireEvent.click(screen.getByRole("button", { name: "Preview accrual schedule" }));
  await screen.findByRole("region", { name: "Accrual occurrence preview" });
}
beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(api.getAccrualSchedules).mockResolvedValue({ canConfigure: true, schedules: [] });
  vi.mocked(recurring.getRecurringJournalQueue).mockResolvedValue({ ...scope, occurrences: [] });
  vi.mocked(api.previewAccrualSchedule).mockResolvedValue(preview);
  vi.mocked(api.configureAccrualSchedule).mockResolvedValue({ scheduleId: "accrual:fees", version: 1, contentHash: "b".repeat(64) });
  vi.mocked(api.runAccrualSchedule).mockResolvedValue([]);
});
describe("accrual schedule journey", () => {
  it("previews exact inputs, retains reviewed schedule, generates due occurrences and hands off a retained draft", async () => {
    render(<AccrualScheduleWorkbench {...props} />); await fill();
    expect(api.previewAccrualSchedule).toHaveBeenCalledWith(expect.objectContaining({ ...scope, amount: "123.123456789123456789", evidence: [expect.objectContaining(evidence[0]!)] }), expect.anything());
    expect(screen.getByRole("button", { name: "Retain accrual schedule" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Retain accrual schedule" })).toHaveAttribute("title", expect.stringContaining("confirm the review"));
    expectDisabledPrimaryReasons();
    fireEvent.click(screen.getByRole("checkbox")); fireEvent.click(screen.getByRole("button", { name: "Retain accrual schedule" }));
    await screen.findByRole("button", { name: "Generate due accrual drafts" });
    expect(screen.getByRole("button", { name: "Retain accrual schedule" })).toHaveAttribute("title", expect.stringContaining("already retained"));
    expectDisabledPrimaryReasons();
    vi.mocked(recurring.getRecurringJournalQueue).mockResolvedValue({ ...scope, occurrences: [{ ...scope, occurrenceId: "occ", scheduleId: "accrual:fees", scheduleVersion: 1, templateId: "template", templateVersion: 1, effectiveDate: "2026-10-01", periodId: "period", state: "Drafted", journalEntryId: "draft", approvalStatus: "Draft", blockers: [], sourceEvidenceReferences: ["retained://ev"], periodLockOwner: null, governedReopenPath: null }] });
    fireEvent.click(screen.getByRole("button", { name: "Generate due accrual drafts" }));
    await screen.findByRole("button", { name: "Review retained draft" });
    expect(api.runAccrualSchedule).toHaveBeenCalledWith("accrual:fees", scope, 1, expect.anything());
    expect(props.onDraftsGenerated).toHaveBeenCalledOnce();
    fireEvent.click(screen.getByRole("button", { name: "Review retained draft" })); expect(props.onSelectDraft).toHaveBeenCalledWith("draft");
  });
  it("invalidates previews on edits and scope changes", async () => {
    const { rerender } = render(<AccrualScheduleWorkbench {...props} />); await fill();
    fireEvent.change(screen.getByLabelText("Amount per occurrence"), { target: { value: "9" } });
    expect(screen.queryByRole("region", { name: "Accrual occurrence preview" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Preview accrual schedule" })); await screen.findByRole("region", { name: "Accrual occurrence preview" });
    rerender(<AccrualScheduleWorkbench {...props} scope={{ ...scope, entityId: "other" }} />);
    expect(screen.queryByRole("region", { name: "Accrual occurrence preview" })).not.toBeInTheDocument();
  });
  it("explains disabled primary actions while permissions load and while due drafts are generated", async () => {
    vi.mocked(api.getAccrualSchedules).mockReturnValueOnce(new Promise(() => {}));
    const { unmount } = render(<AccrualScheduleWorkbench {...props} />);
    const loadingPreview = screen.getByRole("button", { name: "Preview accrual schedule", hidden: true });
    expect(loadingPreview).toHaveAttribute("title", expect.stringContaining("finish loading"));
    expectDisabledPrimaryReasons();
    unmount();
    render(<AccrualScheduleWorkbench {...props} />); await fill();
    fireEvent.click(screen.getByRole("checkbox")); fireEvent.click(screen.getByRole("button", { name: "Retain accrual schedule" }));
    await screen.findByRole("button", { name: "Generate due accrual drafts" });
    let finishRun!: (value: unknown) => void;
    vi.mocked(api.runAccrualSchedule).mockReturnValueOnce(new Promise(resolve => { finishRun = resolve; }));
    fireEvent.click(screen.getByRole("button", { name: "Generate due accrual drafts" }));
    expect(screen.getByRole("button", { name: "Generate due accrual drafts" })).toHaveAttribute("title", expect.stringContaining("journal workbench refresh"));
    expectDisabledPrimaryReasons();
    finishRun([]);
    await waitFor(() => expect(screen.getByRole("button", { name: "Generate due accrual drafts" })).toBeEnabled());
  });
  it("keeps denied and unavailable controls inert", async () => {
    vi.mocked(api.getAccrualSchedules).mockResolvedValue({ canConfigure: false, schedules: [] });
    const { unmount } = render(<AccrualScheduleWorkbench {...props} />); await screen.findByText(/Read only: ManageLedgerReports/);
    expect(screen.getByRole("button", { name: "Preview accrual schedule", hidden: true })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Preview accrual schedule", hidden: true })).toHaveAttribute("title", expect.stringContaining("Schedule management permission"));
    expectDisabledPrimaryReasons(); unmount();
    vi.mocked(api.getAccrualSchedules).mockRejectedValue(new Error("Registry offline")); render(<AccrualScheduleWorkbench {...props} />);
    await screen.findByRole("alert"); expect(screen.getByRole("button", { name: "Preview accrual schedule", hidden: true })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Preview accrual schedule", hidden: true })).toHaveAttribute("title", expect.stringContaining("Refresh retained schedules"));
    expectDisabledPrimaryReasons();
  });
  it("allows a fresh preview after a known stale-authority refusal", async () => {
    vi.mocked(api.configureAccrualSchedule).mockRejectedValueOnce(new ApiError({ path: "/accrual", status: 409,
      title: "Stale preview", responseBody: '{"code":"ACCRUAL_PREVIEW_STALE"}' }));
    render(<AccrualScheduleWorkbench {...props} />); await fill();
    fireEvent.click(screen.getByRole("checkbox")); fireEvent.click(screen.getByRole("button", { name: "Retain accrual schedule" }));
    await screen.findByText(/The schedule was refused/);
    expect(screen.queryByRole("region", { name: "Accrual occurrence preview" })).not.toBeInTheDocument();
    expect(screen.getByLabelText("Amount per occurrence")).toBeEnabled();
  });
  it("keeps an unconfirmed save locked through failed refresh and retries the original command", async () => {
    vi.mocked(api.configureAccrualSchedule).mockRejectedValueOnce(new Error("Connection lost"));
    render(<AccrualScheduleWorkbench {...props} />); await fill();
    fireEvent.click(screen.getByRole("checkbox")); fireEvent.click(screen.getByRole("button", { name: "Retain accrual schedule" }));
    await screen.findByRole("button", { name: "Retry same accrual schedule" });
    expect(screen.getByLabelText("Amount per occurrence")).toBeDisabled();
    expect(screen.getByRole("button", { name: "Preview accrual schedule" })).toHaveAttribute("title", expect.stringContaining("recover the unconfirmed save"));
    expectDisabledPrimaryReasons();
    vi.mocked(api.getAccrualSchedules).mockRejectedValueOnce(new Error("Registry offline"));
    fireEvent.click(screen.getByRole("button", { name: "Refresh retained schedules" }));
    await screen.findByRole("alert");
    expect(screen.getByLabelText("Amount per occurrence")).toBeDisabled();
    expect(screen.getByRole("button", { name: "Retry same accrual schedule" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Refresh retained schedules" }));
    await waitFor(() => expect(screen.getByRole("button", { name: "Retry same accrual schedule" })).toBeEnabled());
    fireEvent.click(screen.getByRole("button", { name: "Retry same accrual schedule" }));
    await screen.findByRole("button", { name: "Generate due accrual drafts" });
    expect(vi.mocked(api.configureAccrualSchedule).mock.calls[1]?.[0]).toEqual(vi.mocked(api.configureAccrualSchedule).mock.calls[0]?.[0]);
  });
  it("shows closed-period blockers and refuses configuration", async () => {
    vi.mocked(api.previewAccrualSchedule).mockResolvedValue({ ...preview, occurrences: preview.occurrences.map(row => ({ ...row, isOpen: false, blocker: "Period locked by controller" })) });
    render(<AccrualScheduleWorkbench {...props} />); await fill(); expect(screen.getByText(/Period locked by controller/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("checkbox")); expect(screen.getByRole("button", { name: "Retain accrual schedule" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Retain accrual schedule" })).toHaveAttribute("title", expect.stringContaining("closed-period blockers"));
    expectDisabledPrimaryReasons();
  });
});
