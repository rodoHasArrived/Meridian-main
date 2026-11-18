import { Link, useLocation } from "react-router-dom";
import type { AppShellOperatingScopeInput } from "@/app-shell.operating-scope";
import { buildAccountingSectionNav } from "@/components/meridian/workspace-nav.view-model";
import { cn } from "@/lib/utils";

/**
 * The menu an Accounting page shows for its own section (UX Phase D). The rail lists the five
 * Accounting groups; this lists the pages of the group the current page belongs to, so the other
 * workspaces stay one click away in the rail while every Accounting page is still one click away.
 */
export function WorkspaceSectionNav({ operatingContextScope = null }: { operatingContextScope?: AppShellOperatingScopeInput | null }) {
  const location = useLocation();
  const section = buildAccountingSectionNav(location.pathname, location.search, operatingContextScope);
  if (!section || section.items.length < 2) return null;
  return (
    <nav aria-label={`${section.groupLabel} pages`} className="workspace-section-nav mb-4 flex flex-wrap items-center gap-1 border-b border-border pb-2" data-section-nav={section.groupId}>
      <span className="mr-2 text-xs font-semibold text-muted-foreground">{section.groupLabel}</span>
      {section.items.map((item) => (
        <Link
          key={item.route}
          to={item.route}
          aria-current={item.ariaCurrent}
          aria-label={item.ariaLabel}
          className={cn(
            "rounded-sm px-2.5 py-1 text-sm text-muted-foreground hover:bg-secondary/60 hover:text-foreground focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-[var(--interaction-focus)]",
            item.active && "bg-secondary font-medium text-foreground"
          )}
        >
          {item.label}
        </Link>
      ))}
    </nav>
  );
}
