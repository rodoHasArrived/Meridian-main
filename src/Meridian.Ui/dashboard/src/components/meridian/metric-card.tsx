import { ArrowDownRight, ArrowUpRight, Minus } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { cn } from "@/lib/utils";
import type { MetricSnapshot } from "@/types";

const toneMap: Record<MetricSnapshot["tone"], string> = {
  default: "text-primary",
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger"
};

export function MetricCard({ label, value, delta, tone }: MetricSnapshot) {
  const TrendIcon = delta.startsWith("-") ? ArrowDownRight : delta === "0%" ? Minus : ArrowUpRight;

  return (
    <Card className="overflow-hidden border-border/90 bg-panel-soft/90">
      <CardHeader className="pb-3">
        <div className="eyebrow-label">{label}</div>
      </CardHeader>
      <CardContent className="space-y-3">
        <div className={cn("font-mono text-3xl font-semibold tracking-tight", toneMap[tone])}>{value}</div>
        <div className="flex items-center gap-2 text-sm text-muted-foreground">
          <TrendIcon className={cn("h-4 w-4", toneMap[tone])} />
          <span>{delta} vs prior session</span>
        </div>
      </CardContent>
    </Card>
  );
}
