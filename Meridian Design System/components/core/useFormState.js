// Meridian form-state hooks — the hard parts of forms as primitives, not per-screen re-inventions.
//   useDirtyState  — snapshot a baseline, track which fields changed, reset/commit.
//   useAsyncValidation — debounced async field checks (symbol exists? name unique?) with status.
//   useWizard — multi-step flow state: step, next/back/goTo, furthest-reached, per-step validity.
import React from "react";

// ── useDirtyState ────────────────────────────────────────────────────────────
// const f = useDirtyState({ name: "", tz: "UTC" });
// f.values, f.set("name", v), f.dirty (bool), f.dirtyFields (Set), f.isDirty("name"),
// f.reset() (back to baseline), f.commit() (baseline := current).
export function useDirtyState(initial) {
  const [baseline, setBaseline] = React.useState(initial);
  const [values, setValues] = React.useState(initial);
  const set = React.useCallback((key, value) => {
    setValues((v) => (typeof key === "object" ? { ...v, ...key } : { ...v, [key]: value }));
  }, []);
  const dirtyFields = React.useMemo(() => {
    const s = new Set();
    for (const k of Object.keys({ ...baseline, ...values })) {
      if (!Object.is(baseline[k], values[k])) s.add(k);
    }
    return s;
  }, [baseline, values]);
  return {
    values, set, setValues,
    dirty: dirtyFields.size > 0,
    dirtyFields,
    isDirty: (k) => dirtyFields.has(k),
    reset: () => setValues(baseline),
    commit: () => setBaseline(values),
    baseline,
  };
}

// ── useAsyncValidation ───────────────────────────────────────────────────────
// const v = useAsyncValidation(symbol, async (s, signal) => {
//   const ok = await api.symbolExists(s, { signal }); return ok ? null : "Unknown symbol";
// }, { debounceMs: 300, skipEmpty: true });
// v.status: "idle" | "checking" | "valid" | "invalid" ; v.error: string|null
export function useAsyncValidation(value, validator, { debounceMs = 300, skipEmpty = true } = {}) {
  const [status, setStatus] = React.useState("idle");
  const [error, setError] = React.useState(null);
  const reqRef = React.useRef(0);
  React.useEffect(() => {
    if (skipEmpty && (value == null || value === "")) { setStatus("idle"); setError(null); return; }
    const reqId = ++reqRef.current;
    const controller = typeof AbortController !== "undefined" ? new AbortController() : { abort() {}, signal: undefined };
    setStatus("checking");
    const t = setTimeout(async () => {
      try {
        const result = await validator(value, controller.signal);
        if (reqId !== reqRef.current) return;
        setError(result || null);
        setStatus(result ? "invalid" : "valid");
      } catch (e) {
        if (e && e.name === "AbortError") return;
        if (reqId !== reqRef.current) return;
        setError(e && e.message ? e.message : "Validation failed");
        setStatus("invalid");
      }
    }, debounceMs);
    return () => { controller.abort(); clearTimeout(t); };
  }, [value, debounceMs, skipEmpty]);
  return { status, error, checking: status === "checking", valid: status === "valid", invalid: status === "invalid" };
}

// ── useWizard ────────────────────────────────────────────────────────────────
// const w = useWizard(["Scope", "Rules", "Review"], { validate: (i, state) => ... });
// w.step, w.stepName, w.next(), w.back(), w.goTo(i), w.isFirst, w.isLast, w.furthest,
// w.canAdvance (validate(step) !== false), w.progress (0..1).
export function useWizard(steps, { validate = () => true, onComplete } = {}) {
  const [step, setStep] = React.useState(0);
  const [furthest, setFurthest] = React.useState(0);
  const last = steps.length - 1;
  const canAdvance = validate(step) !== false;
  const goTo = React.useCallback((i) => {
    const clamped = Math.max(0, Math.min(last, i));
    setStep(clamped);
    setFurthest((f) => Math.max(f, clamped));
  }, [last]);
  const next = React.useCallback(() => {
    if (validate(step) === false) return;
    if (step === last) { onComplete && onComplete(); return; }
    goTo(step + 1);
  }, [step, last, validate, goTo, onComplete]);
  const back = React.useCallback(() => goTo(step - 1), [step, goTo]);
  return {
    step, stepName: steps[step], steps,
    next, back, goTo,
    isFirst: step === 0, isLast: step === last,
    furthest, canAdvance,
    // A step is reachable if it's been reached before (no skipping ahead over invalid steps).
    isReachable: (i) => i <= furthest,
    progress: last === 0 ? 1 : step / last,
  };
}

// Capitalized carrier so these hooks surface on window.<Namespace> (the compiler only exposes
// capital-initial exports; React hooks must stay lowercase). Consume as:
//   const { useDirtyState, useAsyncValidation, useWizard } =
//     window.MeridianDesignSystem_4f61be.FormHooks;
export const FormHooks = { useDirtyState, useAsyncValidation, useWizard };
