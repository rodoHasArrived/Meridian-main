import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { axe } from "jest-axe";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api-errors";
import { AccountClosePricesPanel } from "./accounting-screen.account-close-prices";
import type { ClosePriceSet, CreateClosePriceSetRequest, OversightContext, OversightScope } from "@/types/account-oversight";

const api = vi.hoisted(() => ({ list: vi.fn(), get: vi.fn(), create: vi.fn(), approve: vi.fn() }));
vi.mock("@/lib/api/account-oversight.api", () => ({
  listClosePriceSets: api.list, getClosePriceSet: api.get,
  createClosePriceSet: api.create, approveClosePriceSet: api.approve
}));

const scope: OversightScope = { accountId: "account-a", ledgerBookId: "book-a", baseCurrency: "USD" };
const context: OversightContext = {
  accountId: "account-a", bookId: "book-a", baseCurrency: "USD", bookName: "Account book",
  actor: "independent-reviewer", canWrite: true, canReadPrices: true, canManagePrices: true, canProvision: false
};
const date = new Date().toISOString().slice(0, 10);
const securityId = "11111111-1111-1111-1111-111111111111";
const snapshotId = "22222222-2222-2222-2222-222222222222";
function retained(overrides: Partial<ClosePriceSet["snapshot"]> = {}): ClosePriceSet {
  return {
    snapshot: {
      snapshotId, accountId: scope.accountId, bookId: scope.ledgerBookId, valuationDate: date,
      version: 1, correctsSnapshotId: null, correctionRationale: null,
      evaluatedAsOf: `${date}T00:00:00Z`, knowledgeAsOf: `${date}T00:00:00Z`, maxSourceDispersionPercent: "5",
      createdBy: "creator", createdAt: `${date}T00:00:01Z`, blockers: [],
      lines: [{
        securityId, selection: {
          securityId, goldenCopyPrice: "99.12345678901234567890123456", priceKind: "Raw", selectedSource: "custodian-price",
          priceAsOf: `${date}T00:00:00Z`, isStaleFallback: false, daysStale: 0, unit: "PercentOfPar", comparisonPrices: [],
          selectionReceiptId: "receipt-1", hierarchySnapshot: { securityId, accountId: scope.accountId,
            asOf: `${date}T00:00:00Z`, updatedBy: "hierarchy-operator",
            entries: [{ priority: 1, sourceId: "custodian-price", sourceDisplayName: "Custodian", maxDaysStale: 1 }] }
        },
        observations: [{ sourceId: "custodian-price", price: "99.12345678901234567890123456", priceAsOf: `${date}T00:00:00Z`, unit: "PercentOfPar" }],
        override: null, closePrice: "99.12345678901234567890123456", unit: "PercentOfPar"
      }], ...overrides
    }, approval: null
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => { resolve = done; });
  return { promise, resolve };
}
function captured(request: CreateClosePriceSetRequest): ClosePriceSet {
  return retained({ snapshotId: request.requestId!, createdBy: request.createdBy,
    evaluatedAsOf: request.evaluatedAsOf, knowledgeAsOf: request.knowledgeAsOf!,
    correctsSnapshotId: request.correctsSnapshotId ?? null, correctionRationale: request.correctionRationale ?? null,
    maxSourceDispersionPercent: request.maxSourceDispersionPercent, creationCommandDigest: "a".repeat(64),
    requestedKnowledgeAsOf: request.knowledgeAsOf });
}
function expandPrepare() {
  fireEvent.click(screen.getByText("Prepare a price set or linked correction"));
}
function fillInitial() {
  fireEvent.change(screen.getByLabelText("Close cutoff (UTC)"), { target: { value: `${date}T00:00:00` } });
  fireEvent.change(screen.getByLabelText("Knowledge cutoff (UTC)"), { target: { value: `${date}T00:00:00` } });
  fireEvent.change(screen.getByLabelText("Security ID 1"), { target: { value: securityId } });
}

beforeEach(() => {
  vi.clearAllMocks();
  api.list.mockResolvedValue([]);
  api.get.mockImplementation(async (_scope, _date, id) => retained({ snapshotId: id }));
  api.create.mockImplementation(async (_scope, request: CreateClosePriceSetRequest) => captured(request));
  api.approve.mockResolvedValue({ ...retained(), approval: { snapshotId, snapshotSha256: "retained-digest", reviewer: context.actor, rationale: "Reviewed", approvedAt: `${date}T00:00:02Z` } });
});

describe("AccountClosePricesPanel", () => {
  it("retains exact decimal observations and prevents the creator from approving their own set", async () => {
    api.list.mockResolvedValue([retained({ createdBy: "INDEPENDENT-REVIEWER" })]);
    render(<AccountClosePricesPanel scope={scope} context={context} />);
    expect(await screen.findByText("The creator cannot approve their own price set. An independent reviewer is required.")).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Independent review rationale"), { target: { value: "Checked" } });
    expect(screen.getByRole("button", { name: "Approve immutable price set" })).toBeDisabled();
    expect(screen.getAllByText("99.12345678901234567890123456").length).toBeGreaterThan(0);
    expect(api.approve).not.toHaveBeenCalled();
  });

  it("shows stale and dispersion blockers alongside overridden original observations", async () => {
    const set = retained({ blockers: [
      { securityId, code: "StalePrice", reason: "Selected observation exceeds hierarchy age." },
      { securityId, code: "SourceDispersion", reason: "Source dispersion exceeds the retained limit." }
    ] });
    set.snapshot.lines[0].override = { price: "100", unit: "PercentOfPar", rationale: "Reviewed quote", evidenceReference: "evidence://retained-quote" };
    set.snapshot.lines[0].closePrice = "100";
    api.list.mockResolvedValue([set]);
    render(<AccountClosePricesPanel scope={scope} context={context} />);
    expect(await screen.findByText("Approval blocked by retained source evidence")).toBeInTheDocument();
    expect(screen.getByLabelText("Close-price approval blockers")).toHaveTextContent("StalePrice");
    expect(screen.getByLabelText("Close-price approval blockers")).toHaveTextContent("SourceDispersion");
    fireEvent.click(screen.getByText("Source observations and pricing hierarchy"));
    expect(screen.getByLabelText(`Sources for ${securityId}`)).toHaveTextContent("99.12345678901234567890123456");
    expect(screen.getByText(/Override evidence: evidence:\/\/retained-quote/)).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Independent review rationale"), { target: { value: "Reviewed override" } });
    expect(screen.getByRole("button", { name: "Approve immutable price set" })).toBeDisabled();
  });

  it("prepares a linked correction from the latest version with fixed security coverage", async () => {
    api.list.mockResolvedValue([retained()]);
    api.create.mockImplementation(async (_scope, request: CreateClosePriceSetRequest) => {
      const next = captured(request);
      next.snapshot.version = 2;
      return next;
    });
    render(<AccountClosePricesPanel scope={scope} context={context} />);
    await screen.findByText("Retained version 1 · " + date);
    expandPrepare();
    fireEvent.click(screen.getByRole("button", { name: "Start correction from latest version 1" }));
    expect(screen.getByLabelText("Security ID 1")).toHaveAttribute("readonly");
    expect(screen.queryByRole("button", { name: "Add security" })).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Correction rationale"), { target: { value: "Corrected source evidence" } });
    fireEvent.click(screen.getByRole("button", { name: "Prepare linked correction" }));
    await waitFor(() => expect(api.create).toHaveBeenCalledOnce());
    expect(api.create.mock.calls[0][1]).toMatchObject({
      accountId: scope.accountId, bookId: scope.ledgerBookId, valuationDate: date,
      correctsSnapshotId: snapshotId, correctionRationale: "Corrected source evidence",
      securities: [{ securityId, override: null }], maxSourceDispersionPercent: "5"
    });
    expect(await screen.findByText("Retained version 2 · " + date)).toBeInTheDocument();
  });

  it("requires rationale and records the independent session actor against the exact selected snapshot", async () => {
    api.list.mockResolvedValue([retained()]);
    render(<AccountClosePricesPanel scope={scope} context={context} />);
    await screen.findByText("Retained version 1 · " + date);
    expect(screen.getByRole("button", { name: "Approve immutable price set" })).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Independent review rationale"), { target: { value: "Compared retained source rows and units" } });
    fireEvent.click(screen.getByRole("button", { name: "Approve immutable price set" }));
    await waitFor(() => expect(api.approve).toHaveBeenCalledOnce());
    expect(api.approve.mock.calls[0][1]).toEqual({ accountId: scope.accountId, bookId: scope.ledgerBookId, valuationDate: date, snapshotId, reviewer: context.actor, rationale: "Compared retained source rows and units" });
    expect(await screen.findByText("Approved by independent-reviewer")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Approve immutable price set" })).toBeDisabled();
  });

  it("keeps an unsaved correction draft dirty when a separate retained version is approved", async () => {
    api.list.mockResolvedValue([retained()]);
    const dirty = vi.fn();
    render(<AccountClosePricesPanel scope={scope} context={context} onDirtyChange={dirty} />);
    await screen.findByText("Retained version 1 · " + date);
    expandPrepare();
    fireEvent.click(screen.getByRole("button", { name: "Start correction from latest version 1" }));
    fireEvent.change(screen.getByLabelText("Correction rationale"), { target: { value: "Draft correction in progress" } });
    fireEvent.change(screen.getByLabelText("Independent review rationale"), { target: { value: "Checked retained original" } });
    fireEvent.click(screen.getByRole("button", { name: "Approve immutable price set" }));
    await screen.findByText("Approved by independent-reviewer");
    expect(screen.getByLabelText("Correction rationale")).toHaveValue("Draft correction in progress");
    expect(dirty).toHaveBeenLastCalledWith(true);
  });

  it("preserves edited price inputs after a validation error and rejects duplicate security IDs", async () => {
    api.create.mockRejectedValue(new ApiError({ path: "/prices", status: 422, detail: "Source input needs correction" }));
    const dirty = vi.fn();
    render(<AccountClosePricesPanel scope={scope} context={context} onDirtyChange={dirty} />);
    await screen.findByText("No close-price input is retained for this account, book and date.");
    fillInitial();
    fireEvent.click(screen.getByText("Optional override for security 1"));
    fireEvent.change(screen.getByLabelText("Override price 1"), { target: { value: "99.12345678901234567890123456" } });
    fireEvent.change(screen.getByLabelText("Override rationale 1"), { target: { value: "Source adjustment" } });
    fireEvent.change(screen.getByLabelText("Override evidence reference 1"), { target: { value: "evidence://quote" } });
    fireEvent.click(screen.getByRole("button", { name: "Prepare immutable price set" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Source input needs correction");
    expect(screen.getByLabelText("Override price 1")).toHaveValue("99.12345678901234567890123456");
    expect(screen.getByLabelText("Override rationale 1")).toHaveValue("Source adjustment");
    expect(dirty).toHaveBeenLastCalledWith(true);
    expect(screen.getByRole("button", { name: "Prepare immutable price set" })).toBeEnabled();
    fireEvent.click(screen.getByRole("button", { name: "Add security" }));
    fireEvent.change(screen.getByLabelText("Security ID 2"), { target: { value: securityId.toUpperCase() } });
    expect(screen.getByText("The security population must contain distinct IDs.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Prepare immutable price set" })).toBeDisabled();
  });

  it("recovers a lost create response with the same identified command and one retained version", async () => {
    let stored: ClosePriceSet | null = null;
    api.create.mockImplementation(async (_scope, request: CreateClosePriceSetRequest) => {
      if (!stored) { stored = captured(request); throw new TypeError("Failed to fetch"); }
      return stored;
    });
    api.list.mockImplementation(async () => stored ? [stored] : []);
    render(<AccountClosePricesPanel scope={scope} context={context} />);
    await screen.findByText("No close-price input is retained for this account, book and date.");
    fillInitial();
    fireEvent.click(screen.getByRole("button", { name: "Prepare immutable price set" }));
    expect(await screen.findByText("Write outcome needs verification")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Prepare immutable price set" })).toBeDisabled();
    expect(screen.getByLabelText("Security ID 1")).toHaveValue(securityId);
    expect(screen.getByLabelText("Security ID 1")).toBeDisabled();
    const original = api.create.mock.calls[0][1];
    expect(original.requestId).toMatch(/^[0-9a-f-]{36}$/);
    fireEvent.click(screen.getByRole("button", { name: "Reload price history" }));
    expect(await screen.findByText("Retained version 1 · " + date)).toBeInTheDocument();
    expect(screen.getByText("Write outcome needs verification")).toBeInTheDocument();
    expect(api.create).toHaveBeenCalledOnce();
    expect(screen.getByRole("button", { name: "Approve immutable price set" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Verify retained price capture" }));
    await waitFor(() => expect(screen.queryByText("Write outcome needs verification")).not.toBeInTheDocument());
    expect(api.create).toHaveBeenCalledTimes(2);
    expect(api.create.mock.calls[1][1]).toEqual(original);
    expect(within(screen.getByRole("table", { name: "Retained close-price versions" })).getAllByRole("row")).toHaveLength(2);
    expect(screen.getByText("Start a linked correction from the latest retained version.")).toBeInTheDocument();
  });

  it("keeps failed recovery locked and retries the same identity after an empty authoritative history", async () => {
    api.create.mockRejectedValueOnce(new TypeError("Lost response"));
    render(<AccountClosePricesPanel scope={scope} context={context} />);
    await screen.findByText("No close-price input is retained for this account, book and date.");
    fillInitial();
    fireEvent.click(screen.getByRole("button", { name: "Prepare immutable price set" }));
    await screen.findByText("Write outcome needs verification");
    const original = api.create.mock.calls[0][1];
    api.list.mockRejectedValueOnce(new Error("History is unavailable"));
    fireEvent.click(screen.getByRole("button", { name: "Reload price history" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("History is unavailable");
    expect(screen.getByRole("button", { name: "Prepare immutable price set" })).toBeDisabled();
    expect(screen.getByLabelText("Maximum source dispersion (%)")).toBeDisabled();
    expect(api.create).toHaveBeenCalledOnce();
    fireEvent.click(screen.getByRole("button", { name: "Reload price history" }));
    const retry = await screen.findByRole("button", { name: "Retry same price capture" });
    expect(retry).toBeEnabled();
    expect(screen.getByLabelText("Security ID 1")).toBeDisabled();
    fireEvent.click(retry);
    await screen.findByText("Retained version 1 · " + date);
    expect(api.create.mock.calls[1][1]).toEqual(original);
    expect(screen.queryByText("Write outcome needs verification")).not.toBeInTheDocument();
  });

  it("keeps the capture identity for an unchanged command and creates a new identity after an edit", async () => {
    api.create.mockRejectedValue(new ApiError({ path: "/prices", status: 422, detail: "Source input needs correction" }));
    render(<AccountClosePricesPanel scope={scope} context={context} />);
    await screen.findByText("No close-price input is retained for this account, book and date.");
    fillInitial();
    fireEvent.click(screen.getByRole("button", { name: "Prepare immutable price set" }));
    await screen.findByRole("alert");
    const firstId = api.create.mock.calls[0][1].requestId;
    fireEvent.click(screen.getByRole("button", { name: "Prepare immutable price set" }));
    await waitFor(() => expect(api.create).toHaveBeenCalledTimes(2));
    await screen.findByRole("alert");
    expect(api.create.mock.calls[1][1].requestId).toBe(firstId);
    fireEvent.change(screen.getByLabelText("Maximum source dispersion (%)"), { target: { value: "6" } });
    fireEvent.click(screen.getByRole("button", { name: "Prepare immutable price set" }));
    await waitFor(() => expect(api.create).toHaveBeenCalledTimes(3));
    expect(api.create.mock.calls[2][1].requestId).not.toBe(firstId);
  });

  it("does not recover an earlier rejected capture when an approval response is lost", async () => {
    api.list.mockResolvedValue([retained()]);
    api.create.mockRejectedValue(new ApiError({ path: "/prices", status: 422, detail: "Source input needs correction" }));
    api.approve.mockRejectedValue(new ApiError({ path: "/approval", status: 503, detail: "Approval response lost" }));
    render(<AccountClosePricesPanel scope={scope} context={context} />);
    await screen.findByText("Retained version 1 · " + date);
    expandPrepare();
    fireEvent.click(screen.getByRole("button", { name: "Start correction from latest version 1" }));
    fireEvent.change(screen.getByLabelText("Correction rationale"), { target: { value: "Draft correction" } });
    fireEvent.click(screen.getByRole("button", { name: "Prepare linked correction" }));
    await screen.findByRole("alert");
    fireEvent.change(screen.getByLabelText("Independent review rationale"), { target: { value: "Reviewed original" } });
    fireEvent.click(screen.getByRole("button", { name: "Approve immutable price set" }));
    await screen.findByText("Write outcome needs verification");
    fireEvent.click(screen.getByRole("button", { name: "Reload price history" }));
    await waitFor(() => expect(screen.queryByText("Write outcome needs verification")).not.toBeInTheDocument());
    expect(screen.queryByRole("button", { name: "Retry same price capture" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Verify retained price capture" })).not.toBeInTheDocument();
    expect(api.create).toHaveBeenCalledOnce();
  });

  it("aborts a prior account read and ignores its late response", async () => {
    const pending = deferred<ClosePriceSet[]>();
    api.list.mockReturnValueOnce(pending.promise).mockResolvedValue([]);
    const { rerender } = render(<AccountClosePricesPanel scope={scope} context={context} />);
    await waitFor(() => expect(api.list).toHaveBeenCalledOnce());
    const signal = api.list.mock.calls[0][2].signal as AbortSignal;
    const otherScope = { ...scope, accountId: "account-b" };
    rerender(<AccountClosePricesPanel scope={otherScope} context={{ ...context, accountId: "account-b" }} />);
    expect(signal.aborted).toBe(true);
    await act(async () => { pending.resolve([retained()]); });
    expect(await screen.findByText("No close-price input is retained for this account, book and date.")).toBeInTheDocument();
    expect(screen.queryByText("Retained version 1 · " + date)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Approve immutable price set" })).not.toBeInTheDocument();
  });

  it("invalidates approval when the date changes and cannot restore it from a late selected-version read", async () => {
    const pending = deferred<ClosePriceSet>();
    api.list.mockResolvedValueOnce([retained()]).mockResolvedValue([]);
    api.get.mockReturnValue(pending.promise);
    render(<AccountClosePricesPanel scope={scope} context={context} />);
    await screen.findByText("Retained version 1 · " + date);
    fireEvent.keyDown(screen.getByRole("row", { name: "Inspect close-price version 1" }), { key: "Enter" });
    await waitFor(() => expect(api.get).toHaveBeenCalledOnce());
    const signal = api.get.mock.calls[0][3].signal as AbortSignal;
    fireEvent.change(screen.getByLabelText("Valuation date (UTC)"), { target: { value: "2026-01-01" } });
    expect(signal.aborted).toBe(true);
    await act(async () => { pending.resolve(retained()); });
    await screen.findByText("No close-price input is retained for this account, book and date.");
    expect(screen.queryByRole("button", { name: "Approve immutable price set" })).not.toBeInTheDocument();
    expect(screen.queryByLabelText(`Close-price version 1 evidence`)).not.toBeInTheDocument();
  });

  it("retains stored approval after reload and exposes read-only evidence without mutation controls", async () => {
    const set = retained();
    set.approval = { snapshotId, snapshotSha256: "original-digest", reviewer: "prior-reviewer", rationale: "Reviewed original observations", approvedAt: `${date}T00:00:02Z` };
    api.list.mockResolvedValue([set]);
    render(<AccountClosePricesPanel scope={scope} context={{ ...context, canManagePrices: false }} />);
    expect(await screen.findByText("Approved by prior-reviewer")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Approve immutable price set" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Reload price history" }));
    expect(await screen.findByText("Approved by prior-reviewer")).toBeInTheDocument();
    expect(screen.getByText("Snapshot SHA-256: original-digest")).toBeInTheDocument();
    expect(screen.getByText(/Read only/)).toBeInTheDocument();
    expect(api.create).not.toHaveBeenCalled();
    expect(api.approve).not.toHaveBeenCalled();
  });

  it("announces unavailable history, keeps writes blocked and retries an explicit read", async () => {
    api.list.mockRejectedValueOnce(new Error("Price history is unavailable")).mockResolvedValue([]);
    render(<AccountClosePricesPanel scope={scope} context={context} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("Price history is unavailable");
    fillInitial();
    expect(screen.getByRole("button", { name: "Prepare immutable price set" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Reload price history" }));
    await waitFor(() => expect(screen.getByRole("button", { name: "Prepare immutable price set" })).toBeEnabled());
    expect(api.list).toHaveBeenCalledTimes(2);
  });

  it("has labelled forms, keyboard-native evidence disclosure and no axe violations", async () => {
    api.list.mockResolvedValue([retained()]);
    const { container } = render(<AccountClosePricesPanel scope={scope} context={context} />);
    await screen.findByText("Retained version 1 · " + date);
    expect(within(screen.getByRole("form", { name: "Independent close-price review" })).getByLabelText("Independent review rationale")).toBeInTheDocument();
    expect((await axe(container)).violations).toEqual([]);
  });
});
