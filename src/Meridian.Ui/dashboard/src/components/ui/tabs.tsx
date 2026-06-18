import { Children, useState, type ReactNode } from "react";
import { cn } from "@/lib/utils";

export interface TabItem {
  count?: ReactNode;
  disabled?: boolean;
  id: string;
  label: ReactNode;
  panelId?: string;
}

export interface TabsProps extends React.HTMLAttributes<HTMLDivElement> {
  defaultValue?: string;
  onValueChange?: (value: string, item: TabItem) => void;
  tabs: TabItem[];
  value?: string;
}

export function Tabs({ children, className, defaultValue, onValueChange, tabs, value, ...props }: TabsProps) {
  const [internalValue, setInternalValue] = useState(defaultValue ?? tabs[0]?.id ?? "");
  const activeValue = value ?? internalValue;
  const panels = Children.toArray(children);

  const selectTab = (item: TabItem) => {
    if (item.disabled) {
      return;
    }

    if (value === undefined) {
      setInternalValue(item.id);
    }

    onValueChange?.(item.id, item);
  };

  return (
    <div className={cn("grid gap-4", className)} {...props}>
      <div className="flex flex-wrap gap-0.5 border-b border-border" role="tablist">
        {tabs.map((tab, index) => {
          const selected = tab.id === activeValue;
          const panelId = tab.panelId ?? `tab-panel-${tab.id}`;
          return (
            <button
              key={tab.id}
              type="button"
              role="tab"
              aria-controls={panelId}
              aria-selected={selected}
              disabled={tab.disabled}
              className={cn(
                "mb-[-1px] inline-flex items-center gap-2 border-b-2 px-3.5 py-2.5 text-sm transition-colors",
                "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                selected ? "border-primary font-semibold text-foreground" : "border-transparent text-muted-foreground hover:text-foreground",
                tab.disabled && "cursor-not-allowed opacity-45"
              )}
              onClick={() => selectTab(tab)}
              tabIndex={selected ? 0 : index === 0 ? 0 : -1}
            >
              <span>{tab.label}</span>
              {tab.count ? <span className="rounded-[var(--radius-chip,0.25rem)] border border-border px-1.5 py-0.5 font-mono text-[10px] text-muted-foreground">{tab.count}</span> : null}
            </button>
          );
        })}
      </div>
      {panels.map((panel, index) => {
        const tab = tabs[index];
        const selected = tab?.id === activeValue;
        return (
          <div key={tab?.id ?? index} id={tab?.panelId ?? `tab-panel-${tab?.id ?? index}`} role="tabpanel" hidden={!selected}>
            {panel}
          </div>
        );
      })}
    </div>
  );
}

export function TabPanel({ children }: { children: ReactNode }) {
  return <>{children}</>;
}
