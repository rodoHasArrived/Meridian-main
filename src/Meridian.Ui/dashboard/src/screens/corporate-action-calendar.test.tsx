import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { renderWithRouter } from "@/test/render";
import { CorporateActionCalendarPanel } from "@/screens/corporate-action-calendar";
import { CorporateActionDeadlinePanel } from "@/screens/corporate-action-deadline-panel";
import { corporateActionCalendarRange, updateCorporateActionCalendarSearch } from "@/screens/corporate-action-calendar.view-state";
import * as api from "@/lib/api/corporate-action-calendar.api";
import { ApiError } from "@/lib/api-errors";
import type { CorporateActionCalendarPage, CorporateActionCaseDeadlines, CorporateActionDeadlineMutationResult } from "@/types/corporate-action-calendar.types";

vi.mock("@/lib/api/corporate-action-calendar.api", () => ({ getCorporateActionCalendar: vi.fn(), getCorporateActionCaseDeadlines: vi.fn(), recordCorporateActionDeadline: vi.fn(), previewCorporateActionDeadlineImport: vi.fn(), importCorporateActionDeadlines: vi.fn() }));
const scope = { tenantId: "tenant-1", companyId: "company-1" };
const detail: CorporateActionCaseDeadlines = { case: { caseId: "case-1", proposalId: "proposal-1", securityId: "security-1", corporateActionId: "action-1", scope, version: 1, state: "Pending", createdBy: "Operator", createdAtUtc: "2026-11-10T12:00:00Z", updatedBy: "Operator", updatedAtUtc: "2026-11-10T12:00:00Z" }, history: [], canRecord: true };
const calendar: CorporateActionCalendarPage = { from: "2026-11-12", to: "2026-11-18", generatedAtUtc: "2026-11-12T12:00:00Z", revision: "v1", totalCount: 1, coverage: "Retained sources only.", items: [{ eventId: "event-1", securityId: "security-1", ticker: "ABC", actionType: "Dividend", proposalId: "proposal-1", dateKind: "ExDate", date: "2026-11-12", cases: [{ caseId: "case-1", version: 1, state: "Pending", scope }], source: { providerId: "TEST", sourceEventId: "source-1", sourceEventVersion: "1", observedAtUtc: "2026-11-11T12:00:00Z", releaseStatus: "ReviewOnly" }, proposalState: "Observed" }] };
const receipt = { case: { ...detail.case, version: 2 }, evidence: { evidenceId: "retained-1", caseId: "case-1", evidenceKind: "ElectionDeadline", evidenceReference: "document://notice", metadata: {}, caseVersion: 2, recordedBy: "Operator", recordedAtUtc: "2026-11-12T12:00:00Z" }, replayed: true } satisfies CorporateActionDeadlineMutationResult;
afterEach(() => vi.resetAllMocks());
function Location() { const location = useLocation(); return <output aria-label="URL">{location.search}{location.hash}</output>; }
function QueueFeedbackCalendar() {
  const [selectedCaseId, setSelectedCaseId] = useState<string | null>("case-before");
  const [rerenders, setRerenders] = useState(0);
  const navigate = useNavigate();
  return <>
    <CorporateActionCalendarPanel scopeKey="tenant-1/company-1" selectedCaseId={selectedCaseId} onCaseSelected={(caseId) => setSelectedCaseId(caseId)} onChanged={vi.fn()} />
    <button type="button" onClick={() => setSelectedCaseId("case-other")}>Select another queue case</button>
    <button type="button" onClick={() => setRerenders((value) => value + 1)}>Rerender queue {rerenders}</button>
    <button type="button" onClick={() => void navigate(-1)}>History back</button>
    <button type="button" onClick={() => void navigate(1)}>History forward</button>
    <output aria-label="Selected queue case">{selectedCaseId}</output><Location />
  </>;
}

describe("corporate action calendar", () => {
  it("keeps source dates literal, supports a seven-day operating range, and preserves unrelated URL state", () => {
    expect(corporateActionCalendarRange("?date=2026-11-12", "2020-01-01")).toMatchObject({ from: "2026-11-09", to: "2026-11-15", error: null });
    expect(corporateActionCalendarRange("?caFrom=2026-02-30", "2020-01-01").error).toBeTruthy();
    const query = new URLSearchParams(updateCorporateActionCalendarSearch("?fundAccountId=fund-1&other=keep", { caCase: "case-1" }));
    expect(query.get("fundAccountId")).toBe("fund-1"); expect(query.get("other")).toBe("keep");
  });
  it("defaults to the ISO week across year boundaries and retains explicit ranges", () => {
    expect(corporateActionCalendarRange("?asOf=2027-01-01T12:00:00Z", "2020-01-01")).toMatchObject({ from: "2026-12-28", to: "2027-01-03" });
    expect(corporateActionCalendarRange("", "2026-11-15")).toMatchObject({ from: "2026-11-09", to: "2026-11-15" });
    expect(corporateActionCalendarRange("?date=2026-11-12&from=2026-11-10&to=2026-11-20", "2020-01-01")).toMatchObject({ from: "2026-11-10", to: "2026-11-20" });
    expect(corporateActionCalendarRange("?from=2026-11-10&to=2026-11-20&caFrom=2026-11-11&caTo=2026-11-13", "2020-01-01")).toMatchObject({ from: "2026-11-11", to: "2026-11-13" });
  });
  it("selects the exact calendar case and preserves hash and unrelated scope", async () => {
    vi.mocked(api.getCorporateActionCalendar).mockResolvedValue(calendar);
    vi.mocked(api.getCorporateActionCaseDeadlines).mockResolvedValue(detail);
    const selected = vi.fn(); const user = userEvent.setup();
    renderWithRouter(<><CorporateActionCalendarPanel scopeKey="tenant-1/company-1" onCaseSelected={selected} onChanged={vi.fn()} /><Location /></>, { initialEntries: ["/data/corporate-actions?date=2026-11-12&fundAccountId=fund-1&other=keep#queue"] });
    await user.click(await screen.findByRole("button", { name: "Ex-date for ABC on 2026-11-12" }));
    expect(selected).toHaveBeenCalledWith("case-1", "proposal-1");
    expect(screen.getByLabelText("URL")).toHaveTextContent("fundAccountId=fund-1");
    expect(screen.getByLabelText("URL")).toHaveTextContent("caCase=case-1");
    expect(screen.getByLabelText("URL")).toHaveTextContent("#queue");
    expect(await screen.findByText(/Case case-1/)).toBeInTheDocument();
  });
  it("retains marker evidence through synchronous parent queue feedback and later rerenders", async () => {
    vi.mocked(api.getCorporateActionCalendar).mockResolvedValue(calendar);
    vi.mocked(api.getCorporateActionCaseDeadlines).mockImplementation(async (caseId) => ({ ...detail, case: { ...detail.case, caseId } }));
    const user = userEvent.setup();
    renderWithRouter(<QueueFeedbackCalendar />, { initialEntries: ["/data/corporate-actions?caFrom=2026-11-12&caTo=2026-11-18&caCase=case-before&fundAccountId=fund-1&other=keep#queue"] });
    await user.click(await screen.findByRole("button", { name: "Ex-date for ABC on 2026-11-12" }));
    await screen.findByRole("heading", { name: "Selected date evidence · ABC" });
    expect(screen.getByLabelText("Selected queue case")).toHaveTextContent("case-1");
    for (const expected of ["caCase=case-1", "caEvent=event-1", "fundAccountId=fund-1", "other=keep", "#queue"]) expect(screen.getByLabelText("URL")).toHaveTextContent(expected);
    await user.click(screen.getByRole("button", { name: "Rerender queue 0" }));
    expect(screen.getByRole("heading", { name: "Selected date evidence · ABC" })).toBeInTheDocument();
    expect(screen.getByLabelText("URL")).toHaveTextContent("caEvent=event-1");
  });
  it("clears marker evidence for an external queue selection and restores URL-owned selection through history", async () => {
    vi.mocked(api.getCorporateActionCalendar).mockResolvedValue(calendar);
    vi.mocked(api.getCorporateActionCaseDeadlines).mockImplementation(async (caseId) => ({ ...detail, case: { ...detail.case, caseId } }));
    const user = userEvent.setup();
    renderWithRouter(<QueueFeedbackCalendar />, { initialEntries: ["/data/corporate-actions?caFrom=2026-11-12&caTo=2026-11-18&caCase=case-before&other=keep#queue"] });
    await user.click(await screen.findByRole("button", { name: "Ex-date for ABC on 2026-11-12" }));
    await screen.findByRole("heading", { name: "Selected date evidence · ABC" });
    await user.click(screen.getByRole("button", { name: "Select another queue case" }));
    await waitFor(() => expect(screen.getByLabelText("URL")).toHaveTextContent("caCase=case-other"));
    expect(screen.getByLabelText("URL")).not.toHaveTextContent("caEvent=");
    expect(screen.queryByRole("heading", { name: "Selected date evidence · ABC" })).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "History back" }));
    await screen.findByRole("heading", { name: "Selected date evidence · ABC" });
    expect(screen.getByLabelText("Selected queue case")).toHaveTextContent("case-1");
    expect(screen.getByLabelText("URL")).toHaveTextContent("caEvent=event-1");
    await user.click(screen.getByRole("button", { name: "History forward" }));
    await waitFor(() => expect(screen.getByLabelText("Selected queue case")).toHaveTextContent("case-other"));
    expect(screen.getByLabelText("URL")).not.toHaveTextContent("caEvent=");
    expect(screen.getByLabelText("URL")).toHaveTextContent("other=keep#queue");
  });
  it("retains an explicit unavailable selection instead of substituting the default queue case", async () => {
    vi.mocked(api.getCorporateActionCalendar).mockResolvedValue(calendar);
    vi.mocked(api.getCorporateActionCaseDeadlines).mockRejectedValue(new Error("Case not authorized or unavailable"));
    renderWithRouter(<CorporateActionCalendarPanel scopeKey="tenant-1" selectedCaseId="case-1" onCaseSelected={vi.fn()} onChanged={vi.fn()} />, { initialEntries: ["/data/corporate-actions?caFrom=2026-11-12&caTo=2026-11-18&caCase=missing&caEvent=missing"] });
    expect(await screen.findByText(/explicitly selected date event is unavailable/)).toBeInTheDocument();
    expect(await screen.findByText(/Case not authorized or unavailable/)).toBeInTheDocument();
    expect(api.getCorporateActionCaseDeadlines).toHaveBeenCalledWith("missing", expect.anything());
  });
  it("shows a validated source cutoff with its supplied offset without inventing intermediary or account scope", async () => {
    vi.mocked(api.getCorporateActionCalendar).mockResolvedValue({ ...calendar, items: [{ ...calendar.items[0], eventId: "source-cutoff", dateKind: "ElectionDeadline", at: "2026-11-12T23:30:00-05:00", cases: [], sourceCutoff: { id: "cutoff-1", label: "Instruction cutoff", deadlineAt: "2026-11-12T23:30:00-05:00", source: "Custodian notice", evidenceReference: "document://source-notice/v2" } }] });
    const user = userEvent.setup();
    renderWithRouter(<CorporateActionCalendarPanel scopeKey="tenant-1" onCaseSelected={vi.fn()} onChanged={vi.fn()} />, { initialEntries: ["/data/corporate-actions?caFrom=2026-11-12&caTo=2026-11-18"] });
    await user.click(await screen.findByRole("button", { name: "Source election deadline for ABC on 2026-11-12 at 2026-11-12T23:30:00-05:00" }));
    expect(screen.getByText(/Source-reported cutoff: Instruction cutoff/)).toHaveTextContent("2026-11-12T23:30:00-05:00");
    expect(screen.getByText(/Source-reported cutoff: Instruction cutoff/)).toHaveTextContent("document://source-notice/v2");
    expect(screen.getByText(/Intermediary role and account applicability are not reported/)).toBeInTheDocument();
    expect(screen.queryByText(/operator-recorded/)).not.toBeInTheDocument();
  });
  it("discards an old range response when the date selection changes", async () => {
    let oldResolve!: (value: CorporateActionCalendarPage) => void;
    const old = new Promise<CorporateActionCalendarPage>((resolve) => { oldResolve = resolve; });
    vi.mocked(api.getCorporateActionCalendar).mockImplementation((query) => query.from === "2026-11-12" ? old : Promise.resolve({ ...calendar, from: query.from, to: query.to, items: [], totalCount: 0 }));
    const user = userEvent.setup();
    renderWithRouter(<CorporateActionCalendarPanel scopeKey="tenant-1" onCaseSelected={vi.fn()} onChanged={vi.fn()} />, { initialEntries: ["/data/corporate-actions?caFrom=2026-11-12&caTo=2026-11-18"] });
    await user.clear(screen.getByLabelText("Calendar from"));
    await user.type(screen.getByLabelText("Calendar from"), "2026-11-13");
    await screen.findByText(/No retained date events/);
    oldResolve(calendar);
    await waitFor(() => expect(screen.queryByRole("button", { name: "Ex-date for ABC on 2026-11-12" })).not.toBeInTheDocument());
  });
});

describe("sourced deadline capture", () => {
  it("releases a definitive rejected command while preserving the source draft for correction", async () => {
    vi.mocked(api.getCorporateActionCaseDeadlines).mockResolvedValue(detail);
    vi.mocked(api.recordCorporateActionDeadline).mockRejectedValue(new ApiError({ path: "/deadlines", status: 400, detail: "The source timezone is unknown." }));
    const user = userEvent.setup(); render(<CorporateActionDeadlinePanel caseId="case-1" onChanged={vi.fn()} />);
    await user.click(await screen.findByText("Capture a sourced deadline"));
    await user.type(screen.getByLabelText("Source party"), "Issuer agent");
    await user.type(screen.getByLabelText("Source deadline date"), "2026-11-16");
    await user.type(screen.getByLabelText("Evidence URI"), "document://notice");
    await user.type(screen.getByLabelText("Operator-provided SHA-256 digest"), "a".repeat(64));
    await user.type(screen.getByLabelText("Source review rationale"), "Reviewed source notice");
    await user.type(screen.getByLabelText("Original source text"), "Election deadline is 16 November 2026.");
    await user.click(screen.getByRole("button", { name: "Review deadline" }));
    await user.click(screen.getByRole("button", { name: "Record reviewed deadline" }));
    expect(await screen.findByText(/Deadline rejected:.*source timezone is unknown/)).toBeInTheDocument();
    expect(screen.getByLabelText("Source party")).toBeEnabled();
    expect(screen.getByLabelText("Source party")).toHaveValue("Issuer agent");
    expect(screen.getByLabelText("Original source text")).toHaveValue("Election deadline is 16 November 2026.");
    expect(screen.queryByRole("button", { name: "Retry same request" })).not.toBeInTheDocument();
  });
  it("keeps the exact manual request across an uncertain outcome and retry", async () => {
    vi.mocked(api.getCorporateActionCaseDeadlines).mockResolvedValue(detail);
    vi.mocked(api.recordCorporateActionDeadline).mockRejectedValueOnce(new Error("Connection lost")).mockResolvedValueOnce(receipt);
    const user = userEvent.setup(); render(<CorporateActionDeadlinePanel caseId="case-1" onChanged={vi.fn()} />);
    await user.click(await screen.findByText("Capture a sourced deadline"));
    await user.type(screen.getByLabelText("Source party"), "Issuer agent");
    await user.type(screen.getByLabelText("Source deadline date"), "2026-11-16");
    await user.type(screen.getByLabelText("Evidence URI"), "document://notice");
    await user.type(screen.getByLabelText("Operator-provided SHA-256 digest"), "a".repeat(64));
    await user.type(screen.getByLabelText("Source review rationale"), "Reviewed source notice");
    await user.type(screen.getByLabelText("Original source text"), "Election deadline is 16 November 2026.");
    await user.click(screen.getByRole("button", { name: "Review deadline" }));
    await user.click(screen.getByRole("button", { name: "Record reviewed deadline" }));
    await screen.findByText(/Outcome requires inspection/);
    expect(screen.getByLabelText("Source party")).toBeDisabled();
    await user.click(screen.getByRole("button", { name: "Retry same request" }));
    await screen.findByText(/retained at case version 2/);
    const calls = vi.mocked(api.recordCorporateActionDeadline).mock.calls;
    expect(calls[1][0]).toEqual(calls[0][0]);
    expect(calls[0][0]).toMatchObject({ expectedVersion: 1, tenantId: "tenant-1", companyId: "company-1", deadline: { date: "2026-11-16", sourceText: "Election deadline is 16 November 2026." } });
  });
  it("blocks acceptance of a CSV with server validation errors and invalidates preview on edits", async () => {
    vi.mocked(api.getCorporateActionCaseDeadlines).mockResolvedValue(detail);
    vi.mocked(api.previewCorporateActionDeadlineImport).mockResolvedValue({ previewHash: "preview-1", expectedVersion: 1, rows: [], errors: ["Source timezone is unknown"] });
    const user = userEvent.setup(); render(<CorporateActionDeadlinePanel caseId="case-1" onChanged={vi.fn()} />);
    await user.click(await screen.findByText("Import sourced deadline CSV"));
    await user.click(screen.getByRole("button", { name: "Preview deadline CSV" }));
    expect(await screen.findByText("Source timezone is unknown")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Accept reviewed deadline CSV" })).toBeDisabled();
    await user.type(screen.getByLabelText("Deadline CSV"), "changed");
    expect(screen.queryByRole("button", { name: "Accept reviewed deadline CSV" })).not.toBeInTheDocument();
    expect(api.importCorporateActionDeadlines).not.toHaveBeenCalled();
  });
  it.each(["mismatched-scope", "partial"])("retains the original CSV command for %s outcomes", async (outcome) => {
    vi.mocked(api.getCorporateActionCaseDeadlines).mockResolvedValue(detail);
    vi.mocked(api.previewCorporateActionDeadlineImport).mockResolvedValue({ previewHash: "preview-1", expectedVersion: 1, rows: [{ row: 2, deadline: { seriesId: "series-1", kind: "IssuerElection", sourceParty: "Issuer", date: "2026-11-16", withdrawn: false, sourceText: "Source deadline November 16" }, evidenceReference: "document://notice", evidenceHash: "a".repeat(64), rationale: "Reviewed source" }], errors: [] });
    vi.mocked(api.importCorporateActionDeadlines).mockResolvedValue(outcome === "partial"
      ? { committedCount: 0, complete: false, results: [], failedRow: 2, error: "Case version changed" }
      : { committedCount: 1, complete: true, results: [{ ...receipt, case: { ...receipt.case, scope: { ...scope, companyId: "another-company" } } }] });
    const user = userEvent.setup(); render(<CorporateActionDeadlinePanel caseId="case-1" onChanged={vi.fn()} />);
    await user.click(await screen.findByText("Import sourced deadline CSV"));
    await user.click(screen.getByRole("button", { name: "Preview deadline CSV" }));
    await user.click(await screen.findByRole("button", { name: "Accept reviewed deadline CSV" }));
    await screen.findByRole("button", { name: "Retry same request" });
    expect(screen.getByLabelText("Deadline CSV")).toBeDisabled();
    expect(screen.getByRole("button", { name: "Inspect retained case" })).toBeEnabled();
    if (outcome === "mismatched-scope") expect(screen.getByText(/import receipt does not match/)).toBeInTheDocument();
    else expect(screen.getByText(/import stopped at row 2/)).toBeInTheDocument();
  });
  it("clears prior case forms on a changed case and respects frozen-case capability", async () => {
    vi.mocked(api.getCorporateActionCaseDeadlines).mockImplementation((caseId) => Promise.resolve(caseId === "case-1" ? detail : { ...detail, case: { ...detail.case, caseId }, canRecord: false, blocker: "Case is content-frozen" }));
    const user = userEvent.setup(); const { rerender } = render(<CorporateActionDeadlinePanel caseId="case-1" onChanged={vi.fn()} />);
    await user.click(await screen.findByText("Capture a sourced deadline")); await user.type(screen.getByLabelText("Source party"), "Prior case source");
    rerender(<CorporateActionDeadlinePanel caseId="case-2" onChanged={vi.fn()} />);
    await screen.findByText("Case is content-frozen");
    await user.click(screen.getByText("Capture a sourced deadline"));
    expect(screen.getByLabelText("Source party")).toHaveValue(""); expect(screen.getByLabelText("Source party")).toBeDisabled();
  });
});
