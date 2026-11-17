import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { axe } from "jest-axe";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithRouter } from "@/test/render";
import { getEntityOperation, postEntityOperation } from "@/lib/api/entity-operations.api";
import { ApiError } from "@/lib/api-errors";
import type { LedgerBook } from "@/types";
import type { EntityOperationScope, IntercompanyAccountingArrangement } from "@/types/entity-operations";
import type { IntercompanyAccrualSchedule, IntercompanyAccrualScheduleRun } from "@/types/intercompany-accrual-schedules";
import { EntityAccrualSchedules } from "./accounting-screen.entity-accrual-schedules";
import { EntityOperationsPanel } from "./accounting-screen.entity-operations";
import { today } from "./accounting-screen.entity-operations.shared";

vi.mock("@/lib/api/entity-operations.api", async () => ({
  ...await vi.importActual<typeof import("@/lib/api/entity-operations.api")>("@/lib/api/entity-operations.api"),
  getEntityOperation: vi.fn(), postEntityOperation: vi.fn()
}));

// Synthetic fixtures: a lender book, an approved loan-interest profile, its principal profile and an unrelated loan.
const book: LedgerBook = { ledgerBookId: "book-a", fundProfileId: "group", fundStructureNodeId: "entity-a", fundStructureNodeKind: "Entity",
  displayName: "Entity A primary", baseCurrency: "USD", createdAt: "2026-01-01", updatedAt: "2026-01-01", accountingBasis: "Primary", accountingPolicyId: "operating", accountingPolicyVersion: "v1" };
const capabilities = { canManageAgreements: true, canManageLedger: true, canManageLifecycle: true, canViewLedger: true };
const scope: EntityOperationScope = { book, books: [book], graph: null, capabilities };
const readOnly: EntityOperationScope = { ...scope, capabilities: { ...capabilities, canManageLedger: false } };
function profile(arrangementId: string, templateKind: "Loan" | "LoanInterest", reference: string, canonicalAgreementId = "legal-loan"): IntercompanyAccountingArrangement {
  return { arrangementId, templateKind, canonicalAgreementId, arrangementReference: reference, status: "Approved",
    partyMappings: [{ entityId: "entity-a", ledgerBookId: "book-a" }] } as unknown as IntercompanyAccountingArrangement;
}
const profiles = [profile("interest-a", "LoanInterest", "Loan interest A"), profile("interest-a", "LoanInterest", "Loan interest A"),
  profile("principal-a", "Loan", "Loan principal A"), profile("principal-other", "Loan", "Other loan", "another-agreement")];
const proposedEvent = { eventId: "event-1", requestId: "request-1", action: "Propose" as const, actor: "maker", recordedAt: "2026-11-01T09:00:00Z", reason: "Monthly accrual" };
const proposed: IntercompanyAccrualSchedule = { scheduleId: "schedule-a", tenantId: "tenant-a", companyId: "company-a", ledgerBookId: "book-a", kind: "LoanInterest",
  profileArrangementId: "interest-a", principalArrangementId: "principal-a", version: 1, status: "Proposed", events: [proposedEvent] };
const active: IntercompanyAccrualSchedule = { ...proposed, version: 2, status: "Active", activeFrom: "2026-11-02",
  events: [proposedEvent, { eventId: "event-2", requestId: "request-2", action: "Approve", actor: "checker", recordedAt: "2026-11-02T09:00:00Z" }] };
const dueRun: IntercompanyAccrualScheduleRun = { scheduleId: "schedule-a", asOf: today(), executed: false,
  windows: [{ accrualFrom: "2026-10-01", accrualTo: "2026-11-01", outcome: "Due" }] };
const executedRun: IntercompanyAccrualScheduleRun = { scheduleId: "schedule-a", asOf: today(), executed: true, windows: [
  { accrualFrom: "2026-10-01", accrualTo: "2026-11-01", outcome: "Drafted", draftId: "draft-a", draftStatus: "Draft" },
  { accrualFrom: "2026-11-01", accrualTo: "2026-12-01", outcome: "Refused", reason: "Exactly one open source period must contain the last accrued day." }] };

function serve(schedules: IntercompanyAccrualSchedule[]) {
  vi.mocked(getEntityOperation).mockImplementation(async (path) => {
    if (path.includes("/due?")) return dueRun;
    if (path.startsWith("/api/ledger/intercompany/accrual-schedules?")) return [...schedules];
    if (path.startsWith("/api/ledger/intercompany-accounting-arrangements?")) return profiles;
    if (path.includes("capabilities")) return capabilities;
    if (path === "/api/ledger/books") return [book];
    return null;
  });
}
const lastBody = () => vi.mocked(postEntityOperation).mock.lastCall![1] as Record<string, unknown>;

beforeEach(() => { vi.clearAllMocks(); });

describe("Intercompany accrual schedules", () => {
  it("proposes a schedule from an approved loan interest profile and its principal profile mapping this book", async () => {
    const retained: IntercompanyAccrualSchedule[] = [];
    serve(retained);
    vi.mocked(postEntityOperation).mockImplementation(async () => { retained.push(proposed); return proposed; });
    const { container } = renderWithRouter(<EntityAccrualSchedules scope={scope} />);
    expect(screen.getByText(/A schedule only drafts/)).toBeVisible();
    expect(screen.getByText(/Every draft still needs its own journal review, and nothing posts/)).toBeVisible();
    expect(screen.getByText(/refuses is retried on the next run/)).toBeVisible();
    expect(screen.getByText(/proposes a schedule cannot approve it/)).toBeVisible();
    await screen.findByText("No accrual schedules for this book.");
    expect(getEntityOperation).toHaveBeenCalledWith(`/api/ledger/intercompany-accounting-arrangements?bookId=book-a&asOf=${today()}`, expect.any(Object));
    await waitFor(() => expect(screen.getByLabelText("Schedule loan interest profile").querySelectorAll("option")).toHaveLength(1));
    // Only a Loan profile of the same legal agreement can supply principal.
    expect(screen.getByLabelText("Schedule loan principal profile").querySelectorAll("option")).toHaveLength(1);
    expect(screen.getByRole("button", { name: "Propose accrual schedule" })).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Schedule proposal reason"), { target: { value: "Monthly accrual" } });
    fireEvent.click(screen.getByRole("button", { name: "Propose accrual schedule" }));
    await screen.findByText(/It drafts nothing until a different operator approves it/);
    expect(postEntityOperation).toHaveBeenCalledWith("/api/ledger/intercompany/accrual-schedules", {
      requestId: expect.any(String), ledgerBookId: "book-a", kind: "LoanInterest", profileArrangementId: "interest-a", principalArrangementId: "principal-a", reason: "Monthly accrual"
    }, expect.objectContaining({ signal: expect.any(AbortSignal) }));
    expect(await screen.findByRole("region", { name: "Selected accrual schedule" })).toHaveTextContent("Proposed · version 1");
    expect((await axe(container)).violations).toEqual([]);
  });

  it("approves the exact version seen, shows a self-approval refusal and reuses identity only for an identical retry", async () => {
    serve([proposed]);
    vi.mocked(postEntityOperation).mockRejectedValue(new ApiError({ path: "/api/ledger/intercompany/accrual-schedules/schedule-a/decisions", status: 409,
      detail: "A schedule must be approved by someone other than its proposer." }));
    renderWithRouter(<EntityAccrualSchedules scope={scope} />);
    fireEvent.click(await screen.findByRole("button", { name: "Loan interest A · interest-a" }));
    const detail = screen.getByRole("region", { name: "Selected accrual schedule" });
    expect(within(detail).getByText(/Proposed by maker/)).toBeVisible();
    expect(within(detail).getByRole("button", { name: "Reject schedule" })).toBeDisabled();
    expect(within(detail).queryByRole("button", { name: "Run now" })).not.toBeInTheDocument();
    fireEvent.click(within(detail).getByRole("button", { name: "Approve schedule" }));
    expect(await screen.findByText(/someone other than its proposer/)).toBeVisible();
    expect(postEntityOperation).toHaveBeenCalledWith("/api/ledger/intercompany/accrual-schedules/schedule-a/decisions?ledgerBookId=book-a",
      { requestId: expect.any(String), action: "Approve", expectedVersion: 1, reason: null }, expect.any(Object));
    const firstIdentity = lastBody().requestId;
    fireEvent.click(within(detail).getByRole("button", { name: "Approve schedule" }));
    await waitFor(() => expect(postEntityOperation).toHaveBeenCalledTimes(2));
    expect(lastBody().requestId).toBe(firstIdentity);

    vi.mocked(postEntityOperation).mockResolvedValue({ ...proposed, version: 2, status: "Rejected" });
    fireEvent.change(within(detail).getByLabelText("Schedule decision reason"), { target: { value: "Wrong principal profile" } });
    fireEvent.click(within(detail).getByRole("button", { name: "Reject schedule" }));
    await screen.findByText(/Accrual schedule rejected/);
    expect(lastBody()).toMatchObject({ action: "Reject", expectedVersion: 1, reason: "Wrong principal profile" });
    expect(lastBody().requestId).not.toBe(firstIdentity);
  });

  it("previews due periods, runs an active schedule and requires a reason to disable it", async () => {
    serve([active]);
    vi.mocked(postEntityOperation).mockResolvedValue(executedRun);
    const { container } = renderWithRouter(<EntityAccrualSchedules scope={scope} />);
    fireEvent.click(await screen.findByRole("button", { name: "Loan interest A · interest-a" }));
    const detail = screen.getByRole("region", { name: "Selected accrual schedule" });
    expect(within(detail).getByText(/ending on or after 2026-11-02/)).toBeVisible();
    fireEvent.click(within(detail).getByRole("button", { name: "Preview due periods" }));
    expect(await within(detail).findByText("2026-10-01 up to 2026-11-01")).toBeVisible();
    expect(within(detail).getByText(/preview only, nothing was drafted/)).toBeVisible();
    expect(getEntityOperation).toHaveBeenCalledWith(`/api/ledger/intercompany/accrual-schedules/schedule-a/due?ledgerBookId=book-a&asOf=${today()}`, expect.any(Object));
    expect(postEntityOperation).not.toHaveBeenCalled();

    fireEvent.click(within(detail).getByRole("button", { name: "Run now" }));
    expect(await within(detail).findByRole("link", { name: "Open draft draft-a · Draft" })).toHaveAttribute("href", "/accounting/journal-entries?ledgerBookId=book-a&journalEntryId=draft-a");
    expect(postEntityOperation).toHaveBeenCalledWith("/api/ledger/intercompany/accrual-schedules/schedule-a/run?ledgerBookId=book-a", undefined, expect.any(Object));
    expect(within(detail).getByText(/Exactly one open source period/)).toBeVisible();
    expect(within(detail).getByText(/nothing was posted/)).toBeVisible();
    expect((await axe(container)).violations).toEqual([]);

    expect(within(detail).getByRole("button", { name: "Disable schedule" })).toBeDisabled();
    vi.mocked(postEntityOperation).mockResolvedValue({ ...active, version: 3, status: "Disabled" });
    fireEvent.change(within(detail).getByLabelText("Schedule decision reason"), { target: { value: "Loan repaid" } });
    fireEvent.click(within(detail).getByRole("button", { name: "Disable schedule" }));
    await screen.findByText(/Drafts already created are unchanged/);
    expect(lastBody()).toMatchObject({ action: "Disable", expectedVersion: 2, reason: "Loan repaid" });
  });

  it("shows read-only operators schedules, history and due periods without mutation controls", async () => {
    serve([active]);
    renderWithRouter(<EntityAccrualSchedules scope={readOnly} />);
    fireEvent.click(await screen.findByRole("button", { name: "interest-a" }));
    expect(screen.getByText(/Ledger management permission is required to propose/)).toBeVisible();
    expect(screen.queryByRole("button", { name: "Propose accrual schedule" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Run now" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Disable schedule" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByText("Accrual schedule history"));
    expect(screen.getByRole("list", { name: "Accrual schedule events" })).toHaveTextContent(/Approve · checker/);
    expect(screen.getByRole("list", { name: "Accrual schedule events" })).not.toHaveTextContent("2026-11-02T09:00:00Z");
    fireEvent.click(screen.getByRole("button", { name: "Preview due periods" }));
    expect(await screen.findByText("2026-10-01 up to 2026-11-01")).toBeVisible();
    expect(vi.mocked(getEntityOperation).mock.calls.some(([path]) => path.includes("intercompany-accounting-arrangements"))).toBe(false);
    expect(postEntityOperation).not.toHaveBeenCalled();
  });

  it("is reachable as an entity operations tab on the selected book", async () => {
    serve([proposed]);
    renderWithRouter(<EntityOperationsPanel book={book} />);
    fireEvent.click(await screen.findByRole("tab", { name: "Accrual schedules" }));
    expect(await screen.findByRole("heading", { name: "Intercompany accrual schedules" })).toBeVisible();
    await waitFor(() => expect(getEntityOperation).toHaveBeenCalledWith("/api/ledger/intercompany/accrual-schedules?ledgerBookId=book-a", expect.any(Object)));
  });
});
