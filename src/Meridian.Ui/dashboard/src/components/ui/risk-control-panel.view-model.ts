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
  fatFingerQuantityField: RiskControlDrawdownFieldViewModel;
  fatFingerDeviationField: RiskControlDrawdownFieldViewModel;
  saveFatFingerAction: RiskControlCommandViewModel;
  priceCollarField: RiskControlDrawdownFieldViewModel;
  savePriceCollarAction: RiskControlCommandViewModel;
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
  fatFingerQuantity: string;
  fatFingerDeviation: string;
  submittedFatFinger: boolean;
  priceCollar: string;
  submittedPriceCollar: boolean;
  statusMessage: string | null;
  statusTone: "default" | "success" | "danger";
}

const DRAWDOWN_RULE_NAME = "DrawdownCircuitBreaker";
const FAT_FINGER_RULE_NAME = "FatFinger";
const PRICE_COLLAR_RULE_NAME = "PriceCollar";

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
  fatFingerQuantity: "",
  fatFingerDeviation: "",
  submittedFatFinger: false,
  priceCollar: "",
  submittedPriceCollar: false,
  statusMessage: null,
  statusTone: "default"
};

export function useRiskControlPanelViewModel(
  services: RiskControlPanelServices = defaultServices
): RiskControlPanelViewModel & {
  setDrawdownPercent: (value: string) => void;
  saveDrawdownThreshold: () => Promise<void>;
  setFatFingerQuantity: (value: string) => void;
  setFatFingerDeviation: (value: string) => void;
  saveFatFingerThresholds: () => Promise<void>;
  setPriceCollar: (value: string) => void;
  savePriceCollarThreshold: () => Promise<void>;
  refresh: () => Promise<void>;
} {
  const [statuses, setStatuses] = useState<RiskRuleStatus[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<ApiErrorDisplay | null>(null);
  const [drawdownPercent, setDrawdownPercentState] = useState("");
  const [submitted, setSubmitted] = useState(false);
  const [fatFingerQuantity, setFatFingerQuantityState] = useState("");
  const [fatFingerDeviation, setFatFingerDeviationState] = useState("");
  const [submittedFatFinger, setSubmittedFatFinger] = useState(false);
  const [priceCollar, setPriceCollarState] = useState("");
  const [submittedPriceCollar, setSubmittedPriceCollar] = useState(false);
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
      const [rules, drawdownConfig, fatFingerConfig, priceCollarConfig] = await Promise.all([
        services.getRules(),
        services.getConfig(DRAWDOWN_RULE_NAME),
        services.getConfig(FAT_FINGER_RULE_NAME),
        services.getConfig(PRICE_COLLAR_RULE_NAME)
      ]);
      if (!mountedRef.current || requestRevisionRef.current !== revision) {
        return;
      }

      setStatuses(rules);
      setDrawdownPercentState(formatDrawdown(drawdownConfig));
      setFatFingerQuantityState(formatFatFingerQuantity(fatFingerConfig));
      setFatFingerDeviationState(formatFatFingerDeviation(fatFingerConfig));
      setPriceCollarState(formatPriceCollar(priceCollarConfig));
      setSubmitted(false);
      setSubmittedFatFinger(false);
      setSubmittedPriceCollar(false);
    } catch (loadError) {
      if (!mountedRef.current || requestRevisionRef.current !== revision) {
        return;
      }

      setStatuses([]);
      setDrawdownPercentState("");
      setFatFingerQuantityState("");
      setFatFingerDeviationState("");
      setPriceCollarState("");
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

  const setFatFingerQuantity = (value: string) => {
    setFatFingerQuantityState(value);
    setSubmittedFatFinger(false);
    setStatusMessage(null);
    setStatusTone("default");
    setError(null);
  };

  const setFatFingerDeviation = (value: string) => {
    setFatFingerDeviationState(value);
    setSubmittedFatFinger(false);
    setStatusMessage(null);
    setStatusTone("default");
    setError(null);
  };

  const setPriceCollar = (value: string) => {
    setPriceCollarState(value);
    setSubmittedPriceCollar(false);
    setStatusMessage(null);
    setStatusTone("default");
    setError(null);
  };

  const saveDrawdownThreshold = async () => {
    const nextState: RiskControlPanelCommandState = {
      loading,
      saving,
      loadFailed: Boolean(error && statuses.length === 0),
      drawdownPercent,
      submitted: true,
      fatFingerQuantity,
      fatFingerDeviation,
      submittedFatFinger,
      priceCollar,
      submittedPriceCollar,
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

  const saveFatFingerThresholds = async () => {
    const nextState: RiskControlPanelCommandState = {
      loading,
      saving,
      loadFailed: Boolean(error && statuses.length === 0),
      drawdownPercent,
      submitted,
      fatFingerQuantity,
      fatFingerDeviation,
      submittedFatFinger: true,
      priceCollar,
      submittedPriceCollar,
      statusMessage,
      statusTone
    };
    const command = buildRiskControlCommandState(nextState);
    setSubmittedFatFinger(true);

    if (command.saveFatFingerAction.disabled) {
      return;
    }

    const parsedQuantity = Number(fatFingerQuantity.trim());
    const parsedDeviation = Number(fatFingerDeviation.trim());
    setSaving(true);
    setError(null);
    setStatusMessage(null);
    setStatusTone("default");

    try {
      const updatedConfig = await services.updateConfig(FAT_FINGER_RULE_NAME, {
        maxOrderQuantity: parsedQuantity,
        maxPriceDeviationPercent: parsedDeviation,
        reason: "Updated from risk control panel."
      });
      const refreshed = await services.getRules();
      if (!mountedRef.current) {
        return;
      }

      setStatuses(refreshed);
      setFatFingerQuantityState(formatFatFingerQuantity(updatedConfig));
      setFatFingerDeviationState(formatFatFingerDeviation(updatedConfig));
      setSubmittedFatFinger(false);
      setStatusMessage("Fat-finger limits saved.");
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

  const savePriceCollarThreshold = async () => {
    const nextState: RiskControlPanelCommandState = {
      loading,
      saving,
      loadFailed: Boolean(error && statuses.length === 0),
      drawdownPercent,
      submitted,
      fatFingerQuantity,
      fatFingerDeviation,
      submittedFatFinger,
      priceCollar,
      submittedPriceCollar: true,
      statusMessage,
      statusTone
    };
    const command = buildRiskControlCommandState(nextState);
    setSubmittedPriceCollar(true);

    if (command.savePriceCollarAction.disabled) {
      return;
    }

    const parsedCollar = Number(priceCollar.trim());
    setSaving(true);
    setError(null);
    setStatusMessage(null);
    setStatusTone("default");

    try {
      const updatedConfig = await services.updateConfig(PRICE_COLLAR_RULE_NAME, {
        priceCollarPercent: parsedCollar,
        reason: "Updated from risk control panel."
      });
      const refreshed = await services.getRules();
      if (!mountedRef.current) {
        return;
      }

      setStatuses(refreshed);
      setPriceCollarState(formatPriceCollar(updatedConfig));
      setSubmittedPriceCollar(false);
      setStatusMessage("Price collar saved.");
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
    fatFingerQuantity,
    fatFingerDeviation,
    submittedFatFinger,
    priceCollar,
    submittedPriceCollar,
    statusMessage,
    statusTone
  }), [drawdownPercent, error, fatFingerDeviation, fatFingerQuantity, loading, priceCollar, saving, statusMessage, statusTone, statuses.length, submitted, submittedFatFinger, submittedPriceCollar]);

  const vm = useMemo(
    () => buildRiskControlPanelViewModel(statuses, commandState, error),
    [commandState, error, statuses]
  );

  return {
    ...vm,
    setDrawdownPercent,
    saveDrawdownThreshold,
    setFatFingerQuantity,
    setFatFingerDeviation,
    saveFatFingerThresholds,
    setPriceCollar,
    savePriceCollarThreshold,
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
      fatFingerQuantityField: command.fatFingerQuantityField,
      fatFingerDeviationField: command.fatFingerDeviationField,
      saveFatFingerAction: command.saveFatFingerAction,
      priceCollarField: command.priceCollarField,
      savePriceCollarAction: command.savePriceCollarAction,
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
      ? "Saving risk rule configuration."
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
    fatFingerQuantityField: command.fatFingerQuantityField,
    fatFingerDeviationField: command.fatFingerDeviationField,
    saveFatFingerAction: command.saveFatFingerAction,
    priceCollarField: command.priceCollarField,
    savePriceCollarAction: command.savePriceCollarAction,
    refreshAction: command.refreshAction
  };
}

function buildRiskControlCommandState(state: RiskControlPanelCommandState): {
  drawdownField: RiskControlDrawdownFieldViewModel;
  saveAction: RiskControlCommandViewModel;
  fatFingerQuantityField: RiskControlDrawdownFieldViewModel;
  fatFingerDeviationField: RiskControlDrawdownFieldViewModel;
  saveFatFingerAction: RiskControlCommandViewModel;
  priceCollarField: RiskControlDrawdownFieldViewModel;
  savePriceCollarAction: RiskControlCommandViewModel;
  refreshAction: RiskControlCommandViewModel;
} {
  // Drawdown
  const ddValue = state.drawdownPercent.trim();
  const ddMissing = ddValue.length === 0;
  const ddParsed = Number(ddValue);
  const ddInvalid = !ddMissing && (!Number.isFinite(ddParsed) || ddParsed <= 0);
  const ddValidationVisible = state.submitted || state.statusTone === "danger";
  const ddHasFieldError = ddValidationVisible && (ddMissing || ddInvalid);
  const ddEditDisabledReason = state.loading
    ? "Risk controls are still loading."
    : state.saving
      ? "Drawdown threshold update is already saving."
      : state.loadFailed
        ? "Risk controls must load before editing the drawdown threshold."
        : null;
  const ddSaveDisabledReason = state.loading
    ? "Risk controls are still loading."
    : state.saving
      ? "Drawdown threshold update is already saving."
      : state.loadFailed
        ? "Reload risk controls before saving the drawdown threshold."
        : ddMissing
          ? "Enter a drawdown threshold before saving."
          : ddInvalid
            ? "Enter a positive number for the drawdown threshold."
            : null;
  const ddHelpText = ddHasFieldError
    ? ddMissing
      ? "Drawdown threshold is required before saving risk policy."
      : "Enter a positive percent value, for example 5."
    : "Percent value applied to the DrawdownCircuitBreaker risk rule.";

  // Fat-finger quantity
  const ffqValue = state.fatFingerQuantity.trim();
  const ffqMissing = ffqValue.length === 0;
  const ffqParsed = Number(ffqValue);
  const ffqInvalid = !ffqMissing && (!Number.isFinite(ffqParsed) || !Number.isInteger(ffqParsed) || ffqParsed <= 0);
  const ffValidationVisible = state.submittedFatFinger || state.statusTone === "danger";
  const ffqHasError = ffValidationVisible && (ffqMissing || ffqInvalid);
  const ffEditDisabledReason = state.loading
    ? "Risk controls are still loading."
    : state.saving
      ? "A risk threshold update is already saving."
      : state.loadFailed
        ? "Risk controls must load before editing fat-finger limits."
        : null;
  const ffqHelpText = ffqHasError
    ? ffqMissing
      ? "Maximum order quantity is required before saving."
      : "Enter a positive whole number, for example 1000."
    : "Maximum number of shares or units per order.";

  // Fat-finger deviation
  const ffdValue = state.fatFingerDeviation.trim();
  const ffdMissing = ffdValue.length === 0;
  const ffdParsed = Number(ffdValue);
  const ffdInvalid = !ffdMissing && (!Number.isFinite(ffdParsed) || ffdParsed <= 0 || ffdParsed >= 100);
  const ffdHasError = ffValidationVisible && (ffdMissing || ffdInvalid);
  const ffdHelpText = ffdHasError
    ? ffdMissing
      ? "Price deviation percent is required before saving."
      : "Enter a positive number less than 100, for example 5."
    : "Maximum allowed price deviation from mid for the FatFinger rule.";

  const ffSaveDisabledReason = state.loading
    ? "Risk controls are still loading."
    : state.saving
      ? "A risk threshold update is already saving."
      : state.loadFailed
        ? "Reload risk controls before saving fat-finger limits."
        : ffqMissing
          ? "Enter a maximum order quantity before saving."
          : ffqInvalid
            ? "Enter a positive whole number for maximum order quantity."
            : ffdMissing
              ? "Enter a price deviation percent before saving."
              : ffdInvalid
                ? "Enter a positive number less than 100 for price deviation."
                : null;

  // Price collar
  const pcValue = state.priceCollar.trim();
  const pcMissing = pcValue.length === 0;
  const pcParsed = Number(pcValue);
  const pcInvalid = !pcMissing && (!Number.isFinite(pcParsed) || pcParsed <= 0 || pcParsed >= 100);
  const pcValidationVisible = state.submittedPriceCollar || state.statusTone === "danger";
  const pcHasError = pcValidationVisible && (pcMissing || pcInvalid);
  const pcEditDisabledReason = state.loading
    ? "Risk controls are still loading."
    : state.saving
      ? "A risk threshold update is already saving."
      : state.loadFailed
        ? "Risk controls must load before editing the price collar."
        : null;
  const pcSaveDisabledReason = state.loading
    ? "Risk controls are still loading."
    : state.saving
      ? "A risk threshold update is already saving."
      : state.loadFailed
        ? "Reload risk controls before saving the price collar."
        : pcMissing
          ? "Enter a price collar percent before saving."
          : pcInvalid
            ? "Enter a positive number less than 100 for the price collar."
            : null;
  const pcHelpText = pcHasError
    ? pcMissing
      ? "Price collar percent is required before saving."
      : "Enter a positive number less than 100, for example 3."
    : "Price collar percentage applied to the PriceCollar risk rule.";

  return {
    drawdownField: {
      id: "risk-drawdown-threshold",
      label: "Drawdown threshold percent",
      value: state.drawdownPercent,
      placeholder: "5",
      helpId: "risk-drawdown-threshold-help",
      helpText: ddHelpText,
      describedBy: "risk-drawdown-threshold-help risk-control-status",
      error: ddHasFieldError,
      disabled: ddEditDisabledReason !== null,
      disabledReason: ddEditDisabledReason
    },
    saveAction: {
      label: "Save",
      busy: state.saving,
      busyLabel: "Saving drawdown threshold",
      disabled: ddSaveDisabledReason !== null,
      disabledReason: ddSaveDisabledReason,
      ariaLabel: ddSaveDisabledReason
        ? `Save drawdown threshold unavailable: ${ddSaveDisabledReason}`
        : "Save drawdown threshold"
    },
    fatFingerQuantityField: {
      id: "risk-fat-finger-quantity",
      label: "Maximum order quantity",
      value: state.fatFingerQuantity,
      placeholder: "1000",
      helpId: "risk-fat-finger-quantity-help",
      helpText: ffqHelpText,
      describedBy: "risk-fat-finger-quantity-help risk-control-status",
      error: ffqHasError,
      disabled: ffEditDisabledReason !== null,
      disabledReason: ffEditDisabledReason
    },
    fatFingerDeviationField: {
      id: "risk-fat-finger-deviation",
      label: "Maximum price deviation percent",
      value: state.fatFingerDeviation,
      placeholder: "5",
      helpId: "risk-fat-finger-deviation-help",
      helpText: ffdHelpText,
      describedBy: "risk-fat-finger-deviation-help risk-control-status",
      error: ffdHasError,
      disabled: ffEditDisabledReason !== null,
      disabledReason: ffEditDisabledReason
    },
    saveFatFingerAction: {
      label: "Save",
      busy: state.saving,
      busyLabel: "Saving fat-finger limits",
      disabled: ffSaveDisabledReason !== null,
      disabledReason: ffSaveDisabledReason,
      ariaLabel: ffSaveDisabledReason
        ? `Save fat-finger limits unavailable: ${ffSaveDisabledReason}`
        : "Save fat-finger limits"
    },
    priceCollarField: {
      id: "risk-price-collar",
      label: "Price collar percent",
      value: state.priceCollar,
      placeholder: "3",
      helpId: "risk-price-collar-help",
      helpText: pcHelpText,
      describedBy: "risk-price-collar-help risk-control-status",
      error: pcHasError,
      disabled: pcEditDisabledReason !== null,
      disabledReason: pcEditDisabledReason
    },
    savePriceCollarAction: {
      label: "Save",
      busy: state.saving,
      busyLabel: "Saving price collar",
      disabled: pcSaveDisabledReason !== null,
      disabledReason: pcSaveDisabledReason,
      ariaLabel: pcSaveDisabledReason
        ? `Save price collar unavailable: ${pcSaveDisabledReason}`
        : "Save price collar"
    },
    refreshAction: {
      label: state.loading ? "Refreshing" : "Refresh",
      busy: state.loading,
      busyLabel: "Refreshing risk controls",
      disabled: state.loading || state.saving,
      disabledReason: state.loading
        ? "Risk controls are already refreshing."
        : state.saving
          ? "Wait for the risk threshold update to finish before refreshing."
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

function formatFatFingerQuantity(config: RiskRuleConfig | null | undefined): string {
  if (!config || typeof config.maxOrderQuantity !== "number" || Number.isNaN(config.maxOrderQuantity)) {
    return "";
  }

  return config.maxOrderQuantity.toString();
}

function formatFatFingerDeviation(config: RiskRuleConfig | null | undefined): string {
  if (!config || typeof config.maxPriceDeviationPercent !== "number" || Number.isNaN(config.maxPriceDeviationPercent)) {
    return "";
  }

  return config.maxPriceDeviationPercent.toString();
}

function formatPriceCollar(config: RiskRuleConfig | null | undefined): string {
  if (!config || typeof config.priceCollarPercent !== "number" || Number.isNaN(config.priceCollarPercent)) {
    return "";
  }

  return config.priceCollarPercent.toString();
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
