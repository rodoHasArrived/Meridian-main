import { useRef } from "react";
import { Plus, XCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { buildSourceFieldTokens } from "@/screens/reporting-screen.view-model";
import type { ReportingTemplateRow, ReportingWriterGridRow } from "@/screens/reporting-screen.view-model";
import type { ReportWriterDatasetSource, ReportingTemplateGridFieldMetadata } from "@/types";

const newCustomReportDefaultFields: ReportingTemplateGridFieldMetadata[] = [
  { name: "sector", label: "Sector", role: "Dimension", dataType: "string", dataset: "Preview fixture" },
  { name: "strategy", label: "Strategy", role: "Dimension", dataType: "string", dataset: "Preview fixture" },
  { name: "security", label: "Security", role: "Dimension", dataType: "string", dataset: "Preview fixture" },
  { name: "marketValue", label: "Market value", role: "Metric", dataType: "decimal", dataset: "Preview fixture" },
  { name: "pnl", label: "P&L", role: "Metric", dataType: "decimal", dataset: "Preview fixture" }
];

/**
 * Builds a blank report-writer grid so an operator can start a custom report in the browser.
 * Nothing is persisted until the operator saves it as a template draft. The field palette comes
 * from the first retained dataset source when one exists, otherwise from the preview fixture fields.
 */
export function buildNewCustomReportWriterGrid(
  ordinal: number,
  fields: ReportingTemplateGridFieldMetadata[] | null | undefined,
  previewHost: { templateId: string; templateVersion: string } | null
): ReportingWriterGridRow {
  const templateId = `custom-report-${ordinal}`;
  const title = `Custom report ${ordinal}`;
  const gridId = "grid-1";
  const sourceFields = buildSourceFieldTokens(
    {
      gridId,
      title,
      kind: "Pivot",
      dimensionCount: 0,
      metricCount: 0,
      formulaCount: 0,
      sourceFields: fields && fields.length > 0 ? fields : newCustomReportDefaultFields
    },
    [],
    [],
    [],
    []
  );

  return {
    id: `new:${templateId}:${gridId}`,
    gridId,
    templateId,
    templateVersion: "",
    family: "CustomReport",
    title,
    kind: "Pivot",
    topN: null,
    sortBy: null,
    sortDescending: true,
    summary: "New custom report. Drag fields into Rows, Columns and Metrics, then save it as a draft for review.",
    sourceFields,
    rowFields: [],
    columnFields: [],
    metrics: [],
    formulas: [],
    filters: [],
    topNLabel: "Pivot",
    sortLabel: "Default order",
    filterSummary: "No saved filters",
    ariaLabel: `${title} new report-writer grid`,
    isNew: true,
    previewHost
  };
}

interface NewCustomReportBarProps {
  templates: ReportingTemplateRow[];
  datasetSources: ReportWriterDatasetSource[];
  grids: ReportingWriterGridRow[];
  onGridsChange: (grids: ReportingWriterGridRow[]) => void;
}

/** Report Builder entry point for starting (and discarding) blank, unsaved custom report grids. */
export function NewCustomReportBar({ templates, datasetSources, grids, onGridsChange }: NewCustomReportBarProps) {
  const ordinal = useRef(0);

  function addGrid() {
    // A saved draft with the same name would be versioned onto that template, so skip any
    // ordinal whose name a retained template or another unsaved grid already uses.
    const usedNames = new Set([
      ...templates.map((template) => template.templateName.toLowerCase()),
      ...grids.map((grid) => grid.templateId.toLowerCase())
    ]);
    do {
      ordinal.current += 1;
    } while (usedNames.has(`custom-report-${ordinal.current}`));
    // Preview renders against an approved, accessible template; built-ins always qualify.
    const host = templates.find((template) => template.sourceLabel === "Built-in" && template.isAccessible)
      ?? templates.find((template) => template.canRunOnDemand)
      ?? null;
    const grid = buildNewCustomReportWriterGrid(
      ordinal.current,
      datasetSources[0]?.fields,
      host ? { templateId: host.templateName, templateVersion: host.version } : null
    );
    onGridsChange([grid, ...grids]);
  }

  return (
    <section
      aria-label="Start a custom report"
      className="flex flex-wrap items-center justify-between gap-3 rounded-md border border-border/70 bg-secondary/20 px-3 py-3"
    >
      <p className="min-w-0 text-xs leading-5 text-muted-foreground">
        Start a blank report-writer grid, lay it out, then save it as a draft for controller review.
      </p>
      <span className="flex flex-wrap items-center gap-2">
        {grids.length > 0 ? (
          <Button type="button" size="sm" variant="ghost" onClick={() => onGridsChange([])}>
            <XCircle className="h-4 w-4" aria-hidden="true" />
            Discard new reports
          </Button>
        ) : null}
        <Button type="button" size="sm" variant="outline" onClick={addGrid}>
          <Plus className="h-4 w-4" aria-hidden="true" />
          New custom report
        </Button>
      </span>
    </section>
  );
}
