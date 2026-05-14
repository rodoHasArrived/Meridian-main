import type { ReactNode } from "react";
import { ChevronDown, ChevronRight, Code2, Database, FileText, Play, PlayCircle, Plus, Trash2, X } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { cn } from "@/lib/utils";
import {
  cellOutputToneClass,
  cellStateBadgeVariant,
  cellStateLabel
} from "@/components/meridian/quant-notebook.view-model";
import type { QuantNotebookViewModel } from "@/components/meridian/quant-notebook.view-model";
import type { CellKind, CellOutput, NotebookCell, PriceBar } from "@/types";

// ── Top-level notebook ─────────────────────────────────────────────────────────

interface QuantNotebookProps {
  vm: QuantNotebookViewModel;
  studyChips?: string[];
}

export function QuantNotebook({ vm, studyChips }: QuantNotebookProps) {
  const anyRunning = vm.cells.some((c) => c.state === "running");

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
              disabled={anyRunning}
              aria-label="Run all cells"
            >
              <Play className="mr-1.5 h-3.5 w-3.5" />
              Run all
            </Button>
            <Button
              size="sm"
              variant="ghost"
              onClick={vm.clearOutputs}
              disabled={anyRunning}
              aria-label="Clear all outputs"
            >
              Clear
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
  const { context, dataResult, dataFetchState, fetchError } = vm;
  const loading = dataFetchState === "loading";

  return (
    <div className="rounded-lg border border-border/70 bg-secondary/20 p-3">
      <div className="mb-2 flex items-center gap-2">
        <Database className="h-3.5 w-3.5 text-primary" />
        <span className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">
          Data context
        </span>
        {dataResult && (
          <Badge variant="success" className="ml-auto text-xs">
            {dataResult.rowCount.toLocaleString()} bars
          </Badge>
        )}
      </div>

      <div className="grid gap-2 sm:grid-cols-[1fr_1fr_1fr_auto_auto]">
        <Input
          placeholder="Symbol (e.g. AAPL)"
          value={context.symbol ?? ""}
          onChange={(e) => vm.setContext({ symbol: e.target.value })}
          aria-label="Symbol"
        />
        <Input
          type="date"
          value={context.from ?? ""}
          onChange={(e) => vm.setContext({ from: e.target.value })}
          aria-label="From date"
        />
        <Input
          type="date"
          value={context.to ?? ""}
          onChange={(e) => vm.setContext({ to: e.target.value })}
          aria-label="To date"
        />
        <Select
          value={context.interval ?? "daily"}
          onChange={(e) => vm.setContext({ interval: e.target.value })}
          aria-label="Interval"
        >
          <option value="daily">Daily</option>
          <option value="hourly">Hourly</option>
          <option value="minute">Minute</option>
        </Select>
        <Button
          size="sm"
          variant="secondary"
          onClick={() => void vm.fetchData()}
          disabled={loading || !context.symbol}
          aria-label="Fetch data"
        >
          {loading ? "Loading…" : "Fetch"}
        </Button>
      </div>

      {fetchError && (
        <p className="mt-2 text-xs text-danger">{fetchError}</p>
      )}

      {dataResult && (
        <DataResultTable result={dataResult} onDismiss={vm.dismissDataResult} />
      )}
    </div>
  );
}

// ── Data result table ──────────────────────────────────────────────────────────

function DataResultTable({
  result,
  onDismiss
}: {
  result: { symbol: string; interval: string; bars: PriceBar[]; rowCount: number };
  onDismiss: () => void;
}) {
  const preview = result.bars.slice(0, 5);

  return (
    <div className="mt-3 rounded-md border border-border/60 bg-background/50">
      <div className="flex items-center justify-between px-3 py-2">
        <span className="text-xs text-muted-foreground">
          {result.symbol} · {result.interval} · {result.rowCount.toLocaleString()} rows
          {result.rowCount > 5 ? " (showing first 5)" : ""}
        </span>
        <Button
          size="sm"
          variant="ghost"
          onClick={onDismiss}
          aria-label="Dismiss data result"
          className="h-6 px-1.5"
        >
          <X className="h-3 w-3" />
        </Button>
      </div>
      {preview.length > 0 && (
        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-border/50 text-left text-xs">
            <thead className="bg-secondary/30">
              <tr>
                {["Date", "Open", "High", "Low", "Close", "Volume"].map((col) => (
                  <th key={col} className="px-2 py-1.5 font-semibold uppercase tracking-[0.12em] text-muted-foreground">
                    {col}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-border/40">
              {preview.map((bar) => (
                <tr key={bar.timestamp}>
                  <td className="px-2 py-1.5 font-mono text-muted-foreground">{bar.timestamp.slice(0, 10)}</td>
                  <td className="px-2 py-1.5 font-mono">{bar.open.toFixed(2)}</td>
                  <td className="px-2 py-1.5 font-mono">{bar.high.toFixed(2)}</td>
                  <td className="px-2 py-1.5 font-mono">{bar.low.toFixed(2)}</td>
                  <td className="px-2 py-1.5 font-mono">{bar.close.toFixed(2)}</td>
                  <td className="px-2 py-1.5 font-mono">{bar.volume.toLocaleString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

// ── Single notebook cell ───────────────────────────────────────────────────────

interface NotebookCellItemProps {
  cell: NotebookCell;
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
              placeholder={
                isMarkdown
                  ? `## Section ${cell.ordinal.toString()}\n\nNarrative, hypothesis, or analysis notes.`
                  : `// Cell ${cell.ordinal.toString()}\n// Use Data.Prices(symbol), Backtest.WithSymbols(...), or any C# expression`
              }
              disabled={isRunning}
              aria-label={`Cell ${cell.ordinal.toString()} source`}
              rows={4}
              spellCheck={isMarkdown}
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
  cell: NotebookCell;
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
            disabled={isRunning}
            aria-label={`Run cell ${cell.ordinal.toString()}`}
            className="h-6 px-1.5"
          >
            <Play className="h-3 w-3" />
          </Button>
        )}
        {canDelete && (
          <Button
            size="sm"
            variant="ghost"
            onClick={onDelete}
            disabled={isRunning}
            aria-label={`Delete cell ${cell.ordinal.toString()}`}
            className="h-6 px-1.5 text-muted-foreground hover:text-danger"
          >
            <Trash2 className="h-3 w-3" />
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
