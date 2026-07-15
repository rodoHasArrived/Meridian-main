import { FunctionSquare } from "lucide-react";
import { Link } from "react-router-dom";
import { EmptyState } from "@/components/data/empty-state";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { ScreenLayout } from "@/components/ui/screen-layout";

export function StrategyFormulaWorkbenchScreen() {
  return (
    <ScreenLayout
      title={
        <span className="flex items-center gap-2">
          <FunctionSquare className="h-6 w-6 text-primary" aria-hidden="true" />
          Formula Workbench
        </span>
      }
      scope="Strategy Lane"
      description="Strategy formula authoring needs a connected formula catalog before fields, saved cells, and local previews can support operator decisions."
    >
      <Card className="panel-surface border-border/80">
        <CardContent className="space-y-4">
          <EmptyState
            icon="docs"
            title="Formula catalog is not connected"
            detail="No strategy formula endpoint is available in this workstation build. Connect a governed catalog before authoring formulas here."
          />
          <div className="flex flex-wrap justify-center gap-2">
            <Button asChild variant="outline">
              <Link to="/settings/providers" aria-label="Review provider connections required by Formula Workbench">
                Review provider connections
              </Link>
            </Button>
            <Button asChild>
              <Link to="/strategy">Open Strategy workspace</Link>
            </Button>
          </div>
        </CardContent>
      </Card>
    </ScreenLayout>
  );
}
