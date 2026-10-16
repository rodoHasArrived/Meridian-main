import { beforeEach, describe, expect, it, vi } from "vitest";
import { apiGetJson, apiPostJson } from "@/lib/api";
import * as api from "@/lib/api/account-oversight.api";
import type { LedgerBook } from "@/types";
import type {
  AccountCashForecastRequest, AccountCashForecastSnapshot, AccountCashProvenance,
  AccountCashReconciliationCase, AccountCashReconciliationRequest, AccountCashReconciliationRun,
  ClosePriceSet, CreateClosePriceSetRequest, OversightScope
} from "@/types/account-oversight";

vi.mock("@/lib/api", () => ({ apiGetJson: vi.fn(), apiPostJson: vi.fn() }));

const scope: OversightScope = {
  accountId: "11111111-1111-1111-1111-111111111111",
  ledgerBookId: "22222222-2222-2222-2222-222222222222", baseCurrency: "USD"
};
const foreignAccount = "33333333-3333-3333-3333-333333333333";
const priceId = "bbbbbbbb-4444-4444-4444-444444444444";
const securityId = "aaaaaaaa-5555-5555-5555-555555555555";
const precise = "123456789012345.1234567890123";
const businessDate = "2026-10-14";
const timestamp = "2026-10-14T12:00:00Z";
const base = `/api/ledger/books/${scope.ledgerBookId}/accounts/${scope.accountId}`;
const get = vi.mocked(apiGetJson);
const post = vi.mocked(apiPostJson);

function comparison(): AccountCashReconciliationRequest {
  const source = (sourceId: string, sourceType: 0 | 1) => ({
    sourceId, sourceType, version: "source-v1", evidenceReference: "retained:balance:1", capturedAtUtc: timestamp,
    completePopulation: true, rows: [{ originalPayload: '{"nativeAmount":"123456789012345.1234567890123"}', row: {
      cashEntryId: `${sourceId}-balance`, accountId: scope.accountId, amount: precise, currency: "USD",
      amountBase: precise, baseCurrency: "USD", postedAtUtc: timestamp, accountingPeriod: businessDate,
      sourceReference: `${sourceId}:statement`
    } }]
  });
  return {
    runId: "run / 1", scope: { tenantId: "", companyId: "", accountId: scope.accountId, ledgerBookId: scope.ledgerBookId },
    businessDate, observedAtUtc: timestamp, left: source("prime", 0), right: source("custodian", 1), initialOwner: "cash-ops",
    tolerance: { profileId: "cash-policy", version: 1, cashRules: [{ ruleId: "cash-absolute", absoluteCashTolerance: "0.01", basisPointCashTolerance: null,
      settlementDateTolerance: "00:05:00" }], positionRules: [], transactionRules: [] }
  };
}
function run(): AccountCashReconciliationRun {
  return { input: comparison(), inputHash: "input-sha256", resultHash: "result-sha256", comparisons: [{
    currency: "USD", observationState: "New", matches: [], breaks: [], evidence: [{
      evidenceId: "evidence-1", ruleId: "cash-absolute", stage: "Cash", narrative: "Exact retained comparison",
      numericDelta: "0.0000000000001", attributes: {}
    }]
  }] };
}
function cashCase(): AccountCashReconciliationCase {
  return { caseId: "case / 1", scope: comparison().scope, leftSourceId: "prime", rightSourceId: "custodian", currency: "USD",
    owner: "cash-ops", version: 2, firstObservedAtUtc: timestamp, lastObservedAtUtc: timestamp, state: "SourceCleared",
    resolvedAtUtc: timestamp, resolutionRunId: "run / 1", agingDays: 0, history: [], assignments: [] };
}
function provenance(): AccountCashProvenance {
  return { sourceDomain: "EvidenceVault", sourceEntityId: "ev-balance", sourceContentHash: "a".repeat(64), role: 0,
    evidence: { evidenceId: "doc:ev-balance", evidenceUri: "evidence://evidence-vault/ev-balance", contentHashSha256: "b".repeat(64),
      sourceSystem: "Custodian", sourceReference: "statement-1", reviewStatus: "Accepted", reviewedBy: "independent-reviewer",
      reviewedAtUtc: timestamp, effectiveDate: businessDate, evidenceVersion: 1, retainedAtUtc: timestamp, retainedBy: "independent-reviewer",
      subjectType: "AccountCash", subjectId: scope.accountId } };
}
function forecast(): AccountCashForecastRequest {
  return { runId: "forecast / 1", scope: { tenantId: "", companyId: "", accountId: scope.accountId, bookId: scope.ledgerBookId, baseCurrency: "USD" },
    periodStart: businessDate, periodEnd: businessDate, openingCash: { accountId: scope.accountId, bookId: scope.ledgerBookId,
      asOfDate: businessDate, amount: precise, currency: "USD", isSettled: true, provenance: provenance() },
    closingCash: null, flows: [{ flowId: "coupon-1", versionId: "coupon-v1", accountId: scope.accountId, bookId: scope.ledgerBookId,
      status: 0, flowType: "Coupon", amount: "0.0000000000001", currency: "USD", economicDate: businessDate,
      settlementDate: businessDate, cashReceiptDate: businessDate, provenance: { ...provenance(), role: 1 } }],
    fxConversions: [], recordedAtUtc: timestamp, minimumCashThreshold: "0", bucketDays: 1 };
}
function snapshot(): AccountCashForecastSnapshot {
  return { snapshotId: "forecast / snapshot 1", evidenceHash: "snapshot-sha256", engineVersion: "engine-1", inputs: forecast(),
    openingBaseCash: precise, actualNetBaseFlow: "0", reconciledClosingBaseCash: precise, observedClosingBaseCash: null,
    closingVariance: null, isCertified: false, blockers: [{ code: "MissingClosingCash", message: "Closing cash has not been observed." }],
    forecastLadder: { engineVersion: "ladder-1", asOfDate: businessDate, horizonDays: 1, baseCurrency: "USD", scenarioId: "Base",
      openingCash: precise, minimumCashThreshold: "0", buckets: [], contributions: [], availableScenarios: [], assumptions: [], warnings: [],
      generatedAt: timestamp, securitiesEvaluated: 0, securitiesWithFlows: 0, isDecisionReady: false, blockingReasons: [] } };
}
function priceSet(): ClosePriceSet {
  return { snapshot: { snapshotId: priceId, accountId: scope.accountId, bookId: scope.ledgerBookId, valuationDate: businessDate,
    version: 1, correctsSnapshotId: null, correctionRationale: null, evaluatedAsOf: timestamp, knowledgeAsOf: timestamp,
    maxSourceDispersionPercent: "1.25", createdBy: "preparer", createdAt: timestamp, lines: [{ securityId,
      selection: { securityId, goldenCopyPrice: precise, priceKind: "MarketGoldenCopy", selectedSource: "Custodian", priceAsOf: timestamp,
        isStaleFallback: false, daysStale: 0, comparisonPrices: [], unit: "CurrencyPerUnit", selectionReceiptId: priceId,
        hierarchySnapshot: { securityId, accountId: scope.accountId, asOf: timestamp, updatedBy: "preparer", entries: [] } },
      observations: [{ sourceId: "Custodian", price: precise, priceAsOf: timestamp, unit: "CurrencyPerUnit" }], override: null,
      closePrice: precise, unit: "CurrencyPerUnit" }], blockers: [] }, approval: null };
}
function priceCommand(): CreateClosePriceSetRequest {
  return { accountId: scope.accountId, bookId: scope.ledgerBookId, valuationDate: businessDate, evaluatedAsOf: timestamp,
    knowledgeAsOf: timestamp, securities: [{ securityId }], maxSourceDispersionPercent: "1.25", createdBy: "preparer", requestId: priceId };
}

describe("account oversight API authority and exact evidence", () => {
  beforeEach(() => { vi.clearAllMocks(); });

  it("discovers only valid account-owned books without selecting a book", async () => {
    const accountBook = { ledgerBookId: scope.ledgerBookId, fundProfileId: "fund / 1", fundStructureNodeId: scope.accountId,
      fundStructureNodeKind: "Account", baseCurrency: "USD", displayName: "Account book" } as LedgerBook;
    get.mockResolvedValue([accountBook, { ...accountBook, fundStructureNodeKind: "Fund" }, { ...accountBook, ledgerBookId: "invalid" }]);
    expect(await api.listAccountOversightBooks("fund / 1")).toEqual([accountBook]);
    expect(get).toHaveBeenCalledWith("/api/ledger/books?fundProfileId=fund+%2F+1", { allowDevelopmentFallback: false });
    get.mockResolvedValue([{ ...accountBook, fundProfileId: "another-fund" }]);
    await expect(api.listAccountOversightBooks("fund / 1")).rejects.toThrow("fund scope");
  });

  it("forces authoritative reads and preserves cancellation even when callers request fixture fallback", async () => {
    const controller = new AbortController(); get.mockResolvedValue([]);
    await api.listCashCases(scope, { signal: controller.signal, allowDevelopmentFallback: true });
    expect(get).toHaveBeenCalledWith(`${base}/cash-reconciliation/cases`, { signal: controller.signal, allowDevelopmentFallback: false });
    const abort = new DOMException("The operator changed account.", "AbortError"); get.mockRejectedValue(abort);
    await expect(api.listCashCases(scope, { signal: controller.signal })).rejects.toBe(abort);
    expect(get).toHaveBeenCalledTimes(2);
  });

  it("verifies trusted context and provisioning before exposing authority", async () => {
    get.mockResolvedValue({ accountId: scope.accountId, bookId: scope.ledgerBookId, baseCurrency: "USD", bookName: "Account book",
      actor: "operator", canWrite: false, canReadPrices: true, canManagePrices: false, canProvision: false });
    expect((await api.getAccountOversightContext(scope)).canWrite).toBe(false);
    expect(get).toHaveBeenCalledWith(`${base}/oversight/context`, { allowDevelopmentFallback: false });
    post.mockResolvedValue({ accountId: scope.accountId, bookId: scope.ledgerBookId, provisioned: true });
    await api.provisionAccountOversight(scope);
    expect(post).toHaveBeenCalledWith(`${base}/oversight/provision`, undefined, { allowDevelopmentFallback: false });
    get.mockResolvedValue({ accountId: foreignAccount, bookId: scope.ledgerBookId, baseCurrency: "USD" });
    await expect(api.getAccountOversightContext(scope)).rejects.toThrow("selected account");
    post.mockResolvedValue({ accountId: scope.accountId, bookId: scope.ledgerBookId, provisioned: false });
    await expect(api.provisionAccountOversight(scope)).rejects.toThrow("not confirmed");
  });

  it("encodes replay and case identities while retaining exact evidence and compare-and-set assignment", async () => {
    get.mockResolvedValue(run());
    const retained = await api.replayCashRun(scope, "run / 1");
    expect(retained.input.left.rows[0].row.amount).toBe(precise);
    expect(get).toHaveBeenCalledWith(`${base}/cash-reconciliation/runs/run%20%2F%201/replay`, { allowDevelopmentFallback: false });
    post.mockResolvedValue(cashCase());
    const request = { expectedVersion: 1, owner: "cash-ops", reason: "Retained assignment" };
    await api.assignCashCase(scope, "case / 1", request);
    expect(post).toHaveBeenCalledWith(`${base}/cash-reconciliation/cases/case%20%2F%201/assignments`, request, { allowDevelopmentFallback: false });
  });

  it("submits decimal strings and source payload bytes unchanged", async () => {
    const request = comparison(); post.mockResolvedValue(run());
    await api.runCashComparison(scope, request);
    expect(post).toHaveBeenCalledWith(`${base}/cash-reconciliation/runs`, request, { allowDevelopmentFallback: false });
    expect(JSON.stringify(post.mock.calls[0][1])).toContain(`"amount":"${precise}"`);
    expect(request.left.rows[0].originalPayload).toBe(comparison().left.rows[0].originalPayload);
  });

  it("rejects foreign cases, replay runs and returned assignments", async () => {
    get.mockResolvedValue([{ ...cashCase(), scope: { ...comparison().scope, accountId: foreignAccount } }]);
    await expect(api.listCashCases(scope)).rejects.toThrow("selected account");
    const wrongRun = run(); wrongRun.input.runId = "another-run"; get.mockResolvedValue(wrongRun);
    await expect(api.replayCashRun(scope, "run / 1")).rejects.toThrow("selected run");
    post.mockResolvedValue({ ...cashCase(), caseId: "another-case" });
    await expect(api.assignCashCase(scope, "case / 1", { expectedVersion: 1, owner: "owner", reason: "reason" })).rejects.toThrow("another cash case");
  });

  it("reads, creates and approves exactly scoped retained price versions", async () => {
    const value = priceSet(); get.mockResolvedValueOnce([value]).mockResolvedValueOnce(value);
    expect((await api.listClosePriceSets(scope, businessDate))[0].snapshot.lines[0].closePrice).toBe(precise);
    await api.getClosePriceSet(scope, businessDate, priceId);
    expect(get.mock.calls.map(call => call[0])).toEqual([`${base}/close-price-sets/${businessDate}`, `${base}/close-price-sets/${businessDate}/${priceId}`]);
    const create = { accountId: scope.accountId, bookId: scope.ledgerBookId, valuationDate: businessDate, evaluatedAsOf: timestamp,
      securities: [{ securityId: securityId.toUpperCase(), override: { price: precise, unit: "CurrencyPerUnit" as const, rationale: "Verified source", evidenceReference: "retained:price" } }],
      maxSourceDispersionPercent: "1.25", createdBy: "" };
    post.mockResolvedValue(value); await api.createClosePriceSet(scope, create);
    expect(post).toHaveBeenLastCalledWith(`${base}/close-price-sets/${businessDate}`, create, { allowDevelopmentFallback: false });
    const approve = { accountId: scope.accountId, bookId: scope.ledgerBookId, valuationDate: businessDate, snapshotId: priceId, reviewer: "", rationale: "Independent review" };
    value.approval = { snapshotId: priceId, snapshotSha256: "retained-digest", reviewer: "independent-reviewer", rationale: approve.rationale, approvedAt: timestamp };
    await api.approveClosePriceSet(scope, approve);
    expect(post).toHaveBeenLastCalledWith(`${base}/close-price-sets/${businessDate}/${priceId}/approval`, approve, { allowDevelopmentFallback: false });
  });

  it.each(["account", "date", "snapshot", "hierarchy", "approval"])("rejects mismatched price %s evidence", async mismatch => {
    const value = priceSet();
    if (mismatch === "account") value.snapshot.accountId = foreignAccount;
    if (mismatch === "date") value.snapshot.valuationDate = "2026-10-13";
    if (mismatch === "snapshot") value.snapshot.snapshotId = foreignAccount;
    if (mismatch === "hierarchy") value.snapshot.lines[0].selection!.hierarchySnapshot!.accountId = foreignAccount;
    if (mismatch === "approval") value.approval = { snapshotId: foreignAccount, snapshotSha256: "digest", reviewer: "reviewer", rationale: "Reviewed", approvedAt: timestamp };
    get.mockResolvedValue(value);
    await expect(api.getClosePriceSet(scope, businessDate, priceId)).rejects.toThrow();
  });

  it("preserves one exact price command identity across retries and accepts its retained approved result", async () => {
    const request = priceCommand(); request.requestId = priceId.toUpperCase();
    const retained = priceSet(); retained.snapshot.creationCommandDigest = "c".repeat(64);
    retained.snapshot.requestedKnowledgeAsOf = timestamp;
    retained.approval = { snapshotId: priceId, snapshotSha256: "approved-digest", reviewer: "independent-reviewer", rationale: "Reviewed", approvedAt: timestamp };
    post.mockResolvedValue(retained);
    const first = await api.createClosePriceSet(scope, request);
    const replay = await api.createClosePriceSet(scope, request);
    expect(first).toBe(retained); expect(replay).toBe(retained);
    expect(replay.snapshot.creationCommandDigest).toBe("c".repeat(64));
    expect(replay.snapshot.requestedKnowledgeAsOf).toBe(timestamp);
    expect(replay.approval?.reviewer).toBe("independent-reviewer");
    expect(post.mock.calls.map(call => call[1])).toEqual([request, request]);
    expect(request.requestId).toBe(priceId.toUpperCase());
  });

  it.each(["invalid", "", "00000000-0000-0000-0000-000000000000"])("rejects invalid price creation request identity %s before writing", async requestId => {
    await expect(api.createClosePriceSet(scope, { ...priceCommand(), requestId })).rejects.toThrow("request identity");
    expect(post).not.toHaveBeenCalled();
  });

  it.each(["snapshot", "missing digest", "invalid digest"])("rejects unconfirmed identified price creation %s", async mismatch => {
    const retained = priceSet();
    retained.snapshot.creationCommandDigest = "c".repeat(64);
    if (mismatch === "snapshot") retained.snapshot.snapshotId = foreignAccount;
    if (mismatch === "missing digest") delete retained.snapshot.creationCommandDigest;
    if (mismatch === "invalid digest") retained.snapshot.creationCommandDigest = "not-a-retained-command-hash";
    post.mockResolvedValue(retained);
    await expect(api.createClosePriceSet(scope, priceCommand())).rejects.toThrow();
  });

  it("exposes a reused price command conflict without rotating its identity or retrying", async () => {
    const request = priceCommand(); const conflict = new Error("409: Creation command identity was reused with changed inputs.");
    post.mockRejectedValue(conflict);
    await expect(api.createClosePriceSet(scope, request)).rejects.toBe(conflict);
    expect(post).toHaveBeenCalledTimes(1); expect(request.requestId).toBe(priceId);
    expect(post).toHaveBeenCalledWith(`${base}/close-price-sets/${businessDate}`, request, { allowDevelopmentFallback: false });
  });

  it("preserves forecast provenance, explicit dates, history and linked corrections", async () => {
    const value = snapshot(); value.inputs.previousSnapshotId = "prior-snapshot"; value.inputs.correctionRationale = "Factor restatement";
    get.mockResolvedValueOnce([value]).mockResolvedValueOnce(value); post.mockResolvedValue(value);
    const history = await api.listCashForecasts(scope);
    expect(history[0].inputs.previousSnapshotId).toBe("prior-snapshot");
    expect(history[0].inputs.flows[0].amount).toBe("0.0000000000001");
    await api.getCashForecast(scope, value.snapshotId); await api.captureCashForecast(scope, value.inputs);
    expect(get).toHaveBeenLastCalledWith(`${base}/cash-forecasts/forecast%20%2F%20snapshot%201`, { allowDevelopmentFallback: false });
    expect(post).toHaveBeenLastCalledWith(`${base}/cash-forecasts`, value.inputs, { allowDevelopmentFallback: false });
    expect(history[0].isCertified).toBe(false); expect(history[0].blockers[0].code).toBe("MissingClosingCash");
  });

  it.each(["account", "book", "currency", "flow", "ladder", "snapshot"])("rejects mismatched forecast %s evidence", async mismatch => {
    const value = snapshot();
    if (mismatch === "account") value.inputs.scope.accountId = foreignAccount;
    if (mismatch === "book") value.inputs.scope.bookId = foreignAccount;
    if (mismatch === "currency") value.inputs.scope.baseCurrency = "EUR";
    if (mismatch === "flow") value.inputs.flows[0].accountId = foreignAccount;
    if (mismatch === "ladder") value.forecastLadder.baseCurrency = "EUR";
    if (mismatch === "snapshot") value.snapshotId = "another-snapshot";
    get.mockResolvedValue(value);
    await expect(api.getCashForecast(scope, "forecast / snapshot 1")).rejects.toThrow();
  });

  it("rejects rounded JSON numbers anywhere in returned financial evidence", async () => {
    const cash = run(); Object.assign(cash.comparisons[0].evidence[0], { numericDelta: 0.1 }); get.mockResolvedValue(cash);
    await expect(api.replayCashRun(scope, "run / 1")).rejects.toThrow("exact decimal string");
    const prices = priceSet(); Object.assign(prices.snapshot.lines[0].observations[0], { price: 1.2 }); get.mockResolvedValue([prices]);
    await expect(api.listClosePriceSets(scope, businessDate)).rejects.toThrow("exact decimal string");
    const prediction = snapshot(); Object.assign(prediction.forecastLadder, { openingCash: 1.2 }); get.mockResolvedValue([prediction]);
    await expect(api.listCashForecasts(scope)).rejects.toThrow("exact decimal string");
  });

  it("normalizes named response enums into numeric request contracts without changing financial evidence", async () => {
    const cash = run(); Object.assign(cash.input.left, { sourceType: "Prime" }); Object.assign(cash.input.right, { sourceType: "Custodian" });
    get.mockResolvedValue(cash);
    const replay = await api.replayCashRun(scope, "run / 1");
    expect(replay.input.left.sourceType).toBe(0); expect(replay.input.right.sourceType).toBe(1);
    const prediction = snapshot(); Object.assign(prediction.inputs.flows[0], { status: "Actual" });
    Object.assign(prediction.inputs.flows[0].provenance, { role: "Observation" }); get.mockResolvedValue([prediction]);
    const history = await api.listCashForecasts(scope);
    expect(history[0].inputs.flows[0].status).toBe(1); expect(history[0].inputs.flows[0].provenance.role).toBe(0);
    expect(history[0].inputs.flows[0].amount).toBe("0.0000000000001");
  });

  it("keeps server authority and stale-version failures visible without retrying fixtures", async () => {
    const conflict = new Error("409: Source evidence requires independent review."); post.mockRejectedValue(conflict);
    await expect(api.captureCashForecast(scope, forecast(), { allowDevelopmentFallback: true })).rejects.toBe(conflict);
    expect(post).toHaveBeenCalledTimes(1); expect(post.mock.calls[0][2]?.allowDevelopmentFallback).toBe(false);
  });

  it("rejects incomplete or contradictory certification and approval responses", async () => {
    const prediction = snapshot(); prediction.isCertified = true; get.mockResolvedValue([prediction]);
    await expect(api.listCashForecasts(scope)).rejects.toThrow("certification is inconsistent");
    const missingAmount = snapshot(); delete (missingAmount as unknown as Record<string, unknown>).closingVariance; get.mockResolvedValue([missingAmount]);
    await expect(api.listCashForecasts(scope)).rejects.toThrow("exact decimal string");
    post.mockResolvedValue(priceSet());
    await expect(api.approveClosePriceSet(scope, { accountId: scope.accountId, bookId: scope.ledgerBookId, valuationDate: businessDate,
      snapshotId: priceId, reviewer: "", rationale: "Independent review" })).rejects.toThrow("not retained");
  });
});

describe("account oversight handoff imports", () => {
  it("imports cash comparison with exact native balances and retained original bytes", () => {
    const value = comparison(); const imported = api.parseCashComparisonImport(JSON.stringify(value), scope);
    expect(imported).toEqual(value); expect(imported.left.rows[0].row.amount).toBe(precise);
  });
  it.each(["account", "row", "date", "number", "source", "population", "tolerance", "payload"])("rejects invalid cash handoff %s", invalid => {
    const value = comparison();
    if (invalid === "account") value.scope.accountId = foreignAccount;
    if (invalid === "row") value.left.rows[0].row.accountId = foreignAccount;
    if (invalid === "date") value.left.rows[0].row.accountingPeriod = "2026-10-13";
    if (invalid === "number") Object.assign(value.left.rows[0].row, { amount: 0.1 });
    if (invalid === "source") value.right.sourceId = value.left.sourceId;
    if (invalid === "population") value.left.completePopulation = false;
    if (invalid === "tolerance") value.tolerance.version = 0;
    if (invalid === "payload") value.left.rows[0].originalPayload = "";
    expect(() => api.parseCashComparisonImport(JSON.stringify(value), scope)).toThrow();
  });
  it("imports expected/actual dates and missing observations for server blocker assessment", () => {
    const value = forecast();
    expect(api.parseCashForecastImport(JSON.stringify(value), scope)).toEqual(value);
    value.openingCash = null; value.flows[0].cashReceiptDate = null;
    expect(api.parseCashForecastImport(JSON.stringify(value), scope)).toEqual(value);
  });
  it("normalizes supported named handoff enums for minimal HTTP request binding", () => {
    const cash = comparison(); Object.assign(cash.left, { sourceType: "Prime" });
    expect(api.parseCashComparisonImport(JSON.stringify(cash), scope).left.sourceType).toBe(0);
    const prediction = forecast(); Object.assign(prediction.flows[0], { status: "Actual" });
    Object.assign(prediction.flows[0].provenance, { role: "Observation" });
    const imported = api.parseCashForecastImport(JSON.stringify(prediction), scope);
    expect(imported.flows[0].status).toBe(1); expect(imported.flows[0].provenance.role).toBe(0);
  });
  it.each(["account", "flow", "number", "fx", "dates", "provenance", "status"])("rejects invalid forecast handoff %s", invalid => {
    const value = forecast();
    if (invalid === "account") value.scope.accountId = foreignAccount;
    if (invalid === "flow") value.flows[0].bookId = foreignAccount;
    if (invalid === "number") Object.assign(value.flows[0], { amount: 0.1 });
    if (invalid === "fx") value.fxConversions.push({ currency: "EUR", baseCurrency: "GBP", conversionDate: businessDate, baseUnitsPerCurrencyUnit: "1.25", provenance: provenance() });
    if (invalid === "dates") value.flows[0].economicDate = "not-a-date";
    if (invalid === "provenance") value.flows[0].provenance.evidence.evidenceId = "";
    if (invalid === "status") Object.assign(value.flows[0], { status: "Unknown" });
    expect(() => api.parseCashForecastImport(JSON.stringify(value), scope)).toThrow();
  });
  it("rejects invalid JSON and oversized files before reading financial values", () => {
    expect(() => api.parseCashForecastImport("not-json", scope)).toThrow("valid JSON");
    expect(() => api.parseCashComparisonImport(" ".repeat(1024 * 1024 + 1), scope)).toThrow("1 MiB");
  });
});
