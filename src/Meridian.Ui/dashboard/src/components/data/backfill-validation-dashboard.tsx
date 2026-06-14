import { AlertCircle, CheckCircle2, AlertTriangle, RefreshCw } from "lucide-react";
import { useState, useEffect } from "react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Progress } from "@/components/ui/progress";
import { Badge } from "@/components/ui/badge";
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

function getStatusColor(completeness: number): string {
  if (completeness >= 0.95) return "bg-green-500";
  if (completeness >= 0.8) return "bg-yellow-500";
  return "bg-red-500";
}

function getStatusBadgeVariant(status: string): "success" | "warning" | "danger" | "default" {
  if (status === "Complete") return "success";
  if (status === "Good") return "warning";
  if (status === "Incomplete") return "danger";
  return "default";
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
                aria-label={isRefreshing ? "Refreshing backfill data…" : "Refresh backfill data"}
              >
                <RefreshCw className={cn("h-4 w-4 mr-1.5", (isLoading || isRefreshing) && "animate-spin")} aria-hidden="true" />
                Refresh
              </Button>
            </div>
          </CardHeader>
          <CardContent>
            <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
              <div className="p-4 rounded-lg border bg-card">
                <div className="text-sm text-muted-foreground">Complete (≥95%)</div>
                <div className="text-3xl font-bold text-green-600 mt-1">{summary.complete}</div>
              </div>
              <div className="p-4 rounded-lg border bg-card">
                <div className="text-sm text-muted-foreground">Good (80-95%)</div>
                <div className="text-3xl font-bold text-yellow-600 mt-1">{summary.good}</div>
              </div>
              <div className="p-4 rounded-lg border bg-card">
                <div className="text-sm text-muted-foreground">Poor (&lt;80%)</div>
                <div className="text-3xl font-bold text-red-600 mt-1">{summary.poor}</div>
              </div>
              <div className="p-4 rounded-lg border bg-card">
                <div className="text-sm text-muted-foreground">Average Completeness</div>
                <div className="text-3xl font-bold mt-1">{Math.round(summary.average * 100)}%</div>
              </div>
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
            <div className="flex flex-col items-center justify-center py-8 text-center">
              <AlertCircle className="h-8 w-8 text-muted-foreground mb-2" aria-hidden="true" />
              <p className="text-sm text-muted-foreground">
                No symbols configured yet. Add symbols to monitor backfill completeness.
              </p>
            </div>
          ) : (
            <div className="space-y-3">
              {validations.map((validation) => (
                <div
                  key={validation.symbol}
                  className="p-4 rounded-lg border hover:bg-muted/50 transition-colors"
                >
                  <div className="flex items-center justify-between mb-3">
                    <div className="flex items-center gap-3">
                      <span className="font-semibold text-base">{validation.symbol}</span>
                      <Badge variant={getStatusBadgeVariant(validation.status)} className="text-xs">
                        {validation.status}
                      </Badge>
                    </div>
                    <div className="text-right">
                      <div className="text-2xl font-bold">{Math.round(validation.completeness * 100)}%</div>
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
                    <div className="text-xs text-muted-foreground mb-2">
                      {validation.firstDataPoint} to {validation.lastDataPoint}
                    </div>
                  )}

                  {/* Gaps */}
                  {validation.gaps && validation.gaps.length > 0 && (
                    <details className="cursor-pointer">
                      <summary className="text-xs font-semibold text-yellow-700 hover:text-yellow-900">
                        {validation.gaps.length} gap{validation.gaps.length > 1 ? "s" : ""} detected
                      </summary>
                      <ul className="mt-2 space-y-1 text-xs text-muted-foreground list-disc list-inside">
                        {validation.gaps.slice(0, 3).map((gap) => (
                          <li key={gap}>{gap}</li>
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
