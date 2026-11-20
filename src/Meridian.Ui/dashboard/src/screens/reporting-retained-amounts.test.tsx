import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { getAmountPassport } from "@/lib/amount-passport-api";
import { ApiError } from "@/lib/api-errors";
import { getReportAmountBindings } from "@/lib/report-amount-proof-api";
import { ReportingRetainedAmountsPanel } from "@/screens/reporting-retained-amounts";
import { createAmountPassport, passportIdentity } from "@/test/amount-passport-fixtures";
import type { AmountPassport } from "@/types/amount-passport";
import type { ReportLedgerAmountBinding } from "@/types/report-amount-proof";
import type { GovernedReportingRun } from "@/types/reporting-governance";

vi.mock("@/lib/amount-passport-api", async (importOriginal) => ({
  ...await importOriginal<typeof import("@/lib/amount-passport-api")>(), getAmountPassport: vi.fn()
}));
vi.mock("@/lib/report-amount-proof-api", () => ({ getReportAmountBindings: vi.fn() }));

const run: Pick<GovernedReportingRun, "runId" | "scope" | "snapshot"> = {
  runId: "run-1",
  scope: { tenantId: "tenant-1", organizationId: "org-1", companyId: "company-1", fundId: "fund-1", bookId: "book-1", periodId: "period-1" },
  snapshot: { snapshotId: "snapshot-1", snapshotHash: "b".repeat(64), reconciliationCheckpointId: "recon-1",
    capturedAtUtc: "2026-07-31T12:00:00Z", sourceCheckpointHash: "a".repeat(64) }
};
const identity = passportIdentity({ subjectId: "report:run-1:account-cash", revision: "a".repeat(64) });
identity.scope.organizationId = run.scope.organizationId;
const proof = createAmountPassport(identity, "Cash report balance", "250");
const binding: ReportLedgerAmountBinding = {
  amountId: "account-cash", subjectId: "report:run-1:account-cash", label: "Cash", amount: 250, currency: "USD",
  scope: { tenantId: "tenant-1", companyId: "company-1", fundProfileId: "fund-1", ledgerBookId: "book-1", periodId: "period-1" },
  journalEntryIds: ["22222222-2222-2222-2222-222222222222"], ledgerEntryIds: ["33333333-3333-3333-3333-333333333333"], sourceSnapshotHash: "a".repeat(64),
  proofAvailable: true, amountPassport: proof.reference
};

describe("retained report amounts", () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(getReportAmountBindings).mockResolvedValue([binding]);
    vi.mocked(getAmountPassport).mockResolvedValue(createAmountPassport(identity, "Cash report balance", "250"));
  });

  it("opens verified exact report support, preserves accounting identity and returns keyboard focus", async () => {
    const user = userEvent.setup();
    const { container } = render(<ReportingRetainedAmountsPanel run={run} />);
    const amount = await screen.findByRole("button", { name: "Inspect Cash balance 250.00 USD" });
    expect(getReportAmountBindings).toHaveBeenCalledWith("run-1", { signal: expect.any(AbortSignal) });
    await user.click(screen.getByText("1 journal entry · 1 ledger entry"));
    expect(screen.getByText(binding.journalEntryIds[0]!)).toBeVisible();
    expect(screen.getByText(binding.ledgerEntryIds[0]!)).toBeVisible();
    expect((await axe(container)).violations).toEqual([]);
    await user.click(amount);
    const dialog = await screen.findByRole("dialog", { name: "Amount passport" });
    await within(dialog).findByRole("link", { name: /^Retained journal line/ });
    expect(getAmountPassport).toHaveBeenCalledWith(identity, { signal: expect.any(AbortSignal) });
    expect(dialog).toHaveTextContent("Retained server calculation.");
    expect(dialog).toHaveTextContent("book-1");
    expect(dialog).toHaveTextContent("period-1");
    expect((await axe(container)).violations).toEqual([]);
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(amount).toHaveFocus();
  });

  it.each([false, undefined])("shows balances with a restricted passport when proofAvailable is %s", async (proofAvailable) => {
    vi.mocked(getReportAmountBindings).mockResolvedValue([{ ...binding, proofAvailable }]);
    const { container } = render(<ReportingRetainedAmountsPanel run={run} />);
    expect(await screen.findByText("250.00 USD")).toBeVisible();
    expect(screen.getByText("Amount proof needs ledger report access")).toBeVisible();
    expect(screen.queryByRole("button", { name: /^Inspect/ })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Amount passport restricted for Cash balance" }));
    expect(await screen.findByRole("dialog")).toHaveTextContent("Permission denied");
    expect(getAmountPassport).not.toHaveBeenCalled();
    expect((await axe(container)).violations).toEqual([]);
  });

  it.each(["tenantId", "companyId", "fundProfileId", "ledgerBookId", "periodId"] as const)("blocks a foreign binding %s before exposing its amount", async (field) => {
    vi.mocked(getReportAmountBindings).mockResolvedValue([{ ...binding, scope: { ...binding.scope, [field]: "foreign" } }]);
    render(<ReportingRetainedAmountsPanel run={run} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("accounting scope or checkpoint do not match");
    expect(screen.queryByRole("button", { name: /^Inspect/ })).not.toBeInTheDocument();
    expect(getAmountPassport).not.toHaveBeenCalled();
  });

  it.each(["checkpoint", "subject", "duplicate", "missing-journals", "missing-entries", "invalid-amount", "invalid-currency"])("blocks %s binding evidence", async (condition) => {
    const altered = structuredClone(binding);
    if (condition === "checkpoint") altered.sourceSnapshotHash = "c".repeat(64);
    if (condition === "subject") altered.subjectId = "report:foreign-run:account-cash";
    if (condition === "missing-journals") altered.journalEntryIds = [];
    if (condition === "missing-entries") altered.ledgerEntryIds = [];
    if (condition === "invalid-amount") altered.amount = NaN;
    if (condition === "invalid-currency") altered.currency = "";
    vi.mocked(getReportAmountBindings).mockResolvedValue(condition === "duplicate" ? [binding, binding] : [altered]);
    render(<ReportingRetainedAmountsPanel run={run} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("Blocked:");
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it.each(["missing", "altered", "foreign", "unavailable", "restricted", "partial"] as const)("keeps %s source evidence blocked in the report drawer", async (condition) => {
    const packet = createAmountPassport(identity, "Cash report balance", "250");
    if (condition === "partial") {
      packet.status = "blocked";
      packet.evidence = [];
      packet.gaps = ["A contributing retained source is missing."];
    }
    if (condition === "altered") packet.exactValue = "250.0000000000000000001";
    if (condition === "foreign") packet.reference.identity = { ...identity, scope: { ...identity.scope, fundProfileId: "foreign" } };
    if (condition === "missing" || condition === "restricted" || condition === "unavailable") {
      vi.mocked(getAmountPassport).mockRejectedValue(new ApiError({ path: "amount-passport", status:
        condition === "missing" ? 404 : condition === "restricted" ? 403 : 503 }));
    } else vi.mocked(getAmountPassport).mockResolvedValue(packet);
    const user = userEvent.setup();
    render(<ReportingRetainedAmountsPanel run={run} />);
    await user.click(await screen.findByRole("button", { name: /^Inspect Cash balance/ }));
    const dialog = await screen.findByRole("dialog");
    const posture = { missing: "Source unavailable", altered: "Scope mismatch", foreign: "Scope mismatch", unavailable: "Unavailable", restricted: "Permission denied", partial: "Blocked" }[condition];
    await waitFor(() => expect(dialog).toHaveTextContent(posture));
    expect(within(dialog).queryByRole("link")).not.toBeInTheDocument();
  });

  it("shows absent retention as blocked and retries a failed authoritative read", async () => {
    vi.mocked(getReportAmountBindings).mockRejectedValueOnce(new Error("404")).mockResolvedValueOnce([]);
    const user = userEvent.setup();
    render(<ReportingRetainedAmountsPanel run={run} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("retained report amounts are unavailable");
    await user.click(screen.getByRole("button", { name: "Retry retained amounts" }));
    expect(await screen.findByRole("status")).toHaveTextContent("No retained amount bindings are available");
    expect(getAmountPassport).not.toHaveBeenCalled();
  });

  it("displays zero and negative retained balances without inferring new evidence", async () => {
    vi.mocked(getReportAmountBindings).mockResolvedValue([
      { ...binding, amount: 0, amountPassport: null },
      { ...binding, amountId: "account-capital", subjectId: "report:run-1:account-capital", label: "Capital", amount: -250, amountPassport: null }
    ]);
    render(<ReportingRetainedAmountsPanel run={run} />);
    expect(await screen.findByRole("button", { name: "Inspect Cash balance 0.00 USD" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Inspect Capital balance -250.00 USD" })).toBeInTheDocument();
  });

  it("discards an old selected amount and a late proof after a run and scope round trip", async () => {
    let resolveOld!: (packet: AmountPassport) => void;
    let proofSignal: AbortSignal | undefined;
    vi.mocked(getAmountPassport).mockImplementation((_selection, options) => {
      proofSignal = options?.signal;
      return new Promise((resolve) => { resolveOld = resolve; });
    });
    const user = userEvent.setup();
    const view = render(<ReportingRetainedAmountsPanel run={run} />);
    await user.click(await screen.findByRole("button", { name: /^Inspect Cash balance/ }));
    expect(await screen.findByRole("dialog")).toHaveTextContent("Loading amount passport");
    const foreignRun = { ...run, runId: "run-2", scope: { ...run.scope, fundId: "fund-2", bookId: "book-2" } };
    const foreignBinding = { ...binding, amountPassport: null, subjectId: "report:run-2:account-cash", scope: { ...binding.scope, fundProfileId: "fund-2", ledgerBookId: "book-2" } };
    vi.mocked(getReportAmountBindings).mockResolvedValueOnce([foreignBinding]).mockResolvedValueOnce([binding]);
    view.rerender(<ReportingRetainedAmountsPanel run={foreignRun} />);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(proofSignal?.aborted).toBe(true);
    await screen.findByRole("button", { name: /^Inspect Cash balance/ });
    view.rerender(<ReportingRetainedAmountsPanel run={run} />);
    await screen.findByRole("button", { name: /^Inspect Cash balance/ });
    await act(async () => resolveOld(createAmountPassport(identity, "Cash report balance", "250")));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(getAmountPassport).toHaveBeenCalledOnce();
  });

  it("aborts an old run read and ignores its late same-name account bindings", async () => {
    let resolveOld!: (bindings: ReportLedgerAmountBinding[]) => void;
    let oldSignal: AbortSignal | undefined;
    vi.mocked(getReportAmountBindings).mockImplementationOnce((_runId, options) => {
      oldSignal = options?.signal;
      return new Promise((resolve) => { resolveOld = resolve; });
    }).mockResolvedValueOnce([]);
    const view = render(<ReportingRetainedAmountsPanel run={run} />);
    view.rerender(<ReportingRetainedAmountsPanel run={{ ...run, runId: "run-2" }} />);
    expect(oldSignal?.aborted).toBe(true);
    await screen.findByText(/No retained amount bindings/);
    await act(async () => resolveOld([binding]));
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });
});
