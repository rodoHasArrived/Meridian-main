import { useId, useState } from "react";
import { GripVertical, LayoutGrid, Plus, Sparkles, Trash2 } from "lucide-react";
import { MetricCard } from "@/components/meridian/metric-card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { cn } from "@/lib/utils";
import {
  useStrategyDesignerViewModel,
  type ParticipationSliceViewModel,
  type ParticipationViewModel,
  type PayoffChartViewModel,
  type StrategyCanvasLegViewModel,
  type StrategyDesignerViewModel,
  type StrategyLeg,
  type StrategyLegPaletteEntry
} from "@/screens/strategy-designer-screen.view-model";

const PALETTE_DRAG_TYPE = "application/x-meridian-strategy-leg-kind";
const LEG_DRAG_TYPE = "application/x-meridian-strategy-leg-id";

export function StrategyDesignerScreen() {
  const vm = useStrategyDesignerViewModel();
  const [dropActive, setDropActive] = useState(false);

  return (
    <div className="space-y-6" data-testid="strategy-designer-screen">
      <Card>
        <CardHeader>
          <div className="eyebrow-label">Strategy Lane</div>
          <CardTitle className="flex items-center gap-2">
            <LayoutGrid className="h-5 w-5 text-primary" />
            Visual Strategy Designer
          </CardTitle>
          <CardDescription>
            Compose multi-leg strategies by dragging blocks onto the canvas. Payoff and participation visualizations update live as you tune strikes, premiums, and direction.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-3">
          <div className="flex flex-wrap items-center gap-3">
            <SpotPriceField vm={vm} />
            <div className="ml-auto flex flex-wrap items-center gap-2">
              <Button
                type="button"
                variant="outline"
                onClick={vm.loadSample}
                disabled={vm.loadSampleCommand.disabled}
                disabledReason={vm.loadSampleCommand.disabledReason}
                aria-label={vm.loadSampleCommand.ariaLabel}
              >
                <Sparkles className="h-4 w-4" aria-hidden="true" />
                <span className="ml-1.5">{vm.loadSampleCommand.label}</span>
              </Button>
              <Button
                type="button"
                variant="outline"
                onClick={vm.clearCanvas}
                disabled={vm.clearCanvasCommand.disabled}
                disabledReason={vm.clearCanvasCommand.disabledReason}
                aria-label={vm.clearCanvasCommand.ariaLabel}
              >
                <Trash2 className="h-4 w-4" aria-hidden="true" />
                <span className="ml-1.5">{vm.clearCanvasCommand.label}</span>
              </Button>
            </div>
          </div>
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            {vm.metrics.map((metric) => (
              <MetricCard
                key={metric.id}
                id={metric.id}
                label={metric.label}
                value={metric.value}
                delta={metric.detail}
                tone={metric.tone}
              />
            ))}
          </div>
        </CardContent>
      </Card>

      <div className="grid gap-4 lg:grid-cols-[280px_1fr]">
        <PalettePanel palette={vm.palette} onAdd={vm.addLegFromPalette} />
        <CanvasPanel vm={vm} dropActive={dropActive} setDropActive={setDropActive} />
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <PayoffCard payoff={vm.payoff} legCount={vm.legs.length} />
        <ParticipationCard participation={vm.participation} />
      </div>
    </div>
  );
}

interface SpotPriceFieldProps {
  vm: StrategyDesignerViewModel;
}

function SpotPriceField({ vm }: SpotPriceFieldProps) {
  const id = useId();

  return (
    <label htmlFor={id} className="flex items-center gap-2 text-xs uppercase tracking-[0.14em] text-muted-foreground">
      <span>{vm.spotPriceField.label}</span>
      <Input
        id={id}
        type="number"
        step={vm.spotPriceField.step}
        min={vm.spotPriceField.min}
        inputMode={vm.spotPriceField.inputMode}
        value={vm.spotPriceField.value}
        onChange={(event) => vm.updateSpotPriceDraft(event.target.value)}
        onBlur={(event) => vm.commitSpotPriceDraft(event.target.value)}
        className="w-32"
        aria-label={vm.spotPriceField.ariaLabel}
      />
    </label>
  );
}

interface PalettePanelProps {
  palette: StrategyLegPaletteEntry[];
  onAdd: (kind: StrategyLegPaletteEntry["kind"]) => void;
}

function PalettePanel({ palette, onAdd }: PalettePanelProps) {
  return (
    <Card data-testid="strategy-designer-palette">
      <CardHeader>
        <CardTitle className="text-base">Block palette</CardTitle>
        <CardDescription>Drag onto the canvas, or click to append.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-2">
        {palette.map((entry) => (
          <button
            key={entry.kind}
            type="button"
            draggable
            onDragStart={(event) => {
              event.dataTransfer.setData(PALETTE_DRAG_TYPE, entry.kind);
              event.dataTransfer.effectAllowed = "copy";
            }}
            onClick={() => onAdd(entry.kind)}
            className={cn(
              "group flex w-full items-start gap-3 rounded-md border border-border/70 bg-secondary/30 px-3 py-2 text-left",
              "transition-colors hover:border-primary/50 hover:bg-primary/10",
              "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
            )}
            aria-label={`Add ${entry.label} block`}
          >
            <GripVertical
              className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground group-hover:text-primary"
              aria-hidden="true"
            />
            <div className="flex-1 space-y-1">
              <div className="flex items-center gap-2">
                <span className="text-sm font-medium text-foreground">{entry.label}</span>
                <Badge variant="outline" className="ml-auto">
                  {entry.badge}
                </Badge>
              </div>
              <p className="text-xs leading-5 text-muted-foreground">{entry.description}</p>
            </div>
          </button>
        ))}
      </CardContent>
    </Card>
  );
}

interface CanvasPanelProps {
  vm: StrategyDesignerViewModel;
  dropActive: boolean;
  setDropActive: (active: boolean) => void;
}

function CanvasPanel({ vm, dropActive, setDropActive }: CanvasPanelProps) {
  const handleDragOver = (event: React.DragEvent<HTMLDivElement>) => {
    if (
      event.dataTransfer.types.includes(PALETTE_DRAG_TYPE) ||
      event.dataTransfer.types.includes(LEG_DRAG_TYPE)
    ) {
      event.preventDefault();
      event.dataTransfer.dropEffect = event.dataTransfer.types.includes(PALETTE_DRAG_TYPE) ? "copy" : "move";
      setDropActive(true);
    }
  };

  const handleDragLeave = () => {
    setDropActive(false);
  };

  const handleDrop = (event: React.DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    setDropActive(false);
    const kind = event.dataTransfer.getData(PALETTE_DRAG_TYPE);
    if (kind) {
      vm.addLegFromPalette(kind as StrategyLegPaletteEntry["kind"]);
    }
  };

  return (
    <Card data-testid="strategy-designer-canvas">
      <CardHeader>
        <div className="flex items-center justify-between gap-2">
          <div>
            <CardTitle className="text-base">{vm.canvasTitle}</CardTitle>
            <CardDescription>Reorder legs by dragging; click to select and tune parameters.</CardDescription>
          </div>
          <Button
            type="button"
            variant="outline"
            onClick={() => vm.addLegFromPalette("long-call")}
            disabled={vm.addLongCallCommand.disabled}
            disabledReason={vm.addLongCallCommand.disabledReason}
            aria-label={vm.addLongCallCommand.ariaLabel}
          >
            <Plus className="h-4 w-4" aria-hidden="true" />
            <span className="ml-1.5">{vm.addLongCallCommand.label}</span>
          </Button>
        </div>
      </CardHeader>
      <CardContent>
        <div
          role="list"
          aria-label="Strategy legs canvas"
          data-drop-active={dropActive ? "true" : "false"}
          onDragOver={handleDragOver}
          onDragLeave={handleDragLeave}
          onDrop={handleDrop}
          className={cn(
            "min-h-[260px] space-y-2 rounded-md border-2 border-dashed border-border/70 bg-secondary/15 p-3 transition-colors",
            dropActive && "border-primary/60 bg-primary/10"
          )}
        >
          {vm.legs.length === 0 ? (
            <div className="flex h-full min-h-[220px] items-center justify-center text-center text-sm text-muted-foreground">
              {vm.emptyStateMessage}
            </div>
          ) : (
            vm.canvasLegs.map((leg) => (
              <CanvasLeg
                key={leg.id}
                leg={leg}
                onSelect={() => vm.selectLeg(leg.id)}
                onRemove={() => vm.removeLeg(leg.id)}
                onUpdate={(patch) => vm.updateLeg(leg.id, patch)}
                onReorder={(sourceId) => vm.reorderLeg(sourceId, leg.id)}
              />
            ))
          )}
        </div>
      </CardContent>
    </Card>
  );
}

interface CanvasLegProps {
  leg: StrategyCanvasLegViewModel;
  onSelect: () => void;
  onRemove: () => void;
  onUpdate: (patch: Partial<Pick<StrategyLeg, "quantity" | "strike" | "premium" | "direction">>) => void;
  onReorder: (sourceLegId: string) => void;
}

function CanvasLeg({ leg, onSelect, onRemove, onUpdate, onReorder }: CanvasLegProps) {
  return (
    <div
      role="listitem"
      aria-label={leg.containerAriaLabel}
      data-leg-id={leg.id}
      data-selected={leg.isSelected ? "true" : "false"}
      onClick={(event) => {
        if (isInteractiveCanvasTarget(event.target)) return;
        onSelect();
      }}
      onDragOver={(event) => {
        if (event.dataTransfer.types.includes(LEG_DRAG_TYPE)) {
          event.preventDefault();
          event.dataTransfer.dropEffect = "move";
        }
      }}
      onDrop={(event) => {
        event.preventDefault();
        event.stopPropagation();
        const sourceId = event.dataTransfer.getData(LEG_DRAG_TYPE);
        if (sourceId && sourceId !== leg.id) {
          onReorder(sourceId);
        }
      }}
      className={cn(
        "flex flex-col gap-2 rounded-md border border-border/70 bg-card px-3 py-2 transition-colors",
        "focus-within:border-primary/60",
        leg.isSelected && "border-primary/60 ring-1 ring-primary/40"
      )}
    >
      <div className="flex items-center gap-2">
        <span
          className="cursor-grab text-muted-foreground"
          draggable
          onDragStart={(event) => {
            event.dataTransfer.setData(LEG_DRAG_TYPE, leg.id);
            event.dataTransfer.effectAllowed = "move";
          }}
          aria-hidden="true"
        >
          <GripVertical className="h-4 w-4" />
        </span>
        <div className="flex-1">
          <div className="flex items-center gap-2">
            <span className="text-sm font-medium text-foreground">{leg.label}</span>
            <Badge variant={leg.directionTone} dot>
              {leg.direction}
            </Badge>
            <Badge variant="outline">{leg.instrument}</Badge>
          </div>
        </div>
        <Button
          type="button"
          variant={leg.isSelected ? "secondary" : "ghost"}
          size="sm"
          onClick={onSelect}
          aria-pressed={leg.isSelected}
          aria-label={leg.selectButtonAriaLabel}
        >
          {leg.selectButtonLabel}
        </Button>
        <Button
          type="button"
          variant="ghost"
          onClick={(event) => {
            event.stopPropagation();
            onRemove();
          }}
          aria-label={leg.removeButtonAriaLabel}
        >
          <Trash2 className="h-4 w-4" aria-hidden="true" />
        </Button>
      </div>
      <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
        <LegField
          label="Direction"
          control={
            <Select
              value={leg.direction}
              onChange={(event) => onUpdate({ direction: event.target.value as "Long" | "Short" })}
              aria-label={leg.directionAriaLabel}
              onClick={(event) => event.stopPropagation()}
            >
              <option value="Long">Long</option>
              <option value="Short">Short</option>
            </Select>
          }
        />
        <LegField
          label="Quantity"
          control={
            <Input
              type="number"
              min={1}
              step={1}
              value={leg.quantity}
              onChange={(event) =>
                onUpdate({ quantity: Number.parseFloat(event.target.value) })
              }
              aria-label={leg.quantityAriaLabel}
              onClick={(event) => event.stopPropagation()}
            />
          }
        />
        <LegField
          label={leg.strikeFieldLabel}
          control={
            <Input
              type="number"
              min={0}
              step="0.01"
              value={leg.strike}
              onChange={(event) =>
                onUpdate({ strike: Number.parseFloat(event.target.value) })
              }
              aria-label={leg.strikeAriaLabel}
              onClick={(event) => event.stopPropagation()}
            />
          }
        />
        {leg.isOption ? (
          <LegField
            label="Premium"
            control={
              <Input
                type="number"
                min={0}
                step="0.01"
                value={leg.premium}
                onChange={(event) =>
                  onUpdate({ premium: Number.parseFloat(event.target.value) })
                }
                aria-label={leg.premiumAriaLabel}
                onClick={(event) => event.stopPropagation()}
              />
            }
          />
        ) : (
          <LegField label="Premium" control={<Input value="—" disabled aria-label={leg.premiumUnavailableAriaLabel} />} />
        )}
      </div>
    </div>
  );
}

function isInteractiveCanvasTarget(target: EventTarget): boolean {
  if (!(target instanceof Element)) return false;
  return target.closest("a,button,input,select,textarea,[role='button'],[role='link']") !== null;
}

function LegField({ label, control }: { label: string; control: React.ReactNode }) {
  const id = useId();
  return (
    <label htmlFor={id} className="flex flex-col gap-1 text-[10px] font-medium uppercase tracking-[0.14em] text-muted-foreground">
      {label}
      <span className="block normal-case tracking-normal">
        {control}
      </span>
    </label>
  );
}

interface PayoffCardProps {
  payoff: PayoffChartViewModel;
  legCount: number;
}

function PayoffCard({ payoff, legCount }: PayoffCardProps) {
  return (
    <Card data-testid="strategy-designer-payoff">
      <CardHeader>
        <CardTitle className="text-base">Payoff at expiry</CardTitle>
        <CardDescription>{payoff.caption}</CardDescription>
      </CardHeader>
      <CardContent>
        {payoff.isEmpty ? (
          <div className="flex h-[220px] items-center justify-center rounded-md border border-dashed border-border/70 text-sm text-muted-foreground">
            Add a leg to render the payoff curve.
          </div>
        ) : (
          <svg
            viewBox={`0 0 ${payoff.width} ${payoff.height}`}
            role="img"
            aria-label={payoff.ariaLabel}
            className="w-full max-w-full"
          >
            <rect
              x={payoff.paddingLeft}
              y={payoff.paddingTop}
              width={payoff.width - payoff.paddingLeft - payoff.paddingRight}
              height={payoff.height - payoff.paddingTop - payoff.paddingBottom}
              fill="transparent"
              stroke="hsl(var(--border))"
              strokeOpacity={0.4}
            />
            {payoff.zeroLineY !== null && (
              <line
                x1={payoff.paddingLeft}
                x2={payoff.width - payoff.paddingRight}
                y1={payoff.zeroLineY}
                y2={payoff.zeroLineY}
                stroke="hsl(var(--muted-foreground))"
                strokeDasharray="4 4"
                strokeOpacity={0.6}
              />
            )}
            {payoff.spotLineX !== null && (
              <line
                x1={payoff.spotLineX}
                x2={payoff.spotLineX}
                y1={payoff.paddingTop}
                y2={payoff.height - payoff.paddingBottom}
                stroke="hsl(var(--primary))"
                strokeDasharray="2 4"
                strokeOpacity={0.6}
              />
            )}
            <polyline
              points={payoff.points.map((p) => `${p.x},${p.y}`).join(" ")}
              fill="none"
              stroke="hsl(var(--primary))"
              strokeWidth={1.75}
              strokeLinecap="round"
              strokeLinejoin="round"
              data-testid="strategy-designer-payoff-polyline"
            />
            <text
              x={payoff.paddingLeft}
              y={payoff.height - 8}
              fontSize={10}
              fill="hsl(var(--muted-foreground))"
            >
              {payoff.axisLabels.minPrice}
            </text>
            <text
              x={payoff.width - payoff.paddingRight}
              y={payoff.height - 8}
              fontSize={10}
              fill="hsl(var(--muted-foreground))"
              textAnchor="end"
            >
              {payoff.axisLabels.maxPrice}
            </text>
            <text
              x={payoff.paddingLeft - 6}
              y={payoff.paddingTop + 10}
              fontSize={10}
              fill="hsl(var(--muted-foreground))"
              textAnchor="end"
            >
              {payoff.axisLabels.maxPnl}
            </text>
            <text
              x={payoff.paddingLeft - 6}
              y={payoff.height - payoff.paddingBottom - 2}
              fontSize={10}
              fill="hsl(var(--muted-foreground))"
              textAnchor="end"
            >
              {payoff.axisLabels.minPnl}
            </text>
          </svg>
        )}
        <p className="mt-2 text-xs text-muted-foreground" data-testid="strategy-designer-payoff-legcount">
          Sampled across {legCount} leg{legCount === 1 ? "" : "s"} · spot reference {payoff.axisLabels.minPrice} → {payoff.axisLabels.maxPrice}
        </p>
      </CardContent>
    </Card>
  );
}

interface ParticipationCardProps {
  participation: ParticipationViewModel;
}

function ParticipationCard({ participation }: ParticipationCardProps) {
  return (
    <Card data-testid="strategy-designer-participation">
      <CardHeader>
        <CardTitle className="text-base">Participation by leg</CardTitle>
        <CardDescription>
          {participation.isEmpty
            ? "Add legs to see how each contributes to total notional exposure."
            : `Total notional ${participation.totalNotionalLabel} · ${participation.netDirectionLabel}`}
        </CardDescription>
      </CardHeader>
      <CardContent>
        {participation.isEmpty ? (
          <div className="flex h-[180px] items-center justify-center rounded-md border border-dashed border-border/70 text-sm text-muted-foreground">
            Awaiting first leg.
          </div>
        ) : (
          <ul className="space-y-2" data-testid="strategy-designer-participation-list">
            {participation.slices.map((slice) => (
              <ParticipationRow key={slice.legId} slice={slice} />
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  );
}

function ParticipationRow({ slice }: { slice: ParticipationSliceViewModel }) {
  const toneClass = slice.tone === "long" ? "bg-success" : "bg-warning";
  return (
    <li className="space-y-1">
      <div className="flex items-center justify-between gap-2 text-sm">
        <div className="flex items-center gap-2">
          <Badge variant={slice.tone === "long" ? "success" : "warning"} dot>
            {slice.direction}
          </Badge>
          <span className="font-medium text-foreground">{slice.label}</span>
        </div>
        <span className="font-mono text-xs text-muted-foreground">{slice.sharePercent}</span>
      </div>
      <div
        className="h-2 w-full overflow-hidden rounded-full bg-secondary/40"
        role="presentation"
      >
        <div
          className={cn("h-full rounded-full", toneClass)}
          style={{ width: slice.barWidth }}
          data-testid={`participation-bar-${slice.legId}`}
        />
      </div>
      <div className="text-xs text-muted-foreground">{slice.detail}</div>
    </li>
  );
}
