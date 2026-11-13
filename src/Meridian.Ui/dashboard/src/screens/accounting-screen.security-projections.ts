import { type ApiErrorDisplay } from "@/lib/api-errors";
import { type ReferenceDataEndpointProbeResult, type ReferenceDataWorkbenchCoverage } from "@/lib/api";
import { markDevelopmentFixtureUsage } from "@/lib/api";
import { resolveDevSecurityScheduleEvents } from "@/lib/security-schedule-dev-fixtures";
import { formatCount, formatDateTimeLabel } from "./accounting-screen.formatting";
import { formatCorporateActionPayload } from "./accounting-screen.corporate-action-formatting";
import { cashFlowBlockerEvidence, declaredExerciseEvidence, governanceEvidence, type SecurityMasterPolicyEvidence } from "./security-master-policy-evidence";
import {
  formatFinanceFacingSourceSummary,
  formatSecurityDate,
  formatSecurityScheduleEventType,
  formatSecuritySchedulePostingStatus,
  referenceDataStatusBadgeVariant,
  referenceDataStatusLabel,
  referenceDataStatusTone,
  summarizeReferenceDataRoutes
} from "./accounting-screen.security-master-presenters";
import { normalizeApiErrorDisplay } from "./accounting-screen.view-model.shared";
import type {
  CorporateAction,
  InstrumentPassport,
  InstrumentPassportOperationsReadiness,
  InstrumentPassportOperationsWorkbenchItem,
  InstrumentPassportOperationsWorkbenchPanel,
  InstrumentPassportProviderConfidence,
  SecurityMasterDeclaredExerciseOutcome,
  SecurityMasterGovernance,
  SecurityMasterOpenLot,
  SecurityMasterOpenLotReadModel,
  SecurityMasterScheduleBlocker,
  SecurityMasterTrustSnapshot,
  TradingParameters
} from "@/types";
import { formatCount as formatCountValue, formatNumber } from "@/lib/format";

// Domain-specific projections and components shared by the screen composition.


export interface CorporateActionRowViewModel extends CorporateAction {
  rowId: string;
  eventTypeLabel: string;
  exDateLabel: string;
  payDateLabel: string;
  amountLabel: string;
  ariaLabel: string;
  selectAriaLabel: string;
  detailPanelId: string;
  isExpanded: boolean;
}


export interface CorporateActionDetailFieldViewModel {
  label: string;
  value: string;
  tone?: "default" | "warning";
}


export interface CorporateActionDetailViewState {
  id: string;
  eyebrow: string;
  title: string;
  subtitle: string;
  description: string;
  ariaLabel: string;
  statusLabel: string;
  fields: CorporateActionDetailFieldViewModel[];
}


export interface CorporateActionsViewState {
  securityId: string;
  tableLabel: string;
  tableCaption: string;
  detailPanelId: string;
  rows: CorporateActionRowViewModel[];
  selectedRowId: string | null;
  selectedDetail: CorporateActionDetailViewState | null;
  emptyText: string;
  detailEmptyTitle: string;
  detailEmptyText: string;
  detailEmptyAriaLabel: string;
  loadingText: string | null;
  errorText: string | null;
  errorDetails: string[];
  hasRows: boolean;
  statusAnnouncement: string;
}


export type SecurityScheduleFamily = "bond" | "structured" | "fund" | "derivative";

export type SecurityScheduleEventType = string;

export type SecuritySchedulePostingStatus = string;


export interface SecurityCashFlowScheduleEvent {
  eventId: string;
  securityId: string;
  scheduleFamily: SecurityScheduleFamily;
  eventType: SecurityScheduleEventType;
  paymentDate: string;
  accrualStartDate: string | null;
  accrualEndDate: string | null;
  couponRatePct: number | null;
  expectedAmount: number | null;
  actualAmount: number | null;
  principalAmount: number | null;
  interestAmount: number | null;
  factorStart: number | null;
  factorEnd: number | null;
  currency: string;
  postingStatus: SecuritySchedulePostingStatus;
  auditReference: string | null;
  note: string | null;
}


export interface SecurityScheduleRowViewModel extends SecurityCashFlowScheduleEvent {
  rowId: string;
  eventTypeLabel: string;
  paymentDateLabel: string;
  expectedAmountLabel: string;
  actualAmountLabel: string;
  varianceLabel: string;
  factorLabel: string;
  postingStatusLabel: string;
  postingStatusTone: "success" | "warning" | "danger" | "outline";
  ariaLabel: string;
  selectAriaLabel: string;
  detailPanelId: string;
  isExpanded: boolean;
}


export interface SecurityScheduleDetailFieldViewModel {
  label: string;
  value: string;
  tone?: "default" | "success" | "warning" | "danger";
}


export interface SecurityScheduleDetailViewState {
  id: string;
  eyebrow: string;
  title: string;
  subtitle: string;
  description: string;
  ariaLabel: string;
  statusLabel: string;
  statusTone: "success" | "warning" | "danger" | "outline";
  fields: SecurityScheduleDetailFieldViewModel[];
}


export interface SecurityScheduleToolbarItemViewModel {
  id: string;
  label: string;
  value?: string;
  active?: boolean;
}


export interface SecuritySchedulesViewState {
  securityId: string;
  title: string;
  description: string;
  tableLabel: string;
  tableCaption: string;
  detailPanelId: string;
  toolbarAriaLabel: string;
  toolbarItems: SecurityScheduleToolbarItemViewModel[];
  rows: SecurityScheduleRowViewModel[];
  selectedRowId: string | null;
  selectedDetail: SecurityScheduleDetailViewState | null;
  emptyText: string;
  detailEmptyTitle: string;
  detailEmptyText: string;
  detailEmptyAriaLabel: string;
  loadingText: string | null;
  errorText: string | null;
  errorDetails: string[];
  hasRows: boolean;
  statusAnnouncement: string;
  declaredExercise: SecurityMasterPolicyEvidence | null;
  cashFlowBlocker: SecurityMasterPolicyEvidence | null;
  governance: SecurityMasterPolicyEvidence;
}


export type TradingParametersField = { label: string; value: string; tone?: "default" | "warning" };


export interface TradingParametersViewState {
  securityId: string;
  asOfLabel: string;
  fields: TradingParametersField[];
  errorText: string | null;
  errorDetails: string[];
  loadingText: string | null;
  statusAnnouncement: string;
}


export interface SecurityOpenLotRowViewModel extends SecurityMasterOpenLot {
  rowId: string;
  tradeDateLabel: string;
  settleDateLabel: string;
  directionLabel: "Long" | "Short";
  quantityLabel: string;
  faceLabel: string;
  factorAdjustedLabel: string;
  costBasisLabel: string;
  entryPriceLabel: string;
  unrealizedPnlLabel: string;
  scopeLabel: string;
  statusLabel: string;
  statusTone: "success" | "warning" | "danger" | "outline";
  ariaLabel: string;
  selectAriaLabel: string;
  detailPanelId: string;
  isExpanded: boolean;
}


export interface SecurityOpenLotDetailViewState {
  id: string;
  eyebrow: string;
  title: string;
  subtitle: string;
  description: string;
  ariaLabel: string;
  statusLabel: string;
  statusTone: "success" | "warning" | "danger" | "outline";
  fields: SecurityScheduleDetailFieldViewModel[];
}


export interface SecurityOpenLotReadModelViewState {
  securityId: string;
  title: string;
  description: string;
  summary: string;
  asOfLabel: string;
  tableLabel: string;
  tableCaption: string;
  detailPanelId: string;
  toolbarAriaLabel: string;
  toolbarItems: SecurityScheduleToolbarItemViewModel[];
  rows: SecurityOpenLotRowViewModel[];
  selectedRowId: string | null;
  selectedDetail: SecurityOpenLotDetailViewState | null;
  emptyText: string;
  detailEmptyTitle: string;
  detailEmptyText: string;
  detailEmptyAriaLabel: string;
  loadingText: string | null;
  errorText: string | null;
  errorDetails: string[];
  hasRows: boolean;
  statusAnnouncement: string;
}


export interface InstrumentPassportProviderConfidenceRowViewModel extends InstrumentPassportProviderConfidence {
  rowId: string;
  providerLabel: string;
  symbolLabel: string;
  confidenceLabel: string;
  freshnessLabel: string;
  statusLabel: string;
  statusTone: "success" | "warning";
  ariaLabel: string;
}


export interface InstrumentPassportFieldViewModel {
  label: string;
  value: string;
  tone?: "default" | "success" | "warning";
}


export interface InstrumentPassportOperationsWorkbenchItemViewModel extends InstrumentPassportOperationsWorkbenchItem {
  statusLabel: string;
  statusBadgeVariant: "success" | "warning" | "outline";
  evidenceLabel: string;
  blockerLabel: string;
}


export interface InstrumentPassportOperationsWorkbenchPanelViewModel extends Omit<InstrumentPassportOperationsWorkbenchPanel, "items"> {
  statusLabel: string;
  statusBadgeVariant: "success" | "warning" | "outline";
  items: InstrumentPassportOperationsWorkbenchItemViewModel[];
}


export interface InstrumentPassportOperationsReadinessViewModel extends InstrumentPassportOperationsReadiness {
  statusLabel: string;
  statusBadgeVariant: "success" | "warning" | "outline";
  evidenceLabel: string;
  blockerLabel: string;
}


export interface InstrumentPassportViewState {
  securityId: string;
  title: string;
  description: string;
  statusLabel: string;
  statusBadgeVariant: "success" | "warning" | "outline";
  fields: InstrumentPassportFieldViewModel[];
  providerRows: InstrumentPassportProviderConfidenceRowViewModel[];
  operationsWorkbenchTitle: string;
  operationsWorkbenchSummary: string;
  operationsWorkbenchStatusLabel: string;
  operationsWorkbenchStatusBadgeVariant: "success" | "warning" | "outline";
  operationsReadiness: InstrumentPassportOperationsReadinessViewModel[];
  operationsPanels: InstrumentPassportOperationsWorkbenchPanelViewModel[];
  providerTableLabel: string;
  providerTableCaption: string;
  providerEmptyText: string;
  loadingText: string | null;
  errorText: string | null;
  errorDetails: string[];
  statusAnnouncement: string;
}

export interface ReferenceDataWorkbenchMetricViewModel {
  id: "routes" | "ready" | "review" | "deferred";
  label: string;
  value: string;
  detail: string;
  tone: "default" | "success" | "warning" | "danger";
}


export interface ReferenceDataEndpointRowViewModel extends ReferenceDataEndpointProbeResult {
  rowId: string;
  familyLabel: string;
  methodLabel: string;
  accessLabel: string;
  displaySummary: string;
  statusLabel: string;
  statusBadgeVariant: "success" | "warning" | "outline" | "danger";
  countLabel: string;
  latencyLabel: string;
  ariaLabel: string;
  selectAriaLabel: string;
  detailPanelId: string;
  isExpanded: boolean;
}


export interface ReferenceDataEndpointDetailViewState {
  id: string;
  eyebrow: string;
  title: string;
  subtitle: string;
  description: string;
  ariaLabel: string;
  statusLabel: string;
  statusBadgeVariant: "success" | "warning" | "outline" | "danger";
  fields: SecurityScheduleDetailFieldViewModel[];
  responsePreview: string | null;
  errorSummary: string | null;
  errorDetails: string[];
}


export interface ReferenceDataWorkbenchViewState {
  securityId: string | null;
  title: string;
  description: string;
  metrics: ReferenceDataWorkbenchMetricViewModel[];
  rows: ReferenceDataEndpointRowViewModel[];
  selectedRowId: string | null;
  selectedDetail: ReferenceDataEndpointDetailViewState | null;
  tableLabel: string;
  tableCaption: string;
  detailPanelId: string;
  emptyText: string;
  detailEmptyTitle: string;
  detailEmptyText: string;
  detailEmptyAriaLabel: string;
  loadingText: string | null;
  errorText: string | null;
  errorDetails: string[];
  hasRows: boolean;
  statusAnnouncement: string;
}


export function resolveSecurityScheduleEvents(securityId: string | null): SecurityCashFlowScheduleEvent[] {
  // Fixture schedules are a development-only affordance. In production a security without a
  // live trust snapshot must show an honest empty schedule, never fabricated rows with
  // posted/variance statuses. The DEV check is read at call time so tests can stub it.
  if (!securityId || !import.meta.env.DEV) {
    return [];
  }

  const events = resolveDevSecurityScheduleEvents(securityId);
  if (events.length > 0) {
    // Drives the app-shell "Demo data" notice, matching every other DEV fixture lane.
    markDevelopmentFixtureUsage();
  }
  return events;
}


export function mapScheduleBookToCashFlowScheduleEvents(
  securityId: string | null,
  snapshot: SecurityMasterTrustSnapshot | null
): SecurityCashFlowScheduleEvent[] {
  const scheduleBook = snapshot?.scheduleBook ?? null;
  if (!securityId || !scheduleBook) {
    return [];
  }

  return scheduleBook.events.map((event) => ({
    eventId: event.eventId,
    securityId,
    scheduleFamily: event.factorStart !== null || event.factorEnd !== null ? "structured" : "bond",
    eventType: event.eventType,
    paymentDate: event.payDate ?? event.effectiveDate,
    accrualStartDate: event.accrualStartDate,
    accrualEndDate: event.accrualEndDate,
    couponRatePct: null,
    expectedAmount: event.expectedAmount,
    actualAmount: event.actualAmount,
    principalAmount: null,
    interestAmount: null,
    factorStart: event.factorStart,
    factorEnd: event.factorEnd,
    currency: event.currency || scheduleBook.currency,
    postingStatus: event.postingStatus,
    auditReference: formatScheduleEventSource(event.sourceSystem, event.sourceRecordId),
    note: event.sourceReason ?? (event.isCurrentProjection ? "Current schedule projection." : null)
  }));
}


export function buildSecurityScheduleRows(
  schedules: SecurityCashFlowScheduleEvent[] | null,
  selectedRowId: string | null = null
): SecurityScheduleRowViewModel[] {
  const detailPanelId = "security-schedule-detail-panel";
  const rows = schedules ?? [];
  const effectiveSelectedRowId = selectedRowId && rows.some((event) => event.eventId === selectedRowId)
    ? selectedRowId
    : rows[0]?.eventId ?? null;

  return rows.map((event) => {
    const isSelected = event.eventId === effectiveSelectedRowId;
    const eventTypeLabel = formatSecurityScheduleEventType(event.eventType);
    const paymentDateLabel = formatSecurityDate(event.paymentDate);
    const expectedAmountLabel = formatScheduleAmount(event.expectedAmount, event.currency);
    const actualAmountLabel = formatScheduleAmount(event.actualAmount, event.currency);
    const varianceLabel = formatScheduleVariance(event.expectedAmount, event.actualAmount, event.currency);
    const factorLabel = formatScheduleFactor(event.factorStart, event.factorEnd);
    const postingStatusTone = securitySchedulePostingTone(event.postingStatus);

    return {
      ...event,
      rowId: event.eventId,
      eventTypeLabel,
      paymentDateLabel,
      expectedAmountLabel,
      actualAmountLabel,
      varianceLabel,
      factorLabel,
      postingStatusLabel: formatSecuritySchedulePostingStatus(event.postingStatus),
      postingStatusTone,
      ariaLabel: `${eventTypeLabel} for ${event.securityId}, payment ${paymentDateLabel}, expected ${expectedAmountLabel}, actual ${actualAmountLabel}, variance ${varianceLabel}, status ${event.postingStatus}`,
      selectAriaLabel: `Inspect schedule event ${eventTypeLabel} for ${event.securityId} on ${paymentDateLabel}`,
      detailPanelId,
      isExpanded: isSelected
    };
  });
}


export function buildSecuritySchedulesViewState({
  securityId,
  displayName,
  assetClass,
  schedules,
  selectedRowId,
  loading = false,
  error = null,
  factorHistoryCount = 0,
  provenanceCount = 0,
  sourceSummary = null,
  declaredExercise = null,
  governance = null,
  cashFlowBlocker = null
}: {
  securityId: string | null;
  displayName: string | null;
  assetClass: string | null;
  schedules: SecurityCashFlowScheduleEvent[] | null;
  selectedRowId: string | null;
  loading?: boolean;
  error?: string | ApiErrorDisplay | null;
  factorHistoryCount?: number;
  provenanceCount?: number;
  sourceSummary?: string | null;
  declaredExercise?: SecurityMasterDeclaredExerciseOutcome | null;
  governance?: SecurityMasterGovernance | null;
  cashFlowBlocker?: SecurityMasterScheduleBlocker | null;
}): SecuritySchedulesViewState {
  const normalizedError = normalizeApiErrorDisplay(error);
  const errorText = normalizedError?.summary ?? null;
  const displaySecurityId = securityId ?? "selected security";
  const displayNameLabel = displayName?.trim() || displaySecurityId;
  const displayAssetClass = assetClass?.trim() || "Unclassified";
  const rows = buildSecurityScheduleRows(schedules, selectedRowId);
  const effectiveSelectedRowId = rows.find((row) => row.rowId === selectedRowId)?.rowId ?? rows[0]?.rowId ?? null;
  const selectedRow = rows.find((row) => row.rowId === effectiveSelectedRowId) ?? null;
  const eventCount = rows.length;
  const pendingCount = rows.filter((row) => row.postingStatus === "Pending" || row.postingStatus === "Forecast").length;
  const varianceCount = rows.filter((row) => row.postingStatus === "Variance").length;
  const factorCount = rows.filter((row) => row.factorStart !== null || row.factorEnd !== null).length;
  const exerciseEvidence = declaredExerciseEvidence(declaredExercise);

  return {
    securityId: displaySecurityId,
    title: "Cash-flow and factor schedules",
    description: sourceSummary?.trim()
      ? `${displayNameLabel} schedule events stay attached to the selected ${displayAssetClass} reference record. ${sourceSummary}`
      : `${displayNameLabel} schedule events stay attached to the selected ${displayAssetClass} reference record for payment, posting, variance, and audit review.`,
    tableLabel: `Cash-flow and factor schedules for ${displaySecurityId}`,
    tableCaption: `Cash-flow and factor schedule evidence for ${displaySecurityId}; select a row to inspect event detail.`,
    detailPanelId: "security-schedule-detail-panel",
    toolbarAriaLabel: `Cash-flow schedule status for ${displaySecurityId}`,
    toolbarItems: [
      { id: "events", label: "Events", value: String(eventCount), active: eventCount > 0 },
      { id: "pending", label: "Pending", value: String(pendingCount) },
      { id: "variance", label: "Variance", value: String(varianceCount) },
      { id: "factor", label: "Factor rows", value: String(Math.max(factorCount, factorHistoryCount)) },
      { id: "sources", label: "Sources", value: String(provenanceCount) }
    ],
    rows,
    selectedRowId: effectiveSelectedRowId,
    selectedDetail: selectedRow ? buildSecurityScheduleDetailViewState(selectedRow) : null,
    emptyText: `No cash-flow or factor schedule rows are available for ${displaySecurityId}.`,
    detailEmptyTitle: "No schedule event selected",
    detailEmptyText: "Select a schedule row to inspect payment dates, expected and actual amounts, factors, posting state, and audit evidence.",
    detailEmptyAriaLabel: "No cash-flow schedule event selected",
    loadingText: loading ? "Loading schedule book..." : null,
    errorText,
    errorDetails: normalizedError?.details ?? [],
    hasRows: eventCount > 0,
    declaredExercise: exerciseEvidence,
    cashFlowBlocker: cashFlowBlockerEvidence(cashFlowBlocker),
    governance: governanceEvidence(governance),
    statusAnnouncement: errorText
      ? `Schedule book error: ${errorText}`
      : loading
        ? `Loading schedule book for ${displaySecurityId}.`
        : cashFlowBlocker
          ? `Cash-flow calculation blocked. ${cashFlowBlocker.code}: ${cashFlowBlocker.reason}`
        : exerciseEvidence?.blocked
          ? `${exerciseEvidence.title}. ${exerciseEvidence.messages.join(" ")}`
        : eventCount > 0
          ? `${eventCount} cash-flow schedule ${eventCount === 1 ? "event" : "events"} loaded for ${displaySecurityId}.`
          : ""
  };
}


export function buildSecurityScheduleDetailViewState(row: SecurityScheduleRowViewModel): SecurityScheduleDetailViewState {
  const accrualWindow = `${formatSecurityDate(row.accrualStartDate)} → ${formatSecurityDate(row.accrualEndDate)}`;
  const varianceTone = scheduleVarianceTone(row.expectedAmount, row.actualAmount, row.postingStatus);

  return {
    id: row.detailPanelId,
    eyebrow: "Schedule event detail",
    title: row.eventTypeLabel,
    subtitle: `${row.securityId} · ${row.paymentDateLabel}`,
    description: `${row.eventTypeLabel} event expected at ${row.expectedAmountLabel}; posting state is ${row.postingStatusLabel}.`,
    ariaLabel: `Cash-flow schedule detail for ${row.eventTypeLabel} on ${row.securityId}`,
    statusLabel: row.postingStatusLabel,
    statusTone: row.postingStatusTone,
    fields: [
      { label: "Schedule event ID", value: row.eventId },
      { label: "Event type", value: row.eventTypeLabel },
      { label: "Payment date", value: row.paymentDateLabel },
      { label: "Accrual window", value: accrualWindow, tone: accrualWindow.includes("—") ? "warning" : "default" },
      { label: "Coupon rate", value: row.couponRatePct !== null ? `${row.couponRatePct.toFixed(3)}%` : "—" },
      { label: "Interest", value: formatScheduleAmount(row.interestAmount, row.currency) },
      { label: "Principal", value: formatScheduleAmount(row.principalAmount, row.currency) },
      { label: "Expected", value: row.expectedAmountLabel },
      { label: "Actual", value: row.actualAmountLabel, tone: row.actualAmount === null ? "warning" : "default" },
      { label: "Variance", value: row.varianceLabel, tone: varianceTone },
      { label: "Factor", value: row.factorLabel },
      { label: "Audit reference", value: row.auditReference ?? "—", tone: row.auditReference ? "default" : "warning" },
      { label: "Note", value: row.note ?? "—" }
    ]
  };
}


export function buildCorporateActionRows(
  actions: CorporateAction[] | null,
  selectedRowId: string | null = null
): CorporateActionRowViewModel[] {
  const detailPanelId = "corporate-action-detail-panel";
  const rows = actions ?? [];
  const effectiveSelectedRowId = selectedRowId && rows.some((action) => action.corpActId === selectedRowId)
    ? selectedRowId
    : rows[0]?.corpActId ?? null;

  return rows.map((action) => {
    const amountLabel = formatCorpActAmount(action);
    const eventTypeLabel = formatCorpActEventType(action.eventType);
    const exDateLabel = formatSecurityDate(action.exDate);
    const isSelected = action.corpActId === effectiveSelectedRowId;

    return {
      ...action,
      rowId: action.corpActId,
      eventTypeLabel,
      exDateLabel,
      payDateLabel: action.payDate ? formatSecurityDate(action.payDate) : "—",
      amountLabel,
      ariaLabel: `${eventTypeLabel} for ${action.securityId}, ex-date ${exDateLabel}, ${amountLabel}`,
      selectAriaLabel: `Inspect corporate action ${eventTypeLabel} for ${action.securityId}`,
      detailPanelId,
      isExpanded: isSelected
    };
  });
}


export function buildCorporateActionsViewState(
  securityId: string | null,
  actions: CorporateAction[] | null,
  selectedRowId: string | null,
  loading: boolean,
  error: string | ApiErrorDisplay | null
): CorporateActionsViewState {
  const normalizedError = normalizeApiErrorDisplay(error);
  const errorText = normalizedError?.summary ?? null;
  const rows = buildCorporateActionRows(actions, selectedRowId);
  const effectiveSelectedRowId = rows.find((row) => row.rowId === selectedRowId)?.rowId ?? rows[0]?.rowId ?? null;
  const selectedRow = rows.find((row) => row.rowId === effectiveSelectedRowId) ?? null;
  const displaySecurityId = securityId ?? "selected security";
  const detailPanelId = "corporate-action-detail-panel";

  return {
    securityId: displaySecurityId,
    tableLabel: `Corporate actions for ${displaySecurityId}`,
    tableCaption: `Corporate actions evidence for ${displaySecurityId}; select a row to inspect event detail.`,
    detailPanelId,
    rows,
    selectedRowId: effectiveSelectedRowId,
    selectedDetail: selectedRow ? buildCorporateActionDetailViewState(selectedRow, detailPanelId) : null,
    emptyText: `No corporate actions recorded for ${displaySecurityId}.`,
    detailEmptyTitle: "No corporate action selected",
    detailEmptyText: "Select a corporate action row to inspect dates, ratios, securities, and cash terms.",
    detailEmptyAriaLabel: "No corporate action selected",
    loadingText: loading ? "Loading corporate actions..." : null,
    errorText,
    errorDetails: normalizedError?.details ?? [],
    hasRows: rows.length > 0,
    statusAnnouncement: errorText
      ? `Corporate actions error: ${errorText}`
      : loading
        ? `Loading corporate actions for ${displaySecurityId}.`
        : rows.length > 0
          ? `${rows.length} corporate action${rows.length === 1 ? "" : "s"} loaded for ${displaySecurityId}.`
          : ""
  };
}


export function buildCorporateActionDetailViewState(
  row: CorporateActionRowViewModel,
  detailPanelId: string
): CorporateActionDetailViewState {
  return {
    id: detailPanelId,
    eyebrow: "Corporate action detail",
    title: row.eventTypeLabel,
    subtitle: `${row.securityId} · ${row.corpActId}`,
    description: `${row.eventTypeLabel} event with ex-date ${row.exDateLabel} and recorded amount ${row.amountLabel}.`,
    ariaLabel: `Corporate action detail for ${row.eventTypeLabel} on ${row.securityId}`,
    statusLabel: row.lifecycleState ?? (row.payDate ? "Pay date scheduled" : "Pay date unavailable"),
    fields: [
      { label: "Corporate action ID", value: row.corpActId },
      { label: "Event type", value: row.eventTypeLabel },
      { label: "Ex-date", value: row.exDateLabel },
      { label: "Record date", value: row.recordDate ? formatSecurityDate(row.recordDate) : "—", tone: row.recordDate ? "default" : "warning" },
      { label: "Pay date", value: row.payDateLabel, tone: row.payDate ? "default" : "warning" },
      { label: "Lifecycle", value: row.lifecycleState ?? "—", tone: row.lifecycleState ? "default" : "warning" },
      { label: "Supersedes", value: row.supersedesCorpActId ?? "—" },
      { label: "Amount or ratio", value: row.amountLabel, tone: row.amountLabel === "—" ? "warning" : "default" },
      { label: "Currency", value: row.currency ?? "—", tone: row.currency ? "default" : "warning" },
      { label: "Redemption price (% par)", value: row.redemptionPricePercentOfPar?.toString() ?? "—" },
      { label: "New security", value: row.newSecurityId ?? "—" },
      { label: "Acquirer security", value: row.acquirerSecurityId ?? "—" },
      { label: "Payload schema", value: row.payloadSchemaVersion?.toString() ?? "—", tone: row.payloadSchemaVersion === undefined || row.payloadSchemaVersion === null ? "warning" : "default" },
      { label: "Typed payload", value: formatCorporateActionPayload(row.payload), tone: row.payload ? "default" : "warning" }
    ]
  };
}


export function buildSecurityOpenLotRows(
  readModel: SecurityMasterOpenLotReadModel | null,
  selectedRowId: string | null = null
): SecurityOpenLotRowViewModel[] {
  const detailPanelId = "security-open-lot-detail-panel";
  const rows = readModel?.lots ?? [];
  const effectiveSelectedRowId = selectedRowId && rows.some((lot) => lot.lotId === selectedRowId)
    ? selectedRowId
    : rows[0]?.lotId ?? null;

  return rows.map((lot) => {
    const isSelected = lot.lotId === effectiveSelectedRowId;
    const scopeLabel = formatOpenLotScope(lot);
    const directionLabel = lot.isShort ? "Short" : "Long";
    const quantityLabel = formatQuantity(lot.currentQuantity);
    const faceLabel = lot.currentFace !== null ? formatQuantity(lot.currentFace) : "—";
    const factorAdjustedLabel = lot.factorAdjustedQuantity !== null
      ? formatQuantity(lot.factorAdjustedQuantity)
      : lot.factorAdjustedFace !== null
        ? formatQuantity(lot.factorAdjustedFace)
        : "—";
    const costBasisLabel = formatScheduleAmount(lot.costBasis, lot.currency);
    const unrealizedPnlLabel = lot.unrealizedPnl !== null ? formatSignedScheduleAmount(lot.unrealizedPnl, lot.currency) : "—";
    const statusTone = openLotStatusTone(lot.lotStatus);

    return {
      ...lot,
      rowId: lot.lotId,
      tradeDateLabel: formatSecurityDate(lot.tradeDate),
      settleDateLabel: formatSecurityDate(lot.settleDate),
      directionLabel,
      quantityLabel,
      faceLabel,
      factorAdjustedLabel,
      costBasisLabel,
      entryPriceLabel: formatScheduleAmount(lot.entryPrice, lot.currency),
      unrealizedPnlLabel,
      scopeLabel,
      statusLabel: lot.lotStatus || "Unknown",
      statusTone,
      ariaLabel: `${directionLabel} open lot ${lot.lotId} for ${lot.symbol}, ${scopeLabel}, quantity ${quantityLabel}, cost ${costBasisLabel}, status ${lot.lotStatus}`,
      selectAriaLabel: `Inspect open lot ${lot.lotId} for ${lot.symbol}`,
      detailPanelId,
      isExpanded: isSelected
    };
  });
}


export function buildSecurityOpenLotReadModelViewState({
  securityId,
  readModel,
  selectedRowId,
  loading = false,
  error = null
}: {
  securityId: string | null;
  readModel: SecurityMasterOpenLotReadModel | null;
  selectedRowId: string | null;
  loading?: boolean;
  error?: string | ApiErrorDisplay | null;
}): SecurityOpenLotReadModelViewState {
  const normalizedError = normalizeApiErrorDisplay(error);
  const errorText = normalizedError?.summary ?? null;
  const displaySecurityId = securityId ?? "selected security";
  const rows = buildSecurityOpenLotRows(readModel, selectedRowId);
  const effectiveSelectedRowId = rows.find((row) => row.rowId === selectedRowId)?.rowId ?? rows[0]?.rowId ?? null;
  const selectedRow = rows.find((row) => row.rowId === effectiveSelectedRowId) ?? null;
  const lotCount = rows.length;
  const longTermCount = rows.filter((row) => row.isLongTerm).length;
  const factorAdjustedCount = rows.filter((row) => row.factorAdjustedQuantity !== null || row.factorAdjustedFace !== null).length;
  const provenanceCount = readModel?.provenanceHistory.length ?? 0;

  return {
    securityId: displaySecurityId,
    title: "Open lot read model",
    description: `Open lots for ${displaySecurityId} stay tied to run, account, cost, quantity, and factor-adjusted exposure evidence.`,
    summary: readModel?.summary ?? "Open lot read model is unavailable for the selected security.",
    asOfLabel: readModel?.asOfUtc ? formatDateTimeLabel(readModel.asOfUtc) : "—",
    tableLabel: `Open lot read model for ${displaySecurityId}`,
    tableCaption: `Open lots for ${displaySecurityId}; select a row to inspect source, account, and exposure detail.`,
    detailPanelId: "security-open-lot-detail-panel",
    toolbarAriaLabel: `Open lot posture for ${displaySecurityId}`,
    toolbarItems: [
      { id: "lots", label: "Lots", value: String(lotCount), active: lotCount > 0 },
      { id: "long-term", label: "Long term", value: String(longTermCount) },
      { id: "factor", label: "Factor adj.", value: String(factorAdjustedCount) },
      { id: "sources", label: "Sources", value: String(provenanceCount) }
    ],
    rows,
    selectedRowId: effectiveSelectedRowId,
    selectedDetail: selectedRow ? buildSecurityOpenLotDetailViewState(selectedRow, readModel) : null,
    emptyText: `No open lots are available for ${displaySecurityId}.`,
    detailEmptyTitle: "No open lot selected",
    detailEmptyText: "Select an open lot to inspect quantity model, cost, account scope, source record, and provenance.",
    detailEmptyAriaLabel: "No open lot selected",
    loadingText: loading ? "Loading open lot read model..." : null,
    errorText,
    errorDetails: normalizedError?.details ?? [],
    hasRows: lotCount > 0,
    statusAnnouncement: errorText
      ? `Open lot read model error: ${errorText}`
      : loading
        ? `Loading open lot read model for ${displaySecurityId}.`
        : lotCount > 0
          ? `${lotCount} open ${lotCount === 1 ? "lot" : "lots"} loaded for ${displaySecurityId}.`
          : ""
  };
}


export function buildSecurityOpenLotDetailViewState(
  row: SecurityOpenLotRowViewModel,
  readModel: SecurityMasterOpenLotReadModel | null
): SecurityOpenLotDetailViewState {
  const quantityModel = readModel?.quantityModel || "Unspecified quantity model";
  const factor = readModel?.currentFactor !== null && readModel?.currentFactor !== undefined
    ? readModel.currentFactor.toFixed(6)
    : "—";

  return {
    id: row.detailPanelId,
    eyebrow: "Open lot detail",
    title: row.lotId,
    subtitle: `${row.symbol} · ${row.scopeLabel}`,
    description: `${row.directionLabel} ${row.lotStatus.toLowerCase()} lot using ${quantityModel}; current quantity ${row.quantityLabel}.`,
    ariaLabel: `Open lot detail for ${row.lotId} on ${row.symbol}`,
    statusLabel: row.statusLabel,
    statusTone: row.statusTone,
    fields: [
      { label: "Run", value: row.runId },
      { label: "Portfolio", value: row.portfolioId },
      { label: "Account", value: row.accountScopeDisplayName ?? row.accountScopeId ?? "—", tone: row.accountScopeId ? "default" : "warning" },
      { label: "Vehicle", value: row.vehicleScopeDisplayName ?? row.vehicleScopeId ?? "—" },
      { label: "Trade date", value: row.tradeDateLabel },
      { label: "Settle date", value: row.settleDateLabel, tone: row.settleDate ? "default" : "warning" },
      { label: "Direction", value: row.directionLabel },
      { label: "Original quantity", value: formatQuantity(row.originalQuantity) },
      { label: "Current quantity", value: row.quantityLabel },
      { label: "Current face", value: row.faceLabel },
      { label: "Factor-adjusted exposure", value: row.factorAdjustedLabel, tone: row.factorAdjustedLabel === "—" ? "warning" : "success" },
      { label: "Current factor", value: factor },
      { label: "Cost basis", value: row.costBasisLabel },
      { label: "Entry price", value: row.entryPriceLabel },
      { label: "Unrealized P&L", value: row.unrealizedPnlLabel, tone: row.unrealizedPnl === null ? "warning" : row.unrealizedPnl >= 0 ? "success" : "danger" },
      { label: "Source", value: formatScheduleEventSource(row.sourceSystem, row.sourceRecordId) },
      { label: "Source reason", value: row.sourceReason ?? "—" },
      { label: "Notes", value: row.notes ?? "—" }
    ]
  };
}


export function buildReferenceDataWorkbenchViewState({
  securityId,
  coverage,
  loading = false,
  error = null,
  selectedRowId = null
}: {
  securityId: string | null;
  coverage: ReferenceDataWorkbenchCoverage | null;
  loading?: boolean;
  error?: string | ApiErrorDisplay | null;
  selectedRowId?: string | null;
}): ReferenceDataWorkbenchViewState {
  const normalizedError = normalizeApiErrorDisplay(error);
  const errorText = normalizedError?.summary ?? null;
  const rows = buildReferenceDataEndpointRows(coverage, selectedRowId);
  const effectiveSelectedRowId = rows.some((row) => row.rowId === selectedRowId)
    ? selectedRowId
    : rows[0]?.rowId ?? null;
  const selectedRow = rows.find((row) => row.rowId === effectiveSelectedRowId) ?? null;
  const routeCounts = summarizeReferenceDataRoutes(rows);
  const displaySecurityId = securityId ?? "selected security";

  return {
    securityId,
    title: "Multi-asset reference data",
    description: securityId
      ? `Reference data coverage for ${displaySecurityId} across bonds, options, equities, futures, FX spot, swaps, commodities, crypto, deposits, money-market funds, CDs, and EDGAR.`
      : "Select a security to inspect multi-asset reference data coverage.",
    metrics: [
      {
        id: "routes",
        label: "Mapped routes",
        value: routeCounts.totalCount > 0 ? formatCountValue(routeCounts.totalCount) : "Pending",
        detail: routeCounts.totalCount > 0
          ? `${formatCountValue(routeCounts.readyCount)} ready · ${formatCountValue(routeCounts.reviewCount)} need review · ${formatCountValue(routeCounts.deferredOrBlockedCount)} deferred or blocked · ${formatCountValue(routeCounts.totalCount)} total.`
          : "Select a security to build reference data checks.",
        tone: routeCounts.totalCount > 0 ? "default" : "warning"
      },
      {
        id: "ready",
        label: "Ready data",
        value: formatCountValue(routeCounts.readyCount),
        detail: routeCounts.readyCount > 0 ? `${formatCount(routeCounts.readyCount, "reference route")} returned data.` : "No reference route has returned data yet.",
        tone: routeCounts.readyCount > 0 ? "success" : "default"
      },
      {
        id: "review",
        label: "Needs review",
        value: formatCountValue(routeCounts.reviewCount),
        detail: routeCounts.reviewCount > 0 ? `${formatCount(routeCounts.reviewCount, "data source")} returned empty, missing, or error status.` : "No checked data source is flagged for review.",
        tone: routeCounts.reviewCount > 0 ? "warning" : "success"
      },
      {
        id: "deferred",
        label: "Deferred / blocked",
        value: formatCountValue(routeCounts.deferredOrBlockedCount),
        detail: routeCounts.deferredOrBlockedCount > 0
          ? [
              routeCounts.deferredCount > 0 ? `${formatCount(routeCounts.deferredCount, "write-capable source")} intentionally deferred.` : null,
              routeCounts.blockedCount > 0 ? `${formatCount(routeCounts.blockedCount, "data source")} blocked.` : null
            ].filter((detail): detail is string => detail !== null).join(" ")
          : "No reference route is deferred or blocked.",
        tone: routeCounts.deferredOrBlockedCount > 0 ? "warning" : "default"
      }
    ],
    rows,
    selectedRowId: effectiveSelectedRowId,
    selectedDetail: selectedRow ? buildReferenceDataEndpointDetailViewState(selectedRow, coverage) : null,
    tableLabel: `Reference data source coverage for ${displaySecurityId}`,
    tableCaption: `Read-only status for mapped reference data sources backing ${displaySecurityId}.`,
    detailPanelId: "reference-data-endpoint-detail",
    emptyText: securityId ? "No reference data source rows are available for this security." : "Select a security to load reference data source coverage.",
    detailEmptyTitle: "No data source selected",
    detailEmptyText: "Select a data source row to inspect coverage detail, response summary, and error evidence.",
    detailEmptyAriaLabel: `Reference data source detail for ${displaySecurityId}`,
    loadingText: loading ? "Loading multi-asset reference data coverage..." : null,
    errorText,
    errorDetails: normalizedError?.details ?? [],
    hasRows: rows.length > 0,
    statusAnnouncement: errorText
        ? `Reference data workbench error: ${errorText}`
      : loading
        ? `Loading multi-asset reference data coverage for ${displaySecurityId}.`
        : rows.length > 0
          ? `${formatCount(rows.length, "reference data source")} loaded for ${displaySecurityId}; ${formatCountValue(routeCounts.readyCount)} ready, ${formatCountValue(routeCounts.reviewCount)} need review, and ${formatCountValue(routeCounts.deferredOrBlockedCount)} deferred or blocked.`
          : ""
  };
}


export function buildReferenceDataEndpointRows(
  coverage: ReferenceDataWorkbenchCoverage | null,
  selectedRowId: string | null
): ReferenceDataEndpointRowViewModel[] {
  return (coverage?.endpoints ?? []).map((endpoint) => {
    const rowId = `reference-data-${endpoint.id}`;
    const statusBadgeVariant = referenceDataStatusBadgeVariant(endpoint.status);
    const countLabel = endpoint.responseCount === null ? "-" : formatCount(endpoint.responseCount, "record");
    const latencyLabel = endpoint.durationMs === null ? "-" : `${endpoint.durationMs} ms`;
    const statusLabel = referenceDataStatusLabel(endpoint.status);
    const accessLabel = endpoint.mutation ? "Write-capable" : "Read-only";
    const displaySummary = formatFinanceFacingSourceSummary(endpoint.responseSummary, endpoint.errorSummary);

    return {
      ...endpoint,
      rowId,
      familyLabel: endpoint.family,
      methodLabel: endpoint.method,
      accessLabel,
      displaySummary,
      statusLabel,
      statusBadgeVariant,
      countLabel,
      latencyLabel,
      ariaLabel: `${endpoint.family} ${endpoint.label}, ${statusLabel}. ${displaySummary}`,
      selectAriaLabel: `Inspect ${endpoint.label} reference data source`,
      detailPanelId: "reference-data-endpoint-detail",
      isExpanded: rowId === selectedRowId
    };
  });
}


export function buildReferenceDataEndpointDetailViewState(
  row: ReferenceDataEndpointRowViewModel,
  coverage: ReferenceDataWorkbenchCoverage | null
): ReferenceDataEndpointDetailViewState {
  return {
    id: row.detailPanelId,
    eyebrow: row.familyLabel,
    title: row.label,
    subtitle: `Reference data source: ${row.familyLabel}`,
    description: row.errorSummary ?? row.displaySummary,
    ariaLabel: `${row.label} reference data source detail`,
    statusLabel: row.statusLabel,
    statusBadgeVariant: row.statusBadgeVariant,
    fields: [
      { label: "Family", value: row.familyLabel },
      { label: "Access", value: row.accessLabel, tone: row.mutation ? "warning" : "default" },
      { label: "Source family", value: row.familyLabel },
      { label: "Review scope", value: formatFinanceFacingSourceSummary(row.requestLabel) },
      { label: "Status", value: row.statusLabel, tone: referenceDataStatusTone(row.status) },
      { label: "Records", value: row.countLabel },
      { label: "Latency", value: row.latencyLabel },
      { label: "Catalogued", value: coverage?.requestedAtUtc ? formatDateTimeLabel(coverage.requestedAtUtc) : "-" }
    ],
    responsePreview: row.responsePreview,
    errorSummary: row.errorSummary,
    errorDetails: row.errorDetails
  };
}


export function buildInstrumentPassportViewState({
  securityId,
  passport,
  loading = false,
  error = null
}: {
  securityId: string | null;
  passport: InstrumentPassport | null;
  loading?: boolean;
  error?: string | ApiErrorDisplay | null;
}): InstrumentPassportViewState {
  const normalizedError = normalizeApiErrorDisplay(error);
  const errorText = normalizedError?.summary ?? null;
  const displaySecurityId = securityId ?? passport?.securityId ?? "selected security";
  const providerRows = buildInstrumentPassportProviderRows(passport);
  const trustTone = passport?.trustPosture?.tone?.trim() || "Unknown";
  const trustSummary = passport?.trustPosture?.summary?.trim() || "Trust posture is unavailable.";
  const identifierSummary = passport?.identifierSummary?.summary?.trim() || "Identifier summary is unavailable.";
  const usageSummary = passport?.usage?.summary?.trim() || "Downstream usage is unavailable.";
  const pricingStatus = passport?.pricing?.status?.trim() || "Unknown";
  const pricingSummary = passport?.pricing?.summary?.trim() || "Pricing and trading controls are unavailable.";
  const operatingModel = passport?.operatingModel ?? null;
  const operatingModelStatus = operatingModel?.status?.trim() || "Unavailable";
  const operatingModelSummary = operatingModel?.summary?.trim() || "Security Master operating model is unavailable.";
  const operatingModelStageFields = (operatingModel?.stages ?? []).map((stage) => ({
    label: stage.title,
    value: `${stage.status}: ${stage.summary} Evidence ${stage.evidenceCount}; blockers ${stage.blockingIssueCount}.`,
    tone: stage.status.toLowerCase() === "ready" ? "success" as const : "warning" as const
  }));
  const mostSpecificEntitlements = operatingModel?.entitlementApplicability?.filter((row) => row.isApplicable && row.isMostSpecific) ?? [];
  const approvalPosture = operatingModel?.manualChangeApproval ?? null;
  const referenceDataWorkbench = passport?.referenceDataWorkbench ?? null;
  const referenceDataWorkbenchStatus = referenceDataWorkbench?.status?.trim() || "Unavailable";
  const referenceDataWorkbenchSummary = referenceDataWorkbench?.summary?.trim() || "Reference-data workbench is unavailable.";
  const referenceDataWorkbenchSections = (referenceDataWorkbench?.sections ?? []).map((section) => ({
    label: section.title,
    value: `${section.status}: ${section.summary} Evidence ${section.evidenceCount}; blockers ${section.blockingIssueCount}.`,
    tone: section.status.toLowerCase() === "ready" ? "success" as const : "warning" as const
  }));
  const classificationProfile = passport?.classificationProfile ?? null;
  const classificationFields: InstrumentPassportFieldViewModel[] = classificationProfile
    ? [
        {
          label: "Instrument type",
          value: `${classificationProfile.displayName} (${classificationProfile.instrumentType})`,
          tone: classificationProfile.isReferenceOnly ? "default" : "success"
        },
        {
          label: "Provider routing",
          value: `${classificationProfile.defaultProviderSecurityType}: ${formatInstrumentPassportProfileList(
            classificationProfile.providerCapabilities,
            "No provider capabilities mapped."
          )}`
        },
        {
          label: "Lifecycle profile",
          value: formatInstrumentPassportProfileList(
            classificationProfile.lifecycleEvents,
            "No lifecycle events mapped."
          )
        },
        {
          label: "Ledger behavior",
          value: formatInstrumentPassportProfileList(
            classificationProfile.ledgerBehaviorHints,
            "No ledger behavior hints mapped."
          )
        }
      ]
    : [];
  const enabledHandoffs = referenceDataWorkbench?.operationsHandoffs?.filter((handoff) => handoff.isEnabled).length ?? 0;
  const totalHandoffs = referenceDataWorkbench?.operationsHandoffs?.length ?? 0;
  const operationsWorkbench = passport?.operationsWorkbench ?? null;
  const operationsWorkbenchStatus = operationsWorkbench?.status?.trim() || "Unavailable";
  const operationsReadiness = buildInstrumentPassportOperationsReadinessRows(passport);
  const operationsPanels = buildInstrumentPassportOperationsPanelRows(passport);
  const activeProviderCount = providerRows.filter((row) => row.isActive).length;
  const statusBadgeVariant = trustTone.toLowerCase() === "trusted"
    ? "success"
    : trustTone.toLowerCase() === "blocked" || trustTone.toLowerCase() === "review"
      ? "warning"
      : "outline";

  return {
    securityId: displaySecurityId,
    title: "Instrument passport",
    description: passport
      ? `${passport.identity?.displayName ?? "Selected security"} passport combines identifiers, provider confidence, lifecycle, pricing, and downstream usage evidence.`
      : `Instrument passport evidence for ${displaySecurityId}.`,
    statusLabel: trustTone,
    statusBadgeVariant,
    fields: [
      { label: "Security ID", value: passport?.securityId ?? displaySecurityId },
      { label: "Display name", value: passport?.identity?.displayName ?? "-" },
      { label: "Asset class", value: passport?.identity?.assetClass ?? "-" },
      ...classificationFields,
      { label: "Trust", value: trustSummary, tone: statusBadgeVariant === "success" ? "success" : statusBadgeVariant === "warning" ? "warning" : "default" },
      { label: "Identifiers", value: identifierSummary },
      { label: "Provider confidence", value: `${activeProviderCount} active / ${providerRows.length} total` },
      { label: "Pricing", value: `${pricingStatus}: ${pricingSummary}` },
      { label: "Usage", value: usageSummary },
      {
        label: "Operating model",
        value: `${operatingModelStatus}: ${operatingModelSummary}`,
        tone: operatingModelStatus.toLowerCase() === "ready" ? "success" : "warning"
      },
      ...operatingModelStageFields,
      {
        label: "Entitlement applicability",
        value: `${mostSpecificEntitlements.length} most-specific applicable entitlement(s).`,
        tone: mostSpecificEntitlements.length > 0 ? "success" : "warning"
      },
      {
        label: "Manual-change approval",
        value: approvalPosture
          ? `${approvalPosture.status}: ${approvalPosture.policyKey} via ${approvalPosture.gate}; ${approvalPosture.summary}`
          : "Unavailable: operations approval policy posture is unavailable.",
        tone: approvalPosture?.status?.toLowerCase() === "ready" ? "success" : "warning"
      },
      {
        label: "Reference-data workbench",
        value: `${referenceDataWorkbenchStatus}: ${referenceDataWorkbenchSummary}`,
        tone: referenceDataWorkbenchStatus.toLowerCase() === "ready" ? "success" : "warning"
      },
      ...referenceDataWorkbenchSections,
      {
        label: "Operations handoff",
        value: `${enabledHandoffs} enabled / ${totalHandoffs} total handoff(s).`,
        tone: enabledHandoffs > 0 ? "success" : "warning"
      },
      {
        label: "Operations workbench",
        value: `${operationsWorkbenchStatus}: ${operationsWorkbench?.summary?.trim() || "Security Master operations workbench is unavailable."}`,
        tone: operationsWorkbenchStatus.toLowerCase() === "ready" ? "success" : "warning"
      },
      { label: "Retrieved", value: passport?.retrievedAtUtc ? formatDateTimeLabel(passport.retrievedAtUtc) : "-" }
    ],
    providerRows,
    operationsWorkbenchTitle: "Operations workbench",
    operationsWorkbenchSummary: operationsWorkbench?.summary?.trim() || "Operations workbench evidence is unavailable for this passport.",
    operationsWorkbenchStatusLabel: operationsWorkbenchStatus,
    operationsWorkbenchStatusBadgeVariant: instrumentPassportStatusVariant(operationsWorkbenchStatus),
    operationsReadiness,
    operationsPanels,
    providerTableLabel: `Provider confidence for ${displaySecurityId}`,
    providerTableCaption: `Provider symbol confidence and conflict evidence for ${displaySecurityId}.`,
    providerEmptyText: `No provider confidence rows are available for ${displaySecurityId}.`,
    loadingText: loading ? "Loading instrument passport..." : null,
    errorText,
    errorDetails: normalizedError?.details ?? [],
    statusAnnouncement: errorText
      ? `Instrument passport error: ${errorText}`
      : loading
        ? `Loading instrument passport for ${displaySecurityId}.`
        : passport
          ? `Instrument passport loaded for ${displaySecurityId}.`
          : ""
  };
}


export function formatInstrumentPassportProfileList(values: string[] | null | undefined, fallback: string): string {
  const normalized = Array.from(new Set(
    (values ?? [])
      .map((value) => value.trim())
      .filter(Boolean)
  ));

  if (normalized.length === 0) {
    return fallback;
  }

  const visible = normalized.slice(0, 4);
  return normalized.length > visible.length
    ? `${visible.join(", ")}, +${normalized.length - visible.length} more`
    : visible.join(", ");
}


export function buildInstrumentPassportOperationsReadinessRows(
  passport: InstrumentPassport | null
): InstrumentPassportOperationsReadinessViewModel[] {
  return (passport?.operationsWorkbench?.readiness ?? []).map((row) => ({
    ...row,
    route: normalizeInstrumentPassportRoute(row.route),
    statusLabel: row.status,
    statusBadgeVariant: instrumentPassportStatusVariant(row.status),
    evidenceLabel: `${row.evidenceCount} evidence`,
    blockerLabel: `${row.blockingIssueCount} blocker${row.blockingIssueCount === 1 ? "" : "s"}`
  }));
}


export function buildInstrumentPassportOperationsPanelRows(
  passport: InstrumentPassport | null
): InstrumentPassportOperationsWorkbenchPanelViewModel[] {
  return (passport?.operationsWorkbench?.panels ?? []).map((panel) => ({
    ...panel,
    statusLabel: panel.status,
    statusBadgeVariant: instrumentPassportStatusVariant(panel.status),
    items: panel.items.map((item) => ({
      ...item,
      route: normalizeInstrumentPassportRoute(item.route),
      statusLabel: item.status,
      statusBadgeVariant: instrumentPassportStatusVariant(item.status),
      evidenceLabel: `${item.evidenceCount} evidence`,
      blockerLabel: `${item.blockingIssueCount} blocker${item.blockingIssueCount === 1 ? "" : "s"}`
    }))
  }));
}


export function normalizeInstrumentPassportRoute(route: string | null | undefined): string | null {
  const trimmed = route?.trim();
  if (!trimmed) {
    return null;
  }

  if (trimmed.startsWith("/workstation/")) {
    return trimmed.slice("/workstation".length);
  }

  return trimmed.startsWith("/") ? trimmed : null;
}


export function instrumentPassportStatusVariant(status: string | null | undefined): "success" | "warning" | "outline" {
  const normalized = status?.trim().toLowerCase();
  if (normalized === "ready" || normalized === "trusted" || normalized === "complete") {
    return "success";
  }

  if (normalized === "review" || normalized === "blocked" || normalized === "unavailable") {
    return "warning";
  }

  return "outline";
}


export function buildInstrumentPassportProviderRows(
  passport: InstrumentPassport | null
): InstrumentPassportProviderConfidenceRowViewModel[] {
  return (passport?.providerConfidence ?? []).map((row, index) => {
    const confidenceLabel = formatInstrumentPassportConfidenceLabel(row.confidenceScore);
    const freshnessLabel = row.freshnessMinutes !== null
      ? `${row.freshnessMinutes} min`
      : row.freshnessAsOf
        ? formatDateTimeLabel(row.freshnessAsOf)
        : "-";
    const statusLabel = row.isPrimary ? "Primary" : row.isActive ? "Active" : "Inactive";

    return {
      ...row,
      rowId: `${row.provider}-${row.normalizedSymbol || row.symbol}-${index}`,
      providerLabel: `${row.provider} / ${row.providerSource}`,
      symbolLabel: `${row.mappingKind}: ${row.symbol}`,
      confidenceLabel,
      freshnessLabel,
      statusLabel,
      statusTone: row.isActive ? "success" : "warning",
      ariaLabel: `${row.provider} ${row.symbol}, confidence ${confidenceLabel}, ${statusLabel}. ${row.confidenceReason}`
    };
  });
}

export function formatInstrumentPassportConfidenceLabel(score: number): string {
  if (!Number.isFinite(score)) {
    return "-";
  }

  const normalizedScore = Math.abs(score) <= 1 ? score * 100 : score;
  const boundedScore = Math.max(0, Math.min(100, normalizedScore));
  return `${Math.round(boundedScore)}%`;
}

export function buildTradingParametersViewState(
  params: TradingParameters | null,
  loading: boolean,
  error: string | ApiErrorDisplay | null
): TradingParametersViewState {
  const normalizedError = normalizeApiErrorDisplay(error);
  const errorText = normalizedError?.summary ?? null;
  const fields: TradingParametersField[] = params
    ? [
        { label: "Lot size", value: params.lotSize !== null ? String(params.lotSize) : "—" },
        { label: "Tick size", value: params.tickSize !== null ? String(params.tickSize) : "—" },
        { label: "Contract multiplier", value: params.contractMultiplier !== null ? String(params.contractMultiplier) : "—" },
        {
          label: "Margin requirement",
          value: params.marginRequirementPct !== null ? `${params.marginRequirementPct}%` : "—",
          tone: params.marginRequirementPct !== null && params.marginRequirementPct > 50 ? "warning" : "default"
        },
        { label: "Trading hours (UTC)", value: params.tradingHoursUtc ?? "—" },
        {
          label: "Circuit breaker",
          value: params.circuitBreakerThresholdPct !== null ? `${params.circuitBreakerThresholdPct}%` : "—",
          tone: params.circuitBreakerThresholdPct !== null ? "warning" : "default"
        }
      ]
    : [];

  return {
    securityId: params?.securityId ?? "",
    asOfLabel: params?.asOf ? formatSecurityDate(params.asOf) : "—",
    fields,
    errorText,
    errorDetails: normalizedError?.details ?? [],
    loadingText: loading ? "Loading trading parameters..." : null,
    statusAnnouncement: errorText
      ? `Trading parameters error: ${errorText}`
      : loading
        ? "Loading trading parameters."
        : params
          ? `Trading parameters loaded for ${params.securityId}.`
          : ""
  };
}


export function securitySchedulePostingTone(
  status: SecuritySchedulePostingStatus
): SecurityScheduleRowViewModel["postingStatusTone"] {
  if (status === "Posted") {
    return "success";
  }

  if (status === "Variance") {
    return "danger";
  }

  if (status === "Pending") {
    return "warning";
  }

  return "outline";
}


export function formatScheduleEventSource(sourceSystem: string, sourceRecordId: string | null | undefined): string {
  const source = sourceSystem?.trim() || "Unknown source";
  return sourceRecordId?.trim() ? `${source} · ${sourceRecordId}` : source;
}


export function formatScheduleAmount(value: number | null, currency: string): string {
  if (value === null || !Number.isFinite(value)) {
    return "—";
  }

  const prefix = value >= 0 ? "" : "-";
  const amount = formatNumber(Math.abs(value), { maximumFractionDigits: 2, minimumFractionDigits: 0 });
  return `${prefix}${amount} ${currency}`;
}


export function formatSignedScheduleAmount(value: number, currency: string): string {
  if (!Number.isFinite(value)) {
    return "—";
  }

  if (value === 0) {
    return `0 ${currency}`;
  }

  const sign = value > 0 ? "+" : "-";
  return `${sign}${formatNumber(Math.abs(value), { maximumFractionDigits: 2 })} ${currency}`;
}


export function formatQuantity(value: number | null): string {
  if (value === null || !Number.isFinite(value)) {
    return "—";
  }

  return formatNumber(value, { maximumFractionDigits: 4 });
}


export function formatOpenLotScope(lot: SecurityMasterOpenLot): string {
  return lot.accountScopeDisplayName?.trim()
    || lot.accountScopeId?.trim()
    || lot.vehicleScopeDisplayName?.trim()
    || lot.vehicleScopeId?.trim()
    || lot.portfolioId;
}


export function openLotStatusTone(status: string): SecurityOpenLotRowViewModel["statusTone"] {
  const normalized = status.trim().toLowerCase();
  if (normalized === "open" || normalized === "active") {
    return "success";
  }

  if (normalized === "closed" || normalized === "settled") {
    return "outline";
  }

  if (normalized === "blocked" || normalized === "failed") {
    return "danger";
  }

  return "warning";
}


export function formatScheduleVariance(expected: number | null, actual: number | null, currency: string): string {
  if (expected === null || actual === null || !Number.isFinite(expected) || !Number.isFinite(actual)) {
    return "—";
  }

  const variance = actual - expected;
  if (variance === 0) {
    return `0 ${currency}`;
  }

  const sign = variance > 0 ? "+" : "-";
  return `${sign}${formatNumber(Math.abs(variance), { maximumFractionDigits: 2 })} ${currency}`;
}


export function formatScheduleFactor(start: number | null, end: number | null): string {
  if (start === null && end === null) {
    return "—";
  }

  const startLabel = start === null ? "—" : start.toFixed(6);
  const endLabel = end === null ? "—" : end.toFixed(6);
  return `${startLabel} → ${endLabel}`;
}


export function scheduleVarianceTone(
  expected: number | null,
  actual: number | null,
  postingStatus: SecuritySchedulePostingStatus
): SecurityScheduleDetailFieldViewModel["tone"] {
  if (postingStatus === "Variance") {
    return "danger";
  }

  if (expected === null || actual === null) {
    return "warning";
  }

  return Math.abs(actual - expected) > 0.0001 ? "warning" : "success";
}


export function formatCorpActEventType(eventType: string): string {
  const labels: Record<string, string> = {
    Dividend: "Dividend",
    StockSplit: "Stock split",
    SpinOff: "Spin-off",
    Merger: "Merger",
    RightsIssue: "Rights issue"
  };

  return labels[eventType] ?? eventType;
}


export function formatCorpActAmount(action: CorporateAction): string {
  if (action.dividendPerShare !== null) {
    const currency = action.currency ? ` ${action.currency}` : "";
    return `${action.dividendPerShare}${currency} / share`;
  }

  if (action.splitRatio !== null) {
    return `${action.splitRatio}:1 split`;
  }

  if (action.exchangeRatio !== null) {
    return `${action.exchangeRatio}:1 exchange`;
  }

  if (action.distributionRatio !== null) {
    return `${action.distributionRatio}:1 distribution`;
  }

  if (action.rightsPerShare !== null) {
    const price = action.subscriptionPricePerShare !== null
      ? ` @ ${action.subscriptionPricePerShare}${action.currency ? ` ${action.currency}` : ""}`
      : "";
    return `${action.rightsPerShare} rights/share${price}`;
  }

  return "—";
}
