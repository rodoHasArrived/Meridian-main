import { useCallback, useEffect, useMemo, useRef, useState, type Dispatch, type SetStateAction } from "react";
import { describeApiError } from "@/lib/api-errors";

export type RequestLifecyclePhase = "idle" | "running" | "succeeded" | "failed" | "aborted" | "stale";

export interface RequestBackoffMetadata {
  attempt: number;
  retryCount: number;
  nextRetryDelayMs: number | null;
  maxRetries: number;
}

export interface RequestLifecycleStatus {
  operation: string;
  phase: RequestLifecyclePhase;
  inFlight: boolean;
  version: number;
  message: string;
  error: string | null;
  startedAt: string | null;
  settledAt: string | null;
  /** Timestamp of the most recent successful settle; survives later failures. */
  lastSucceededAt: string | null;
  staleDiscardCount: number;
  backoff: RequestBackoffMetadata;
}

export interface RequestLifecycleToken {
  operation: string;
  version: number;
  signal: AbortSignal;
  startedAt: string;
  isCurrent: () => boolean;
  safeSetState: <T>(setter: Dispatch<SetStateAction<T>>, value: SetStateAction<T>) => boolean;
}

export interface RequestRetryContext {
  /** 1-based attempt number the retry should run as (failed attempt + 1). */
  attempt: number;
}

export interface RequestLifecycleOptions {
  operation: string;
  idleMessage?: string;
  runningMessage?: string;
  successMessage?: string;
  abortedMessage?: string;
  staleMessage?: string;
  failureMessage?: string;
  maxRetries?: number;
  baseBackoffMs?: number;
  backoffMultiplier?: number;
  /**
   * Runs the retry that backoff metadata advertises. `nextRetryDelayMs` (and the
   * "Retrying in Xs" affordance derived from it) is only populated when this is
   * provided, so surfaces never promise a retry that nothing will execute.
   * Leave unset for mutations — only idempotent reads should auto-retry.
   */
  onRetry?: (context: RequestRetryContext) => void;
}

export interface StartRequestOptions {
  busyMode?: "supersede" | "drop";
  runningMessage?: string;
  attempt?: number;
}

export interface SettleRequestOptions {
  message?: string;
}

export interface FailRequestOptions extends SettleRequestOptions {
  fallback?: string;
}

const DEFAULT_BASE_BACKOFF_MS = 500;
const DEFAULT_BACKOFF_MULTIPLIER = 2;

export function useRequestLifecycle({
  operation,
  idleMessage = "Ready to refresh.",
  runningMessage = "Refresh in progress.",
  successMessage = "Refresh complete.",
  abortedMessage = "Refresh was cancelled.",
  staleMessage = "An older refresh response was discarded.",
  failureMessage = "Refresh failed.",
  maxRetries = 0,
  baseBackoffMs = DEFAULT_BASE_BACKOFF_MS,
  backoffMultiplier = DEFAULT_BACKOFF_MULTIPLIER,
  onRetry
}: RequestLifecycleOptions) {
  const mountedRef = useRef(true);
  const versionRef = useRef(0);
  const controllerRef = useRef<AbortController | null>(null);
  const inFlightRef = useRef(false);
  const attemptRef = useRef(0);
  const retryTimerRef = useRef<number | null>(null);
  const onRetryRef = useRef(onRetry);

  useEffect(() => {
    onRetryRef.current = onRetry;
  }, [onRetry]);
  const [status, setStatus] = useState<RequestLifecycleStatus>(() => ({
    operation,
    phase: "idle",
    inFlight: false,
    version: 0,
    message: idleMessage,
    error: null,
    startedAt: null,
    settledAt: null,
    lastSucceededAt: null,
    staleDiscardCount: 0,
    backoff: {
      attempt: 0,
      retryCount: 0,
      nextRetryDelayMs: null,
      maxRetries
    }
  }));

  const isCurrentVersion = useCallback((version: number) => (
    mountedRef.current && versionRef.current === version
  ), []);

  const safeSetState = useCallback(<T,>(
    version: number,
    setter: Dispatch<SetStateAction<T>>,
    value: SetStateAction<T>
  ) => {
    if (!isCurrentVersion(version)) {
      return false;
    }

    setter(value);
    return true;
  }, [isCurrentVersion]);

  const clearScheduledRetry = useCallback(() => {
    if (retryTimerRef.current !== null) {
      window.clearTimeout(retryTimerRef.current);
      retryTimerRef.current = null;
    }
  }, []);

  const markStale = useCallback((version: number) => {
    if (!mountedRef.current || versionRef.current === version) {
      return;
    }

    setStatus((current) => ({
      ...current,
      phase: current.inFlight ? current.phase : "stale",
      message: current.inFlight ? current.message : staleMessage,
      staleDiscardCount: current.staleDiscardCount + 1
    }));
  }, [staleMessage]);

  const abort = useCallback(() => {
    clearScheduledRetry();
    versionRef.current += 1;
    controllerRef.current?.abort();
    controllerRef.current = null;
    inFlightRef.current = false;
    if (mountedRef.current) {
      setStatus((current) => ({
        ...current,
        phase: "aborted",
        inFlight: false,
        version: versionRef.current,
        message: abortedMessage,
        settledAt: new Date().toISOString()
      }));
    }
  }, [abortedMessage, clearScheduledRetry]);

  const invalidate = useCallback(() => {
    clearScheduledRetry();
    versionRef.current += 1;
    controllerRef.current?.abort();
    controllerRef.current = null;
    inFlightRef.current = false;
    if (mountedRef.current) {
      const settledAt = new Date().toISOString();
      setStatus((current) => ({
        ...current,
        phase: current.inFlight ? "stale" : current.phase,
        inFlight: false,
        version: versionRef.current,
        message: current.inFlight ? staleMessage : current.message,
        settledAt: current.inFlight ? settledAt : current.settledAt
      }));
    }
  }, [clearScheduledRetry, staleMessage]);

  const start = useCallback((options: StartRequestOptions = {}): RequestLifecycleToken | null => {
    if (!mountedRef.current) {
      return null;
    }

    if (options.busyMode === "drop" && inFlightRef.current) {
      return null;
    }

    clearScheduledRetry();
    versionRef.current += 1;
    controllerRef.current?.abort();
    const controller = new AbortController();
    controllerRef.current = controller;
    inFlightRef.current = true;
    const version = versionRef.current;
    const startedAt = new Date().toISOString();
    const attempt = Math.max(1, options.attempt ?? 1);
    const retryCount = Math.max(0, attempt - 1);
    attemptRef.current = attempt;

    setStatus((current) => ({
      ...current,
      operation,
      phase: "running",
      inFlight: true,
      version,
      message: options.runningMessage ?? runningMessage,
      error: null,
      startedAt,
      settledAt: null,
      backoff: {
        attempt,
        retryCount,
        nextRetryDelayMs: null,
        maxRetries
      }
    }));

    return {
      operation,
      version,
      signal: controller.signal,
      startedAt,
      isCurrent: () => isCurrentVersion(version),
      safeSetState: <T,>(setter: Dispatch<SetStateAction<T>>, value: SetStateAction<T>) => safeSetState(version, setter, value)
    };
  }, [clearScheduledRetry, isCurrentVersion, maxRetries, operation, runningMessage, safeSetState]);

  const succeed = useCallback((token: RequestLifecycleToken, options: SettleRequestOptions = {}) => {
    if (!isCurrentVersion(token.version)) {
      markStale(token.version);
      return false;
    }

    clearScheduledRetry();
    if (controllerRef.current?.signal === token.signal) {
      controllerRef.current = null;
    }
    inFlightRef.current = false;
    const settledAt = new Date().toISOString();
    setStatus((current) => ({
      ...current,
      phase: "succeeded",
      inFlight: false,
      version: token.version,
      message: options.message ?? successMessage,
      error: null,
      settledAt,
      lastSucceededAt: settledAt,
      backoff: {
        ...current.backoff,
        nextRetryDelayMs: null
      }
    }));
    return true;
  }, [clearScheduledRetry, isCurrentVersion, markStale, successMessage]);

  const fail = useCallback((token: RequestLifecycleToken, error: unknown, options: FailRequestOptions = {}) => {
    if (!isCurrentVersion(token.version)) {
      markStale(token.version);
      return false;
    }

    if (controllerRef.current?.signal === token.signal) {
      controllerRef.current = null;
    }
    inFlightRef.current = false;
    const display = describeApiError(error, options.fallback ?? failureMessage);
    const failedAttempt = Math.max(1, attemptRef.current);
    const retryCount = failedAttempt - 1;
    // Advertise a retry delay only when a retry is actually scheduled below.
    const willRetry = typeof onRetryRef.current === "function" && retryCount < maxRetries;
    const nextRetryDelayMs = willRetry
      ? Math.round(baseBackoffMs * Math.pow(backoffMultiplier, retryCount))
      : null;
    if (nextRetryDelayMs !== null) {
      clearScheduledRetry();
      retryTimerRef.current = window.setTimeout(() => {
        retryTimerRef.current = null;
        // A newer request (manual refresh, poll tick) supersedes the pending retry.
        if (!mountedRef.current || versionRef.current !== token.version) {
          return;
        }
        onRetryRef.current?.({ attempt: failedAttempt + 1 });
      }, nextRetryDelayMs);
    }
    setStatus((current) => ({
      ...current,
      phase: "failed",
      inFlight: false,
      version: token.version,
      message: options.message ?? display.summary,
      error: display.summary,
      settledAt: new Date().toISOString(),
      backoff: {
        attempt: failedAttempt,
        retryCount,
        nextRetryDelayMs,
        maxRetries
      }
    }));
    return true;
  }, [backoffMultiplier, baseBackoffMs, clearScheduledRetry, failureMessage, isCurrentVersion, markStale, maxRetries]);

  // Must not clear the scheduled retry: callers invoke finish() in a finally
  // block right after fail() has armed the retry timer.
  const finish = useCallback((token: RequestLifecycleToken) => {
    if (!isCurrentVersion(token.version)) {
      markStale(token.version);
      return false;
    }

    if (controllerRef.current?.signal === token.signal) {
      controllerRef.current = null;
    }
    inFlightRef.current = false;
    setStatus((current) => ({ ...current, inFlight: false, settledAt: new Date().toISOString() }));
    return true;
  }, [isCurrentVersion, markStale]);

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
      versionRef.current += 1;
      controllerRef.current?.abort();
      controllerRef.current = null;
      inFlightRef.current = false;
      if (retryTimerRef.current !== null) {
        window.clearTimeout(retryTimerRef.current);
        retryTimerRef.current = null;
      }
    };
  }, []);

  return useMemo(() => ({
    status,
    start,
    succeed,
    fail,
    finish,
    abort,
    invalidate,
    isCurrentVersion,
    markStale
  }), [abort, fail, finish, invalidate, isCurrentVersion, markStale, start, status, succeed]);
}
