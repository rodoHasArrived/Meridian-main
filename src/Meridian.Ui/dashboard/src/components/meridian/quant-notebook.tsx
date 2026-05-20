import type { ReactNode } from "react";
import { ChevronDown, ChevronRight, Code2, Database, FileText, Play, PlayCircle, Plus, Trash2, X } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { FieldSupportText } from "@/components/ui/field-support";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { cn } from "@/lib/utils";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import {
  cellOutputToneClass,
  cellStateBadgeVariant,
  cellStateLabel
} from "@/components/meridian/quant-notebook.view-model";
import type {
  QuantNotebookCellViewModel,
  QuantNotebookDataContextFieldViewModel,
  QuantNotebookDataResultRowViewModel,
  QuantNotebookDataResultViewModel,
  QuantNotebookViewModel
} from "@/components/meridian/quant-notebook.view-model";
import type { CellKind, CellOutput } from "@/types";

const dataResultColumns: DenseDataTableColumn<QuantNotebookDataResultRowViewModel>[] = [
  {
    id: "timestamp",
    label: "Timestamp",
    render: (row) => <span className="font-mono text-muted-foreground">{row.timestampLabel}</span>
  },
  {
    id: "open",
    label: "Open",
    align: "right",
    render: (row) => <span className="font-mono">{row.openLabel}</span>
  },
  {
    id: "high",
    label: "High",
    align: "right",
    render: (row) => <span className="font-mono">{row.highLabel}</span>
  },
  {
    id: "low",
    label: "Low",
    align: "right",
    render: (row) => <span className="font-mono">{row.lowLabel}</span>
  },
  {
    id: "close",
    label: "Close",
    align: "right",
    render: (row) => <span className="font-mono">{row.closeLabel}</span>
  },
  {
    id: "volume",
    label: "Volume",
    align: "right",
    render: (row) => <span className="font-mono">{row.volumeLabel}</span>
  }
];

// ── Top-level notebook ─────────────────────────────────────────────────────────

interface QuantNotebookProps {
  vm: QuantNotebookViewModel;
  studyChips?: string[];
}

export function QuantNotebook({ vm, studyChips }: QuantNotebookProps) {
  return (
    <Card className="border-border/70 bg-background/35">
      <CardHeader className="pb-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <CardTitle className="flex items-center gap-2 text-base">
            <PlayCircle className="h-4 w-4 text-primary" />
            Strategy notebook
          </CardTitle>
          <div className="flex items-center gap-2">
            <Button
              size="sm"
              variant="secondary"
              onClick={() => void vm.runAll()}
              disabled={vm.runAllCommand.disabled}
              disabledReason={vm.runAllCommand.disabledReason}
              busy={vm.runAllCommand.busy}
              busyLabel={vm.runAllCommand.label}
              aria-label={vm.runAllCommand.ariaLabel}
            >
              <Play className="mr-1.5 h-3.5 w-3.5" />
              {vm.runAllCommand.label}
            </Button>
            <Button
              size="sm"
              variant={vm.clearOutputsConfirmationPending ? "secondary" : "ghost"}
              onClick={vm.clearOutputs}
              disabled={Boolean(vm.clearOutputsDisabledReason)}
              disabledReason={vm.clearOutputsDisabledReason}
              aria-label={vm.clearOutputsAriaLabel}
            >
              {vm.clearOutputsLabel}
            </Button>
            <Button
              size="sm"
              variant="ghost"
              onClick={() => vm.addCell("code")}
              aria-label="Add code cell"
            >
              <Plus className="mr-1.5 h-3.5 w-3.5" />
              <Code2 className="mr-1 h-3.5 w-3.5" />
              Code
            </Button>
            <Button
              size="sm"
              variant="ghost"
              onClick={() => vm.addCell("markdown")}
              aria-label="Add markdown cell"
            >
              <Plus className="mr-1.5 h-3.5 w-3.5" />
              <FileText className="mr-1 h-3.5 w-3.5" />
              Note
            </Button>
            <SnippetMenu vm={vm} />
          </div>
        </div>

        {studyChips && studyChips.length > 0 && (
          <div className="flex flex-wrap gap-1.5 pt-1">
            {studyChips.map((chip) => (
              <Badge key={chip} variant="outline" className="text-xs">
                {chip}
              </Badge>
            ))}
          </div>
        )}
      </CardHeader>

      <CardContent className="space-y-3">
        <DataFetchPanel vm={vm} />

        <div className="space-y-2">
          {vm.cells.map((cell) => (
            <NotebookCellItem
              key={cell.id}
              cell={cell}
              canDelete={vm.cells.length > 1}
              onRun={() => void vm.runCell(cell.id)}
              onDelete={() => vm.removeCell(cell.id)}
              onToggleCollapse={() => vm.toggleCellCollapse(cell.id)}
              onSourceChange={(source) => vm.updateCellSource(cell.id, source)}
              onKindChange={(kind) => vm.setCellKind(cell.id, kind)}
            />
          ))}
        </div>
      </CardContent>
    </Card>
  );
}

// ── Data fetch panel ───────────────────────────────────────────────────────────

function DataFetchPanel({ vm }: { vm: QuantNotebookViewModel }) {
  const { context, dataContextPanel: panel } = vm;

  return (
    <section
      className="rounded-lg border border-border/70 bg-secondary/20 p-3"
      aria-describedby={`${panel.descriptionId} ${panel.statusId}`}
    >
      <div className="mb-2 flex items-center gap-2">
        <Database className="h-3.5 w-3.5 text-primary" />
        <span className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">
          Data context
        </span>
        {panel.result && (
          <Badge variant="success" className="ml-auto text-xs">
            {panel.result.summaryText}
          </Badge>
        )}
      </div>
      <p id={panel.descriptionId} className="sr-only">
        Fetch price bars into the notebook execution context.
      </p>

      <div className="grid gap-2 sm:grid-cols-[minmax(9rem,1fr)_minmax(8rem,1fr)_minmax(8rem,1fr)_minmax(8rem,0.8fr)_auto]">
        <DataContextInput
          field={panel.fields.symbol}
          value={context.symbol ?? ""}
          placeholder="AAPL"
          onChange={(value) => vm.setContext({ symbol: value })}
        />
        <DataContextInput
          field={panel.fields.from}
          type="date"
          value={context.from ?? ""}
          onChange={(value) => vm.setContext({ from: value })}
        />
        <DataContextInput
          field={panel.fields.to}
          type="date"
          value={context.to ?? ""}
          onChange={(value) => vm.setContext({ to: value })}
        />
        <div className="grid gap-1">
          <label htmlFor={panel.fields.interval.id} className="text-[0.65rem] font-semibold uppercase tracking-[0.14em] text-muted-foreground">
            {panel.fields.interval.label}
          </label>
            <Select
              id={panel.fields.interval.id}
              value={context.interval ?? "daily"}
              onChange={(e) => vm.setContext({ interval: e.target.value as "daily" | "hourly" | "minute" })}
              aria-label={panel.fields.interval.ariaLabel}
              aria-describedby={panel.fields.interval.describedBy ?? undefined}
              disabled={panel.fields.interval.disabled}
            >
            <option value="daily">Daily</option>
            <option value="hourly">Hourly</option>
            <option value="minute">Minute</option>
          </Select>
          <FieldSupportText
            helpId={panel.fields.interval.helpId}
            helpText={panel.fields.interval.helpText}
            helpClassName="sr-only"
            disabledReason={panel.fields.interval.disabledReason}
            disabledReasonId={panel.fields.interval.disabledReasonId}
          />
        </div>
        <Button
          size="sm"
          variant="secondary"
          onClick={() => void vm.fetchData()}
          disabled={panel.fetchCommand.disabled}
          disabledReason={panel.fetchCommand.disabledReason}
          busy={panel.fetchCommand.busy}
          busyLabel={panel.fetchCommand.label}
          aria-label={panel.fetchCommand.ariaLabel}
          className="self-end"
        >
          {panel.fetchCommand.label}
        </Button>
      </div>

      <p
        id={panel.statusId}
        role={panel.statusTone === "error" ? "alert" : "status"}
        className={cn(
          "mt-2 text-xs",
          panel.statusTone === "error"
            ? "text-danger"
            : panel.statusTone === "loading"
              ? "text-warning"
              : panel.statusTone === "success"
                ? "text-success"
                : "text-muted-foreground"
        )}
      >
        {panel.statusText}
      </p>

      {panel.result && (
        <DataResultTable result={panel.result} onDismiss={vm.dismissDataResult} />
      )}
    </section>
  );
}

function DataContextInput({
  field,
  value,
  onChange,
  type = "text",
  placeholder
}: {
  field: QuantNotebookDataContextFieldViewModel;
  value: string;
  onChange: (value: string) => void;
  type?: "text" | "date";
  placeholder?: string;
}) {
  return (
    <div className="grid gap-1">
      <label htmlFor={field.id} className="text-[0.65rem] font-semibold uppercase tracking-[0.14em] text-muted-foreground">
        {field.label}
      </label>
      <Input
        id={field.id}
        type={type}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        aria-label={field.ariaLabel}
        aria-describedby={field.describedBy ?? undefined}
        error={field.error}
        disabled={field.disabled}
      />
      <FieldSupportText
        helpId={field.helpId}
        helpText={field.helpText}
        helpClassName="sr-only"
        disabledReason={field.disabledReason}
        disabledReasonId={field.disabledReasonId}
      />
    </div>
  );
}

// ── Data result table ──────────────────────────────────────────────────────────

function DataResultTable({
  result,
  onDismiss
}: {
  result: QuantNotebookDataResultViewModel;
  onDismiss: () => void;
}) {
  return (
    <div className="mt-3 rounded-md border border-border/60 bg-background/50">
      <div className="flex items-center justify-between px-3 py-2">
        <span className="text-xs text-muted-foreground">
          {result.summaryText}
          {result.previewNotice ? ` (${result.previewNotice})` : ""}
        </span>
        <Button
          size="sm"
          variant="ghost"
          onClick={onDismiss}
          aria-label={result.dismissAriaLabel}
          className="h-6 px-1.5"
        >
          <X className="h-3 w-3" />
        </Button>
      </div>
      <DenseDataTable
        columns={dataResultColumns}
        rows={result.rows}
        getRowId={(row) => row.id}
        emptyText={result.emptyText}
        ariaLabel={result.ariaLabel}
        caption={result.caption}
      />
    </div>
  );
}

// ── Single notebook cell ───────────────────────────────────────────────────────

interface NotebookCellItemProps {
  cell: QuantNotebookCellViewModel;
  canDelete: boolean;
  onRun: () => void;
  onDelete: () => void;
  onToggleCollapse: () => void;
  onSourceChange: (source: string) => void;
  onKindChange: (kind: CellKind) => void;
}

function NotebookCellItem({
  cell,
  canDelete,
  onRun,
  onDelete,
  onToggleCollapse,
  onSourceChange,
  onKindChange
}: NotebookCellItemProps) {
  const isRunning = cell.state === "running";
  const isMarkdown = cell.kind === "markdown";

  return (
    <div
      className={cn(
        "rounded-lg border transition-colors",
        isMarkdown
          ? "border-border/50 bg-secondary/15"
          : cell.state === "error"
            ? "border-danger/40 bg-danger/5"
            : cell.state === "done"
              ? "border-success/30 bg-success/5"
              : "border-border/70 bg-background/40"
      )}
    >
      <CellHeader
        cell={cell}
        canDelete={canDelete}
        onRun={onRun}
        onDelete={onDelete}
        onToggleCollapse={onToggleCollapse}
        onKindChange={onKindChange}
      />

      {!cell.collapsed && (
        <>
          <div className="border-t border-border/60 px-3 py-2">
            <textarea
              className={cn(
                "w-full resize-none rounded-md border border-border/60 bg-secondary/30 px-3 py-2",
                isMarkdown ? "text-sm" : "font-mono text-xs",
                "text-foreground",
                "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                "disabled:cursor-not-allowed disabled:opacity-50",
                "min-h-[80px]"
              )}
              value={cell.source}
              onChange={(e) => onSourceChange(e.target.value)}
              placeholder={cell.sourceField.placeholder}
              disabled={cell.sourceField.disabled}
              aria-label={cell.sourceField.label}
              aria-describedby={cell.sourceField.describedBy ?? undefined}
              rows={4}
              spellCheck={cell.sourceField.spellCheck}
            />
            <FieldSupportText
              disabledReason={cell.sourceField.disabledReason}
              disabledReasonId={cell.sourceField.disabledReasonId}
              disabledReasonClassName="mt-1"
            />
          </div>

          {isMarkdown && cell.source.trim().length > 0 && (
            <MarkdownPreview source={cell.source} />
          )}

          {!isMarkdown && cell.output.length > 0 && (
            <CellOutputPanel output={cell.output} />
          )}
        </>
      )}
    </div>
  );
}

// ── Cell header ────────────────────────────────────────────────────────────────

function CellHeader({
  cell,
  canDelete,
  onRun,
  onDelete,
  onToggleCollapse,
  onKindChange
}: {
  cell: QuantNotebookCellViewModel;
  canDelete: boolean;
  onRun: () => void;
  onDelete: () => void;
  onToggleCollapse: () => void;
  onKindChange: (kind: CellKind) => void;
}) {
  const isRunning = cell.state === "running";
  const isMarkdown = cell.kind === "markdown";

  return (
    <div className="flex items-center justify-between gap-2 px-3 py-2">
      <div className="flex items-center gap-2">
        <button
          type="button"
          onClick={onToggleCollapse}
          className="text-muted-foreground hover:text-foreground"
          aria-label={cell.collapsed ? `Expand cell ${cell.ordinal.toString()}` : `Collapse cell ${cell.ordinal.toString()}`}
        >
          {cell.collapsed
            ? <ChevronRight className="h-3.5 w-3.5" />
            : <ChevronDown className="h-3.5 w-3.5" />
          }
        </button>
        <span className="text-xs font-semibold text-muted-foreground">
          Cell {cell.ordinal}
        </span>
        <button
          type="button"
          onClick={() => onKindChange(isMarkdown ? "code" : "markdown")}
          aria-label={isMarkdown ? `Convert cell ${cell.ordinal.toString()} to code` : `Convert cell ${cell.ordinal.toString()} to markdown`}
          className="inline-flex items-center gap-1 rounded border border-border/60 bg-secondary/30 px-1.5 py-0.5 text-xs text-muted-foreground hover:text-foreground"
        >
          {isMarkdown
            ? <><FileText className="h-3 w-3" />Note</>
            : <><Code2 className="h-3 w-3" />Code</>
          }
        </button>
        {!isMarkdown && (
          <Badge variant={cellStateBadgeVariant(cell.state)} className="text-xs">
            {cellStateLabel(cell.state)}
          </Badge>
        )}
        {!isMarkdown && cell.statusText && cell.statusText !== cellStateLabel(cell.state) && (
          <span className="text-xs text-muted-foreground">{cell.statusText}</span>
        )}
      </div>

      <div className="flex items-center gap-1.5">
        {!isMarkdown && (
          <Button
            size="sm"
            variant="ghost"
            onClick={onRun}
            disabled={cell.runCommand.disabled}
            disabledReason={cell.runCommand.disabledReason}
            aria-label={cell.runCommand.ariaLabel}
            className="h-6 px-1.5"
          >
            <Play className="h-3 w-3" />
          </Button>
        )}
        {canDelete && (
          <Button
            size="sm"
            variant={cell.deleteConfirmationPending ? "secondary" : "ghost"}
            onClick={onDelete}
            disabled={isRunning}
            disabledReason={cell.deleteDisabledReason}
            aria-label={cell.deleteAriaLabel}
            className={cn(
              "h-6 px-1.5",
              cell.deleteConfirmationPending
                ? "text-danger hover:text-danger"
                : "text-muted-foreground hover:text-danger"
            )}
          >
            <Trash2 className="h-3 w-3" />
            {cell.deleteConfirmationPending && (
              <span className="ml-1 text-xs">{cell.deleteLabel}</span>
            )}
          </Button>
        )}
      </div>
    </div>
  );
}

// ── Cell output ────────────────────────────────────────────────────────────────

function CellOutputPanel({ output }: { output: CellOutput[] }) {
  return (
    <div className="border-t border-border/60 px-3 py-2">
      <div className="rounded-md border border-border/60 bg-background/60 px-3 py-2 font-mono text-xs">
        {output.map((line, index) => (
          <CellOutputLine key={`${line.kind}-${index}`} line={line} />
        ))}
      </div>
    </div>
  );
}

function CellOutputLine({ line }: { line: CellOutput }) {
  const prefix =
    line.kind === "metric" ? "→ "
    : line.kind === "signal" ? "⚡ "
    : line.kind === "error" ? "✗ "
    : "";

  return (
    <div className={cn("leading-5", cellOutputToneClass(line.tone))}>
      {prefix && <span className="opacity-60">{prefix}</span>}
      {line.text}
      {line.timestamp && (
        <span className="ml-2 text-muted-foreground opacity-50">{line.timestamp}</span>
      )}
    </div>
  );
}

// ── Markdown preview ───────────────────────────────────────────────────────────

function MarkdownPreview({ source }: { source: string }) {
  return (
    <div className="border-t border-border/60 px-3 py-2">
      <div className="rounded-md border border-border/40 bg-background/30 px-3 py-2 text-sm leading-6 text-foreground">
        {renderMarkdownBlocks(source)}
      </div>
    </div>
  );
}

function renderMarkdownBlocks(source: string): ReactNode[] {
  const lines = source.split(/\r?\n/);
  const blocks: ReactNode[] = [];
  let bulletBuffer: string[] = [];

  const flushBullets = (keyPrefix: string) => {
    if (bulletBuffer.length === 0) {
      return;
    }
    blocks.push(
      <ul key={`${keyPrefix}-list`} className="ml-5 list-disc text-muted-foreground">
        {bulletBuffer.map((item, idx) => (
          <li key={`${keyPrefix}-${idx.toString()}`}>{renderInlineMarkdown(item)}</li>
        ))}
      </ul>
    );
    bulletBuffer = [];
  };

  lines.forEach((rawLine, index) => {
    const line = rawLine.trim();
    const key = `md-${index.toString()}`;

    if (!line) {
      flushBullets(key);
      return;
    }

    if (line.startsWith("### ")) {
      flushBullets(key);
      blocks.push(<h4 key={key} className="mt-1 text-sm font-semibold text-foreground">{line.slice(4)}</h4>);
      return;
    }

    if (line.startsWith("## ")) {
      flushBullets(key);
      blocks.push(<h3 key={key} className="mt-1 text-base font-semibold text-foreground">{line.slice(3)}</h3>);
      return;
    }

    if (line.startsWith("# ")) {
      flushBullets(key);
      blocks.push(<h2 key={key} className="mt-1 text-lg font-semibold text-foreground">{line.slice(2)}</h2>);
      return;
    }

    if (line.startsWith("- ") || line.startsWith("* ")) {
      bulletBuffer.push(line.slice(2));
      return;
    }

    flushBullets(key);
    blocks.push(<p key={key} className="text-muted-foreground">{renderInlineMarkdown(line)}</p>);
  });

  flushBullets("md-final");
  return blocks;
}

function renderInlineMarkdown(text: string): ReactNode {
  const parts: ReactNode[] = [];
  const pattern = /(`[^`]+`|\*\*[^*]+\*\*)/g;
  let last = 0;
  let match: RegExpExecArray | null;
  let keyIndex = 0;

  while ((match = pattern.exec(text)) !== null) {
    if (match.index > last) {
      parts.push(<span key={`t-${(keyIndex++).toString()}`}>{text.slice(last, match.index)}</span>);
    }

    const token = match[0];
    if (token.startsWith("`")) {
      parts.push(
        <code key={`c-${(keyIndex++).toString()}`} className="rounded bg-secondary/40 px-1 font-mono text-xs">
          {token.slice(1, -1)}
        </code>
      );
    } else {
      parts.push(
        <strong key={`b-${(keyIndex++).toString()}`} className="font-semibold text-foreground">
          {token.slice(2, -2)}
        </strong>
      );
    }

    last = match.index + token.length;
  }

  if (last < text.length) {
    parts.push(<span key={`t-${(keyIndex++).toString()}`}>{text.slice(last)}</span>);
  }

  return <>{parts}</>;
}

// ── Snippet menu ───────────────────────────────────────────────────────────────

function SnippetMenu({ vm }: { vm: QuantNotebookViewModel }) {
  return (
    <Select
      value=""
      onChange={(e) => {
        if (e.target.value) {
          vm.insertSnippet(e.target.value);
          e.target.value = "";
        }
      }}
      aria-label="Insert snippet"
      className="h-8 text-xs"
    >
      <option value="">+ Snippet</option>
      {vm.snippets.map((snippet) => (
        <option key={snippet.id} value={snippet.id}>
          {snippet.label}
        </option>
      ))}
    </Select>
  );
}
