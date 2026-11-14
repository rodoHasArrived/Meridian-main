// Loads the scope options for the scope picker and the scope bar's names, once per session and
// only when needed: when the picker first opens, or when the scope already names an entity or fund
// the bar should print by name. The read is `/api/workstation/scope-options`, open to every
// operator and filtered to what the caller may see; a host without that route (404) is read through
// the organization graph instead. A failed or refused read leaves the structure unavailable; the
// picker then takes typed ids and says why.
import { useEffect, useRef, useState } from "react";
import {
  collectScopeStructureOptions,
  loadScopeStructure,
  UNAVAILABLE_SCOPE_STRUCTURE,
  type ScopeStructureOptions
} from "@/lib/operating-scope/structure-options";

export interface ScopeStructureState {
  structure: ScopeStructureOptions;
  loading: boolean;
}

export function useScopeStructure(
  needed: boolean,
  load: typeof loadScopeStructure = loadScopeStructure
): ScopeStructureState {
  const [state, setState] = useState<ScopeStructureState>({ structure: UNAVAILABLE_SCOPE_STRUCTURE, loading: false });
  const requestedRef = useRef(false);

  useEffect(() => {
    if (!needed || requestedRef.current) return undefined;
    requestedRef.current = true;
    const controller = new AbortController();
    let settled = false;
    setState((current) => ({ ...current, loading: true }));
    void load({ signal: controller.signal })
      .then((payload) => {
        settled = true;
        if (!controller.signal.aborted) setState({ structure: collectScopeStructureOptions(payload), loading: false });
      })
      .catch(() => {
        settled = true;
        if (controller.signal.aborted) return;
        setState({ structure: UNAVAILABLE_SCOPE_STRUCTURE, loading: false });
      });
    return () => {
      // A read still in flight when the effect is torn down (StrictMode's replay, unmount, or the
      // need going away) is aborted and may run again; a settled one is kept for the session.
      if (!settled) {
        controller.abort();
        requestedRef.current = false;
        setState((current) => ({ ...current, loading: false }));
      }
    };
  }, [needed, load]);

  return state;
}
