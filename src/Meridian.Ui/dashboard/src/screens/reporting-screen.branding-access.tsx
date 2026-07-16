import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import type {
  AccountingWorkspaceResponse,
  ReportBrandingTheme
} from "@/types";

export type ReportBrandingDraftField =
  | "themeId"
  | "name"
  | "firmName"
  | "primaryColor"
  | "accentColor"
  | "textColor"
  | "backgroundColor"
  | "logoUri"
  | "footerText"
  | "disclaimer";

export interface ReportBrandingDraftState {
  themeId: string;
  name: string;
  firmName: string;
  primaryColor: string;
  accentColor: string;
  textColor: string;
  backgroundColor: string;
  logoUri: string;
  footerText: string;
  disclaimer: string;
}

interface ReportingBrandingAccessPanelProps {
  themes: ReportBrandingTheme[];
  draft: ReportBrandingDraftState;
  onDraftChange: (field: ReportBrandingDraftField, value: string) => void;
}

export function ReportingBrandingAccessPanel({
  themes,
  draft,
  onDraftChange
}: ReportingBrandingAccessPanelProps) {
  if (themes.length === 0) {
    return null;
  }

  return (
    <section role="region" aria-label="Report branding themes">
      <Card className="panel-surface">
        <CardHeader>
          <div className="eyebrow-label">Branding</div>
          <CardTitle>Investor-ready styling themes</CardTitle>
          <CardDescription>Report packs carry shared firm identity, colors, footer text, and disclaimer metadata into generated documents.</CardDescription>
        </CardHeader>
        <CardContent>
          <div role="list" aria-label="Report branding theme rows" className="grid gap-3 lg:grid-cols-3">
            {themes.map((theme) => (
              <div
                key={theme.themeId}
                role="listitem"
                aria-label={`${theme.name} report branding theme`}
                className="rounded-md border border-border/70 bg-secondary/20 px-3 py-2"
              >
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <span className="min-w-0">
                    <span className="block font-semibold text-foreground">{theme.name}</span>
                    <span className="mt-1 block text-xs text-muted-foreground">{theme.firmName}</span>
                  </span>
                  <Badge variant="outline">{theme.isBuiltIn ? "Built-in" : "Custom"}</Badge>
                </div>
                <div className="mt-3 flex flex-wrap gap-2" aria-label={`${theme.name} color palette`}>
                  {[
                    ["Primary", theme.primaryColor],
                    ["Accent", theme.accentColor],
                    ["Text", theme.textColor],
                    ["Background", theme.backgroundColor]
                  ].map(([label, color]) => (
                    <span key={`${theme.themeId}-${label}`} className="inline-flex items-center gap-1.5 text-xs text-muted-foreground">
                      <span
                        aria-hidden="true"
                        className="h-4 w-4 rounded-sm border border-border"
                        style={{ backgroundColor: color }}
                      />
                      <span className="font-mono">{color}</span>
                    </span>
                  ))}
                </div>
                <p className="mt-3 text-xs leading-5 text-muted-foreground">
                  {theme.footerText ?? "No footer text"} · {theme.disclaimer ?? "No disclaimer"}
                </p>
                <p className="mt-2 break-all font-mono text-[11px] text-muted-foreground">{theme.logoUri ?? theme.themeId}</p>
              </div>
            ))}
          </div>
          <div role="group" aria-label="Custom report branding override" className="mt-3 rounded-md border border-border/70 bg-background/30 px-3 py-3">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div className="min-w-0">
                <h4 className="text-sm font-semibold text-foreground">Custom styling override</h4>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">
                  Stage firm-specific colors, logo, footer, and disclaimer metadata for a schedule or governed run.
                </p>
              </div>
              <Badge variant="outline">Override</Badge>
            </div>
            <div className="mt-3 grid gap-2 md:grid-cols-3">
              <label className="space-y-1">
                <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Theme ID</span>
                <Input
                  value={draft.themeId}
                  onChange={(event) => onDraftChange("themeId", event.target.value)}
                  aria-label="Custom branding theme ID"
                  className="font-mono"
                />
              </label>
              <label className="space-y-1">
                <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Theme name</span>
                <Input
                  value={draft.name}
                  onChange={(event) => onDraftChange("name", event.target.value)}
                  aria-label="Custom branding theme name"
                />
              </label>
              <label className="space-y-1">
                <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Firm</span>
                <Input
                  value={draft.firmName}
                  onChange={(event) => onDraftChange("firmName", event.target.value)}
                  aria-label="Custom branding firm name"
                />
              </label>
              {([
                ["primaryColor", "Primary", draft.primaryColor],
                ["accentColor", "Accent", draft.accentColor],
                ["textColor", "Text", draft.textColor],
                ["backgroundColor", "Background", draft.backgroundColor]
              ] as const).map(([field, label, value]) => (
                <label key={field} className="space-y-1">
                  <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">{label}</span>
                  <span className="flex items-center gap-2">
                    <span
                      aria-hidden="true"
                      className="h-6 w-6 shrink-0 rounded-sm border border-border"
                      style={{ backgroundColor: normalizeBrandingColor(value, "#FFFFFF") }}
                    />
                    <Input
                      value={value}
                      onChange={(event) => onDraftChange(field, event.target.value)}
                      aria-label={`Custom branding ${label.toLowerCase()} color`}
                      className="font-mono"
                    />
                  </span>
                </label>
              ))}
              <label className="space-y-1 md:col-span-2">
                <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Logo URI</span>
                <Input
                  value={draft.logoUri}
                  onChange={(event) => onDraftChange("logoUri", event.target.value)}
                  aria-label="Custom branding logo URI"
                />
              </label>
            </div>
            <div className="mt-2 grid gap-2 md:grid-cols-2">
              <label className="space-y-1">
                <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Footer</span>
                <Input
                  value={draft.footerText}
                  onChange={(event) => onDraftChange("footerText", event.target.value)}
                  aria-label="Custom branding footer text"
                />
              </label>
              <label className="space-y-1">
                <span className="text-[10px] uppercase tracking-[0.14em] text-muted-foreground">Disclaimer</span>
                <Input
                  value={draft.disclaimer}
                  onChange={(event) => onDraftChange("disclaimer", event.target.value)}
                  aria-label="Custom branding disclaimer"
                />
              </label>
            </div>
            <div className="mt-3 flex flex-wrap items-center justify-between gap-3 rounded-md border border-primary/25 bg-primary/10 px-3 py-2">
              <p role="status" className="max-w-3xl text-xs leading-5 text-primary">
                Pack preview and generation now use the canonical governed-run workflow, where readiness, certified artifacts, and release evidence are server-owned.
              </p>
              <Button asChild type="button" size="sm" variant="outline">
                <Link to={WORKSTATION_ROUTE_CATALOG.reportingRunParameters}>Configure governed run</Link>
              </Button>
            </div>
          </div>
        </CardContent>
      </Card>
    </section>
  );
}

export function buildDefaultReportBrandingDraft(reporting: AccountingWorkspaceResponse["reporting"] | null): ReportBrandingDraftState {
  const theme = reporting?.brandingThemes?.find((item) => !item.isBuiltIn)
    ?? reporting?.brandingThemes?.[0]
    ?? null;

  return {
    themeId: normalizeIdentifierToken(theme?.themeId, "custom-report-branding"),
    name: normalizeDraftText(theme?.name, "Custom Report Theme"),
    firmName: normalizeDraftText(theme?.firmName, "Meridian Reporting"),
    primaryColor: normalizeBrandingColor(theme?.primaryColor, "#195E63"),
    accentColor: normalizeBrandingColor(theme?.accentColor, "#C99700"),
    textColor: normalizeBrandingColor(theme?.textColor, "#111827"),
    backgroundColor: normalizeBrandingColor(theme?.backgroundColor, "#FFFFFF"),
    logoUri: normalizeDraftText(theme?.logoUri, ""),
    footerText: normalizeDraftText(theme?.footerText, "Confidential report pack."),
    disclaimer: normalizeDraftText(theme?.disclaimer, "Prepared for authorized investor review.")
  };
}

export function buildReportBrandingOverride(draft: ReportBrandingDraftState): ReportBrandingTheme {
  const themeId = normalizeIdentifierToken(draft.themeId, "custom-report-branding");
  const name = normalizeDraftText(draft.name, "Custom Report Theme");
  const logoUri = normalizeDraftText(draft.logoUri, "");
  const footerText = normalizeDraftText(draft.footerText, "");
  const disclaimer = normalizeDraftText(draft.disclaimer, "");

  return {
    themeId,
    name,
    firmName: normalizeDraftText(draft.firmName, "Meridian Reporting"),
    primaryColor: normalizeBrandingColor(draft.primaryColor, "#195E63"),
    accentColor: normalizeBrandingColor(draft.accentColor, "#C99700"),
    textColor: normalizeBrandingColor(draft.textColor, "#111827"),
    backgroundColor: normalizeBrandingColor(draft.backgroundColor, "#FFFFFF"),
    logoUri: logoUri || null,
    footerText: footerText || null,
    disclaimer: disclaimer || null,
    isBuiltIn: false
  };
}

function normalizeBrandingColor(value: string | null | undefined, fallback: string): string {
  const normalized = normalizeDraftText(value, fallback).replace(/^#?([0-9A-Fa-f]{6})$/, "#$1");
  return /^#[0-9A-Fa-f]{6}$/.test(normalized)
    ? normalized.toUpperCase()
    : fallback;
}

function normalizeDraftText(value: string | null | undefined, fallback: string): string {
  const normalized = value?.trim();
  return normalized || fallback;
}

function normalizeIdentifierToken(value: string | null | undefined, fallback: string): string {
  const normalized = normalizeDraftText(value, fallback)
    .replace(/[^A-Za-z0-9_.-]+/g, "-")
    .replace(/^-+|-+$/g, "");
  return normalized || fallback;
}
