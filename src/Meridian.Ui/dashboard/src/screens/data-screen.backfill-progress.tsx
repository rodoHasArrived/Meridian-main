import { cn } from "@/lib/utils";
import type { BackfillLiveProgressState } from "@/screens/data-screen.backfill-progress.view-model";

export function BackfillLiveProgress({ state }: { state: BackfillLiveProgressState }) {
  return (
    <section
      role="status"
      aria-live="polite"
      aria-label={state.ariaLabel}
      className="mt-4 space-y-3 rounded-md border border-primary/25 bg-primary/5 p-3 text-sm"
    >
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <div className="font-semibold text-foreground">{state.title}</div>
          <div className="mt-1 text-xs text-muted-foreground">{state.summary}</div>
        </div>
        <span className="rounded-full border border-border/70 bg-background px-2 py-1 text-xs font-medium text-foreground">
          {state.active ? "Running" : "Settled"}
        </span>
      </div>

      <div
        role="progressbar"
        aria-label="Overall backfill progress"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={Math.round(state.overallPercent)}
        className="h-2 overflow-hidden rounded-full bg-secondary"
      >
        <div
          className="h-full rounded-full bg-primary transition-[width] duration-300"
          style={{ width: `${state.overallPercent}%` }}
        />
      </div>

      {state.symbols.length > 0 ? (
        <div className="grid gap-2" aria-label="Backfill symbol provider progress">
          {state.symbols.map((symbol) => (
            <article key={symbol.symbol} className="rounded-md border border-border/70 bg-background/70 p-2.5">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span className="font-mono font-semibold text-foreground">{symbol.symbol}</span>
                <span className="text-xs font-medium text-foreground">{symbol.progress}</span>
              </div>
              <dl className="mt-2 grid gap-1 text-xs text-muted-foreground sm:grid-cols-2">
                <div><dt className="inline font-medium text-foreground">Range:</dt> <dd className="inline">{symbol.range}</dd></div>
                <div><dt className="inline font-medium text-foreground">Provider:</dt> <dd className="inline">{symbol.provider}</dd></div>
                <div><dt className="inline font-medium text-foreground">Fallback:</dt> <dd className="inline">{symbol.attempt}</dd></div>
                <div><dt className="inline font-medium text-foreground">State:</dt> <dd className="inline">{symbol.status}</dd></div>
              </dl>
              {symbol.error ? <p className="mt-2 text-xs text-danger">{symbol.error}</p> : null}
            </article>
          ))}
        </div>
      ) : (
        <p className="text-xs text-muted-foreground">Waiting for the first provider attempt.</p>
      )}

      {state.recentAttempts.length > 0 ? (
        <div>
          <div className="text-xs font-semibold text-muted-foreground">Recent fallback attempts</div>
          <ul className="mt-2 space-y-1.5">
            {state.recentAttempts.map((attempt) => (
              <li
                key={attempt.id}
                className={cn(
                  "rounded-md border px-2.5 py-2 text-xs",
                  attempt.tone === "danger"
                    ? "border-danger/30 bg-danger/10 text-danger"
                    : attempt.tone === "warning"
                      ? "border-warning/30 bg-warning/10 text-warning"
                      : "border-border/70 bg-background/60 text-muted-foreground"
                )}
              >
                <span className="font-semibold text-foreground">{attempt.label}</span>
                <span> · {attempt.detail}</span>
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      {state.droppedNotificationWarning ? (
        <p role="alert" className="rounded-md border border-warning/30 bg-warning/10 px-2.5 py-2 text-xs text-warning">
          {state.droppedNotificationWarning}
        </p>
      ) : null}
      <p className="text-right text-xs text-muted-foreground">Observed {state.observedAt}</p>
    </section>
  );
}
