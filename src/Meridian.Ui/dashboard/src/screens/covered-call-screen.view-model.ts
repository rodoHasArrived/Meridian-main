import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import * as coveredCallApi from "@/lib/api/covered-call";
import type {
  CoveredCallBacktestRequest,
  CoveredCallChainPreview,
  CoveredCallRunPhase,
  CoveredCallRunResult,
  CoveredCallRunStatus,
  CoveredCallRunSummary,
  CoveredCallScoringMode
} from "@/types/covered-call";

export type CoveredCallStage = "configure" | "run" | "results";

export interface CoveredCallFormState {
  underlyingSymbol: string;
  from: string;
  to: string;
  minStrike: string;
  overwriteRatio: string;
  maxDelta: string;
  minDte: string;
  maxDte: string;
  minIvPercentile: string;
  minOpenInterest: string;
  minVolume: string;
  maxSpreadPct: string;
  takeProfitCapture: string;
  rollDelta: string;
  exDivWindowDays: string;
  scoringMode: CoveredCallScoringMode;
  depthBonusWeight: string;
  riskFreeRate: string;
  initialCash: string;
  initialUnderlyingShares: string;
  label: string;
}

export type CoveredCallFormErrors = Partial<Record<keyof CoveredCallFormState, string>>;

export interface CoveredCallChainPreviewState {
  status: "idle" | "loading" | "ready" | "error";
  data: CoveredCallChainPreview | null;
  error: string | null;
  selectedIndex: number;
}

export interface CoveredCallRunState {
  runId: string | null;
  status: CoveredCallRunStatus | null;
  result: CoveredCallRunResult | null;
  selectedPositionIndex: number;
}

export interface CoveredCallScreenServices {
  startRun: typeof coveredCallApi.startCoveredCallBacktest;
  getStatus: typeof coveredCallApi.getCoveredCallRunStatus;
  getResult: typeof coveredCallApi.getCoveredCallRunResult;
  cancelRun: typeof coveredCallApi.cancelCoveredCallRun;
  previewChain: typeof coveredCallApi.previewCoveredCallChain;
  listRuns: typeof coveredCallApi.listCoveredCallRuns;
}

export const DEFAULT_COVERED_CALL_FORM: CoveredCallFormState = {
  underlyingSymbol: "SPY",
  from: defaultIsoDate(-365),
  to: defaultIsoDate(0),
  minStrike: "",
  overwriteRatio: "0.75",
  maxDelta: "0.35",
  minDte: "7",
  maxDte: "60",
  minIvPercentile: "50",
  minOpenInterest: "1000",
  minVolume: "100",
  maxSpreadPct: "0.05",
  takeProfitCapture: "0.80",
  rollDelta: "0.55",
  exDivWindowDays: "7",
  scoringMode: "Relative",
  depthBonusWeight: "0.05",
  riskFreeRate: "0.04",
  initialCash: "100000",
  initialUnderlyingShares: "100",
  label: ""
};

/** Returns an ISO yyyy-MM-dd date `daysOffset` days from today (UTC). */
export function defaultIsoDate(daysOffset: number): string {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() + daysOffset);
  return d.toISOString().slice(0, 10);
}

/** Pure validator — returns a map of field-level error messages. Empty object means valid. */
export function validateForm(form: CoveredCallFormState): CoveredCallFormErrors {
  const errors: CoveredCallFormErrors = {};

  if (!form.underlyingSymbol.trim()) {
    errors.underlyingSymbol = "Underlying symbol is required.";
  }
  if (!form.from) errors.from = "From date is required.";
  if (!form.to) errors.to = "To date is required.";
  if (form.from && form.to && form.from > form.to) {
    errors.to = "'To' must be on or after 'From'.";
  }

  const minStrike = Number(form.minStrike);
  if (!form.minStrike.trim() || Number.isNaN(minStrike) || minStrike <= 0) {
    errors.minStrike = "Minimum strike must be greater than zero.";
  }

  const ratio = Number(form.overwriteRatio);
  if (Number.isNaN(ratio) || ratio <= 0 || ratio > 1) {
    errors.overwriteRatio = "Overwrite ratio must be in (0, 1].";
  }

  const maxDelta = Number(form.maxDelta);
  if (Number.isNaN(maxDelta) || maxDelta < 0 || maxDelta > 1) {
    errors.maxDelta = "Max delta must be in [0, 1].";
  }

  if (Number(form.minDte) < 0 || Number.isNaN(Number(form.minDte))) {
    errors.minDte = "Min DTE must be non-negative.";
  }

  if (form.maxDte.trim() !== "" && (Number(form.maxDte) < 0 || Number.isNaN(Number(form.maxDte)))) {
    errors.maxDte = "Max DTE must be non-negative when supplied.";
  }

  const cash = Number(form.initialCash);
  if (Number.isNaN(cash) || cash <= 0) {
    errors.initialCash = "Initial cash must be greater than zero.";
  }

  const shares = Number(form.initialUnderlyingShares);
  if (Number.isNaN(shares) || shares < 0) {
    errors.initialUnderlyingShares = "Initial underlying shares cannot be negative.";
  }

  return errors;
}

/** Pure: maps form state to the API request shape. Caller must validate first. */
export function formToRequest(form: CoveredCallFormState): CoveredCallBacktestRequest {
  const maybeNum = (v: string): number | null => {
    if (!v.trim()) return null;
    const n = Number(v);
    return Number.isFinite(n) ? n : null;
  };

  return {
    underlyingSymbol: form.underlyingSymbol.trim().toUpperCase(),
    from: form.from,
    to: form.to,
    minStrike: Number(form.minStrike),
    overwriteRatio: Number(form.overwriteRatio),
    maxDelta: Number(form.maxDelta),
    minDte: Number(form.minDte),
    maxDte: maybeNum(form.maxDte),
    minIvPercentile: Number(form.minIvPercentile),
    minOpenInterest: Number(form.minOpenInterest),
    minVolume: Number(form.minVolume),
    maxSpreadPct: Number(form.maxSpreadPct),
    takeProfitCapture: Number(form.takeProfitCapture),
    rollDelta: Number(form.rollDelta),
    exDivWindowDays: Number(form.exDivWindowDays),
    scoringMode: form.scoringMode,
    depthBonusWeight: Number(form.depthBonusWeight),
    riskFreeRate: Number(form.riskFreeRate),
    initialCash: Number(form.initialCash),
    initialUnderlyingShares: Number(form.initialUnderlyingShares),
    label: form.label.trim() ? form.label.trim() : null
  };
}

export function isTerminalPhase(phase: CoveredCallRunPhase): boolean {
  return phase === "Completed" || phase === "Failed" || phase === "Cancelled";
}

export interface CoveredCallScreenState {
  stage: CoveredCallStage;
  form: CoveredCallFormState;
  formErrors: CoveredCallFormErrors;
  chainPreview: CoveredCallChainPreviewState;
  run: CoveredCallRunState;
  history: CoveredCallRunSummary[];
  historyError: string | null;
  errorBanner: string | null;
}

export interface CoveredCallScreenViewModel extends CoveredCallScreenState {
  setField: <K extends keyof CoveredCallFormState>(key: K, value: CoveredCallFormState[K]) => void;
  resetForm: () => void;
  refreshChainPreview: () => Promise<void>;
  selectChainRow: (index: number) => void;
  startRun: () => Promise<void>;
  cancelRun: () => Promise<void>;
  loadHistory: () => Promise<void>;
  openRun: (runId: string) => Promise<void>;
  selectOpenPosition: (index: number) => void;
  goToStage: (stage: CoveredCallStage) => void;
  dismissError: () => void;
}

export interface UseCoveredCallScreenOptions {
  services?: Partial<CoveredCallScreenServices>;
  pollIntervalMs?: number;
  chainPreviewDebounceMs?: number;
}

const DEFAULT_SERVICES: CoveredCallScreenServices = {
  startRun: coveredCallApi.startCoveredCallBacktest,
  getStatus: coveredCallApi.getCoveredCallRunStatus,
  getResult: coveredCallApi.getCoveredCallRunResult,
  cancelRun: coveredCallApi.cancelCoveredCallRun,
  previewChain: coveredCallApi.previewCoveredCallChain,
  listRuns: coveredCallApi.listCoveredCallRuns
};

export function useCoveredCallScreenViewModel(
  options: UseCoveredCallScreenOptions = {}
): CoveredCallScreenViewModel {
  const optionsServices = options.services;
  // Stabilise the services ref across renders so dependent callbacks aren't invalidated and
  // the chain-preview debounce isn't reset on every parent render.
  const services: CoveredCallScreenServices = useMemo(
    () => ({ ...DEFAULT_SERVICES, ...optionsServices }),
    // We intentionally take optionsServices as-is; consumers passing a fresh literal each render
    // accept that they bust the memo on purpose.
    [optionsServices]
  );
  const pollIntervalMs = options.pollIntervalMs ?? 1500;
  const chainDebounceMs = options.chainPreviewDebounceMs ?? 300;

  const [stage, setStage] = useState<CoveredCallStage>("configure");
  const [form, setForm] = useState<CoveredCallFormState>(DEFAULT_COVERED_CALL_FORM);
  const [formErrors, setFormErrors] = useState<CoveredCallFormErrors>({});
  const [chainPreview, setChainPreview] = useState<CoveredCallChainPreviewState>({
    status: "idle",
    data: null,
    error: null,
    selectedIndex: 0
  });
  const [run, setRun] = useState<CoveredCallRunState>({
    runId: null,
    status: null,
    result: null,
    selectedPositionIndex: 0
  });
  const [history, setHistory] = useState<CoveredCallRunSummary[]>([]);
  const [historyError, setHistoryError] = useState<string | null>(null);
  const [errorBanner, setErrorBanner] = useState<string | null>(null);

  const setField = useCallback(<K extends keyof CoveredCallFormState>(key: K, value: CoveredCallFormState[K]) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setFormErrors((prev) => {
      if (!(key in prev)) return prev;
      const next = { ...prev };
      delete next[key];
      return next;
    });
  }, []);

  const resetForm = useCallback(() => {
    setForm(DEFAULT_COVERED_CALL_FORM);
    setFormErrors({});
  }, []);

  const dismissError = useCallback(() => setErrorBanner(null), []);
  const goToStage = useCallback((next: CoveredCallStage) => setStage(next), []);

  // ---- Chain preview (debounced) -----------------------------------------
  const chainAbortRef = useRef<AbortController | null>(null);
  const chainDebounceRef = useRef<number | null>(null);

  const refreshChainPreview = useCallback(async () => {
    chainAbortRef.current?.abort();
    const controller = new AbortController();
    chainAbortRef.current = controller;
    setChainPreview((prev) => ({ ...prev, status: "loading", error: null }));

    try {
      const minStrike = Number(form.minStrike);
      if (!form.underlyingSymbol.trim() || !(minStrike > 0)) {
        setChainPreview({
          status: "idle",
          data: null,
          error: null,
          selectedIndex: 0
        });
        return;
      }
      const data = await services.previewChain({
        underlyingSymbol: form.underlyingSymbol.trim().toUpperCase(),
        asOf: form.from || defaultIsoDate(0),
        minStrike,
        maxDelta: Number(form.maxDelta),
        minDte: Number(form.minDte),
        maxDte: form.maxDte.trim() ? Number(form.maxDte) : null,
        minOpenInterest: Number(form.minOpenInterest),
        minVolume: Number(form.minVolume),
        maxSpreadPct: Number(form.maxSpreadPct)
      }, controller.signal);

      setChainPreview({ status: "ready", data, error: null, selectedIndex: 0 });
    } catch (error) {
      if ((error as Error).name === "AbortError") return;
      setChainPreview({
        status: "error",
        data: null,
        error: (error as Error).message,
        selectedIndex: 0
      });
    }
  }, [
    form.underlyingSymbol,
    form.from,
    form.minStrike,
    form.maxDelta,
    form.minDte,
    form.maxDte,
    form.minOpenInterest,
    form.minVolume,
    form.maxSpreadPct,
    services
  ]);

  // Debounce auto-refresh when form fields that affect the preview change.
  useEffect(() => {
    if (stage !== "configure") return;
    if (chainDebounceRef.current !== null) {
      clearTimeout(chainDebounceRef.current);
    }
    chainDebounceRef.current = window.setTimeout(() => {
      void refreshChainPreview();
    }, chainDebounceMs);
    return () => {
      if (chainDebounceRef.current !== null) {
        clearTimeout(chainDebounceRef.current);
        chainDebounceRef.current = null;
      }
    };
  }, [stage, chainDebounceMs, refreshChainPreview]);

  const selectChainRow = useCallback((index: number) => {
    setChainPreview((prev) => ({ ...prev, selectedIndex: index }));
  }, []);

  const selectOpenPosition = useCallback((index: number) => {
    setRun((prev) => ({ ...prev, selectedPositionIndex: index }));
  }, []);

  // ---- Run lifecycle -----------------------------------------------------
  const pollAbortRef = useRef<AbortController | null>(null);
  const pollTimerRef = useRef<number | null>(null);
  const activeRunIdRef = useRef<string | null>(null);

  const stopPolling = useCallback(() => {
    if (pollTimerRef.current !== null) {
      clearTimeout(pollTimerRef.current);
      pollTimerRef.current = null;
    }
    pollAbortRef.current?.abort();
    pollAbortRef.current = null;
  }, []);

  const pollOnce = useCallback(async (runId: string) => {
    if (activeRunIdRef.current !== runId) return;
    const controller = new AbortController();
    pollAbortRef.current = controller;
    try {
      const status = await services.getStatus(runId, controller.signal);
      if (activeRunIdRef.current !== runId) return;
      setRun((prev) => ({ ...prev, status }));

      if (isTerminalPhase(status.phase)) {
        stopPolling();
        if (status.phase === "Completed") {
          try {
            const result = await services.getResult(runId);
            if (activeRunIdRef.current !== runId) return;
            setRun((prev) => ({ ...prev, result, selectedPositionIndex: 0 }));
            setStage("results");
          } catch (resultErr) {
            setErrorBanner(`Result fetch failed: ${(resultErr as Error).message}`);
          }
        } else if (status.phase === "Failed" && status.failureMessage) {
          setErrorBanner(status.failureMessage);
        }
      } else {
        pollTimerRef.current = window.setTimeout(() => {
          void pollOnce(runId);
        }, pollIntervalMs);
      }
    } catch (error) {
      if ((error as Error).name === "AbortError") return;
      setErrorBanner(`Status poll failed: ${(error as Error).message}`);
      stopPolling();
    }
  }, [pollIntervalMs, services, stopPolling]);

  const startRun = useCallback(async () => {
    const errors = validateForm(form);
    setFormErrors(errors);
    if (Object.keys(errors).length > 0) {
      setErrorBanner("Fix the highlighted form fields before running.");
      return;
    }

    // Stop any in-flight polling and invalidate the previous active run id so a stale poll that
    // resolves during the startRun await can't push status/result into the new run's state.
    stopPolling();
    activeRunIdRef.current = null;

    setErrorBanner(null);
    setRun({ runId: null, status: null, result: null, selectedPositionIndex: 0 });
    setStage("run");

    try {
      const handle = await services.startRun(formToRequest(form));
      activeRunIdRef.current = handle.runId;
      setRun({
        runId: handle.runId,
        status: { runId: handle.runId, phase: "Queued", percentComplete: 0, currentBacktestDate: null, failureMessage: null },
        result: null,
        selectedPositionIndex: 0
      });
      pollTimerRef.current = window.setTimeout(() => {
        void pollOnce(handle.runId);
      }, pollIntervalMs);
    } catch (error) {
      setErrorBanner((error as Error).message);
      setStage("configure");
    }
  }, [form, pollIntervalMs, pollOnce, services, stopPolling]);

  const cancelRun = useCallback(async () => {
    const runId = run.runId;
    if (!runId) return;
    try {
      const status = await services.cancelRun(runId);
      setRun((prev) => ({ ...prev, status }));
    } catch (error) {
      setErrorBanner(`Cancel failed: ${(error as Error).message}`);
    }
  }, [run.runId, services]);

  const loadHistory = useCallback(async () => {
    setHistoryError(null);
    try {
      const items = await services.listRuns(50);
      setHistory(items);
    } catch (error) {
      setHistoryError((error as Error).message);
    }
  }, [services]);

  const openRun = useCallback(async (runId: string) => {
    setErrorBanner(null);
    try {
      const result = await services.getResult(runId);
      activeRunIdRef.current = runId;
      stopPolling();
      setRun({
        runId,
        status: {
          runId,
          phase: "Completed",
          percentComplete: 1,
          currentBacktestDate: null,
          failureMessage: null
        },
        result,
        selectedPositionIndex: 0
      });
      setStage("results");
    } catch (error) {
      setErrorBanner(`Could not load run ${runId}: ${(error as Error).message}`);
    }
  }, [services, stopPolling]);

  // Stop polling and abort in-flight requests on unmount.
  useEffect(() => () => {
    stopPolling();
    chainAbortRef.current?.abort();
    if (chainDebounceRef.current !== null) clearTimeout(chainDebounceRef.current);
  }, [stopPolling]);

  return useMemo(() => ({
    stage,
    form,
    formErrors,
    chainPreview,
    run,
    history,
    historyError,
    errorBanner,
    setField,
    resetForm,
    refreshChainPreview,
    selectChainRow,
    startRun,
    cancelRun,
    loadHistory,
    openRun,
    selectOpenPosition,
    goToStage,
    dismissError
  }), [
    stage,
    form,
    formErrors,
    chainPreview,
    run,
    history,
    historyError,
    errorBanner,
    setField,
    resetForm,
    refreshChainPreview,
    selectChainRow,
    startRun,
    cancelRun,
    loadHistory,
    openRun,
    selectOpenPosition,
    goToStage,
    dismissError
  ]);
}
