import { useEffect, useRef } from "react";

/**
 * Runs `callback` every `intervalMs` while the document is visible. The interval is
 * cleared when the tab is hidden and restarted on return, with one immediate refresh so
 * the screen catches up on what it missed. Pass `null` to disable polling.
 */
export function useVisibleInterval(callback: () => void | Promise<void>, intervalMs: number | null) {
  const callbackRef = useRef(callback);

  useEffect(() => {
    callbackRef.current = callback;
  }, [callback]);

  useEffect(() => {
    if (intervalMs === null || typeof window === "undefined" || typeof document === "undefined") {
      return undefined;
    }

    let timer: number | undefined;
    const start = () => {
      if (timer === undefined) {
        timer = window.setInterval(() => {
          void callbackRef.current();
        }, intervalMs);
      }
    };
    const stop = () => {
      if (timer !== undefined) {
        window.clearInterval(timer);
        timer = undefined;
      }
    };
    const handleVisibilityChange = () => {
      if (document.visibilityState === "hidden") {
        stop();
        return;
      }
      void callbackRef.current();
      start();
    };

    if (document.visibilityState !== "hidden") {
      start();
    }
    document.addEventListener("visibilitychange", handleVisibilityChange);
    return () => {
      document.removeEventListener("visibilitychange", handleVisibilityChange);
      stop();
    };
  }, [intervalMs]);
}
