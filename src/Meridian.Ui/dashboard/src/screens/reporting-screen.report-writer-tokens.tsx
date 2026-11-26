// Report-writer drop zones and field tokens, split out of reporting-screen.report-writer.tsx.
import { type DragEvent } from "react";
import { ChevronLeft, ChevronRight, GripVertical, XCircle } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Select } from "@/components/ui/select";
import { cn } from "@/lib/utils";
import { usesTrackedCaps } from "@/design-system/label-case";
import type { ReportingWriterGridRow, ReportingWriterToken } from "@/screens/reporting-screen.view-model";
import type { ReportWriterDropZone, ReportWriterTokenDragOrigin } from "@/screens/reporting-screen.report-writer";

interface ReportWriterDropZoneViewProps {
  grid: ReportingWriterGridRow;
  zone: ReportWriterDropZone;
  label: string;
  tokens: ReportingWriterToken[];
  sourceFields?: ReportingWriterToken[];
  onFieldAdd?: (grid: ReportingWriterGridRow, zone: ReportWriterDropZone, token: ReportingWriterToken) => void;
  onTokenDragStart: (event: DragEvent<HTMLElement>, token: ReportingWriterToken, origin?: ReportWriterTokenDragOrigin | null) => void;
  onZoneDrop: (event: DragEvent<HTMLElement>, grid: ReportingWriterGridRow, zone: ReportWriterDropZone) => void;
  onTokenRemove: (grid: ReportingWriterGridRow, zone: ReportWriterDropZone, tokenId: string) => void;
  onTokenMove: (grid: ReportingWriterGridRow, zone: ReportWriterDropZone, tokenId: string, direction: -1 | 1) => void;
}

export function ReportWriterDropZoneView({
  grid,
  zone,
  label,
  tokens,
  sourceFields,
  onFieldAdd,
  onTokenDragStart,
  onZoneDrop,
  onTokenRemove,
  onTokenMove
}: ReportWriterDropZoneViewProps) {
  const fields = sourceFields?.filter((field) => zone !== "metrics" || field.kind !== "formula") ?? [];
  return (
    // The zone is a labelled drop target; its tokens form a list only when there are tokens, so
    // an empty zone is not announced as a list with no items (axe aria-required-children).
    <div
      role="group"
      aria-label={`${grid.title} ${label} drop zone`}
      className="min-h-24 rounded-md border border-dashed border-border/70 bg-background/25 px-2.5 py-2"
      onDragOver={(event) => event.preventDefault()}
      onDrop={(event) => onZoneDrop(event, grid, zone)}
    >
      <div className={cn("mb-2 text-xs text-muted-foreground", usesTrackedCaps(label) && "type-label--caps")}>{label}</div>
      <div
        role={tokens.length > 0 ? "list" : undefined}
        aria-label={tokens.length > 0 ? `${grid.title} ${label}` : undefined}
        className="flex flex-wrap gap-1.5"
      >
        {tokens.length > 0 ? tokens.map((token, tokenIndex) => (
          <ReportWriterTokenChip
            key={token.id}
            token={token}
            draggable
            onDragStart={onTokenDragStart}
            dragOrigin={{ gridId: grid.id, zone }}
            canMovePrevious={tokenIndex > 0}
            canMoveNext={tokenIndex < tokens.length - 1}
            onMove={(direction) => onTokenMove(grid, zone, token.id, direction)}
            onRemove={() => onTokenRemove(grid, zone, token.id)}
          />
        )) : (
          <span className="text-xs text-muted-foreground">No fields</span>
        )}
      </div>
      {fields.length > 0 && onFieldAdd ? (
        <Select
          className="mt-2"
          aria-label={`Add field to ${grid.title} ${label}`}
          value=""
          onChange={(event) => {
            const field = fields.find((item) => item.id === event.target.value);
            if (field) onFieldAdd(grid, zone, field);
          }}
        >
          <option value="">Add field</option>
          {fields.map((field) => (
            <option key={field.id} value={field.id}
              disabled={tokens.some((token) => token.fieldName?.toLowerCase() === field.fieldName?.toLowerCase())}>
              {field.label}
            </option>
          ))}
        </Select>
      ) : null}
    </div>
  );
}

export function ReportWriterTokenChip({
  token,
  draggable,
  onDragStart,
  dragOrigin,
  canMovePrevious,
  canMoveNext,
  onMove,
  onRemove
}: {
  token: ReportingWriterToken;
  draggable?: boolean;
  onDragStart: (event: DragEvent<HTMLElement>, token: ReportingWriterToken, origin?: ReportWriterTokenDragOrigin | null) => void;
  dragOrigin?: ReportWriterTokenDragOrigin | null;
  canMovePrevious?: boolean;
  canMoveNext?: boolean;
  onMove?: (direction: -1 | 1) => void;
  onRemove?: () => void;
}) {
  return (
    <span
      role="listitem"
      draggable={draggable}
      onDragStart={(event) => onDragStart(event, token, dragOrigin ?? null)}
      className="inline-flex min-w-0 max-w-full flex-wrap items-center gap-1.5 rounded-sm border border-border/70 bg-secondary/35 px-2 py-1 text-xs text-foreground"
      title={token.detail}
    >
      <span className={cn("inline-flex min-w-0 flex-wrap items-center gap-1.5", (onMove || onRemove) && "w-full")}>
        <GripVertical className="h-3.5 w-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
        <Badge variant={token.kind === "formula" ? "warning" : token.kind === "metric" ? "success" : "outline"}>{token.kind}</Badge>
        <span className="max-w-full break-all font-mono">{token.label}</span>
      </span>
      {(onMove || onRemove) ? (
        <span className="inline-flex shrink-0 items-center gap-0.5">
          {onMove ? (
            <>
              <Button
                type="button"
                variant="ghost"
                size="icon"
                className="shrink-0"
                style={{ width: 24, height: 24 }}
                aria-label={`Move ${token.label} left`}
                disabled={!canMovePrevious}
                disabledReason="This token is already first in the list."
                onClick={(event) => {
                  event.preventDefault();
                  event.stopPropagation();
                  onMove(-1);
                }}
              >
                <ChevronLeft className="h-3.5 w-3.5" aria-hidden="true" />
              </Button>
              <Button
                type="button"
                variant="ghost"
                size="icon"
                className="shrink-0"
                style={{ width: 24, height: 24 }}
                aria-label={`Move ${token.label} right`}
                disabled={!canMoveNext}
                disabledReason="This token is already last in the list."
                onClick={(event) => {
                  event.preventDefault();
                  event.stopPropagation();
                  onMove(1);
                }}
              >
                <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
              </Button>
            </>
          ) : null}
          {onRemove ? (
            <Button
              type="button"
              variant="ghost"
              size="icon"
              className="shrink-0"
              style={{ width: 24, height: 24 }}
              aria-label={`Remove ${token.label}`}
              onClick={(event) => {
                event.preventDefault();
                event.stopPropagation();
                onRemove();
              }}
            >
              <XCircle className="h-3.5 w-3.5" aria-hidden="true" />
            </Button>
          ) : null}
        </span>
      ) : null}
    </span>
  );
}
