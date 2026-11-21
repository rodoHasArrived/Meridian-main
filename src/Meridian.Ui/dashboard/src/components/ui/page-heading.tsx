import type { ReactNode } from "react";
import { SectionDescription } from "@/components/ui/section-description";

/**
 * The one page heading for workspace pages: the page's name as it appears in the sidebar, then an
 * optional one-line purpose on its own line. Every overview uses it, so "where am I?" reads the
 * same way on every page. Whether the purpose line shows is the operator's setting
 * (Settings > Preferences), not a control on the heading.
 */
export function PageHeading({ title, purpose, descriptionKey, actions, id }: {
  title: string;
  /** One sentence in the operator's terms: what the page is for. */
  purpose?: ReactNode;
  /** Names the page for the description; defaults to the title. */
  descriptionKey?: string;
  /** Page-level actions, right-aligned beside the title. */
  actions?: ReactNode;
  id?: string;
}) {
  return (
    <div className="flex min-w-0 flex-wrap items-start justify-between gap-x-3 gap-y-2" data-page-heading>
      <div className="min-w-0">
        <h2 id={id} className="font-display text-lg font-semibold leading-tight text-foreground">{title}</h2>
        {purpose ? (
          <div className="mt-1">
            <SectionDescription descriptionKey={descriptionKey ?? title} label={`About ${title}`}>{purpose}</SectionDescription>
          </div>
        ) : null}
      </div>
      {actions ? <div className="flex shrink-0 flex-wrap items-center gap-2">{actions}</div> : null}
    </div>
  );
}
