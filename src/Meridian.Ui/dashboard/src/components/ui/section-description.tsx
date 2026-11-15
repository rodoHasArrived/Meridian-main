import { createContext, useContext, useId, useSyncExternalStore, type ReactNode } from "react";
import { InfoNote } from "@/components/ui/info-note";

const OperatorContext = createContext<string | null>(null);

/** Scope browser-local reading preferences to the loaded session operator. */
export function SectionDescriptionPreferences({ operatorKey, children }: { operatorKey: string | null; children: ReactNode }) {
  return <OperatorContext.Provider value={operatorKey}>{children}</OperatorContext.Provider>;
}

const STORAGE_PREFIX = "meridian.workstation.pageDescriptions.v1";
const listeners = new Set<() => void>();
// Used when storage is blocked or no operator is loaded, so the choice still holds for the visit.
const sessionChoices = new Map<string, boolean>();

const storageKey = (operator: string | null) => `${STORAGE_PREFIX}:${encodeURIComponent(operator ?? "anonymous")}`;

function readHidden(operator: string | null): boolean {
  const key = storageKey(operator);
  if (sessionChoices.has(key)) return sessionChoices.get(key)!;
  try { return operator !== null && localStorage.getItem(key) === "hidden"; } catch { return false; }
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => { listeners.delete(listener); };
}

/**
 * The operator's one setting for page and section descriptions (Settings > Preferences): shown as
 * a line under each heading, or tucked behind an information icon. It replaces the per-section
 * inline "Hide" link, so descriptions look the same on every page and across visits.
 */
export function useSectionDescriptionsHidden(): [hidden: boolean, setHidden: (hidden: boolean) => void] {
  const operator = useContext(OperatorContext);
  const hidden = useSyncExternalStore(subscribe, () => readHidden(operator), () => false);
  const setHidden = (next: boolean) => {
    const key = storageKey(operator);
    sessionChoices.set(key, next);
    try { if (operator !== null) localStorage.setItem(key, next ? "hidden" : "shown"); } catch { /* Storage is optional. */ }
    listeners.forEach((listener) => listener());
  };
  return [hidden, setHidden];
}

/** Test seam: forget choices held for the visit. */
export function resetSectionDescriptionChoices() {
  sessionChoices.clear();
  listeners.forEach((listener) => listener());
}

/**
 * Instructional copy only: scope, warnings and source evidence must stay visible. Rendered as a
 * line of text, or behind an information icon when the operator has turned descriptions off.
 * `descriptionKey` names the section for callers and tests; the preference is not per section.
 */
export function SectionDescription({ label = "About this section", children }: {
  descriptionKey: string;
  label?: string;
  children: ReactNode;
}) {
  const id = useId();
  const [hidden] = useSectionDescriptionsHidden();

  if (!hidden) {
    return <div className="min-w-0 text-xs leading-5 text-muted-foreground" id={id}>{children}</div>;
  }

  return <InfoNote label={label}>{children}</InfoNote>;
}
