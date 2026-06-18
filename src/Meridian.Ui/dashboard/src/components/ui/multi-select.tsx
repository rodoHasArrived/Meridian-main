import type { ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { cn } from "@/lib/utils";

export interface MultiSelectAction {
  danger?: boolean;
  icon?: ReactNode;
  id: string;
  label: ReactNode;
  onSelect: (selectedIds: Set<string>) => void;
}

export interface MultiSelectProps extends React.HTMLAttributes<HTMLDivElement> {
  actions?: MultiSelectAction[];
  allIds?: string[];
  onClearSelection?: () => void;
  onSelectionChange?: (selectedIds: Set<string>) => void;
  selectedIds?: Set<string>;
  totalCount?: number;
}

export function MultiSelect({
  actions = [],
  allIds = [],
  className,
  onClearSelection,
  onSelectionChange,
  selectedIds = new Set<string>(),
  totalCount = allIds.length,
  ...props
}: MultiSelectProps) {
  const selectedCount = selectedIds.size;
  const allSelected = totalCount > 0 && selectedCount === totalCount;

  const toggleAll = (checked: boolean) => {
    onSelectionChange?.(checked ? new Set(allIds) : new Set());
    if (!checked) {
      onClearSelection?.();
    }
  };

  return (
    <div
      className={cn(
        "flex flex-wrap items-center gap-3 border-b border-border px-3 py-2 text-sm",
        selectedCount > 0 ? "bg-primary/10" : "bg-transparent",
        className
      )}
      {...props}
    >
      <Checkbox
        checked={allSelected}
        label={selectedCount > 0 ? `${selectedCount} selected` : "Select all"}
        onCheckedChange={toggleAll}
        aria-label={allSelected ? "Clear all selected rows" : "Select all rows"}
      />
      {selectedCount > 0 ? (
        <>
          <div className="h-5 w-px bg-border" aria-hidden="true" />
          <div className="flex flex-wrap items-center gap-2">
            {actions.map((action) => (
              <Button
                key={action.id}
                type="button"
                size="sm"
                variant={action.danger ? "destructive" : "outline"}
                onClick={() => action.onSelect(selectedIds)}
              >
                {action.icon}
                {action.label}
              </Button>
            ))}
          </div>
          <Button type="button" size="sm" variant="ghost" className="ml-auto" onClick={() => {
            onSelectionChange?.(new Set());
            onClearSelection?.();
          }}>
            Clear
          </Button>
        </>
      ) : null}
    </div>
  );
}

export interface SelectCheckboxProps extends Omit<React.InputHTMLAttributes<HTMLInputElement>, "onChange" | "type"> {
  onCheckedChange?: (rowId: string, checked: boolean) => void;
  rowId: string;
}

export function SelectCheckbox({ checked = false, onCheckedChange, rowId, ...props }: SelectCheckboxProps) {
  return (
    <Checkbox
      checked={Boolean(checked)}
      aria-label={`Select row ${rowId}`}
      onCheckedChange={(nextChecked) => onCheckedChange?.(rowId, nextChecked)}
      {...props}
    />
  );
}
