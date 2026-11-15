import type { ReactNode } from "react";
import { Info } from "lucide-react";

/**
 * A note behind an information icon: method notes, chart scaling rules and other "how this is
 * calculated" text that supports a figure without belonging in the reading path. Opens on click or
 * Enter, closes on Escape and returns focus to the icon.
 */
export function InfoNote({ label, children }: { label: string; children: ReactNode }) {
  return (
    <details className="relative inline-block align-middle" onKeyDown={(event) => {
      if (event.key === "Escape") {
        event.preventDefault();
        event.currentTarget.open = false;
        event.currentTarget.querySelector("summary")?.focus();
      }
    }}>
      <summary aria-label={label} title={label}
        className="flex size-6 cursor-pointer list-none items-center justify-center rounded-sm text-muted-foreground hover:text-foreground focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary [&::-webkit-details-marker]:hidden">
        <Info className="size-3.5" aria-hidden="true" />
      </summary>
      <div className="absolute left-0 top-full z-30 w-80 max-w-[calc(100vw-4rem)] rounded-sm border border-border bg-card p-3 text-xs font-normal normal-case leading-5 tracking-normal text-muted-foreground shadow-md">{children}</div>
    </details>
  );
}
