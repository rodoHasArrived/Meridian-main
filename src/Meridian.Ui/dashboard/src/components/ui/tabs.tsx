import { Children, useState, type KeyboardEvent, type ReactNode } from "react";
import { cn } from "@/lib/utils";

export interface TabItem {
  ariaLabel?: string;
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
  const selectedTabIndex = tabs.findIndex((tab) => tab.id === activeValue && !tab.disabled);
  const firstEnabledTabIndex = tabs.findIndex((tab) => !tab.disabled);
  const focusableTabIndex = selectedTabIndex >= 0 ? selectedTabIndex : firstEnabledTabIndex;

  const selectTab = (item: TabItem) => {
    if (item.disabled) {
      return;
    }

    if (value === undefined) {
      setInternalValue(item.id);
    }

    onValueChange?.(item.id, item);
  };

  const handleTabKeyDown = (event: KeyboardEvent<HTMLButtonElement>, item: TabItem) => {
    const targetItem = resolveKeyboardTabTarget(event, tabs, item.id);
    if (!targetItem) {
      return;
    }

    event.preventDefault();
    selectTab(targetItem);
    const tabList = event.currentTarget.closest("[role='tablist']");
    const targetButton = tabList?.querySelector<HTMLButtonElement>(`[data-tab-id="${escapeTabSelectorValue(targetItem.id)}"]`);
    targetButton?.focus();
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
              aria-label={tab.ariaLabel}
              aria-selected={selected}
              disabled={tab.disabled}
              className={cn(
                "mb-[-1px] inline-flex items-center gap-2 border-b-2 px-3.5 py-2.5 text-sm transition-colors",
                "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                selected ? "border-primary font-semibold text-foreground" : "border-transparent text-muted-foreground hover:text-foreground",
                tab.disabled && "cursor-not-allowed opacity-45"
              )}
              onClick={() => selectTab(tab)}
              onKeyDown={(event) => handleTabKeyDown(event, tab)}
              tabIndex={index === focusableTabIndex ? 0 : -1}
              data-tab-id={tab.id}
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

function resolveKeyboardTabTarget(
  event: KeyboardEvent<HTMLButtonElement>,
  tabs: TabItem[],
  activeId: string
): TabItem | null {
  const enabledTabs = tabs.filter((tab) => !tab.disabled);
  if (enabledTabs.length === 0) {
    return null;
  }

  const activeIndex = enabledTabs.findIndex((tab) => tab.id === activeId);
  if (activeIndex < 0) {
    return enabledTabs[0] ?? null;
  }

  switch (event.key) {
    case "ArrowRight":
    case "ArrowDown":
      return enabledTabs[(activeIndex + 1) % enabledTabs.length] ?? null;
    case "ArrowLeft":
    case "ArrowUp":
      return enabledTabs[(activeIndex - 1 + enabledTabs.length) % enabledTabs.length] ?? null;
    case "Home":
      return enabledTabs[0] ?? null;
    case "End":
      return enabledTabs[enabledTabs.length - 1] ?? null;
    default:
      return null;
  }
}

function escapeTabSelectorValue(value: string): string {
  return value.replace(/\\/g, "\\\\").replace(/"/g, "\\\"");
}
