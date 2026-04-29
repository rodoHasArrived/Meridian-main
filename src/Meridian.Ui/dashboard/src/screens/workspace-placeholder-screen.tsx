import { ArrowRight, CheckCircle2, Clock3 } from "lucide-react";
import { Link } from "react-router-dom";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { buildWorkspacePlaceholderViewModel } from "@/screens/workspace-placeholder-screen.view-model";
import type { SessionInfo, SystemOverviewResponse, WorkspaceSummary } from "@/types";

interface WorkspacePlaceholderScreenProps {
  workspace: WorkspaceSummary;
  session: SessionInfo | null;
  overview: SystemOverviewResponse | null;
}

export function WorkspacePlaceholderScreen({ workspace, session, overview }: WorkspacePlaceholderScreenProps) {
  const viewModel = buildWorkspacePlaceholderViewModel({ workspace, session, overview });

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader>
          <div className="eyebrow-label">Workspace route</div>
          <CardTitle className="flex items-center gap-2">
            <CheckCircle2 className="h-5 w-5 text-success" />
            {viewModel.title}
          </CardTitle>
          <CardDescription>{viewModel.description}</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3 md:grid-cols-3">
          {viewModel.statusCells.map((cell) => (
            <StatusCell key={cell.id} label={cell.label} value={cell.value} />
          ))}
        </CardContent>
      </Card>

      <Card className="border-border/70">
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <Clock3 className="h-4 w-4 text-warning" />
            {viewModel.pendingTitle}
          </CardTitle>
          <CardDescription>{viewModel.pendingDescription}</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3 md:grid-cols-2">
          {viewModel.actions.map((action) => (
            <Link
              key={action.id}
              to={action.route}
              className="flex items-start justify-between gap-3 rounded-lg border border-border/70 bg-secondary/25 px-3 py-3 text-sm hover:bg-secondary/50"
            >
              <span>
                <span className="block font-semibold">{action.label}</span>
                <span className="mt-1 block text-xs leading-5 text-muted-foreground">{action.detail}</span>
              </span>
              <ArrowRight className="mt-0.5 h-4 w-4 shrink-0" />
            </Link>
          ))}
          {viewModel.telemetryCells.map((cell) => (
            <StatusCell key={cell.id} label={cell.label} value={cell.value} />
          ))}
        </CardContent>
      </Card>
    </div>
  );
}

function StatusCell({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-border/70 bg-secondary/25 px-3 py-3">
      <div className="eyebrow-label">{label}</div>
      <div className="mt-2 text-sm font-semibold text-foreground">{value}</div>
    </div>
  );
}
