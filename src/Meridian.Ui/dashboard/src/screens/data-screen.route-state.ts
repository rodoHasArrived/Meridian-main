import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import type {
  DataOperationsBackfillDetailState,
  DataOperationsDetailField,
  DataOperationsEmptyState,
  DataOperationsLoadingState
} from "@/screens/data-screen.view-model";

export const DATA_BACKFILL_ROUTE_FOCUS_CARD_ID = "data-backfill-route-focus";

export interface DataOperationsRouteFocusActionState {
  label: string;
  href: string;
  ariaLabel: string;
}

export interface DataOperationsRouteFocusCardState {
  id: string;
  role: "region" | "status";
  ariaLabel: string;
  eyebrow: string;
  title: string;
  description: string;
  rows: DataOperationsDetailField[];
  action: DataOperationsRouteFocusActionState | null;
}

export type DataOperationsWorkstream =
  | "overview"
  | "providers"
  | "import"
  | "backfills"
  | "operations"
  | "assurance"
  | "exports"
  | "query";

export interface DataWorkstationViewState {
  workstream: DataOperationsWorkstream;
  selectedId: string | null;
}

export function resolveDataWorkstream(pathname: string): DataOperationsWorkstream {
  if (pathname.includes("/import")) {
    return "import";
  }

  if (pathname.includes("/providers")) {
    return "providers";
  }

  if (pathname.includes("/backfills")) {
    return "backfills";
  }

  const segments = pathname.split("/");
  if (segments.includes("operations")) {
    return "operations";
  }

  if (segments.includes("assurance")) {
    return "assurance";
  }

  if (pathname.includes("/exports")) {
    return "exports";
  }

  if (pathname.includes("/query")) {
    return "query";
  }

  return "overview";
}

export function buildDataLoadingState(
  workstream: DataOperationsWorkstream = "overview"
): DataOperationsLoadingState {
  const backfillFocus = workstream === "backfills";
  const providersFocus = workstream === "providers";
  const importFocus = workstream === "import";
  const exportsFocus = workstream === "exports";
  const queryFocus = workstream === "query";
  const focusedTitle = backfillFocus
    ? "Loading backfill queue"
    : providersFocus
      ? "Loading Data providers"
      : importFocus
        ? "Loading Data import"
        : exportsFocus
          ? "Loading Data exports"
          : queryFocus
            ? "Loading SQL query workspace"
            : "Loading Data workspace";
  const focusedDescription = backfillFocus
    ? "Waiting for historical repair jobs, provider pressure, and review-required backfills."
    : providersFocus
      ? "Waiting for provider status, credential posture, and setup evidence."
      : importFocus
        ? "Waiting for governed upload templates and retained-file preview readiness."
        : exportsFocus
          ? "Waiting for retained export records and handoff readiness."
          : queryFocus
            ? "Waiting for the read-only query workspace and catalog posture."
            : "Waiting for provider posture, market data health, and export evidence.";

  return {
    title: focusedTitle,
    description: focusedDescription,
    statusLabel: "Workspace data pending",
    detail: backfillFocus
      ? "Queued and review-required jobs will appear here as soon as workspace data is available."
      : providersFocus
        ? "Provider health, credential verification, and routing posture will appear when workspace data is available."
        : importFocus
          ? "Upload templates, field guidance, and retained-source preview controls will appear when workspace data is available."
          : exportsFocus
            ? "Export packages and selected export details will appear when workspace data is available."
            : queryFocus
              ? "Saved query, recent query, and result controls will appear when the workspace is ready."
              : "Provider health, data-quality handoffs, and export readiness will appear when workspace data is available.",
    regionLabel: backfillFocus
      ? "Data backfill loading state"
      : importFocus
        ? "Data import loading state"
        : "Data workspace loading state",
    role: "status",
    ariaLive: "polite",
    ariaBusy: true,
    chips: [
      { label: "Providers", value: "Pending" },
      { label: "Data quality", value: "Pending" },
      { label: backfillFocus ? "Backfills" : importFocus ? "Upload templates" : "Exports", value: "Pending" }
    ],
    actions: [
      {
        id: "settings",
        label: "Check provider setup",
        href: WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup,
        ariaLabel: "Open Alpaca paper provider setup while Data workspace loads",
        variant: "default"
      },
      {
        id: "quotes",
        label: "Open live quotes",
        href: WORKSTATION_ROUTE_CATALOG.dataQuotes,
        ariaLabel: "Open live quotes while Data workspace loads",
        variant: "outline"
      }
    ]
  };
}

export function buildRouteFocusCardState({
  workstream,
  selectedBackfillDetail,
  backfillDetailEmptyState
}: {
  workstream: DataOperationsWorkstream;
  selectedBackfillDetail: DataOperationsBackfillDetailState | null;
  backfillDetailEmptyState: DataOperationsEmptyState | null;
}): DataOperationsRouteFocusCardState {
  if (workstream === "backfills") {
    if (selectedBackfillDetail) {
      return {
        id: DATA_BACKFILL_ROUTE_FOCUS_CARD_ID,
        role: "region",
        ariaLabel: "Backfill route focus",
        eyebrow: "Backfill Detail",
        title: "Backfill queue focus",
        description: selectedBackfillDetail.description,
        rows: selectedBackfillDetail.rows,
        action: null
      };
    }

    const title = backfillDetailEmptyState?.title ?? "Backfill queue focus";
    const description = backfillDetailEmptyState?.description ?? "No backfill selected.";
    return {
      id: DATA_BACKFILL_ROUTE_FOCUS_CARD_ID,
      role: "status",
      ariaLabel: "Backfill route focus empty state",
      eyebrow: "Backfill Detail",
      title,
      description,
      rows: [],
      action: null
    };
  }

  if (workstream === "providers") {
    return {
      id: "data-route-focus-providers",
      role: "region",
      ariaLabel: "Provider route focus",
      eyebrow: "Provider Setup",
      title: "Provider status and setup",
      description: "Credential posture, verification evidence, routing trust, and provider setup stay on the provider route.",
      rows: [
        { id: "provider-status", label: "Provider status", value: "Catalog and readiness" },
        { id: "credentials", label: "Credentials", value: "Masked and auditable" },
        { id: "routing", label: "Routing", value: "Backup and trust snapshots" }
      ],
      action: null
    };
  }

  if (workstream === "import") {
    return {
      id: "data-route-focus-import",
      role: "region",
      ariaLabel: "Data import route focus",
      eyebrow: "File Intake",
      title: "Governed file import",
      description: "Choose a server-provided template, preview a retained source file, and review validation evidence before downstream handoff.",
      rows: [
        { id: "template", label: "Template", value: "Server-owned catalog" },
        { id: "preview", label: "Preview", value: "Retained source evidence" },
        { id: "handoff", label: "Handoff", value: "Validation and reconciliation" }
      ],
      action: null
    };
  }

  if (workstream === "exports") {
    return {
      id: "data-route-focus-exports",
      role: "region",
      ariaLabel: "Exports route focus",
      eyebrow: "Export Records",
      title: "Export package review",
      description: "Retained export records and handoff readiness are isolated from provider setup and query work.",
      rows: [
        { id: "records", label: "Records", value: "Recent exports" },
        { id: "handoff", label: "Handoff", value: "Reporting and package outputs" },
        { id: "selected-export", label: "Inspector", value: "Selected export detail" }
      ],
      action: null
    };
  }

  if (workstream === "query") {
    return {
      id: "data-route-focus-query",
      role: "region",
      ariaLabel: "Query route focus",
      eyebrow: "SQL Query",
      title: "Read-only query workspace",
      description: "SQL querying is separated from monitoring and setup work while keeping local-store analysis in the Data workspace.",
      rows: [
        { id: "catalog", label: "Catalog", value: "meridian_files" },
        { id: "history", label: "History", value: "Recent and saved queries" },
        { id: "exports", label: "Results", value: "Copy or download CSV" }
      ],
      action: null
    };
  }

  return {
    id: "data-route-focus-overview",
    role: "region",
    ariaLabel: "Data workspace route focus",
    eyebrow: "Lane Evidence",
    title: "Provider and export readiness",
    description: "Keep provider recovery, backfill pressure, and export handoffs visible while Data prepares inputs for Accounting and Reporting.",
    rows: [
      { id: "primary-checks", label: "Primary checks", value: "Providers / backfills / exports" },
      { id: "security-coverage", label: "Security coverage", value: "Accounting lane" },
      { id: "operator-handoff", label: "Operator handoff", value: "Reporting export evidence" }
    ],
    action: {
      label: "Open Security Master",
      href: WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster,
      ariaLabel: "Open Security Master in Accounting"
    }
  };
}
