import {
  AlertTriangle,
  CheckCircle2,
  ChevronDown,
  FlaskConical,
  Loader2,
  Pencil,
  Plus,
  RotateCcw,
  Trash2,
  XCircle,
} from "lucide-react";
import { useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { StatusBanner } from "@/components/ui/status-banner";
import { Toggle } from "@/components/ui/checkbox";
import { ProviderCapabilityBadges } from "@/components/data/provider-capability-badges";
import { AddProviderDrawer } from "@/components/data/add-provider-drawer";
import { EditProviderDrawer } from "@/components/data/edit-provider-drawer";
import { useProviderSetup } from "@/lib/provider-setup/use-provider-setup";
import type { ProviderModuleStatus } from "@/types/provider-setup";
import { cn } from "@/lib/utils";

export function ProviderSetupPanel() {
  const {
    modules,
    catalogue,
    loading,
    error,
    pendingRestart,
    refresh,
    addModule,
    editModule,
    removeModule,
    toggleEnabled,
    testModule,
    triggerRestart,
  } = useProviderSetup();

  const [addOpen, setAddOpen] = useState(false);
  const [editTarget, setEditTarget] = useState<ProviderModuleStatus | null>(null);
  const [restartBusy, setRestartBusy] = useState(false);
  const [testingId, setTestingId] = useState<string | null>(null);
  const [togglingId, setTogglingId] = useState<string | null>(null);
  const [removingId, setRemovingId] = useState<string | null>(null);
  const [testResults, setTestResults] = useState<Record<string, boolean | null>>({});

  const handleRestart = async () => {
    setRestartBusy(true);
    try {
      await triggerRestart();
    } finally {
      setRestartBusy(false);
    }
  };

  const handleToggle = async (moduleId: string, enabled: boolean) => {
    setTogglingId(moduleId);
    try {
      await toggleEnabled(moduleId, enabled);
    } finally {
      setTogglingId(null);
    }
  };

  const handleTest = async (moduleId: string) => {
    setTestingId(moduleId);
    try {
      const result = await testModule(moduleId);
      setTestResults(prev => ({ ...prev, [moduleId]: result?.success ?? null }));
    } finally {
      setTestingId(null);
    }
  };

  const handleRemove = async (moduleId: string) => {
    setRemovingId(moduleId);
    try {
      await removeModule(moduleId);
    } finally {
      setRemovingId(null);
    }
  };

  return (
    <div className="space-y-4">
      {pendingRestart && (
        <StatusBanner
          tone="warning"
          title="Restart required"
          detail="Provider configuration has changed. Restart the provider host to apply changes."
        >
          <div className="mt-2">
            <Button
              size="sm"
              variant="outline"
              busy={restartBusy}
              busyLabel="Restarting…"
              onClick={() => void handleRestart()}
            >
              <RotateCcw className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
              Restart provider host
            </Button>
          </div>
        </StatusBanner>
      )}

      {error && (
        <StatusBanner
          tone="danger"
          title="Failed to load provider modules"
          detail={error}
        />
      )}

      <Card>
        <CardHeader>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div>
              <div className="eyebrow-label">Data ingestion</div>
              <CardTitle className="mt-2">Provider Modules</CardTitle>
              <CardDescription className="mt-1">
                Configure and manage market data provider adapters. Each provider can supply streaming quotes,
                historical backfill, symbol search, or brokerage execution.
              </CardDescription>
            </div>
            <div className="flex shrink-0 items-center gap-2">
              <Button
                size="sm"
                variant="outline"
                onClick={() => void refresh()}
                disabled={loading}
              >
                {loading ? (
                  <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" aria-hidden="true" />
                ) : (
                  <ChevronDown className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
                )}
                Refresh
              </Button>
              <Button size="sm" onClick={() => setAddOpen(true)}>
                <Plus className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
                Add provider
              </Button>
            </div>
          </div>
        </CardHeader>

        <CardContent>
          {loading && modules.length === 0 ? (
            <div className="flex items-center gap-2 py-6 text-sm text-muted-foreground">
              <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
              Loading provider modules…
            </div>
          ) : modules.length === 0 ? (
            <EmptyState onAdd={() => setAddOpen(true)} />
          ) : (
            <div className="grid gap-3">
              {modules.map(mod => (
                <ProviderModuleCard
                  key={mod.moduleId}
                  module={mod}
                  testResult={testResults[mod.moduleId] ?? undefined}
                  testingNow={testingId === mod.moduleId}
                  togglingNow={togglingId === mod.moduleId}
                  removingNow={removingId === mod.moduleId}
                  onToggle={(enabled) => void handleToggle(mod.moduleId, enabled)}
                  onTest={() => void handleTest(mod.moduleId)}
                  onEdit={() => setEditTarget(mod)}
                  onRemove={() => void handleRemove(mod.moduleId)}
                />
              ))}
            </div>
          )}
        </CardContent>
      </Card>

      <AddProviderDrawer
        open={addOpen}
        catalogue={catalogue}
        onClose={() => setAddOpen(false)}
        onAdd={addModule}
      />

      {editTarget && (
        <EditProviderDrawer
          open={true}
          module={editTarget}
          onClose={() => setEditTarget(null)}
          onEdit={(req) => editModule(editTarget.moduleId, req)}
        />
      )}
    </div>
  );
}

interface ModuleCardProps {
  module: ProviderModuleStatus;
  testResult?: boolean;
  testingNow: boolean;
  togglingNow: boolean;
  removingNow: boolean;
  onToggle: (enabled: boolean) => void;
  onTest: () => void;
  onEdit: () => void;
  onRemove: () => void;
}

function ProviderModuleCard({
  module,
  testResult,
  testingNow,
  togglingNow,
  removingNow,
  onToggle,
  onTest,
  onEdit,
  onRemove,
}: ModuleCardProps) {
  const configured = module.isConfigured;
  const running = module.isRunning;

  return (
    <article className="rounded-md border border-border/70 bg-background/35 p-3">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div className="min-w-0 flex-1 space-y-2">
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-sm font-semibold text-foreground">{module.displayName}</span>
            <span className="font-mono text-xs text-muted-foreground">{module.moduleId}</span>
            {running ? (
              <Badge variant="success" dot>Running</Badge>
            ) : configured ? (
              <Badge variant="warning">Stopped</Badge>
            ) : (
              <Badge variant="outline">Not configured</Badge>
            )}
            {testResult === true && (
              <span className="inline-flex items-center gap-1 text-xs text-success">
                <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
                Connected
              </span>
            )}
            {testResult === false && (
              <span className="inline-flex items-center gap-1 text-xs text-danger">
                <XCircle className="h-3.5 w-3.5" aria-hidden="true" />
                Failed
              </span>
            )}
          </div>

          <ProviderCapabilityBadges capabilities={module.capabilityNames} />

          <CredentialStatusRow credentials={module.credentialStatus} />
        </div>

        <div className="flex shrink-0 flex-wrap items-center gap-2">
          <Toggle
            checked={module.enabled}
            disabled={togglingNow}
            onCheckedChange={onToggle}
            aria-label={module.enabled ? "Disable provider" : "Enable provider"}
          />
          <Button
            size="sm"
            variant="ghost"
            busy={testingNow}
            busyLabel="Testing…"
            onClick={onTest}
            aria-label="Test connection"
            title="Test connection"
          >
            <FlaskConical className="h-3.5 w-3.5" aria-hidden="true" />
          </Button>
          <Button
            size="sm"
            variant="ghost"
            onClick={onEdit}
            aria-label="Edit provider"
            title="Edit provider"
          >
            <Pencil className="h-3.5 w-3.5" aria-hidden="true" />
          </Button>
          <Button
            size="sm"
            variant="ghost"
            busy={removingNow}
            busyLabel="Removing…"
            onClick={onRemove}
            aria-label="Remove provider"
            title="Remove provider"
            className="text-danger hover:text-danger"
          >
            <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
          </Button>
        </div>
      </div>

      {module.priority !== 100 && (
        <p className="mt-2 text-xs text-muted-foreground">Priority: {module.priority}</p>
      )}
    </article>
  );
}

function CredentialStatusRow({ credentials }: { credentials: ProviderModuleStatus["credentialStatus"] }) {
  if (credentials.length === 0) return null;
  return (
    <div className="flex flex-wrap items-center gap-2">
      {credentials.map(c => (
        <span key={c.key} className="inline-flex items-center gap-1 text-xs">
          <span
            className={cn(
              "inline-block h-2 w-2 rounded-full",
              c.hasValue ? "bg-success" : "bg-border"
            )}
            aria-hidden="true"
          />
          <span className={c.hasValue ? "text-foreground" : "text-muted-foreground"}>
            {c.key}
          </span>
        </span>
      ))}
    </div>
  );
}

function EmptyState({ onAdd }: { onAdd: () => void }) {
  return (
    <div className="flex flex-col items-center gap-3 py-10 text-center">
      <AlertTriangle className="h-8 w-8 text-muted-foreground/50" aria-hidden="true" />
      <div>
        <p className="text-sm font-medium text-foreground">No provider modules configured</p>
        <p className="mt-1 text-xs text-muted-foreground">
          Add a provider to start receiving market data.
        </p>
      </div>
      <Button size="sm" onClick={onAdd}>
        <Plus className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
        Add provider
      </Button>
    </div>
  );
}
