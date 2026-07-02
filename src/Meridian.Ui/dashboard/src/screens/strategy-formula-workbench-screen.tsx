import { FunctionSquare } from "lucide-react";
import { Link } from "react-router-dom";
import { EmptyState } from "@/components/data/empty-state";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";

export function StrategyFormulaWorkbenchScreen() {
  return (
    <section className="space-y-4" aria-labelledby="strategy-formula-workbench-title">
      <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
        <div className="min-w-0">
          <div className="eyebrow-label">Strategy Lane</div>
          <h1 id="strategy-formula-workbench-title" className="flex items-center gap-2 text-2xl font-semibold tracking-normal text-foreground">
            <FunctionSquare className="h-6 w-6 text-primary" aria-hidden="true" />
            Formula Workbench
          </h1>
          <p className="mt-2 max-w-3xl text-sm leading-6 text-muted-foreground">
            Strategy formula authoring needs a connected formula catalog before fields, saved cells, and local previews can support operator decisions.
          </p>
        </div>
      </div>

      <Card className="panel-surface border-border/80">
        <CardContent className="space-y-4">
          <EmptyState
            icon="docs"
            title="Formula catalog is not connected"
            detail="No strategy formula endpoint is available in this workstation build. Connect a governed catalog before authoring formulas here."
          />
          <div className="flex justify-center">
            <Button asChild>
              <Link to="/strategy">Open Strategy workspace</Link>
            </Button>
          </div>
        </CardContent>
      </Card>
    </section>
  );
}
