import { RefreshCw } from "lucide-react";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Progress } from "@/components/ui/progress";
import { EmptyState, MetricCard } from "@/components/data/concrete";
import { SeverityBadge } from "@/components/operations";
import { cn } from "@/lib/utils";

interface BackfillValidation {
  symbol: string;
  isComplete: boolean;
  totalDays: number;
  coveredDays: number;
  completeness: number;
  gaps?: string[];
  firstDataPoint?: string;
  lastDataPoint?: string;
  status: string;
}

interface BackfillCompletenessSummary {
  complete: number;
  good: number;
  poor: number;
  average: number;
}

interface BackfillValidationDashboardProps {
  validations?: BackfillValidation[];
  summary?: BackfillCompletenessSummary;
  onRefresh?: () => Promise<void>;
  isLoading?: boolean;
}

// Concrete severity mapping: Complete reads Ready (spruce-green), Good reads Action
// (ochre — attention, not blocked), Incomplete reads Blocked (brick-red). The original
// human label is preserved via SeverityBadge's `label` prop.
function getStatusSeverity(status: string): string {
  if (status === "Complete") return "Ready";
  if (status === "Good") return "Action";
  if (status === "Incomplete") return "Blocked";
  return "Info";
}

// Completeness-driven tone for the value readout: alpha layer only, semantic tokens.
function getCompletenessToneClass(completeness: number): string {
  if (completeness >= 0.95) return "text-success";
  if (completeness >= 0.8) return "text-warning";
  return "text-danger";
}

export function BackfillValidationDashboard({
  validations = [],
  summary,
  onRefresh,
  isLoading,
}: BackfillValidationDashboardProps) {
  const [isRefreshing, setIsRefreshing] = useState(false);

  const handleRefresh = async () => {
    if (!onRefresh) return;
    setIsRefreshing(true);
    try {
      await onRefresh();
    } finally {
      setIsRefreshing(false);
    }
  };

  return (
    <>
      {/* Summary metrics */}
      {summary && (
        <Card>
          <CardHeader>
            <div className="flex items-center justify-between">
              <div>
                <CardTitle>Backfill Completeness</CardTitle>
                <CardDescription>Overall data coverage across configured symbols.</CardDescription>
              </div>
              <Button
                variant="outline"
                size="sm"
                onClick={handleRefresh}
                disabled={isLoading || isRefreshing}
              >
                <RefreshCw className={cn("h-4 w-4 mr-1.5", (isLoading || isRefreshing) && "animate-spin")} />
                Refresh
              </Button>
            </div>
          </CardHeader>
          <CardContent>
            <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
              <MetricCard label="Complete (≥95%)" value={summary.complete} tone="success" />
              <MetricCard label="Good (80-95%)" value={summary.good} tone="warning" />
              <MetricCard label="Poor (<80%)" value={summary.poor} tone="danger" />
              <MetricCard label="Average Completeness" value={`${Math.round(summary.average * 100)}%`} tone="neutral" />
            </div>
          </CardContent>
        </Card>
      )}

      {/* Symbol validation details */}
      <Card>
        <CardHeader>
          <CardTitle>Symbol-Level Validation</CardTitle>
          <CardDescription>
            {validations.length > 0
              ? `${validations.length} symbols analyzed`
              : "No symbols configured for validation"}
          </CardDescription>
        </CardHeader>
        <CardContent>
          {validations.length === 0 ? (
            <EmptyState
              icon="table"
              title="No symbols configured yet"
              detail="Add symbols to monitor backfill completeness."
            />
          ) : (
            <div className="space-y-3">
              {validations.map((validation) => (
                <div
                  key={validation.symbol}
                  className="rounded-[var(--radius-card,2px)] border border-border/70 bg-background/35 p-4 transition-colors hover:border-border hover:bg-secondary/40"
                >
                  <div className="flex items-center justify-between mb-3">
                    <div className="flex items-center gap-3">
                      <span className="font-semibold text-base text-foreground">{validation.symbol}</span>
                      <SeverityBadge status={getStatusSeverity(validation.status)} label={validation.status} />
                    </div>
                    <div className="text-right">
                      <div className={cn("font-mono text-2xl font-semibold tabular-nums", getCompletenessToneClass(validation.completeness))}>
                        {Math.round(validation.completeness * 100)}%
                      </div>
                      <div className="text-xs text-muted-foreground">
                        {validation.coveredDays} of {validation.totalDays} days
                      </div>
                    </div>
                  </div>

                  {/* Progress bar */}
                  <div className="mb-2">
                    <Progress
                      value={validation.completeness * 100}
                      className="h-2"
                    />
                  </div>

                  {/* Date range */}
                  {validation.firstDataPoint && validation.lastDataPoint && (
                    <div className="font-mono text-xs text-muted-foreground mb-2">
                      {validation.firstDataPoint} to {validation.lastDataPoint}
                    </div>
                  )}

                  {/* Gaps */}
                  {validation.gaps && validation.gaps.length > 0 && (
                    <details className="cursor-pointer">
                      <summary className="text-xs font-semibold text-warning hover:text-warning/80">
                        {validation.gaps.length} gap{validation.gaps.length > 1 ? "s" : ""} detected
                      </summary>
                      <ul className="mt-2 space-y-1 font-mono text-xs text-muted-foreground list-disc list-inside">
                        {validation.gaps.slice(0, 3).map((gap, idx) => (
                          <li key={idx}>{gap}</li>
                        ))}
                        {validation.gaps.length > 3 && (
                          <li>... and {validation.gaps.length - 3} more</li>
                        )}
                      </ul>
                    </details>
                  )}
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>
    </>
  );
}
