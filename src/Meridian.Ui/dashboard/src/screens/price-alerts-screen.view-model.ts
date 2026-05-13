import { useEffect, useMemo, useRef, useState } from "react";
import type { PriceAlertsApi } from "@/lib/price-alerts/service";
import { PRICE_ALERTS_POLL_INTERVAL_MS } from "@/lib/price-alerts/service";
import { describeCondition } from "@/lib/price-alerts/evaluator";
import type { PriceAlert, PriceAlertCondition, PriceAlertDraft, PriceAlertField, PriceAlertTrigger } from "@/lib/price-alerts/types";
import type { MetricSnapshot } from "@/types";

export interface PriceAlertFormState {
  symbol: string;
  condition: PriceAlertCondition;
  field: PriceAlertField;
  threshold: string;
  note: string;
}

export const DEFAULT_PRICE_ALERT_FORM: PriceAlertFormState = {
  symbol: "",
  condition: "above",
  field: "last",
  threshold: "",
  note: ""
};

export interface PriceAlertFormValidation {
  symbolError: string | null;
  thresholdError: string | null;
  canSubmit: boolean;
}

export interface PriceAlertsScreenViewModel {
  form: PriceAlertFormState;
  setSymbol: (value: string) => void;
  setCondition: (value: PriceAlertCondition) => void;
  setField: (value: PriceAlertField) => void;
  setThreshold: (value: string) => void;
  setNote: (value: string) => void;
  validation: PriceAlertFormValidation;
  submitAction: PriceAlertSubmitAction;
  submitFeedback: PriceAlertSubmitFeedback | null;
  summaryMetrics: MetricSnapshot[];
  heroDescription: string;
  pollErrorPanel: PriceAlertStatusPanel | null;
  notificationPanel: PriceAlertNotificationPanel | null;
  triggerSection: PriceAlertTriggerSectionViewModel;
  alertSection: PriceAlertListSectionViewModel;
  requestNotifications: () => Promise<void>;
  submit: () => void;
  selectTrigger: (triggerId: string) => void;
  selectAlert: (alertId: string) => void;
  acknowledgeTrigger: (triggerId: string) => void;
  acknowledgeAllTriggers: () => void;
  clearTriggers: () => void;
  wakeAlert: (alertId: string) => void;
  snoozeAlert: (alertId: string) => void;
  resetAlert: (alertId: string) => void;
  toggleAlert: (alertId: string) => void;
  deleteAlert: (alertId: string) => void;
}

export interface PriceAlertSubmitAction {
  disabled: boolean;
  disabledReason: string | null;
  ariaLabel: string;
}

export interface PriceAlertSubmitFeedback {
  text: string;
  ariaLabel: string;
}

export interface PriceAlertStatusPanel {
  id: string;
  role: "alert" | "status";
  ariaLive: "assertive" | "polite";
  className: string;
  text: string;
}

export interface PriceAlertNotificationPanel {
  id: string;
  role: "alert" | "status";
  tone: "warning" | "muted";
  text: string;
  action: PriceAlertNotificationAction | null;
}

export interface PriceAlertNotificationAction {
  label: string;
  ariaLabel: string;
}

export interface PriceAlertTriggerSectionViewModel {
  title: string;
  summary: string;
  tableLabel: string;
  caption: string;
  emptyText: string;
  hasRows: boolean;
  detailPanelId: string;
  selectedRowId: string | null;
  selectedDetail: PriceAlertRowDetailViewModel | null;
  detailEmptyState: PriceAlertDetailEmptyState;
  rows: PriceAlertTriggerRowViewModel[];
  acknowledgeAllAction: PriceAlertToolbarAction;
  clearHistoryAction: PriceAlertToolbarAction;
}

export interface PriceAlertListSectionViewModel {
  title: string;
  summary: string;
  tableLabel: string;
  caption: string;
  emptyText: string;
  hasRows: boolean;
  detailPanelId: string;
  selectedRowId: string | null;
  selectedDetail: PriceAlertRowDetailViewModel | null;
  detailEmptyState: PriceAlertDetailEmptyState;
  rows: PriceAlertRowViewModel[];
}

export interface PriceAlertToolbarAction {
  label: string;
  ariaLabel: string;
  disabled: boolean;
  disabledReason: string | null;
}

export interface PriceAlertTriggerRowViewModel {
  id: string;
  detailPanelId: string;
  expanded: boolean;
  symbol: string;
  conditionLabel: string;
  priceLabel: string;
  triggeredAtLabel: string;
  noteLabel: string;
  acknowledged: boolean;
  statusLabel: string;
  statusVariant: "outline" | "warning";
  rowAriaLabel: string;
  rowSelectAriaLabel: string;
  quoteHref: string;
  quoteAriaLabel: string;
  acknowledgeAction: PriceAlertToolbarAction | null;
}

export interface PriceAlertRowViewModel {
  id: string;
  detailPanelId: string;
  expanded: boolean;
  symbol: string;
  conditionLabel: string;
  lastObservedLabel: string;
  noteLabel: string;
  statusLabel: string;
  statusVariant: "default" | "warning" | "outline";
  rowClassName: string;
  rowAriaLabel: string;
  rowSelectAriaLabel: string;
  quoteHref: string;
  quoteAriaLabel: string;
  primaryAction: PriceAlertRowAction;
  pauseAction: PriceAlertRowAction;
  deleteAction: PriceAlertRowAction;
}

export interface PriceAlertRowAction {
  id: "wake" | "snooze" | "reset" | "pause" | "resume" | "delete";
  label: string;
  ariaLabel: string;
}

export interface PriceAlertDetailField {
  id: string;
  label: string;
  value: string;
  tone: "default" | "muted" | "warning" | "success";
}

export interface PriceAlertDetailAction {
  id: PriceAlertRowAction["id"] | "acknowledge";
  kind: "trigger" | "alert";
  label: string;
  ariaLabel: string;
  disabled: boolean;
  disabledReason: string | null;
}

export interface PriceAlertRowDetailViewModel {
  id: string;
  sourceId: string;
  ariaLabel: string;
  eyebrow: string;
  title: string;
  subtitle: string;
  description: string;
  statusLabel: string;
  statusVariant: "default" | "outline" | "warning";
  fields: PriceAlertDetailField[];
  quoteHref: string;
  quoteAriaLabel: string;
  action: PriceAlertDetailAction | null;
}

export interface PriceAlertDetailEmptyState {
  title: string;
  description: string;
  ariaLabel: string;
}

export const PRICE_ALERT_TRIGGER_DETAIL_PANEL_ID = "price-alert-trigger-detail-panel";
export const PRICE_ALERT_LIST_DETAIL_PANEL_ID = "price-alert-list-detail-panel";

export function usePriceAlertsScreenViewModel({
  alerts,
  seededSymbol,
  onSeededSymbolConsumed
}: {
  alerts: PriceAlertsApi;
  seededSymbol: string | null;
  onSeededSymbolConsumed: () => void;
}): PriceAlertsScreenViewModel {
  const [form, setForm] = useState<PriceAlertFormState>(() => readFormFromSeededSymbol(seededSymbol));
  const [submitFeedback, setSubmitFeedback] = useState<PriceAlertSubmitFeedback | null>(null);
  const [selectedTriggerId, setSelectedTriggerId] = useState<string | null>(null);
  const [selectedAlertId, setSelectedAlertId] = useState<string | null>(null);
  const consumedSeedRef = useRef<string | null>(null);

  useEffect(() => {
    if (!seededSymbol || consumedSeedRef.current === seededSymbol) {
      return;
    }

    consumedSeedRef.current = seededSymbol;
    setForm((current) => ({ ...current, symbol: seededSymbol.toUpperCase() }));
    setSubmitFeedback(null);
    onSeededSymbolConsumed();
  }, [seededSymbol, onSeededSymbolConsumed]);

  const validation = useMemo(() => validatePriceAlertForm(form), [form]);
  const submitAction = useMemo(() => buildPriceAlertSubmitAction(validation), [validation]);
  const sortedAlerts = useMemo(() => sortPriceAlerts(alerts.state.alerts), [alerts.state.alerts]);
  const summaryMetrics = useMemo(() => buildPriceAlertMetrics(alerts), [alerts.enabledCount, alerts.lastPollAt, alerts.unacknowledgedCount]);
  const pollErrorPanel = useMemo(() => buildPollErrorPanel(alerts.pollErrorMessage), [alerts.pollErrorMessage]);
  const notificationPanel = useMemo(
    () => buildNotificationPanel(alerts.notificationPermission),
    [alerts.notificationPermission]
  );
  const triggerSection = useMemo(
    () => buildPriceAlertTriggerSection(alerts.state.triggers, alerts.unacknowledgedCount, selectedTriggerId),
    [alerts.state.triggers, alerts.unacknowledgedCount, selectedTriggerId]
  );
  const alertSection = useMemo(
    () => buildPriceAlertListSection(sortedAlerts, alerts.enabledCount, selectedAlertId),
    [alerts.enabledCount, selectedAlertId, sortedAlerts]
  );

  function submit() {
    const draft = priceAlertDraftFromForm(form);
    if (!draft) {
      return;
    }

    alerts.createAlert(draft);
    setForm({ ...DEFAULT_PRICE_ALERT_FORM, condition: form.condition, field: form.field });
    setSubmitFeedback({
      text: `Alert set: ${draft.symbol} ${describeCondition(draft.condition, draft.threshold, draft.field)}`,
      ariaLabel: `Price alert created for ${draft.symbol}`
    });
  }

  async function requestNotifications() {
    await alerts.requestNotificationPermission();
  }

  return {
    form,
    setSymbol: (value) => setForm((current) => ({ ...current, symbol: value.toUpperCase() })),
    setCondition: (value) => setForm((current) => ({ ...current, condition: value })),
    setField: (value) => setForm((current) => ({ ...current, field: value })),
    setThreshold: (value) => setForm((current) => ({ ...current, threshold: value })),
    setNote: (value) => setForm((current) => ({ ...current, note: value })),
    validation,
    submitAction,
    submitFeedback,
    summaryMetrics,
    heroDescription: `Get notified when a symbol crosses a price. Alerts are evaluated against live quotes every ${Math.round(PRICE_ALERTS_POLL_INTERVAL_MS / 1000)}s while this tab is open.`,
    pollErrorPanel,
    notificationPanel,
    triggerSection,
    alertSection,
    requestNotifications,
    submit,
    selectTrigger: setSelectedTriggerId,
    selectAlert: setSelectedAlertId,
    acknowledgeTrigger: alerts.acknowledgeTrigger,
    acknowledgeAllTriggers: alerts.acknowledgeAllTriggers,
    clearTriggers: alerts.clearTriggers,
    wakeAlert: (alertId) => alerts.snoozeAlert(alertId, null),
    snoozeAlert: (alertId) => alerts.snoozeAlert(alertId, new Date(Date.now() + SNOOZE_MINUTES * 60_000)),
    resetAlert: alerts.resetAlert,
    toggleAlert: (alertId) => {
      const alert = alerts.state.alerts.find((candidate) => candidate.id === alertId);
      if (!alert) {
        return;
      }
      alerts.updateAlertFields(alertId, { enabled: !alert.enabled });
    },
    deleteAlert: alerts.deleteAlert
  };
}

export function validatePriceAlertForm(form: PriceAlertFormState): PriceAlertFormValidation {
  const symbol = form.symbol.trim().toUpperCase();
  const symbolError = !symbol
    ? "Enter a symbol."
    : !/^[A-Z0-9./:_-]{1,16}$/.test(symbol)
      ? "Use 1-16 letters, digits, or . / : _ -"
      : null;

  const thresholdValue = parseThreshold(form.threshold);
  const thresholdError = thresholdValue === null
    ? "Enter a price threshold greater than 0."
    : thresholdValue <= 0
      ? "Threshold must be greater than 0."
      : null;

  return {
    symbolError,
    thresholdError,
    canSubmit: !symbolError && !thresholdError
  };
}

export function priceAlertDraftFromForm(form: PriceAlertFormState): PriceAlertDraft | null {
  const validation = validatePriceAlertForm(form);
  if (!validation.canSubmit) {
    return null;
  }
  const threshold = parseThreshold(form.threshold);
  if (threshold === null) {
    return null;
  }
  return {
    symbol: form.symbol.trim().toUpperCase(),
    condition: form.condition,
    field: form.field,
    threshold,
    note: form.note.trim() || null
  };
}

export function parseThreshold(value: string): number | null {
  const trimmed = value.trim();
  if (!trimmed) {
    return null;
  }
  const parsed = Number(trimmed.replace(/,/g, ""));
  return Number.isFinite(parsed) ? parsed : null;
}

export const PRICE_ALERT_CONDITION_OPTIONS: Array<{ value: PriceAlertCondition; label: string; helper: string }> = [
  { value: "above", label: "At or above", helper: "Fires whenever price is at or above the threshold." },
  { value: "below", label: "At or below", helper: "Fires whenever price is at or below the threshold." },
  { value: "crosses-up", label: "Crosses up through", helper: "Fires once when price rises through the threshold." },
  { value: "crosses-down", label: "Crosses down through", helper: "Fires once when price falls through the threshold." }
];

export const PRICE_ALERT_FIELD_OPTIONS: Array<{ value: PriceAlertField; label: string }> = [
  { value: "last", label: "Last trade" },
  { value: "bid", label: "Bid" },
  { value: "ask", label: "Ask" },
  { value: "mid", label: "Mid" }
];

export function formatPriceAlertPrice(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) {
    return "—";
  }
  return value.toLocaleString(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 4
  });
}

export function formatPriceAlertTimestamp(value: string | null | undefined): string {
  if (!value) {
    return "—";
  }
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "—";
  }
  return `${date.toLocaleString("en-US", {
    timeZone: "UTC",
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    hour12: false
  })} UTC`;
}

const SNOOZE_MINUTES = 30;

function readFormFromSeededSymbol(seededSymbol: string | null): PriceAlertFormState {
  return {
    ...DEFAULT_PRICE_ALERT_FORM,
    symbol: seededSymbol?.toUpperCase() ?? ""
  };
}

export function buildPriceAlertSubmitAction(validation: PriceAlertFormValidation): PriceAlertSubmitAction {
  const disabledReason = validation.canSubmit
    ? null
    : validation.symbolError ?? validation.thresholdError ?? "Complete the alert form before adding it.";

  return {
    disabled: !validation.canSubmit,
    disabledReason,
    ariaLabel: validation.canSubmit ? "Create price alert" : `Create price alert unavailable: ${disabledReason}`
  };
}

export function buildPriceAlertMetrics(alerts: Pick<PriceAlertsApi, "enabledCount" | "unacknowledgedCount" | "lastPollAt">): MetricSnapshot[] {
  return [
    {
      id: "price-alert-active-alerts",
      label: "Active alerts",
      value: String(alerts.enabledCount),
      delta: alerts.enabledCount > 0 ? "Watching" : "Idle",
      tone: alerts.enabledCount > 0 ? "default" : "warning"
    },
    {
      id: "price-alert-unacknowledged-triggers",
      label: "Unacknowledged",
      value: String(alerts.unacknowledgedCount),
      delta: alerts.unacknowledgedCount > 0 ? "Review" : "Clear",
      tone: alerts.unacknowledgedCount > 0 ? "warning" : "success"
    },
    {
      id: "price-alert-last-poll",
      label: "Last poll",
      value: formatPriceAlertTimestamp(alerts.lastPollAt),
      delta: alerts.lastPollAt ? "Refreshed" : "Awaiting quote",
      tone: alerts.lastPollAt ? "default" : "warning"
    }
  ];
}

function buildPollErrorPanel(error: string | null): PriceAlertStatusPanel | null {
  if (!error) {
    return null;
  }

  return {
    id: "price-alert-poll-error",
    role: "alert",
    ariaLive: "assertive",
    className: "mt-3 rounded-md border border-danger/30 bg-danger/10 px-3 py-2 text-sm text-danger",
    text: `Quote refresh failed: ${error}`
  };
}

function buildNotificationPanel(permission: NotificationPermission | "unsupported"): PriceAlertNotificationPanel | null {
  if (permission === "default") {
    return {
      id: "price-alert-notification-request",
      role: "status",
      tone: "warning",
      text: "Browser notifications are off. You'll only see alerts when this tab is in the foreground.",
      action: {
        label: "Enable notifications",
        ariaLabel: "Enable browser notifications for price alerts"
      }
    };
  }

  if (permission === "denied") {
    return {
      id: "price-alert-notification-denied",
      role: "status",
      tone: "muted",
      text: "Browser notifications are blocked. Triggered alerts still appear in this screen and the masthead bell.",
      action: null
    };
  }

  if (permission === "unsupported") {
    return {
      id: "price-alert-notification-unsupported",
      role: "status",
      tone: "muted",
      text: "Browser notifications are unavailable in this runtime. Triggered alerts still appear in this screen.",
      action: null
    };
  }

  return null;
}

export function buildPriceAlertTriggerSection(
  triggers: PriceAlertTrigger[],
  unacknowledgedCount: number,
  selectedTriggerId: string | null = null
): PriceAlertTriggerSectionViewModel {
  const selectedTrigger = resolveSelectedTrigger(triggers, selectedTriggerId);
  const rows = triggers.slice(0, 25).map((trigger) => buildTriggerRow(trigger, selectedTrigger?.id ?? null));
  const count = triggers.length;

  return {
    title: "Triggered alerts",
    summary: count === 0 ? "No alerts have triggered yet." : `${count} historical trigger${count === 1 ? "" : "s"}.`,
    tableLabel: "Triggered price alerts",
    caption: "Triggered price alerts with acknowledgement state, trigger price, and quote links.",
    emptyText: "Triggered alerts will appear here. Each entry stays acknowledged once you confirm it.",
    hasRows: rows.length > 0,
    detailPanelId: PRICE_ALERT_TRIGGER_DETAIL_PANEL_ID,
    selectedRowId: selectedTrigger?.id ?? null,
    selectedDetail: selectedTrigger ? buildTriggerDetail(selectedTrigger) : null,
    detailEmptyState: {
      title: "No triggered alert selected",
      description: count === 0
        ? "Triggered alerts will appear here once a watched price crosses its threshold."
        : "Select a triggered alert row to inspect acknowledgement state, note, and quote handoff.",
      ariaLabel: "Triggered price alert detail empty state"
    },
    rows,
    acknowledgeAllAction: {
      label: "Acknowledge all",
      ariaLabel: "Acknowledge all triggered price alerts",
      disabled: unacknowledgedCount === 0,
      disabledReason: unacknowledgedCount === 0 ? "All triggered alerts are already acknowledged." : null
    },
    clearHistoryAction: {
      label: "Clear history",
      ariaLabel: "Clear triggered price alert history",
      disabled: count === 0,
      disabledReason: count === 0 ? "There is no trigger history to clear." : null
    }
  };
}

function buildTriggerRow(trigger: PriceAlertTrigger, selectedTriggerId: string | null): PriceAlertTriggerRowViewModel {
  const conditionLabel = describeCondition(trigger.condition, trigger.threshold, trigger.field);
  const priceLabel = formatPriceAlertPrice(trigger.triggeredPrice);
  const triggeredAtLabel = formatPriceAlertTimestamp(trigger.triggeredAt);
  const noteLabel = trigger.note ?? "No operator note";
  const expanded = selectedTriggerId === trigger.id;

  return {
    id: trigger.id,
    detailPanelId: PRICE_ALERT_TRIGGER_DETAIL_PANEL_ID,
    expanded,
    symbol: trigger.symbol,
    conditionLabel,
    priceLabel,
    triggeredAtLabel,
    noteLabel,
    acknowledged: trigger.acknowledged,
    statusLabel: trigger.acknowledged ? "Acknowledged" : "New",
    statusVariant: trigger.acknowledged ? "outline" : "warning",
    rowAriaLabel: `${trigger.symbol} triggered alert. ${conditionLabel}. Fired at ${priceLabel} on ${triggeredAtLabel}. ${trigger.acknowledged ? "Acknowledged." : "Needs acknowledgement."}`,
    rowSelectAriaLabel: `Inspect triggered alert ${trigger.symbol}`,
    quoteHref: `/data/quotes?symbol=${encodeURIComponent(trigger.symbol)}`,
    quoteAriaLabel: `Open live quotes for ${trigger.symbol}`,
    acknowledgeAction: trigger.acknowledged
      ? null
      : {
        label: "Acknowledge",
        ariaLabel: `Acknowledge ${trigger.symbol} triggered alert`,
        disabled: false,
        disabledReason: null
      }
  };
}

export function buildPriceAlertListSection(
  alerts: PriceAlert[],
  enabledCount: number,
  selectedAlertId: string | null = null
): PriceAlertListSectionViewModel {
  const selectedAlert = resolveSelectedAlert(alerts, selectedAlertId);
  const rows = alerts.map((alert) => buildAlertRow(alert, selectedAlert?.id ?? null));
  const count = alerts.length;

  return {
    title: "All alerts",
    summary: count === 0 ? "No alerts set." : `${count} alert${count === 1 ? "" : "s"} - ${enabledCount} watching live, the rest waiting to be reset.`,
    tableLabel: "Configured price alerts",
    caption: "Configured price alerts with current state, condition, last observed price, and row actions.",
    emptyText: "Add your first alert above. You can also click Set price alert from any symbol on the Live quotes screen.",
    hasRows: rows.length > 0,
    detailPanelId: PRICE_ALERT_LIST_DETAIL_PANEL_ID,
    selectedRowId: selectedAlert?.id ?? null,
    selectedDetail: selectedAlert ? buildAlertDetail(selectedAlert) : null,
    detailEmptyState: {
      title: "No configured alert selected",
      description: count === 0
        ? "Configured price alerts will appear here after you add a symbol, condition, and threshold."
        : "Select a configured alert row to inspect last observation, snooze state, and available next action.",
      ariaLabel: "Configured price alert detail empty state"
    },
    rows
  };
}

function buildAlertRow(alert: PriceAlert, selectedAlertId: string | null): PriceAlertRowViewModel {
  const snoozedUntilMs = alert.snoozedUntil ? new Date(alert.snoozedUntil).getTime() : null;
  const isSnoozed = snoozedUntilMs !== null && snoozedUntilMs > Date.now();
  const statusLabel = !alert.enabled ? (alert.triggeredAt ? "Triggered" : "Disabled") : isSnoozed ? "Snoozed" : "Watching";
  const statusVariant: PriceAlertRowViewModel["statusVariant"] = !alert.enabled ? "outline" : isSnoozed ? "warning" : "default";
  const conditionLabel = describeCondition(alert.condition, alert.threshold, alert.field);
  const snoozeLabel = isSnoozed ? ` · snoozed until ${formatPriceAlertTimestamp(alert.snoozedUntil)}` : "";
  const lastObservedLabel = `Last seen ${formatPriceAlertPrice(alert.lastObservedPrice)} · ${formatPriceAlertTimestamp(alert.lastObservedAt)}${snoozeLabel}`;

  return {
    id: alert.id,
    detailPanelId: PRICE_ALERT_LIST_DETAIL_PANEL_ID,
    expanded: selectedAlertId === alert.id,
    symbol: alert.symbol,
    conditionLabel,
    lastObservedLabel,
    noteLabel: alert.note ?? "No operator note",
    statusLabel,
    statusVariant,
    rowClassName: alert.enabled && !isSnoozed ? "border-border/70 bg-background/50" : "border-border/50 bg-secondary/25",
    rowAriaLabel: `${alert.symbol} price alert. ${statusLabel}. ${conditionLabel}. ${lastObservedLabel}.`,
    rowSelectAriaLabel: `Inspect configured alert ${alert.symbol}`,
    quoteHref: `/data/quotes?symbol=${encodeURIComponent(alert.symbol)}`,
    quoteAriaLabel: `Open live quotes for ${alert.symbol}`,
    primaryAction: buildPrimaryAlertAction(alert, isSnoozed),
    pauseAction: {
      id: alert.enabled ? "pause" : "resume",
      label: alert.enabled ? "Pause" : "Resume",
      ariaLabel: alert.enabled ? `Pause ${alert.symbol} alert` : `Resume ${alert.symbol} alert`
    },
    deleteAction: {
      id: "delete",
      label: "Delete",
      ariaLabel: `Delete ${alert.symbol} alert`
    }
  };
}

function buildTriggerDetail(trigger: PriceAlertTrigger): PriceAlertRowDetailViewModel {
  const conditionLabel = describeCondition(trigger.condition, trigger.threshold, trigger.field);
  const priceLabel = formatPriceAlertPrice(trigger.triggeredPrice);
  const triggeredAtLabel = formatPriceAlertTimestamp(trigger.triggeredAt);
  const statusLabel = trigger.acknowledged ? "Acknowledged" : "New";

  return {
    id: PRICE_ALERT_TRIGGER_DETAIL_PANEL_ID,
    sourceId: trigger.id,
    ariaLabel: `Triggered price alert detail for ${trigger.symbol}`,
    eyebrow: "Triggered alert",
    title: trigger.symbol,
    subtitle: conditionLabel,
    description: trigger.acknowledged
      ? "Alert trigger has been acknowledged and remains in the trigger history for audit review."
      : "Alert trigger needs acknowledgement before the masthead price-alert count clears.",
    statusLabel,
    statusVariant: trigger.acknowledged ? "outline" : "warning",
    fields: [
      { id: "trigger-price", label: "Trigger price", value: priceLabel, tone: trigger.acknowledged ? "muted" : "warning" },
      { id: "triggered-at", label: "Triggered at", value: triggeredAtLabel, tone: "muted" },
      { id: "note", label: "Operator note", value: trigger.note ?? "No operator note", tone: trigger.note ? "default" : "muted" },
      { id: "alert-id", label: "Alert ID", value: trigger.alertId, tone: "muted" }
    ],
    quoteHref: `/data/quotes?symbol=${encodeURIComponent(trigger.symbol)}`,
    quoteAriaLabel: `Open live quotes for ${trigger.symbol}`,
    action: trigger.acknowledged
      ? null
      : {
        id: "acknowledge",
        kind: "trigger",
        label: "Acknowledge",
        ariaLabel: `Acknowledge ${trigger.symbol} triggered alert`,
        disabled: false,
        disabledReason: null
      }
  };
}

function buildAlertDetail(alert: PriceAlert): PriceAlertRowDetailViewModel {
  const snoozedUntilMs = alert.snoozedUntil ? new Date(alert.snoozedUntil).getTime() : null;
  const isSnoozed = snoozedUntilMs !== null && snoozedUntilMs > Date.now();
  const statusLabel = !alert.enabled ? (alert.triggeredAt ? "Triggered" : "Disabled") : isSnoozed ? "Snoozed" : "Watching";
  const statusVariant: PriceAlertRowDetailViewModel["statusVariant"] = !alert.enabled ? "outline" : isSnoozed ? "warning" : "default";
  const conditionLabel = describeCondition(alert.condition, alert.threshold, alert.field);
  const primaryAction = buildPrimaryAlertAction(alert, isSnoozed);

  return {
    id: PRICE_ALERT_LIST_DETAIL_PANEL_ID,
    sourceId: alert.id,
    ariaLabel: `Configured price alert detail for ${alert.symbol}`,
    eyebrow: "Configured alert",
    title: alert.symbol,
    subtitle: conditionLabel,
    description: alert.enabled
      ? "Alert is evaluated against live quotes while this browser tab is open."
      : "Alert is not watching live quotes until it is reset or resumed.",
    statusLabel,
    statusVariant,
    fields: [
      { id: "last-observed-price", label: "Last observed", value: formatPriceAlertPrice(alert.lastObservedPrice), tone: alert.lastObservedPrice === null ? "muted" : "default" },
      { id: "last-observed-at", label: "Observed at", value: formatPriceAlertTimestamp(alert.lastObservedAt), tone: "muted" },
      { id: "created-at", label: "Created", value: formatPriceAlertTimestamp(alert.createdAt), tone: "muted" },
      { id: "snoozed-until", label: "Snoozed until", value: isSnoozed ? formatPriceAlertTimestamp(alert.snoozedUntil) : "—", tone: isSnoozed ? "warning" : "muted" },
      { id: "note", label: "Operator note", value: alert.note ?? "No operator note", tone: alert.note ? "default" : "muted" }
    ],
    quoteHref: `/data/quotes?symbol=${encodeURIComponent(alert.symbol)}`,
    quoteAriaLabel: `Open live quotes for ${alert.symbol}`,
    action: {
      ...primaryAction,
      kind: "alert",
      disabled: false,
      disabledReason: null
    }
  };
}

function resolveSelectedTrigger(
  triggers: PriceAlertTrigger[],
  selectedTriggerId: string | null
): PriceAlertTrigger | null {
  return triggers.find((trigger) => trigger.id === selectedTriggerId) ?? triggers[0] ?? null;
}

function resolveSelectedAlert(
  alerts: PriceAlert[],
  selectedAlertId: string | null
): PriceAlert | null {
  return alerts.find((alert) => alert.id === selectedAlertId) ?? alerts[0] ?? null;
}

function buildPrimaryAlertAction(alert: PriceAlert, isSnoozed: boolean): PriceAlertRowAction {
  if (!alert.enabled) {
    return {
      id: "reset",
      label: "Reset",
      ariaLabel: `Re-enable ${alert.symbol} alert`
    };
  }

  if (isSnoozed) {
    return {
      id: "wake",
      label: "Wake",
      ariaLabel: `Wake ${alert.symbol} alert`
    };
  }

  return {
    id: "snooze",
    label: `Snooze ${SNOOZE_MINUTES}m`,
    ariaLabel: `Snooze ${alert.symbol} alert for ${SNOOZE_MINUTES} minutes`
  };
}

export function sortPriceAlerts(alerts: PriceAlert[]): PriceAlert[] {
  const list = [...alerts];
  list.sort((a, b) => {
    if (a.enabled !== b.enabled) {
      return a.enabled ? -1 : 1;
    }

    return a.symbol.localeCompare(b.symbol) || a.threshold - b.threshold;
  });
  return list;
}
