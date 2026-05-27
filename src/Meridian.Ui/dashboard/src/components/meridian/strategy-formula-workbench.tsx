import { useMemo, useState } from "react";
import { BookOpen, Braces, FunctionSquare, LayoutGrid, PlayCircle, Search, Sigma } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";

export type StrategyFormulaWorkbenchMode = "visual" | "formula" | "code";
export type StrategyFormulaFieldType = "decimal" | "string" | "date" | "percent";
export type StrategyFormulaSuggestionTone = "calculation" | "filter" | "risk" | "allocation";

export interface StrategyFormulaField {
  fieldId: string;
  label: string;
  category: string;
  typeName: StrategyFormulaFieldType;
  description: string;
  example: string;
}

export interface StrategyFormulaSuggestion {
  suggestionId: string;
  expression: string;
  description: string;
  tone: StrategyFormulaSuggestionTone;
  triggers: string[];
}

export interface StrategyFormulaCell {
  cellId: string;
  label: string;
  mode: StrategyFormulaWorkbenchMode;
  source: string;
  status: "Ready" | "Review" | "Blocked";
}

export interface StrategyFormulaWorkbenchProps {
  title: string;
  description: string;
  fields: StrategyFormulaField[];
  suggestions: StrategyFormulaSuggestion[];
  initialCells: StrategyFormulaCell[];
}

const DEFAULT_SOURCE = "Let score = MOMENTUM_63D - VOLATILITY_20D\nIf score > 0 Then\n  Result = score\nEnd If";

export function StrategyFormulaWorkbench({
  title,
  description,
  fields,
  suggestions,
  initialCells
}: StrategyFormulaWorkbenchProps) {
  const [mode, setMode] = useState<StrategyFormulaWorkbenchMode>("formula");
  const [query, setQuery] = useState("");
  const [source, setSource] = useState(DEFAULT_SOURCE);
  const [selectedCellId, setSelectedCellId] = useState(initialCells[0]?.cellId ?? null);

  const visibleFields = useMemo(() => filterFields(fields, query), [fields, query]);
  const visibleSuggestions = useMemo(() => filterSuggestions(suggestions, source), [source, suggestions]);
  const selectedCell = initialCells.find((cell) => cell.cellId === selectedCellId) ?? initialCells[0] ?? null;
  const fieldTokens = useMemo(() => extractFieldTokens(source, fields), [fields, source]);
  const compiledLines = useMemo(() => buildCompiledPreview(mode, source, fieldTokens), [fieldTokens, mode, source]);

  const insertText = (value: string) => {
    const prefix = source.endsWith(" ") || source.endsWith("\n") ? "" : " ";
    setSource(`${source}${prefix}${value}`);
  };

  return (
    <section className="space-y-4" aria-labelledby="strategy-formula-workbench-title" data-testid="strategy-formula-workbench">
      <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
        <div className="min-w-0">
          <div className="eyebrow-label">Design system component</div>
          <h1 id="strategy-formula-workbench-title" className="flex items-center gap-2 text-2xl font-semibold tracking-normal text-foreground">
            <FunctionSquare className="h-6 w-6 text-primary" aria-hidden="true" />
            {title}
          </h1>
          <p className="mt-2 max-w-3xl text-sm leading-6 text-muted-foreground">{description}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2" role="group" aria-label="Strategy authoring mode">
          {(["visual", "formula", "code"] as const).map((candidate) => (
            <Button
              key={candidate}
              type="button"
              variant={mode === candidate ? "secondary" : "outline"}
              size="sm"
              aria-pressed={mode === candidate}
              onClick={() => setMode(candidate)}
            >
              {modeIcon(candidate)}
              <span className="ml-1.5">{modeLabel(candidate)}</span>
            </Button>
          ))}
        </div>
      </div>

      <div className="grid gap-4 xl:grid-cols-[300px_minmax(0,1fr)_320px]">
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <BookOpen className="h-4 w-4 text-primary" aria-hidden="true" />
              Field picker
            </CardTitle>
            <CardDescription>Search Meridian fields and insert tokens into the active cell.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            <label className="relative block">
              <span className="sr-only">Search strategy fields</span>
              <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" aria-hidden="true" />
              <Input
                value={query}
                onChange={(event) => setQuery(event.target.value)}
                className="pl-9"
                placeholder="Search fields"
                aria-label="Search strategy fields"
              />
            </label>
            <div className="space-y-2" role="list" aria-label="Strategy formula fields">
              {visibleFields.map((field) => (
                <button
                  key={field.fieldId}
                  type="button"
                  className="w-full rounded-md border border-border/70 bg-secondary/20 px-3 py-2 text-left transition-colors hover:border-primary/50 hover:bg-primary/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                  onClick={() => insertText(field.fieldId)}
                  aria-label={`Insert ${field.fieldId}`}
                >
                  <span className="flex flex-wrap items-center gap-2">
                    <span className="font-mono text-xs font-semibold text-foreground">{field.fieldId}</span>
                    <Badge variant={fieldTypeVariant(field.typeName)}>{field.typeName}</Badge>
                  </span>
                  <span className="mt-1 block text-xs font-medium text-foreground">{field.label}</span>
                  <span className="mt-1 block text-xs leading-5 text-muted-foreground">{field.description}</span>
                </button>
              ))}
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <Sigma className="h-4 w-4 text-primary" aria-hidden="true" />
              Active cell
            </CardTitle>
            <CardDescription>{selectedCell ? `${selectedCell.label} · ${selectedCell.status}` : "No selected cell"}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            <textarea
              value={source}
              onChange={(event) => setSource(event.target.value)}
              className="min-h-52 w-full resize-y rounded-md border border-border bg-background px-3 py-2 font-mono text-sm leading-6 text-foreground shadow-[var(--shadow-panel)] outline-none transition-colors placeholder:text-muted-foreground focus:border-primary/70 focus:ring-2 focus:ring-primary/35"
              aria-label="Strategy formula source"
              spellCheck={false}
            />
            <div className="grid gap-2 md:grid-cols-2" role="list" aria-label="Formula suggestions">
              {visibleSuggestions.map((suggestion) => (
                <button
                  key={suggestion.suggestionId}
                  type="button"
                  className="rounded-md border border-border/70 bg-secondary/20 p-3 text-left transition-colors hover:border-primary/50 hover:bg-primary/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
                  onClick={() => insertText(suggestion.expression)}
                  aria-label={`Insert formula suggestion ${suggestion.expression}`}
                >
                  <span className="flex items-center gap-2">
                    <Badge variant={suggestionToneVariant(suggestion.tone)}>{suggestion.tone}</Badge>
                    <code className="min-w-0 break-words text-xs text-foreground">{suggestion.expression}</code>
                  </span>
                  <span className="mt-2 block text-xs leading-5 text-muted-foreground">{suggestion.description}</span>
                </button>
              ))}
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <Braces className="h-4 w-4 text-primary" aria-hidden="true" />
              Run preview
            </CardTitle>
            <CardDescription>Local compile preview for the selected authoring mode.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-2" role="list" aria-label="Strategy cells">
              {initialCells.map((cell) => (
                <button
                  key={cell.cellId}
                  type="button"
                  aria-pressed={cell.cellId === selectedCellId}
                  onClick={() => setSelectedCellId(cell.cellId)}
                  className={cn(
                    "w-full rounded-md border px-3 py-2 text-left transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                    cell.cellId === selectedCellId
                      ? "border-primary/60 bg-primary/10"
                      : "border-border/70 bg-secondary/20 hover:border-primary/50"
                  )}
                >
                  <span className="flex items-center justify-between gap-2">
                    <span className="font-medium text-foreground">{cell.label}</span>
                    <Badge variant={cell.status === "Ready" ? "success" : cell.status === "Review" ? "warning" : "danger"} dot>
                      {cell.status}
                    </Badge>
                  </span>
                  <span className="mt-1 block font-mono text-xs text-muted-foreground">{cell.cellId} · {cell.mode}</span>
                </button>
              ))}
            </div>
            <div className="row-detail-panel">
              <div className="head">Compiled preview</div>
              <div className="body">
                <pre
                  data-testid="strategy-formula-compiled-preview"
                  className="max-h-44 overflow-auto whitespace-pre-wrap font-mono text-xs leading-5 text-muted-foreground"
                >
                  {compiledLines.join("\n")}
                </pre>
              </div>
            </div>
            <Button type="button" variant="secondary" className="w-full" aria-label="Run local strategy formula preview">
              <PlayCircle className="h-4 w-4" aria-hidden="true" />
              <span className="ml-1.5">Preview run</span>
            </Button>
          </CardContent>
        </Card>
      </div>
    </section>
  );
}

function filterFields(fields: StrategyFormulaField[], query: string) {
  const normalized = query.trim().toLowerCase();
  if (!normalized) return fields;
  return fields.filter((field) =>
    [field.fieldId, field.label, field.category, field.description, field.example]
      .join(" ")
      .toLowerCase()
      .includes(normalized)
  );
}

function filterSuggestions(suggestions: StrategyFormulaSuggestion[], source: string) {
  const normalized = source.toLowerCase();
  const matching = suggestions.filter((suggestion) =>
    suggestion.triggers.some((trigger) => normalized.includes(trigger.toLowerCase()))
  );
  return (matching.length > 0 ? matching : suggestions).slice(0, 4);
}

function extractFieldTokens(source: string, fields: StrategyFormulaField[]) {
  const fieldIds = new Set(fields.map((field) => field.fieldId));
  return [...new Set(source.match(/\b[A-Z][A-Z0-9_]{2,}\b/g) ?? [])].filter((token) => fieldIds.has(token));
}

function buildCompiledPreview(mode: StrategyFormulaWorkbenchMode, source: string, fieldTokens: string[]) {
  return [
    `mode: ${mode}`,
    `fields: ${fieldTokens.length ? fieldTokens.join(", ") : "none"}`,
    `lines: ${source.split(/\r?\n/).filter((line) => line.trim()).length}`,
    "status: preview-only",
    "",
    source
      .split(/\r?\n/)
      .map((line, index) => `${String(index + 1).padStart(2, "0")}  ${line}`)
      .join("\n")
  ];
}

function modeLabel(mode: StrategyFormulaWorkbenchMode) {
  switch (mode) {
    case "visual": return "Visual";
    case "formula": return "Formula";
    case "code": return "Code";
  }
}

function modeIcon(mode: StrategyFormulaWorkbenchMode) {
  switch (mode) {
    case "visual": return <LayoutGrid className="h-4 w-4" aria-hidden="true" />;
    case "formula": return <Sigma className="h-4 w-4" aria-hidden="true" />;
    case "code": return <Braces className="h-4 w-4" aria-hidden="true" />;
  }
}

function fieldTypeVariant(typeName: StrategyFormulaFieldType): "default" | "outline" | "success" | "warning" | "danger" | "research" {
  switch (typeName) {
    case "decimal": return "default";
    case "percent": return "success";
    case "date": return "warning";
    case "string": return "outline";
  }
}

function suggestionToneVariant(tone: StrategyFormulaSuggestionTone): "default" | "outline" | "success" | "warning" | "danger" | "research" {
  switch (tone) {
    case "calculation": return "default";
    case "filter": return "outline";
    case "risk": return "warning";
    case "allocation": return "success";
  }
}
