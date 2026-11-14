import { createContext, useContext, useEffect, useId, useRef, useState, type ReactNode } from "react";
import { Info } from "lucide-react";

const OperatorContext = createContext<string | null>(null);

/** Scope browser-local reading preferences to the loaded session operator. */
export function SectionDescriptionPreferences({ operatorKey, children }: { operatorKey: string | null; children: ReactNode }) {
  return <OperatorContext.Provider value={operatorKey}>{children}</OperatorContext.Provider>;
}

/** Instructional copy only: scope, warnings and source evidence must stay visible. */
export function SectionDescription({ descriptionKey, label = "About this section", children }: {
  descriptionKey: string;
  label?: string;
  children: ReactNode;
}) {
  const operator = useContext(OperatorContext);
  const storageKey = operator ? `meridian:section-description:v1:${encodeURIComponent(operator)}:${encodeURIComponent(descriptionKey)}` : null;
  // Remount on operator/section changes so one person's preference cannot hide another's help.
  return <Description key={storageKey ?? descriptionKey} storageKey={storageKey} label={label}>{children}</Description>;
}

function Description({ storageKey, label, children }: { storageKey: string | null; label: string; children: ReactNode }) {
  const id = useId();
  const descriptionRef = useRef<HTMLDivElement>(null);
  const infoRef = useRef<HTMLElement>(null);
  const restoreFocus = useRef(false);
  const [seen, setSeen] = useState(() => {
    try { return storageKey !== null && localStorage.getItem(storageKey) === "seen"; } catch { return false; }
  });
  useEffect(() => {
    // Keep copy visible for this visit. Only remember descriptions actually in view,
    // so mounting help below the fold or inside a closed disclosure does not mark it read.
    if (seen || !storageKey || !descriptionRef.current) return;
    const record = () => {
      try { localStorage.setItem(storageKey, "seen"); } catch { /* Storage is optional. */ }
    };
    if (typeof IntersectionObserver === "undefined") { record(); return; }
    const observer = new IntersectionObserver((entries) => {
      if (entries.some((entry) => entry.isIntersecting && entry.intersectionRatio >= 0.5)) {
        record();
        observer.disconnect();
      }
    }, { threshold: 0.5 });
    observer.observe(descriptionRef.current);
    return () => observer.disconnect();
  }, [seen, storageKey]);
  const remember = () => {
    restoreFocus.current = true;
    setSeen(true);
    try { if (storageKey) localStorage.setItem(storageKey, "seen"); } catch { /* Help remains usable without storage. */ }
  };
  useEffect(() => {
    if (seen && restoreFocus.current) {
      infoRef.current?.focus();
      restoreFocus.current = false;
    }
  }, [seen]);

  if (!seen) {
    return (
      <div ref={descriptionRef} className="flex min-w-0 items-baseline gap-2 text-xs leading-5 text-muted-foreground">
        <div className="min-w-0" id={id}>{children}</div>
        <button type="button" onClick={remember} aria-label={`Hide description: ${label}`} aria-describedby={id}
          className="shrink-0 rounded-sm px-1 underline underline-offset-2 hover:text-foreground focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary">
          Hide
        </button>
      </div>
    );
  }

  return (
    <details className="relative inline-block align-middle" onKeyDown={(event) => {
      if (event.key === "Escape") {
        event.preventDefault();
        event.currentTarget.open = false;
        event.currentTarget.querySelector("summary")?.focus();
      }
    }}>
      <summary ref={infoRef} aria-label={label} title={label}
        className="flex size-6 cursor-pointer list-none items-center justify-center rounded-sm text-muted-foreground hover:text-foreground focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary [&::-webkit-details-marker]:hidden">
        <Info className="size-3.5" aria-hidden="true" />
      </summary>
      <div className="absolute left-0 top-full z-30 w-80 max-w-[calc(100vw-4rem)] rounded-sm border border-border bg-card p-3 text-xs leading-5 text-muted-foreground shadow-md">{children}</div>
    </details>
  );
}
