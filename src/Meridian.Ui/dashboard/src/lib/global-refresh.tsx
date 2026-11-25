import { createContext, useContext, useEffect, useRef, type ReactNode } from "react";

const GlobalRefreshContext = createContext(0);

/**
 * Carries the header Refresh button to data a page loads for itself. The shell bumps `sequence`
 * each time the operator presses Refresh; panels that own a separate request reload through
 * {@link useOnGlobalRefresh} instead of showing a refresh button of their own. A panel shows a
 * local "Retry" only inside its error state.
 */
export function GlobalRefreshProvider({ sequence, children }: { sequence: number; children: ReactNode }) {
  return <GlobalRefreshContext.Provider value={sequence}>{children}</GlobalRefreshContext.Provider>;
}

/** Runs `reload` each time the header Refresh is pressed (never on mount). */
export function useOnGlobalRefresh(reload: () => unknown): void {
  const sequence = useContext(GlobalRefreshContext);
  const latest = useRef(reload);
  const seen = useRef(sequence);
  useEffect(() => {
    latest.current = reload;
  });
  useEffect(() => {
    if (seen.current !== sequence) {
      seen.current = sequence;
      void latest.current();
    }
  }, [sequence]);
}
