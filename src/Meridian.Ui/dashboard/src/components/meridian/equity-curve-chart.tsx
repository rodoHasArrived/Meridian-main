import { useMemo } from "react";
import {
  Area,
  AreaChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis
} from "recharts";
import type { EquityCurveSummary } from "@/types";

interface EquityCurveChartProps {
  data: EquityCurveSummary;
}

interface TooltipPayloadItem {
  dataKey: string;
  value: number;
  color: string;
  name: string;
}

interface CustomTooltipProps {
  active?: boolean;
  label?: string;
  payload?: TooltipPayloadItem[];
}

function CustomTooltip({ active, label, payload }: CustomTooltipProps) {
  if (!active || !payload || payload.length === 0) return null;

  return (
    <div className="rounded-lg border border-border/80 bg-background px-3 py-2 shadow-md text-xs space-y-1">
      <div className="font-semibold text-foreground mb-1">{label}</div>
      {payload.map((item) => (
        <div key={item.dataKey} className="flex items-center gap-2">
          <span className="h-2 w-2 rounded-full shrink-0" style={{ backgroundColor: item.color }} />
          <span className="text-muted-foreground">{item.name}:</span>
          <span className="font-mono font-semibold text-foreground">{item.value.toLocaleString(undefined, { maximumFractionDigits: 2 })}</span>
        </div>
      ))}
    </div>
  );
}

const DRAWDOWN_DOMAIN_PADDING = 1.1;

function formatDate(dateStr: string): string {
  try {
    const d = new Date(dateStr);
    return d.toLocaleDateString(undefined, { month: "short", day: "numeric", year: "2-digit" });
  } catch {
    return dateStr;
  }
}

function formatCurrency(value: number): string {
  if (Math.abs(value) >= 1_000_000) {
    return `$${(value / 1_000_000).toFixed(1)}M`;
  }
  if (Math.abs(value) >= 1_000) {
    return `$${(value / 1_000).toFixed(0)}K`;
  }
  return `$${value.toFixed(0)}`;
}

function formatPercent(value: number): string {
  return `${value.toFixed(1)}%`;
}

export function EquityCurveChart({ data }: EquityCurveChartProps) {
  const equityPoints = useMemo(
    () =>
      data.points.map((p) => ({
        date: formatDate(p.date),
        totalEquity: p.totalEquity,
        cash: p.cash,
        drawdown: -(p.drawdownFromPeakPercent ?? 0),
        dailyReturn: p.dailyReturn * 100
      })),
    [data.points]
  );

  const minDrawdown = useMemo(
    () => Math.min(...equityPoints.map((p) => p.drawdown), 0),
    [equityPoints]
  );

  const tickInterval = Math.max(1, Math.floor(equityPoints.length / 6));

  const xTicks = useMemo(
    () =>
      equityPoints
        .filter((_, i) => i % tickInterval === 0 || i === equityPoints.length - 1)
        .map((p) => p.date),
    [equityPoints, tickInterval]
  );

  if (data.points.length === 0) {
    return (
      <p className="text-sm text-muted-foreground py-6 text-center">No equity curve data available for this run.</p>
    );
  }

  return (
    <div className="space-y-6">
      {/* Summary stats */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        <StatBadge
          label="Initial Equity"
          value={formatCurrency(data.initialEquity)}
          tone="default"
        />
        <StatBadge
          label="Final Equity"
          value={formatCurrency(data.finalEquity)}
          tone={data.finalEquity >= data.initialEquity ? "success" : "danger"}
        />
        <StatBadge
          label="Max Drawdown"
          value={formatPercent(data.maxDrawdownPercent)}
          tone="danger"
        />
        <StatBadge
          label="Sharpe Ratio"
          value={data.sharpeRatio.toFixed(2)}
          tone={data.sharpeRatio >= 1 ? "success" : data.sharpeRatio >= 0 ? "default" : "danger"}
        />
      </div>

      {/* Equity curve chart */}
      <div>
        <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground mb-2">
          Equity Curve
        </div>
        <ResponsiveContainer width="100%" height={220}>
          <AreaChart data={equityPoints} margin={{ top: 4, right: 8, left: 8, bottom: 0 }}>
            <defs>
              <linearGradient id="equityGradient" x1="0" y1="0" x2="0" y2="1">
                <stop offset="5%" stopColor="hsl(var(--success, 142 76% 36%))" stopOpacity={0.25} />
                <stop offset="95%" stopColor="hsl(var(--success, 142 76% 36%))" stopOpacity={0} />
              </linearGradient>
            </defs>
            <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" strokeOpacity={0.5} vertical={false} />
            <XAxis
              dataKey="date"
              ticks={xTicks}
              tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }}
              axisLine={false}
              tickLine={false}
            />
            <YAxis
              tickFormatter={formatCurrency}
              tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }}
              axisLine={false}
              tickLine={false}
              width={64}
            />
            <Tooltip
              content={
                <CustomTooltip />
              }
              labelFormatter={(label) => label as string}
              formatter={(value: number) => [formatCurrency(value), "Total Equity"]}
            />
            <Area
              type="monotone"
              dataKey="totalEquity"
              name="Total Equity"
              stroke="hsl(var(--success, 142 76% 36%))"
              strokeWidth={2}
              fill="url(#equityGradient)"
              dot={false}
              activeDot={{ r: 3 }}
            />
          </AreaChart>
        </ResponsiveContainer>
      </div>

      {/* Drawdown chart */}
      <div>
        <div className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground mb-2">
          Drawdown from Peak
        </div>
        <ResponsiveContainer width="100%" height={140}>
          <AreaChart data={equityPoints} margin={{ top: 4, right: 8, left: 8, bottom: 0 }}>
            <defs>
              <linearGradient id="drawdownGradient" x1="0" y1="0" x2="0" y2="1">
                <stop offset="5%" stopColor="hsl(var(--danger, 0 72% 51%))" stopOpacity={0.35} />
                <stop offset="95%" stopColor="hsl(var(--danger, 0 72% 51%))" stopOpacity={0.05} />
              </linearGradient>
            </defs>
            <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" strokeOpacity={0.5} vertical={false} />
            <XAxis
              dataKey="date"
              ticks={xTicks}
              tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }}
              axisLine={false}
              tickLine={false}
            />
            <YAxis
              tickFormatter={formatPercent}
              tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }}
              axisLine={false}
              tickLine={false}
              width={52}
              domain={[minDrawdown * DRAWDOWN_DOMAIN_PADDING, 0]}
            />
            <Tooltip
              content={<CustomTooltip />}
              formatter={(value: number) => [`${value.toFixed(2)}%`, "Drawdown"]}
            />
            <Area
              type="monotone"
              dataKey="drawdown"
              name="Drawdown"
              stroke="hsl(var(--danger, 0 72% 51%))"
              strokeWidth={1.5}
              fill="url(#drawdownGradient)"
              dot={false}
              activeDot={{ r: 3 }}
            />
          </AreaChart>
        </ResponsiveContainer>
      </div>

      {/* Extra stats row */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
        <StatBadge label="Sortino Ratio" value={data.sortinoRatio.toFixed(2)} tone="default" />
        <StatBadge label="Max DD Recovery" value={`${data.maxDrawdownRecoveryDays} days`} tone="default" />
        <StatBadge
          label="Total Return"
          value={formatPercent(((data.finalEquity - data.initialEquity) / data.initialEquity) * 100)}
          tone={data.finalEquity >= data.initialEquity ? "success" : "danger"}
        />
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------

interface StatBadgeProps {
  label: string;
  value: string;
  tone: "default" | "success" | "danger";
}

function StatBadge({ label, value, tone }: StatBadgeProps) {
  const valueClass =
    tone === "success"
      ? "text-success"
      : tone === "danger"
        ? "text-danger"
        : "text-foreground";

  return (
    <div className="rounded-lg border border-border/70 bg-secondary/20 px-3 py-2">
      <div className="text-xs text-muted-foreground mb-0.5">{label}</div>
      <div className={`text-sm font-mono font-semibold ${valueClass}`}>{value}</div>
    </div>
  );
}
