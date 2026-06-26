import { CheckCircle2, Send, XCircle } from "lucide-react";
import type { ReportingTemplateLifecycleActionRow } from "@/screens/reporting-screen.view-model";

export function TemplateLifecycleActionIcon({ action }: { action: ReportingTemplateLifecycleActionRow["kind"] }) {
  if (action === "approve") {
    return <CheckCircle2 className="h-4 w-4" aria-hidden="true" />;
  }

  if (action === "reject") {
    return <XCircle className="h-4 w-4" aria-hidden="true" />;
  }

  return <Send className="h-4 w-4" aria-hidden="true" />;
}
