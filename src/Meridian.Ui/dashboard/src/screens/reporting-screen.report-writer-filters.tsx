import { Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { isBlankFilterOperator } from "@/screens/reporting-screen.report-writer";
import type { ReportWriterFilterDraft, ReportWriterFilterDraftField } from "@/screens/reporting-screen.report-writer";
import type { ReportingWriterGridRow } from "@/screens/reporting-screen.view-model";

interface ReportWriterExtraFiltersProps {
  grid: ReportingWriterGridRow;
  filters: ReportWriterFilterDraft[];
  onFilterAdd: (grid: ReportingWriterGridRow) => void;
  onFilterRemove: (grid: ReportingWriterGridRow, filterId: string) => void;
  onFilterChange: (grid: ReportingWriterGridRow, filterId: string, field: ReportWriterFilterDraftField, value: string) => void;
}

/** Filter rows after the first, plus the Add filter control, for one report-writer grid. */
export function ReportWriterExtraFilters({ grid, filters, onFilterAdd, onFilterRemove, onFilterChange }: ReportWriterExtraFiltersProps) {
  return (
    <>
      {filters.map((filter, index) => {
        const position = index + 2;
        return (
          <div key={filter.id} className="mt-2 grid gap-2 md:grid-cols-[1fr_0.8fr_1fr_auto]">
            <Select
              value={filter.field}
              onChange={(event) => onFilterChange(grid, filter.id, "field", event.target.value)}
              aria-label={`${grid.title} filter ${position} field`}
            >
              <option value="">Choose a field</option>
              {grid.sourceFields.some((field) => (field.fieldName ?? field.label) === filter.field) || !filter.field ? null : (
                <option value={filter.field}>{filter.field}</option>
              )}
              {grid.sourceFields.map((field) => (
                <option key={field.id} value={field.fieldName ?? field.label}>{field.label}</option>
              ))}
            </Select>
            <Select
              value={filter.operator}
              onChange={(event) => onFilterChange(grid, filter.id, "operator", event.target.value)}
              aria-label={`${grid.title} filter ${position} operator`}
              disabled={!filter.field}
            >
              <option value="Equals">=</option>
              <option value="NotEquals">!=</option>
              <option value="Contains">Contains</option>
              <option value="StartsWith">Starts with</option>
              <option value="EndsWith">Ends with</option>
              <option value="GreaterThan">&gt;</option>
              <option value="GreaterThanOrEqual">&gt;=</option>
              <option value="LessThan">&lt;</option>
              <option value="LessThanOrEqual">&lt;=</option>
              <option value="IsBlank">Is blank</option>
              <option value="IsNotBlank">Is not blank</option>
            </Select>
            <Input
              value={filter.value}
              onChange={(event) => onFilterChange(grid, filter.id, "value", event.target.value)}
              aria-label={`${grid.title} filter ${position} value`}
              className="font-mono"
              disabled={!filter.field || isBlankFilterOperator(filter.operator)}
            />
            <Button
              type="button"
              size="sm"
              variant="outline"
              aria-label={`Remove ${grid.title} filter ${position}`}
              onClick={() => onFilterRemove(grid, filter.id)}
            >
              <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
            </Button>
          </div>
        );
      })}
      <div className="mt-2 flex flex-wrap items-center gap-2">
        <Button
          type="button"
          size="sm"
          variant="outline"
          aria-label={`Add ${grid.title} filter`}
          onClick={() => onFilterAdd(grid)}
        >
          <Plus className="h-3.5 w-3.5" aria-hidden="true" />
          Add filter
        </Button>
        <span className="text-xs text-muted-foreground">A row is kept only when it matches every filter.</span>
      </div>
    </>
  );
}
