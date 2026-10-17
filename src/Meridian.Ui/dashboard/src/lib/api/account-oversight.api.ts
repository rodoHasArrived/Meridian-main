import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type { LedgerBook } from "@/types";
import type {
  AccountCashForecastRequest, AccountCashForecastSnapshot, AccountCashReconciliationCase,
  AccountCashReconciliationRequest, AccountCashReconciliationRun, ApproveClosePriceSetRequest,
  ClosePriceSet, CreateClosePriceSetRequest, OversightContext, OversightScope
} from "@/types/account-oversight";

const authoritative = (options: ApiRequestOptions) => ({ ...options, allowDevelopmentFallback: false });
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const decimal = /^-?\d+(?:\.\d+)?$/;
const sha256 = /^(?!0{64}$)[0-9a-f]{64}$/i;
const decimalFields = new Set([
  "amount", "amountBase", "baseAmount", "quantity", "price", "marketValue", "numericDelta",
  "absoluteCashTolerance", "basisPointCashTolerance", "quantityTolerance", "marketValueTolerance", "priceTolerance",
  "goldenCopyPrice", "pctDiffFromGoldenCopy", "closePrice", "maxSourceDispersionPercent", "baseUnitsPerCurrencyUnit",
  "minimumCashThreshold", "openingBaseCash", "actualNetBaseFlow", "reconciledClosingBaseCash", "observedClosingBaseCash",
  "closingVariance", "inflows", "outflows", "netFlow", "cumulativeCash"
]);

function fail(message: string): never { throw new Error(message); }
function record(value: unknown, label: string): asserts value is Record<string, unknown> {
  if (!value || typeof value !== "object" || Array.isArray(value)) fail(`${label} must be an object.`);
}
function text(value: unknown, label: string): asserts value is string {
  if (typeof value !== "string" || !value.trim()) fail(`${label} is required.`);
}
function identifier(value: unknown, label: string): asserts value is string {
  if (typeof value !== "string" || !guid.test(value) || value === "00000000-0000-0000-0000-000000000000") fail(`${label} must be a valid identity.`);
}
function array(value: unknown, label: string): asserts value is unknown[] {
  if (!Array.isArray(value)) fail(`${label} must be an array.`);
}
function date(value: unknown, label: string): asserts value is string {
  if (typeof value !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(value)) fail(`${label} must be a calendar date.`);
  const parsed = new Date(`${value}T00:00:00Z`);
  if (!Number.isFinite(parsed.getTime()) || parsed.toISOString().slice(0, 10) !== value) fail(`${label} must be a calendar date.`);
}
function utc(value: unknown, label: string): asserts value is string {
  if (typeof value !== "string" || !/(?:Z|\+00:00)$/.test(value) || !/^\d{4}-\d{2}-\d{2}T/.test(value)
    || !Number.isFinite(Date.parse(value))) fail(`${label} must be a UTC timestamp.`);
}
function integer(value: unknown, label: string, minimum = 1): asserts value is number {
  if (typeof value !== "number" || !Number.isSafeInteger(value) || value < minimum) fail(`${label} must be an integer of at least ${minimum}.`);
}
function amount(value: unknown, label: string): asserts value is string {
  if (typeof value !== "string" || !decimal.test(value)) fail(`${label} must be an exact decimal string; JSON numbers are unsupported.`);
}
function digest(value: unknown, label: string): asserts value is string {
  if (typeof value !== "string" || !sha256.test(value)) fail(`${label} must be a retained SHA-256 digest.`);
}
function nullableAmount(value: unknown, label: string): void { if (value !== null) amount(value, label); }
function exactDecimals(value: unknown, parent = "response"): void {
  if (Array.isArray(value)) { value.forEach((entry, i) => exactDecimals(entry, `${parent}[${i}]`)); return; }
  if (!value || typeof value !== "object") return;
  for (const [key, entry] of Object.entries(value)) {
    // Opening cash is a nested observation on requests and a decimal on the shared ladder.
    if ((decimalFields.has(key) || key === "openingCash" && typeof entry !== "object") && entry !== null) amount(entry, `${parent}.${key}`);
    else exactDecimals(entry, `${parent}.${key}`);
  }
}
/** Shared responses may use named enums; the minimal HTTP request binder requires numeric enums. */
function normalizeWireEnums(value: unknown): void {
  if (Array.isArray(value)) { value.forEach(normalizeWireEnums); return; }
  if (!value || typeof value !== "object") return;
  const names: Record<string, readonly string[]> = {
    sourceType: ["Prime", "Custodian", "Administrator", "InternalLedger"],
    classification: ["Matched", "MatchedWithinTolerance", "PotentialBreak", "TrueBreak"],
    status: ["Expected", "Actual"], role: ["Observation", "Projection", "DeclaredFx"]
  };
  const target = value as Record<string, unknown>;
  for (const [key, entry] of Object.entries(target)) {
    if (typeof entry === "string" && names[key]?.includes(entry)) target[key] = names[key].indexOf(entry);
    else normalizeWireEnums(entry);
  }
}
function validateScope(scope: OversightScope): void {
  identifier(scope.accountId, "Selected account"); identifier(scope.ledgerBookId, "Selected ledger book");
  if (!/^[A-Z]{3}$/.test(scope.baseCurrency)) fail("Selected book base currency is required.");
}
function scopeMatches(value: unknown, scope: OversightScope, bookField: "bookId" | "ledgerBookId", withCurrency = false): void {
  record(value, "Retained scope");
  if (value.accountId !== scope.accountId || value[bookField] !== scope.ledgerBookId
    || withCurrency && value.baseCurrency !== scope.baseCurrency) fail("Retained evidence does not match the selected account, book and currency scope.");
}
function path(template: string, scope: OversightScope, variables: Record<string, string> = {}): string {
  validateScope(scope);
  let result = template.replace("{ledgerBookId:guid}", encodeURIComponent(scope.ledgerBookId))
    .replace("{accountId:guid}", encodeURIComponent(scope.accountId));
  for (const [name, value] of Object.entries(variables)) result = result.replace(`{${name}}`, encodeURIComponent(value));
  return result;
}
function reconcileScope(value: unknown, scope: OversightScope): void {
  scopeMatches(value, scope, "ledgerBookId");
}
function validateCashRequest(value: unknown, scope: OversightScope): asserts value is AccountCashReconciliationRequest {
  normalizeWireEnums(value);
  record(value, "Cash comparison"); reconcileScope(value.scope, scope);
  text(value.runId, "Run identity"); date(value.businessDate, "Business date"); utc(value.observedAtUtc, "Observation time");
  text(value.initialOwner, "Initial owner");
  for (const side of ["left", "right"]) {
    const source = value[side]; record(source, `${side} source`);
    text(source.sourceId, "Source identity"); text(source.version, "Source version");
    text(source.evidenceReference, "Source evidence reference"); utc(source.capturedAtUtc, "Source capture time");
    if (![0, 1, 2, 3].includes(source.sourceType as number)) fail("Source type must use the shared numeric enum.");
    if (source.completePopulation !== true) fail("Cash comparisons require a complete source population.");
    array(source.rows, "Source rows"); if (!source.rows.length) fail("Source rows are required.");
    for (const entry of source.rows) {
      record(entry, "Source row"); text(entry.originalPayload, "Original source payload"); record(entry.row, "Normalized source row");
      const row = entry.row;
      if (row.accountId !== scope.accountId || row.accountingPeriod !== value.businessDate) fail("Source rows must match the selected account and business date.");
      text(row.cashEntryId, "Source row identity"); text(row.currency, "Source currency"); text(row.baseCurrency, "Source base currency");
      amount(row.amount, "Source amount"); amount(row.amountBase, "Source base amount");
      utc(row.postedAtUtc, "Source posting time"); text(row.sourceReference, "Source reference");
    }
  }
  record(value.left, "Left source"); record(value.right, "Right source");
  if (value.left.sourceId === value.right.sourceId || value.left.sourceType === value.right.sourceType) fail("Two independent cash sources are required.");
  record(value.tolerance, "Executed tolerance profile");
  text(value.tolerance.profileId, "Tolerance profile identity"); integer(value.tolerance.version, "Tolerance version");
  for (const name of ["cashRules", "positionRules", "transactionRules"]) {
    array(value.tolerance[name], `Tolerance ${name}`);
    for (const rule of value.tolerance[name]) {
      record(rule, "Tolerance rule"); text(rule.ruleId, "Tolerance rule identity");
      if (name === "cashRules") {
        amount(rule.absoluteCashTolerance, "Absolute cash tolerance"); nullableAmount(rule.basisPointCashTolerance, "Basis point tolerance");
        text(rule.settlementDateTolerance, "Settlement timing tolerance");
      }
      if (name === "positionRules") {
        amount(rule.quantityTolerance, "Quantity tolerance"); amount(rule.marketValueTolerance, "Market value tolerance");
        amount(rule.priceTolerance, "Price tolerance");
      }
      if (name === "transactionRules") {
        amount(rule.absoluteCashTolerance, "Transaction cash tolerance"); amount(rule.priceTolerance, "Transaction price tolerance");
        text(rule.settlementDateTolerance, "Transaction timing tolerance");
      }
    }
  }
  const cashRules = value.tolerance.cashRules;
  array(cashRules, "Executed cash tolerance rules");
  if (!cashRules.length) fail("An executed cash tolerance rule is required.");
  exactDecimals(value, "comparison");
}
function validateCashRun(value: unknown, scope: OversightScope, runId?: string): asserts value is AccountCashReconciliationRun {
  normalizeWireEnums(value);
  record(value, "Retained comparison"); validateCashRequest(value.input, scope);
  if (runId !== undefined && value.input.runId !== runId) fail("The replay does not match the selected run.");
  text(value.inputHash, "Input hash"); text(value.resultHash, "Result hash"); array(value.comparisons, "Comparison evidence");
  exactDecimals(value);
}
function validateCashCase(value: unknown, scope: OversightScope): asserts value is AccountCashReconciliationCase {
  record(value, "Cash case"); reconcileScope(value.scope, scope);
  text(value.caseId, "Case identity"); text(value.owner, "Case owner"); text(value.state, "Case state");
  integer(value.version, "Case version"); integer(value.agingDays, "Case aging", 0);
  array(value.history, "Case history"); array(value.assignments, "Case assignments");
}
function provenance(value: unknown): void {
  record(value, "Flow provenance"); text(value.sourceDomain, "Source domain"); text(value.sourceEntityId, "Source entity");
  text(value.sourceContentHash, "Source content hash");
  if (![0, 1, 2].includes(value.role as number)) fail("Evidence role must use the shared numeric enum.");
  record(value.evidence, "Retained evidence identity");
  for (const field of ["evidenceId", "evidenceUri", "contentHashSha256", "sourceSystem", "sourceReference", "reviewStatus", "reviewedBy", "retainedBy", "subjectType", "subjectId"])
    text(value.evidence[field], `Evidence ${field}`);
  utc(value.evidence.reviewedAtUtc, "Evidence review time"); utc(value.evidence.retainedAtUtc, "Evidence retention time");
  date(value.evidence.effectiveDate, "Evidence effective date"); integer(value.evidence.evidenceVersion, "Evidence version");
}
function validateForecastRequest(value: unknown, scope: OversightScope): asserts value is AccountCashForecastRequest {
  normalizeWireEnums(value);
  record(value, "Cash forecast"); scopeMatches(value.scope, scope, "bookId", true);
  text(value.runId, "Forecast run identity"); date(value.periodStart, "Period start"); date(value.periodEnd, "Period end");
  utc(value.recordedAtUtc, "Forecast recording time"); integer(value.bucketDays, "Bucket days"); amount(value.minimumCashThreshold, "Minimum cash threshold");
  array(value.flows, "Forecast flows"); array(value.fxConversions, "Declared FX conversions");
  for (const name of ["openingCash", "closingCash"]) {
    const observation = value[name];
    if (observation === null) continue;
    record(observation, name); scopeMatches(observation, scope, "bookId");
    date(observation.asOfDate, "Settled cash date"); amount(observation.amount, "Settled cash amount");
    text(observation.currency, "Settled cash currency");
    if (typeof observation.isSettled !== "boolean") fail("Settled cash observation status is required.");
    provenance(observation.provenance);
  }
  for (const flow of value.flows) {
    record(flow, "Cash flow"); scopeMatches(flow, scope, "bookId");
    text(flow.flowId, "Flow identity"); text(flow.versionId, "Flow version"); text(flow.flowType, "Flow type");
    if (flow.status !== 0 && flow.status !== 1) fail("Flow status must use the shared numeric enum.");
    amount(flow.amount, "Flow amount"); text(flow.currency, "Flow currency");
    for (const field of ["economicDate", "settlementDate", "cashReceiptDate"]) if (flow[field] !== null) date(flow[field], field);
    provenance(flow.provenance);
  }
  for (const fx of value.fxConversions) {
    record(fx, "FX conversion"); text(fx.currency, "FX native currency");
    if (fx.baseCurrency !== scope.baseCurrency) fail("Declared FX must match the selected book base currency.");
    date(fx.conversionDate, "FX conversion date"); amount(fx.baseUnitsPerCurrencyUnit, "Declared FX rate"); provenance(fx.provenance);
  }
  exactDecimals(value, "forecast");
}
function validateForecast(value: unknown, scope: OversightScope, snapshotId?: string): asserts value is AccountCashForecastSnapshot {
  record(value, "Retained forecast"); validateForecastRequest(value.inputs, scope);
  text(value.snapshotId, "Forecast snapshot identity"); text(value.evidenceHash, "Forecast evidence hash");
  if (snapshotId !== undefined && value.snapshotId !== snapshotId) fail("The forecast does not match the selected snapshot.");
  if (typeof value.isCertified !== "boolean") fail("Forecast certification result is unavailable.");
  array(value.blockers, "Forecast blockers"); record(value.forecastLadder, "Forecast ladder");
  for (const key of ["openingBaseCash", "actualNetBaseFlow", "reconciledClosingBaseCash", "observedClosingBaseCash", "closingVariance"])
    nullableAmount(value[key], key);
  if (value.isCertified && (value.blockers.length > 0 || [value.openingBaseCash, value.actualNetBaseFlow,
    value.reconciledClosingBaseCash, value.observedClosingBaseCash, value.closingVariance].some(entry => entry === null)))
    fail("Forecast certification is inconsistent with its retained cash evidence and blockers.");
  if (value.forecastLadder.baseCurrency !== scope.baseCurrency) fail("The forecast ladder does not match the selected base currency.");
  amount(value.forecastLadder.openingCash, "Ladder opening cash"); amount(value.forecastLadder.minimumCashThreshold, "Ladder minimum cash threshold");
  array(value.forecastLadder.buckets, "Forecast buckets"); array(value.forecastLadder.contributions, "Forecast contributions");
  for (const bucket of value.forecastLadder.buckets) {
    record(bucket, "Forecast bucket");
    for (const key of ["inflows", "outflows", "netFlow", "cumulativeCash"]) amount(bucket[key], key);
    array(bucket.sources, "Forecast source slices");
    for (const source of bucket.sources) { record(source, "Forecast source slice"); amount(source.amount, "Source slice amount"); }
  }
  for (const contribution of value.forecastLadder.contributions) {
    record(contribution, "Forecast contribution"); amount(contribution.amount, "Contribution amount"); amount(contribution.baseAmount, "Contribution base amount");
  }
  exactDecimals(value);
}
function validatePriceSet(value: unknown, scope: OversightScope, valuationDate: string, snapshotId?: string): asserts value is ClosePriceSet {
  record(value, "Close-price set"); record(value.snapshot, "Close-price snapshot");
  scopeMatches(value.snapshot, scope, "bookId");
  identifier(value.snapshot.snapshotId, "Price snapshot identity");
  if (value.snapshot.valuationDate !== valuationDate || snapshotId !== undefined && value.snapshot.snapshotId.toLowerCase() !== snapshotId.toLowerCase())
    fail("The price evidence does not match the selected valuation date and snapshot.");
  integer(value.snapshot.version, "Price snapshot version");
  if (value.snapshot.creationCommandDigest !== undefined && value.snapshot.creationCommandDigest !== null)
    digest(value.snapshot.creationCommandDigest, "Creation command digest");
  if (value.snapshot.requestedKnowledgeAsOf !== undefined && value.snapshot.requestedKnowledgeAsOf !== null)
    utc(value.snapshot.requestedKnowledgeAsOf, "Requested knowledge cutoff");
  amount(value.snapshot.maxSourceDispersionPercent, "Source dispersion limit");
  array(value.snapshot.lines, "Price lines"); array(value.snapshot.blockers, "Price blockers");
  for (const line of value.snapshot.lines) {
    record(line, "Price line"); identifier(line.securityId, "Priced security identity"); array(line.observations, "Price observations");
    nullableAmount(line.closePrice, "Retained close price");
    if (!["Unspecified", "CurrencyPerUnit", "PercentOfPar"].includes(line.unit as string)) fail("Retained price quote units are unsupported.");
    for (const observation of line.observations) { record(observation, "Price observation"); amount(observation.price, "Source observation price"); }
    if (line.selection !== null) {
      record(line.selection, "Selected price");
      amount(line.selection.goldenCopyPrice, "Selected source price");
      if (line.selection.securityId !== line.securityId) fail("Selected price evidence belongs to another security.");
      if (line.selection.hierarchySnapshot !== null && line.selection.hierarchySnapshot !== undefined) {
        record(line.selection.hierarchySnapshot, "Retained hierarchy");
        if (line.selection.hierarchySnapshot.securityId !== line.securityId || line.selection.hierarchySnapshot.accountId !== scope.accountId)
          fail("Retained price hierarchy belongs to another account or security.");
      }
    }
  }
  if (value.approval !== null) {
    record(value.approval, "Price approval");
    if (value.approval.snapshotId !== value.snapshot.snapshotId) fail("The approval does not match the retained price snapshot.");
    text(value.approval.snapshotSha256, "Approved snapshot digest"); text(value.approval.reviewer, "Independent reviewer");
    text(value.approval.rationale, "Reviewer rationale");
    if (value.snapshot.blockers.length || typeof value.snapshot.createdBy !== "string"
      || value.approval.reviewer.toLowerCase() === value.snapshot.createdBy.toLowerCase())
      fail("Price approval requires independent review and no retained blockers.");
  }
  exactDecimals(value);
}
function parseImport(source: string): unknown {
  if (new TextEncoder().encode(source).byteLength > 1024 * 1024) fail("The import exceeds the 1 MiB handoff limit.");
  try { return JSON.parse(source) as unknown; } catch { return fail("The selected handoff file must contain valid JSON."); }
}

export function parseCashComparisonImport(source: string, scope: OversightScope): AccountCashReconciliationRequest {
  validateScope(scope); const value = parseImport(source); validateCashRequest(value, scope); return value;
}
export function parseCashForecastImport(source: string, scope: OversightScope): AccountCashForecastRequest {
  validateScope(scope); const value = parseImport(source); validateForecastRequest(value, scope); return value;
}
export async function listAccountOversightBooks(fundProfileId?: string, options: ApiRequestOptions = {}): Promise<LedgerBook[]> {
  const query = fundProfileId ? `?${new URLSearchParams({ fundProfileId })}` : "";
  const books = await apiGetJson<LedgerBook[]>(`${UI_API_ROUTES.LedgerBooks}${query}`, authoritative(options));
  array(books, "Accessible ledger books");
  if (fundProfileId && books.some(book => book.fundProfileId !== fundProfileId)) fail("Accessible books do not match the selected fund scope.");
  return books.filter(book => book.fundStructureNodeKind === "Account" && guid.test(book.ledgerBookId)
    && guid.test(book.fundStructureNodeId) && book.ledgerBookId !== "00000000-0000-0000-0000-000000000000"
    && book.fundStructureNodeId !== "00000000-0000-0000-0000-000000000000" && /^[A-Z]{3}$/.test(book.baseCurrency));
}
export async function getAccountOversightContext(scope: OversightScope, options: ApiRequestOptions = {}): Promise<OversightContext> {
  const value = await apiGetJson<OversightContext>(path(UI_API_ROUTES.LedgerAccountOversightContext, scope), authoritative(options));
  scopeMatches(value, scope, "bookId", true); text(value.actor, "Authenticated actor"); text(value.bookName, "Ledger book name");
  for (const key of ["canWrite", "canReadPrices", "canManagePrices", "canProvision"] as const)
    if (typeof value[key] !== "boolean") fail("Authoritative oversight permissions are unavailable.");
  return value;
}
export async function provisionAccountOversight(scope: OversightScope, options: ApiRequestOptions = {}): Promise<{ accountId: string; bookId: string; provisioned: boolean }> {
  const value = await apiPostJson<{ accountId: string; bookId: string; provisioned: boolean }>(path(UI_API_ROUTES.LedgerAccountOversightProvision, scope), undefined, authoritative(options));
  scopeMatches(value, scope, "bookId"); if (value.provisioned !== true) fail("Durable oversight initialization was not confirmed."); return value;
}
export async function listCashCases(scope: OversightScope, options: ApiRequestOptions = {}): Promise<AccountCashReconciliationCase[]> {
  const value = await apiGetJson<AccountCashReconciliationCase[]>(path(UI_API_ROUTES.LedgerAccountCashReconciliationCases, scope), authoritative(options));
  array(value, "Cash cases"); value.forEach(entry => validateCashCase(entry, scope)); return value;
}
export async function runCashComparison(scope: OversightScope, request: AccountCashReconciliationRequest, options: ApiRequestOptions = {}): Promise<AccountCashReconciliationRun> {
  validateCashRequest(request, scope);
  const value = await apiPostJson<AccountCashReconciliationRun>(path(UI_API_ROUTES.LedgerAccountCashReconciliationRuns, scope), request, authoritative(options));
  validateCashRun(value, scope, request.runId); return value;
}
export async function replayCashRun(scope: OversightScope, runId: string, options: ApiRequestOptions = {}): Promise<AccountCashReconciliationRun> {
  const value = await apiGetJson<AccountCashReconciliationRun>(path(UI_API_ROUTES.LedgerAccountCashReconciliationReplay, scope, { runId }), authoritative(options));
  validateCashRun(value, scope, runId); return value;
}
export async function assignCashCase(scope: OversightScope, caseId: string, request: { expectedVersion: number; owner: string; reason: string }, options: ApiRequestOptions = {}): Promise<AccountCashReconciliationCase> {
  const value = await apiPostJson<AccountCashReconciliationCase>(path(UI_API_ROUTES.LedgerAccountCashReconciliationAssignment, scope, { caseId }), request, authoritative(options));
  validateCashCase(value, scope); if (value.caseId !== caseId) fail("The assignment belongs to another cash case."); return value;
}
export async function listClosePriceSets(scope: OversightScope, valuationDate: string, options: ApiRequestOptions = {}): Promise<ClosePriceSet[]> {
  date(valuationDate, "Valuation date");
  const value = await apiGetJson<ClosePriceSet[]>(path(UI_API_ROUTES.LedgerAccountClosePriceSets, scope, { valuationDate }), authoritative(options));
  array(value, "Close-price sets"); value.forEach(entry => validatePriceSet(entry, scope, valuationDate)); return value;
}
export async function getClosePriceSet(scope: OversightScope, valuationDate: string, snapshotId: string, options: ApiRequestOptions = {}): Promise<ClosePriceSet> {
  date(valuationDate, "Valuation date");
  const value = await apiGetJson<ClosePriceSet>(path(UI_API_ROUTES.LedgerAccountClosePriceSet, scope, { valuationDate, "snapshotId:guid": snapshotId }), authoritative(options));
  validatePriceSet(value, scope, valuationDate, snapshotId); return value;
}
export async function createClosePriceSet(scope: OversightScope, request: CreateClosePriceSetRequest, options: ApiRequestOptions = {}): Promise<ClosePriceSet> {
  scopeMatches(request, scope, "bookId"); date(request.valuationDate, "Valuation date");
  if (request.requestId !== undefined && request.requestId !== null) identifier(request.requestId, "Price creation request identity");
  amount(request.maxSourceDispersionPercent, "Source dispersion limit"); exactDecimals(request, "price request");
  const value = await apiPostJson<ClosePriceSet>(path(UI_API_ROUTES.LedgerAccountClosePriceSets, scope, { valuationDate: request.valuationDate }), request, authoritative(options));
  validatePriceSet(value, scope, request.valuationDate);
  if (request.requestId !== undefined && request.requestId !== null) {
    if (value.snapshot.snapshotId.toLowerCase() !== request.requestId.toLowerCase())
      fail("The close-price result does not match the retained creation request identity.");
    digest(value.snapshot.creationCommandDigest, "Creation command digest");
  }
  if ((value.snapshot.correctsSnapshotId?.toLowerCase() ?? null) !== (request.correctsSnapshotId?.toLowerCase() ?? null)
    || JSON.stringify(value.snapshot.lines.map(line => line.securityId.toLowerCase()).sort()) !== JSON.stringify(request.securities.map(line => line.securityId.toLowerCase()).sort()))
    fail("The close-price result does not match the requested securities and correction lineage.");
  return value;
}
export async function approveClosePriceSet(scope: OversightScope, request: ApproveClosePriceSetRequest, options: ApiRequestOptions = {}): Promise<ClosePriceSet> {
  scopeMatches(request, scope, "bookId"); date(request.valuationDate, "Valuation date");
  const value = await apiPostJson<ClosePriceSet>(path(UI_API_ROUTES.LedgerAccountClosePriceSetApproval, scope,
    { valuationDate: request.valuationDate, "snapshotId:guid": request.snapshotId }), request, authoritative(options));
  validatePriceSet(value, scope, request.valuationDate, request.snapshotId);
  if (value.approval === null) fail("Independent approval was not retained for the selected price snapshot.");
  return value;
}
export async function listCashForecasts(scope: OversightScope, options: ApiRequestOptions = {}): Promise<AccountCashForecastSnapshot[]> {
  const value = await apiGetJson<AccountCashForecastSnapshot[]>(path(UI_API_ROUTES.LedgerAccountCashForecasts, scope), authoritative(options));
  array(value, "Cash forecasts"); value.forEach(entry => validateForecast(entry, scope)); return value;
}
export async function captureCashForecast(scope: OversightScope, request: AccountCashForecastRequest, options: ApiRequestOptions = {}): Promise<AccountCashForecastSnapshot> {
  validateForecastRequest(request, scope);
  const value = await apiPostJson<AccountCashForecastSnapshot>(path(UI_API_ROUTES.LedgerAccountCashForecasts, scope), request, authoritative(options));
  validateForecast(value, scope); if (value.inputs.runId !== request.runId) fail("The forecast belongs to another capture run."); return value;
}
export async function getCashForecast(scope: OversightScope, snapshotId: string, options: ApiRequestOptions = {}): Promise<AccountCashForecastSnapshot> {
  const value = await apiGetJson<AccountCashForecastSnapshot>(path(UI_API_ROUTES.LedgerAccountCashForecast, scope, { snapshotId }), authoritative(options));
  validateForecast(value, scope, snapshotId); return value;
}
