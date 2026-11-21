import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useId,
  useMemo,
  useState,
  type ReactNode
} from "react";

export type PageTrustTone = "ready" | "review" | "blocked" | "unknown";

/** The freshness fact a page's posture strip shows, as the status bar repeats it. */
export interface PageFreshness {
  tone: PageTrustTone;
  /** The strip's freshness value when it is plain text ("Stale · 187d ago"); null for richer content. */
  value: string | null;
}

interface PageTrustRegistry {
  report: (id: string, freshness: PageFreshness | null) => void;
}

const PageTrustContext = createContext<PageTrustRegistry | null>(null);

const toneRank: Record<PageTrustTone, number> = { ready: 0, unknown: 1, review: 2, blocked: 3 };

/**
 * Shell-side registry: every posture strip on the current route reports its freshness fact here,
 * and the status bar reads the worst of them. The bar's "Sync" answer and the page's "Freshness"
 * answer therefore come from one check and cannot disagree.
 */
export function usePageTrustRegistry(): { registry: PageTrustRegistry; pageFreshness: PageFreshness | null } {
  const [reports, setReports] = useState<ReadonlyMap<string, PageFreshness>>(() => new Map());

  const report = useCallback((id: string, freshness: PageFreshness | null) => {
    setReports((current) => {
      const existing = current.get(id);
      if (!freshness) {
        if (!existing) {
          return current;
        }
        const next = new Map(current);
        next.delete(id);
        return next;
      }
      if (existing && existing.tone === freshness.tone && existing.value === freshness.value) {
        return current;
      }
      return new Map(current).set(id, freshness);
    });
  }, []);

  const registry = useMemo(() => ({ report }), [report]);
  const pageFreshness = useMemo(() => selectWorstPageFreshness([...reports.values()]), [reports]);
  return { registry, pageFreshness };
}

/** The least-trustworthy freshness fact on the page; null when no strip reported one. */
export function selectWorstPageFreshness(reports: readonly PageFreshness[]): PageFreshness | null {
  let worst: PageFreshness | null = null;
  for (const report of reports) {
    if (!worst || toneRank[report.tone] > toneRank[worst.tone]) {
      worst = report;
    }
  }
  return worst;
}

export function PageTrustProvider({ registry, children }: { registry: PageTrustRegistry; children: ReactNode }) {
  return <PageTrustContext.Provider value={registry}>{children}</PageTrustContext.Provider>;
}

/**
 * Called by the posture strip with the freshness fact it renders. Outside the shell (unit tests,
 * isolated previews) there is no registry and this does nothing.
 */
export function useReportPageFreshness(tone: PageTrustTone, value: string | null): void {
  const registry = useContext(PageTrustContext);
  const id = useId();

  useEffect(() => {
    if (!registry) {
      return undefined;
    }
    registry.report(id, { tone, value });
    return () => registry.report(id, null);
  }, [registry, id, tone, value]);
}
