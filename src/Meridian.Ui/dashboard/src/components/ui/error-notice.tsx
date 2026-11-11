import type { ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { presentApiError } from "@/lib/api-errors";
import { cn } from "@/lib/utils";

export interface ErrorNoticeProps {
  /** The rejected request's reason, as caught. Strings are shown as the summary. */
  error: unknown;
  /** What could not be done, in the operator's words: "Recurring journals could not be loaded." */
  title: ReactNode;
  /** Used when the service gave no reason of its own. */
  fallback?: string;
  onRetry?: () => void;
  retryLabel?: string;
  retryBusy?: boolean;
  className?: string;
}

/**
 * The one way a failed request is shown: what did not happen, the reason in plain words, Retry
 * when repeating the request can help, and the request facts collapsed under "Technical details"
 * for support. Request paths and status codes never sit in the default reading path.
 */
export function ErrorNotice({
  error,
  title,
  fallback = "Try again, or open diagnostics if it keeps happening.",
  onRetry,
  retryLabel = "Retry",
  retryBusy = false,
  className
}: ErrorNoticeProps) {
  const presentation = presentApiError(typeof error === "string" ? new Error(error) : error, fallback);
  // A rejected request (validation, permission, conflict) fails the same way on a second try, so
  // the action is offered only when repeating it can succeed.
  const showRetry = Boolean(onRetry) && presentation.retryable;

  return (
    <div
      role="alert"
      data-ui-component="ErrorNotice"
      className={cn(
        "min-w-0 rounded-[2px] border border-border border-l-[3px] border-l-danger bg-danger/10 px-3.5 py-3 text-sm",
        className
      )}
    >
      <p className="font-semibold text-foreground">{title}</p>
      <p className="mt-0.5 break-words leading-5 text-foreground">{presentation.summary}</p>
      {presentation.details.length > 0 ? (
        <ul className="mt-1 list-disc space-y-0.5 pl-4 text-xs leading-5 text-muted-foreground">
          {presentation.details.map((detail) => <li key={detail} className="break-words">{detail}</li>)}
        </ul>
      ) : null}
      {showRetry ? (
        <Button
          type="button"
          size="sm"
          variant="outline"
          className="mt-2"
          busy={retryBusy}
          busyLabel="Retrying…"
          onClick={onRetry}
        >
          {retryLabel}
        </Button>
      ) : null}
      {presentation.technicalDetails.length > 0 ? (
        <TechnicalDetails label="Technical details" className="mt-2" contentClassName="py-2">
          <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-3 gap-y-1 text-xs">
            {presentation.technicalDetails.map((item) => (
              <div key={item.label} className="contents">
                <dt className="text-muted-foreground">{item.label}</dt>
                <dd className="min-w-0 break-all font-mono text-foreground">{item.value}</dd>
              </div>
            ))}
          </dl>
        </TechnicalDetails>
      ) : null}
    </div>
  );
}
