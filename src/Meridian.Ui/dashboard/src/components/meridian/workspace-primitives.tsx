import { Search } from "lucide-react";
import type { ReactNode } from "react";
import { cn } from "@/lib/utils";

export interface WorkspaceFilterBarOption {
  id: string;
  label: string;
  active?: boolean;
  count?: string;
}

export interface WorkspaceFilterBarField {
  id: string;
  label: string;
  value: string;
}

export function WorkspaceFilterBar({
  label,
  searchLabel = "Search",
  searchValue,
  options = [],
  fields = [],
  actions,
  className
}: {
  label: string;
  searchLabel?: string;
  searchValue?: string;
  options?: WorkspaceFilterBarOption[];
  fields?: WorkspaceFilterBarField[];
  actions?: ReactNode;
  className?: string;
}) {
  return (
    <section className={cn("workspace-filter-bar", className)} aria-label={label}>
      <div className="workspace-filter-search" role="search" aria-label={searchLabel}>
        <Search className="h-3.5 w-3.5" aria-hidden="true" />
        <span>{searchValue ?? searchLabel}</span>
      </div>
      {options.length > 0 ? (
        <div className="workspace-filter-segments" role="list" aria-label={`${label} filters`}>
          {options.map((option) => (
            <span
              key={option.id}
              role="listitem"
              className={cn("workspace-filter-segment", option.active && "active")}
              aria-current={option.active ? "true" : undefined}
            >
              <span>{option.label}</span>
              {option.count ? <b>{option.count}</b> : null}
            </span>
          ))}
        </div>
      ) : null}
      {fields.length > 0 ? (
        <dl className="workspace-filter-fields" aria-label={`${label} active fields`}>
          {fields.map((field) => (
            <div key={field.id}>
              <dt>{field.label}</dt>
              <dd>{field.value}</dd>
            </div>
          ))}
        </dl>
      ) : null}
      {actions ? <div className="workspace-filter-actions">{actions}</div> : null}
    </section>
  );
}

export interface WorkspaceTabStripItem {
  id: string;
  label: string;
  selected?: boolean;
  panelId?: string;
  href?: string;
}

export function WorkspaceTabStrip({
  label,
  tabs,
  onSelect,
  className
}: {
  label: string;
  tabs: WorkspaceTabStripItem[];
  onSelect?: (id: string) => void;
  className?: string;
}) {
  return (
    <div className={cn("workspace-tab-strip", className)} role="tablist" aria-label={label}>
      {tabs.map((tab) => {
        const selected = Boolean(tab.selected);
        const className = cn("workspace-tab", selected && "active");
        const content = <span>{tab.label}</span>;

        return tab.href ? (
          <a
            key={tab.id}
            href={tab.href}
            className={className}
            role="tab"
            aria-selected={selected}
            aria-controls={tab.panelId}
          >
            {content}
          </a>
        ) : (
          <button
            key={tab.id}
            type="button"
            className={className}
            role="tab"
            aria-selected={selected}
            aria-controls={tab.panelId}
            onClick={() => onSelect?.(tab.id)}
          >
            {content}
          </button>
        );
      })}
    </div>
  );
}

export function WorkspaceInspectorHost({
  id,
  label,
  title,
  subtitle,
  status,
  children,
  className
}: {
  id?: string;
  label: string;
  title?: string;
  subtitle?: string;
  status?: ReactNode;
  children: ReactNode;
  className?: string;
}) {
  return (
    <aside id={id} role="region" className={cn("workspace-inspector-host", className)} aria-label={label}>
      {(title || subtitle || status) ? (
        <div className="workspace-inspector-head">
          <div className="min-w-0">
            {title ? <h2>{title}</h2> : null}
            {subtitle ? <p>{subtitle}</p> : null}
          </div>
          {status ? <div className="workspace-inspector-status">{status}</div> : null}
        </div>
      ) : null}
      <div className="workspace-inspector-body">{children}</div>
    </aside>
  );
}

export function WorkspaceDocumentCanvas({
  label,
  title,
  children,
  toolbar,
  className
}: {
  label: string;
  title?: string;
  children: ReactNode;
  toolbar?: ReactNode;
  className?: string;
}) {
  return (
    <section className={cn("workspace-document-canvas", className)} aria-label={label}>
      {(title || toolbar) ? (
        <div className="workspace-document-toolbar">
          {title ? <h2>{title}</h2> : <span />}
          {toolbar ? <div>{toolbar}</div> : null}
        </div>
      ) : null}
      <div className="workspace-document-page">{children}</div>
    </section>
  );
}
