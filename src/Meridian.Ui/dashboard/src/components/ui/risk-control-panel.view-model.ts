import { useEffect, useMemo, useRef, useState } from "react";
import { getRiskRuleConfig, getRiskRules, updateRiskRuleConfig } from "@/lib/api";
import { describeApiError, type ApiErrorDisplay } from "@/lib/api-errors";
import type { RiskRuleConfig, RiskRuleConfigUpdateRequest, RiskRuleStatus } from "@/types";

export type RiskRuleTone = "success" | "warning" | "danger";

export interface RiskRuleRowViewModel {
  ruleName: string;
  state: RiskRuleStatus["state"];
  summary: string;
  threshold: string;
  currentValue: string;
  tone: RiskRuleTone;
  violationCount: number;
}

export interface RuleViolationTimelineItem {
  id: string;
  ruleName: string;
  message: string;
}

export interface RiskControlPanelViewModel {
  panelAriaLabel: string;
  panelAriaBusy: boolean;
  overallState: RiskRuleStatus["state"];
  overallSummary: string;
  loading: boolean;
  error: ApiErrorDisplay | null;
  statusMessage: string | null;
  statusRole: "status" | "alert";
  statusTone: "default" | "success" | "danger";
  statusAnnouncement: string;
  rows: RiskRuleRowViewModel[];
  rowsLabel: string;
  emptyRowsText: string;
  violationTimeline: RuleViolationTimelineItem[];
  timelineLabel: string;
  emptyTimelineText: string;
  drawdownField: RiskControlDrawdownFieldViewModel;
  saveAction: RiskControlCommandViewModel;
  refreshAction: RiskControlCommandViewModel;
}

export interface RiskControlDrawdownFieldViewModel {
  id: string;
  label: string;
  value: string;
  placeholder: string;
  helpId: string;
  helpText: string;
  describedBy: string;
  error: boolean;
  disabled: boolean;
  disabledReason: string | null;
}

export interface RiskControlCommandViewModel {
  label: string;
  busy: boolean;
  busyLabel: string | null;
  disabled: boolean;
  disabledReason: string | null;
  ariaLabel: string;
}

export interface RiskControlPanelServices {
  getRules: () => Promise<RiskRuleStatus[]>;
  getConfig: (ruleName: string) => Promise<RiskRuleConfig>;
  updateConfig: (ruleName: string, request: RiskRuleConfigUpdateRequest) => Promise<RiskRuleConfig>;
}

export interface RiskControlPanelCommandState {
  loading: boolean;
  saving: boolean;
  loadFailed: boolean;
  drawdownPercent: string;
  submitted: boolean;
  statusMessage: string | null;
  statusTone: "default" | "success" | "danger";
}

const DRAWDOWN_RULE_NAME = "DrawdownCircuitBreaker";

const defaultServices: RiskControlPanelServices = {
  getRules: getRiskRules,
  getConfig: getRiskRuleConfig,
  updateConfig: updateRiskRuleConfig
};

const defaultCommandState: RiskControlPanelCommandState = {
  loading: false,
  saving: false,
  loadFailed: false,
  drawdownPercent: "",
  submitted: false,
  statusMessage: null,
  statusTone: "default"
};

export function useRiskControlPanelViewModel(
  services: RiskControlPanelServices = defaultServices
): RiskControlPanelViewModel & {
  setDrawdownPercent: (value: string) => void;
  saveDrawdownThreshold: () => Promise<void>;
  refresh: () => Promise<void>;
} {
  const [statuses, setStatuses] = useState<RiskRuleStatus[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<ApiErrorDisplay | null>(null);
  const [drawdownPercent, setDrawdownPercentState] = useState("");
  const [submitted, setSubmitted] = useState(false);
  const [statusMessage, setStatusMessage] = useState<string | null>(null);
  const [statusTone, setStatusTone] = useState<"default" | "success" | "danger">("default");
  const mountedRef = useRef(true);
  const requestRevisionRef = useRef(0);

  useEffect(() => {
    mountedRef.current = true;

    return () => {
      mountedRef.current = false;
      requestRevisionRef.current += 1;
    };
  }, []);

  const refresh = async () => {
    const revision = requestRevisionRef.current + 1;
    requestRevisionRef.current = revision;
    setLoading(true);
    setError(null);
    setStatusMessage(null);
    setStatusTone("default");

    try {
      const [rules, config] = await Promise.all([
        services.getRules(),
        services.getConfig(DRAWDOWN_RULE_NAME)
      ]);
      if (!mountedRef.current || requestRevisionRef.current !== revision) {
        return;
      }

      setStatuses(rules);
      setDrawdownPercentState(formatDrawdown(config));
      setSubmitted(false);
    } catch (loadError) {
      if (!mountedRef.current || requestRevisionRef.current !== revision) {
        return;
      }

      setStatuses([]);
      setDrawdownPercentState("");
      setError(describeApiError(loadError, "Failed to load risk controls."));
      setStatusTone("danger");
    } finally {
      if (mountedRef.current && requestRevisionRef.current === revision) {
        setLoading(false);
      }
    }
  };

  useEffect(() => {
    void refresh();
  }, []);

  const setDrawdownPercent = (value: string) => {
    setDrawdownPercentState(value);
    setSubmitted(false);
    setStatusMessage(null);
    setStatusTone("default");
    setError(null);
  };

  const saveDrawdownThreshold = async () => {
    const nextState = {
      loading,
      saving,
      loadFailed: Boolean(error && statuses.length === 0),
      drawdownPercent,
      submitted: true,
      statusMessage,
      statusTone
    };
    const command = buildRiskControlCommandState(nextState);
    setSubmitted(true);

    if (command.saveAction.disabled) {
      return;
    }

    const parsed = Number(drawdownPercent);
    setSaving(true);
    setError(null);
    setStatusMessage(null);
    setStatusTone("default");

    try {
      const updatedConfig = await services.updateConfig(DRAWDOWN_RULE_NAME, {
        maxDrawdownPercent: parsed,
        reason: "Updated from risk control panel."
      });
      const refreshed = await services.getRules();
      if (!mountedRef.current) {
        return;
      }

      setStatuses(refreshed);
      setDrawdownPercentState(formatDrawdown(updatedConfig));
      setSubmitted(false);
      setStatusMessage("Drawdown threshold saved.");
      setStatusTone("success");
    } catch (updateError) {
      if (!mountedRef.current) {
        return;
      }

      const display = describeApiError(updateError, "Failed to update risk rule config.");
      setError(display);
      setStatusMessage(display.summary);
      setStatusTone("danger");
    } finally {
      if (mountedRef.current) {
        setSaving(false);
      }
    }
  };

  const commandState = useMemo<RiskControlPanelCommandState>(() => ({
    loading,
    saving,
    loadFailed: Boolean(error && statuses.length === 0),
    drawdownPercent,
    submitted,
    statusMessage,
    statusTone
  }), [drawdownPercent, error, loading, saving, statusMessage, statusTone, statuses.length, submitted]);

  const vm = useMemo(
    () => buildRiskControlPanelViewModel(statuses, commandState, error),
    [commandState, error, statuses]
  );

  return {
    ...vm,
    setDrawdownPercent,
    saveDrawdownThreshold,
    refresh
  };
}

export function buildRiskControlPanelViewModel(
  statuses: RiskRuleStatus[],
  commandState: RiskControlPanelCommandState = defaultCommandState,
  error: ApiErrorDisplay | null = null
): RiskControlPanelViewModel {
  const command = buildRiskControlCommandState(commandState);
  if (statuses.length === 0) {
    return {
      panelAriaLabel: "Trading risk controls",
      panelAriaBusy: commandState.loading || commandState.saving,
      overallState: "Observe",
      overallSummary: "Risk runtime status is unavailable.",
      loading: commandState.loading,
      error,
      statusMessage: commandState.statusMessage,
      statusRole: commandState.statusTone === "danger" ? "alert" : "status",
      statusTone: commandState.statusTone,
      statusAnnouncement: commandState.loading
        ? "Loading risk controls."
        : error
          ? `Risk controls failed to load: ${error.summary}`
          : "No risk rules are currently available.",
      rows: [],
      rowsLabel: "Risk rule status",
      emptyRowsText: commandState.loading ? "Loading risk rules..." : "No risk rules are currently available.",
      violationTimeline: [],
      timelineLabel: "Rule violation timeline",
      emptyTimelineText: commandState.loading ? "Loading rule violations..." : "No recent violations recorded.",
      drawdownField: command.drawdownField,
      saveAction: command.saveAction,
      refreshAction: command.refreshAction
    };
  }

  const constrained = statuses.find((status) => status.state === "Constrained");
  const observed = statuses.find((status) => status.state === "Observe");
  const selected = constrained ?? observed ?? statuses[0];

  const rows = statuses.map((status) => ({
    ruleName: status.ruleName,
    state: status.state,
    summary: status.summary,
    threshold: status.threshold,
    currentValue: status.currentValue,
    tone: mapRuleTone(status.state),
    violationCount: status.recentViolations.length
  }));

  const violationTimeline = statuses.flatMap((status) =>
    status.recentViolations.map((message, index) => ({
      id: `${status.ruleName}-${index}`,
      ruleName: status.ruleName,
      message
    })));

  return {
    panelAriaLabel: "Trading risk controls",
    panelAriaBusy: commandState.loading || commandState.saving,
    overallState: selected.state,
    overallSummary: selected.summary,
    loading: commandState.loading,
    error,
    statusMessage: commandState.statusMessage,
    statusRole: commandState.statusTone === "danger" ? "alert" : "status",
    statusTone: commandState.statusTone,
    statusAnnouncement: commandState.saving
      ? "Saving drawdown threshold."
      : commandState.loading
        ? "Loading risk controls."
        : commandState.statusMessage ?? `${statuses.length} risk rules loaded. Overall state ${selected.state}.`,
    rows,
    rowsLabel: "Risk rule status",
    emptyRowsText: "No risk rules are currently available.",
    violationTimeline,
    timelineLabel: "Rule violation timeline",
    emptyTimelineText: "No recent violations recorded.",
    drawdownField: command.drawdownField,
    saveAction: command.saveAction,
    refreshAction: command.refreshAction
  };
}

function buildRiskControlCommandState(state: RiskControlPanelCommandState): {
  drawdownField: RiskControlDrawdownFieldViewModel;
  saveAction: RiskControlCommandViewModel;
  refreshAction: RiskControlCommandViewModel;
} {
  const value = state.drawdownPercent.trim();
  const missing = value.length === 0;
  const parsed = Number(value);
  const invalid = !missing && (!Number.isFinite(parsed) || parsed <= 0);
  const validationVisible = state.submitted || state.statusTone === "danger";
  const hasFieldError = validationVisible && (missing || invalid);
  const editDisabledReason = state.loading
    ? "Risk controls are still loading."
    : state.saving
      ? "Drawdown threshold update is already saving."
      : state.loadFailed
        ? "Risk controls must load before editing the drawdown threshold."
        : null;
  const saveDisabledReason = state.loading
    ? "Risk controls are still loading."
    : state.saving
      ? "Drawdown threshold update is already saving."
      : state.loadFailed
        ? "Reload risk controls before saving the drawdown threshold."
        : missing
          ? "Enter a drawdown threshold before saving."
          : invalid
            ? "Enter a positive number for the drawdown threshold."
            : null;
  const helpText = hasFieldError
    ? missing
      ? "Drawdown threshold is required before saving risk policy."
      : "Enter a positive percent value, for example 5."
    : "Percent value applied to the DrawdownCircuitBreaker risk rule.";

  return {
    drawdownField: {
      id: "risk-drawdown-threshold",
      label: "Drawdown threshold percent",
      value: state.drawdownPercent,
      placeholder: "5",
      helpId: "risk-drawdown-threshold-help",
      helpText,
      describedBy: "risk-drawdown-threshold-help risk-control-status",
      error: hasFieldError,
      disabled: editDisabledReason !== null,
      disabledReason: editDisabledReason
    },
    saveAction: {
      label: "Save",
      busy: state.saving,
      busyLabel: "Saving drawdown threshold",
      disabled: saveDisabledReason !== null,
      disabledReason: saveDisabledReason,
      ariaLabel: saveDisabledReason
        ? `Save drawdown threshold unavailable: ${saveDisabledReason}`
        : "Save drawdown threshold"
    },
    refreshAction: {
      label: state.loading ? "Refreshing" : "Refresh",
      busy: state.loading,
      busyLabel: "Refreshing risk controls",
      disabled: state.loading || state.saving,
      disabledReason: state.loading
        ? "Risk controls are already refreshing."
        : state.saving
          ? "Wait for the drawdown threshold update to finish before refreshing."
          : null,
      ariaLabel: state.loading ? "Refreshing risk controls" : "Refresh risk controls"
    }
  };
}

function formatDrawdown(config: RiskRuleConfig | null | undefined): string {
  if (!config || typeof config.maxDrawdownPercent !== "number" || Number.isNaN(config.maxDrawdownPercent)) {
    return "";
  }

  return config.maxDrawdownPercent.toString();
}

function mapRuleTone(state: RiskRuleStatus["state"]): RiskRuleTone {
  if (state === "Constrained") {
    return "danger";
  }

  if (state === "Observe") {
    return "warning";
  }

  return "success";
}
