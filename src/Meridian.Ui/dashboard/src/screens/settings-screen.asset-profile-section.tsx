// Asset profile governance section of the Settings screen: its private state/handlers hook and
// presentational component.
import { SeverityBadge } from "@/components/operations";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { approveSecurityAssetProfile, createSecurityMasterEntry, draftSecurityAssetProfile, getSecurityAssetProfileLineage, rollbackSecurityAssetProfile } from "@/lib/api";
import { describeApiError } from "@/lib/api-errors";
import { utcToday } from "@/lib/local-date";
import { cn } from "@/lib/utils";
import { type SettingsMutationConfirmation } from "@/screens/settings-mutation-confirm-dialog";
import { type SettingsScreenViewModel } from "@/screens/settings-screen.view-model";
import type { SecurityAssetProfileDefinition, SecurityAssetProfileFieldDefinition, SessionInfo } from "@/types";
import { GitBranch, RefreshCcw, Save, ShieldCheck } from "lucide-react";
import { type FormEvent } from "react";
import { buildProfileFieldPayload, createProfileBackedSecurityState, missingRequiredIdentifierDetails, type ProfileBackedSecurityState } from "./settings-profile-backed-security";
import { diagnosticToneClass, FilterSelect, SettingsFieldRow } from "./settings-provider-integration";
import { SettingsChip, capabilityTone, settingsBannerTone } from "@/screens/settings-screen.section-primitives";
import { createAssetProfileDraftState, type AssetProfileDraftState } from "@/screens/settings-screen.section-state";
import { toSeverity } from "@/design-system/status-styles";

function AssetProfileFieldInput({
  field,
  value,
  disabled,
  onChange
}: {
  field: SecurityAssetProfileFieldDefinition;
  value: string;
  disabled: boolean;
  onChange: (value: string) => void;
}) {
  if (field.fieldType === "Boolean") {
    if (!field.isRequired) {
      // TRI-STATE for optional Booleans: blank means "not asserted" and stays omitted from the
      // payload, so an operator who touched the control can still return the field to absence -
      // a two-state checkbox renders blank and false identically and would trap the field in a
      // meaningful negative assertion once toggled.
      return (
        <FilterSelect
          label={field.label}
          value={value}
          onChange={onChange}
          options={[
            { value: "", label: "Not asserted" },
            { value: "true", label: "Yes" },
            { value: "false", label: "No" }
          ]}
          disabled={disabled}
        />
      );
    }

    return (
      <Checkbox
        checked={value === "true"}
        onCheckedChange={(checked) => onChange(checked ? "true" : "false")}
        className="min-h-16 rounded-md border border-border/60 bg-secondary/15 px-3 py-2 text-xs"
        disabled={disabled}
        label={`${field.label} *`}
      />
    );
  }

  if (field.fieldType === "Enum") {
    return (
      <FilterSelect
        label={`${field.label}${field.isRequired ? " *" : ""}`}
        value={value}
        onChange={onChange}
        options={[
          { value: "", label: "Select value" },
          ...field.allowedValues.map((allowed) => ({ value: allowed, label: allowed }))
        ]}
        disabled={disabled}
      />
    );
  }

  return (
    <label className="grid gap-1 text-xs font-medium text-muted-foreground">
      {field.label}{field.isRequired ? " *" : ""}
      <Input
        type={assetProfileInputType(field)}
        min={field.minValue ?? undefined}
        max={field.maxValue ?? undefined}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        disabled={disabled}
        aria-label={`Profile field ${field.label}`}
      />
    </label>
  );
}

function assetProfileInputType(field: SecurityAssetProfileFieldDefinition): "text" | "number" | "date" {
  if (field.fieldType === "Date") return "date";
  if (field.fieldType === "Decimal" || field.fieldType === "Integer") return "number";
  return "text";
}

function normalizeAssetProfileId(value: string): string {
  return value.trim().toLowerCase().replace(/[^a-z0-9-]+/g, "-").replace(/^-+|-+$/g, "");
}

/**
 * Today as an asset-profile `effectiveFrom`. Deliberately the UTC calendar day, the same day
 * `isWriteSelectableAssetProfile` compares effective windows against, so a profile approved here
 * is selectable for writes straight away for every operator.
 */
function todayDateOnly(): string {
  return utcToday();
}

function createBrowserGuid(): string {
  if (typeof crypto !== "undefined" && "randomUUID" in crypto) {
    return crypto.randomUUID();
  }

  return "10000000-1000-4000-8000-100000000000".replace(/[018]/g, (character) => {
    const value = Number(character);
    return (value ^ (Math.random() * 16 >> (value / 4))).toString(16);
  });
}

interface UseSettingsAssetProfileSectionStateParams {
  approvedAssetProfiles: SecurityAssetProfileDefinition[];
  assetProfileDraft: AssetProfileDraftState;
  firstApprovedAssetProfile: SecurityAssetProfileDefinition;
  onRefresh: (() => Promise<void> | void) | undefined;
  profileBackedSecurity: ProfileBackedSecurityState;
  session: SessionInfo | null;
  setAssetProfileDraft: React.Dispatch<React.SetStateAction<AssetProfileDraftState>>;
  setProfileBackedSecurity: React.Dispatch<React.SetStateAction<ProfileBackedSecurityState>>;
  vm: SettingsScreenViewModel;
}

export function useSettingsAssetProfileSectionState({ approvedAssetProfiles, assetProfileDraft, firstApprovedAssetProfile, onRefresh, profileBackedSecurity, session, setAssetProfileDraft, setProfileBackedSecurity, vm }: UseSettingsAssetProfileSectionStateParams) {
  const selectedDraftStarterProfile = approvedAssetProfiles.find((profile) => profile.profileId === assetProfileDraft.starterProfileId) ??
    firstApprovedAssetProfile;
  const selectedProfileBackedSecurityProfile = approvedAssetProfiles.find((profile) => profile.profileId === profileBackedSecurity.profileId) ??
    firstApprovedAssetProfile;
  // The write seam enforces the profile's REQUIRED identifier preferences at creation, so the form
  // must collect every required kind — an InternalCode-only form makes a profile requiring CUSIP
  // fail SM_CUSTOM_PROFILE_IDENTIFIER_COVERAGE_MISSING on every submission. InternalCode keeps its
  // dedicated primary-identifier input; the remaining required kinds render below.
  const additionalRequiredIdentifierPreferences = (selectedProfileBackedSecurityProfile?.identifierPreferences ?? [])
    .filter((preference) => preference.isRequiredForClose && preference.kind !== "InternalCode");
  const selectAssetProfileStarter = (profileId: string) => {
    const selected = approvedAssetProfiles.find((profile) => profile.profileId === profileId);
    setAssetProfileDraft(createAssetProfileDraftState(selected ?? null));
  };
  const selectProfileBackedSecurityProfile = (profileId: string) => {
    const selected = approvedAssetProfiles.find((profile) => profile.profileId === profileId);
    setProfileBackedSecurity((current) => ({
      ...createProfileBackedSecurityState(selected ?? null),
      displayName: current.displayName,
      internalCode: current.internalCode,
      currency: current.currency || "USD"
    }));
  };
  const updateProfileBackedSecurityField = (fieldKey: string, value: string) => {
    setProfileBackedSecurity((current) => ({
      ...current,
      fieldValues: {
        ...current.fieldValues,
        [fieldKey]: value
      },
      message: null,
      details: [],
      tone: "default"
    }));
  };
  const submitAssetProfileDraft = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!selectedDraftStarterProfile) {
      setAssetProfileDraft((current) => ({
        ...current,
        message: "Choose an approved starter profile before drafting.",
        details: vm.assetProfileGovernancePanel.createDisabledReason ? [vm.assetProfileGovernancePanel.createDisabledReason] : [],
        tone: "warning"
      }));
      return;
    }

    const profileId = normalizeAssetProfileId(assetProfileDraft.profileId);
    const name = assetProfileDraft.name.trim();
    const category = assetProfileDraft.category.trim();
    const rationale = assetProfileDraft.rationale.trim();
    if (!profileId || !name || !category || !rationale) {
      setAssetProfileDraft((current) => ({
        ...current,
        message: "Profile id, name, category, and rationale are required.",
        details: [],
        tone: "warning"
      }));
      return;
    }

    setAssetProfileDraft((current) => ({ ...current, busyAction: "draft", message: null, details: [], tone: "default" }));
    try {
      const result = await draftSecurityAssetProfile({
        profileId,
        name,
        category,
        subType: assetProfileDraft.subType.trim() || null,
        fields: selectedDraftStarterProfile.fields,
        identifierPreferences: selectedDraftStarterProfile.identifierPreferences,
        lifecycleStates: selectedDraftStarterProfile.lifecycleStates,
        accountingImpactHints: selectedDraftStarterProfile.accountingImpactHints,
        dateOrderRules: selectedDraftStarterProfile.dateOrderRules,
        requestedBy: session?.displayName ?? "settings-screen",
        rationale,
        correlationId: `settings-asset-profile-draft-${Date.now()}`
      });
      await onRefresh?.();
      setAssetProfileDraft((current) => ({
        ...current,
        profileId,
        name,
        category,
        busyAction: null,
        rollbackTargetVersion: result.profile.version,
        lineage: result.lineage,
        result,
        message: `Draft saved for ${result.profile.name} v${result.profile.version}.`,
        details: [
          `Audit ${result.auditEvent.auditId}`,
          `Correlation ${result.auditEvent.correlationId}`
        ],
        tone: "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Asset profile draft failed.");
      setAssetProfileDraft((current) => ({
        ...current,
        busyAction: null,
        message: display.summary,
        details: display.details,
        tone: "danger"
      }));
    }
  };
  const approveAssetProfileDraft = async () => {
    const profileId = normalizeAssetProfileId(assetProfileDraft.profileId);
    const version = assetProfileDraft.result?.profile.version ?? assetProfileDraft.rollbackTargetVersion;
    if (!profileId || version < 1) {
      setAssetProfileDraft((current) => ({
        ...current,
        message: "Draft a profile version before approval.",
        details: [],
        tone: "warning"
      }));
      return;
    }

    setAssetProfileDraft((current) => ({ ...current, busyAction: "approve", message: null, details: [], tone: "default" }));
    try {
      const result = await approveSecurityAssetProfile({
        profileId,
        version,
        effectiveFrom: todayDateOnly(),
        approvalReference: `settings:${profileId}:v${version}`,
        requestedBy: session?.displayName ?? "settings-screen",
        rationale: assetProfileDraft.rationale.trim() || "Approve asset profile for governed Security Master creation.",
        correlationId: `settings-asset-profile-approve-${Date.now()}`
      });
      await onRefresh?.();
      setAssetProfileDraft((current) => ({
        ...current,
        busyAction: null,
        lineage: result.lineage,
        result,
        message: `Approved ${result.profile.name} v${result.profile.version}.`,
        details: [
          `Audit ${result.auditEvent.auditId}`,
          `Correlation ${result.auditEvent.correlationId}`,
          `Reference ${result.auditEvent.approvalReference ?? "n/a"}`
        ],
        tone: "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Asset profile approval failed.");
      setAssetProfileDraft((current) => ({
        ...current,
        busyAction: null,
        message: display.summary,
        details: display.details,
        tone: "danger"
      }));
    }
  };
  const loadAssetProfileLineage = async () => {
    const profileId = assetProfileDraft.starterProfileId || assetProfileDraft.profileId;
    if (!profileId) {
      return;
    }

    setAssetProfileDraft((current) => ({ ...current, busyAction: "lineage", message: null, details: [], tone: "default" }));
    try {
      const lineage = await getSecurityAssetProfileLineage(profileId);
      setAssetProfileDraft((current) => ({
        ...current,
        busyAction: null,
        lineage,
        rollbackTargetVersion: lineage.versions[0]?.version ?? current.rollbackTargetVersion,
        message: `Loaded ${lineage.versions.length} version${lineage.versions.length === 1 ? "" : "s"} for ${lineage.profileId}.`,
        details: lineage.auditEvents.slice(-3).map((event) => `Audit ${event.auditId}: ${event.eventType}`),
        tone: "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Asset profile lineage load failed.");
      setAssetProfileDraft((current) => ({
        ...current,
        busyAction: null,
        message: display.summary,
        details: display.details,
        tone: "danger"
      }));
    }
  };
  const rollbackAssetProfile = async () => {
    const profileId = assetProfileDraft.starterProfileId || assetProfileDraft.profileId;
    if (!profileId || assetProfileDraft.rollbackTargetVersion < 1) {
      setAssetProfileDraft((current) => ({
        ...current,
        message: "Choose a profile and target version before rollback.",
        details: [],
        tone: "warning"
      }));
      return;
    }

    setAssetProfileDraft((current) => ({ ...current, busyAction: "rollback", message: null, details: [], tone: "default" }));
    try {
      const result = await rollbackSecurityAssetProfile({
        profileId,
        targetVersion: assetProfileDraft.rollbackTargetVersion,
        effectiveFrom: todayDateOnly(),
        approvalReference: `settings:${profileId}:rollback:${assetProfileDraft.rollbackTargetVersion}`,
        requestedBy: session?.displayName ?? "settings-screen",
        rationale: assetProfileDraft.rationale.trim() || "Rollback asset profile to an approved prior version.",
        correlationId: `settings-asset-profile-rollback-${Date.now()}`
      });
      await onRefresh?.();
      setAssetProfileDraft((current) => ({
        ...current,
        busyAction: null,
        lineage: result.lineage,
        result,
        message: `Rolled back ${result.profile.name} into v${result.profile.version}.`,
        details: [
          `Audit ${result.auditEvent.auditId}`,
          `Correlation ${result.auditEvent.correlationId}`
        ],
        tone: "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Asset profile rollback failed.");
      setAssetProfileDraft((current) => ({
        ...current,
        busyAction: null,
        message: display.summary,
        details: display.details,
        tone: "danger"
      }));
    }
  };
  const submitProfileBackedSecurity = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const selected = selectedProfileBackedSecurityProfile;
    if (!selected) {
      setProfileBackedSecurity((current) => ({
        ...current,
        message: "Choose an approved asset profile before creating a security.",
        details: vm.assetProfileGovernancePanel.createDisabledReason ? [vm.assetProfileGovernancePanel.createDisabledReason] : [],
        tone: "warning"
      }));
      return;
    }

    const missingFields = selected.fields
      .filter((field) => field.isRequired && !profileBackedSecurity.fieldValues[field.key]?.trim())
      .map((field) => field.label);
    const requiredIdentifierPreferences = selected.identifierPreferences
      .filter((preference) => preference.isRequiredForClose && preference.kind !== "InternalCode");
    // Includes the ProviderSymbol provider namespace: the write-side command validation rejects
    // that kind with a blank provider, so the form must catch it up front.
    const missingIdentifiers = missingRequiredIdentifierDetails(requiredIdentifierPreferences, profileBackedSecurity);
    if (!profileBackedSecurity.displayName.trim()
      || !profileBackedSecurity.internalCode.trim()
      || missingFields.length > 0
      || missingIdentifiers.length > 0) {
      setProfileBackedSecurity((current) => ({
        ...current,
        message: "Display name, internal code, required identifiers, and required profile fields are required.",
        details: [...missingIdentifiers, ...missingFields],
        tone: "warning"
      }));
      return;
    }

    const { payload: profileFields, invalidFields } = buildProfileFieldPayload(selected.fields, profileBackedSecurity.fieldValues);
    if (invalidFields.length > 0) {
      setProfileBackedSecurity((current) => ({
        ...current,
        message: "Correct the profile field values before creating the security.",
        details: invalidFields,
        tone: "warning"
      }));
      return;
    }

    setProfileBackedSecurity((current) => ({ ...current, busy: true, message: null, details: [], tone: "default" }));
    try {
      const securityId = createBrowserGuid();
      const effectiveFrom = new Date().toISOString();
      const result = await createSecurityMasterEntry({
        securityId,
        assetClass: "CustomAsset",
        commonTerms: {
          displayName: profileBackedSecurity.displayName.trim(),
          currency: profileBackedSecurity.currency.trim().toUpperCase() || "USD"
        },
        assetSpecificTerms: {
          schemaVersion: 3,
          category: selected.category,
          subType: selected.subType,
          customProfileId: selected.profileId,
          profileVersion: selected.version,
          profileFields,
          profileApproval: {
            approvedBy: selected.approvedBy,
            approvedAtUtc: selected.approvedAtUtc,
            // The write seam verifies this reference against the catalog's own recorded approval
            // reference when one exists, so the governed value must be copied verbatim; the
            // legacy fabricated form remains only for catalog versions predating the field
            // (where the seam has no fact to compare).
            approvalReference: selected.approvalReference ?? `profile:${selected.profileId}:v${selected.version}`
          },
          evidenceLinks: []
        },
        identifiers: [
          {
            kind: "InternalCode",
            value: profileBackedSecurity.internalCode.trim(),
            isPrimary: true,
            validFrom: effectiveFrom
          },
          ...requiredIdentifierPreferences.map((preference) => ({
            kind: preference.kind,
            value: profileBackedSecurity.identifierValues[preference.kind]!.trim(),
            isPrimary: false,
            validFrom: effectiveFrom,
            ...(preference.kind === "ProviderSymbol"
              ? { provider: profileBackedSecurity.identifierProviders[preference.kind]!.trim() }
              : {})
          }))
        ],
        effectiveFrom,
        sourceSystem: "Meridian.Settings.AssetProfiles",
        updatedBy: session?.displayName ?? "settings-screen",
        sourceRecordId: `asset-profile:${selected.profileId}:v${selected.version}`,
        reason: profileBackedSecurity.rationale.trim()
      });
      await onRefresh?.();
      setProfileBackedSecurity((current) => ({
        ...current,
        busy: false,
        message: `Security created for ${result.displayName}.`,
        details: [
          `Security ${result.securityId}`,
          `Profile ${selected.profileId} v${selected.version}`
        ],
        tone: "success"
      }));
    } catch (error) {
      const display = describeApiError(error, "Profile-backed security creation failed.");
      setProfileBackedSecurity((current) => ({
        ...current,
        busy: false,
        message: display.summary,
        details: display.details,
        tone: "danger"
      }));
    }
  };

  return { additionalRequiredIdentifierPreferences, approveAssetProfileDraft, loadAssetProfileLineage, rollbackAssetProfile, selectAssetProfileStarter, selectProfileBackedSecurityProfile, selectedDraftStarterProfile, selectedProfileBackedSecurityProfile, submitAssetProfileDraft, submitProfileBackedSecurity, updateProfileBackedSecurityField };
}

/** Asset profile governance and profile-backed security drafting. */
interface SettingsAssetProfileSectionProps {
  vm: SettingsScreenViewModel;
  submitAssetProfileDraft: ReturnType<typeof useSettingsAssetProfileSectionState>["submitAssetProfileDraft"];
  selectedDraftStarterProfile: ReturnType<typeof useSettingsAssetProfileSectionState>["selectedDraftStarterProfile"];
  assetProfileDraft: AssetProfileDraftState;
  selectAssetProfileStarter: ReturnType<typeof useSettingsAssetProfileSectionState>["selectAssetProfileStarter"];
  approvedAssetProfiles: SecurityAssetProfileDefinition[];
  setAssetProfileDraft: React.Dispatch<React.SetStateAction<AssetProfileDraftState>>;
  approveAssetProfileDraft: ReturnType<typeof useSettingsAssetProfileSectionState>["approveAssetProfileDraft"];
  loadAssetProfileLineage: ReturnType<typeof useSettingsAssetProfileSectionState>["loadAssetProfileLineage"];
  setMutationConfirmation: React.Dispatch<React.SetStateAction<SettingsMutationConfirmation | null>>;
  rollbackAssetProfile: ReturnType<typeof useSettingsAssetProfileSectionState>["rollbackAssetProfile"];
  submitProfileBackedSecurity: ReturnType<typeof useSettingsAssetProfileSectionState>["submitProfileBackedSecurity"];
  selectedProfileBackedSecurityProfile: ReturnType<typeof useSettingsAssetProfileSectionState>["selectedProfileBackedSecurityProfile"];
  profileBackedSecurity: ProfileBackedSecurityState;
  selectProfileBackedSecurityProfile: ReturnType<typeof useSettingsAssetProfileSectionState>["selectProfileBackedSecurityProfile"];
  setProfileBackedSecurity: React.Dispatch<React.SetStateAction<ProfileBackedSecurityState>>;
  additionalRequiredIdentifierPreferences: ReturnType<typeof useSettingsAssetProfileSectionState>["additionalRequiredIdentifierPreferences"];
  updateProfileBackedSecurityField: ReturnType<typeof useSettingsAssetProfileSectionState>["updateProfileBackedSecurityField"];
}

export function SettingsAssetProfileSection({ vm, submitAssetProfileDraft, selectedDraftStarterProfile, assetProfileDraft, selectAssetProfileStarter, approvedAssetProfiles, setAssetProfileDraft, approveAssetProfileDraft, loadAssetProfileLineage, setMutationConfirmation, rollbackAssetProfile, submitProfileBackedSecurity, selectedProfileBackedSecurityProfile, profileBackedSecurity, selectProfileBackedSecurityProfile, setProfileBackedSecurity, additionalRequiredIdentifierPreferences, updateProfileBackedSecurityField }: SettingsAssetProfileSectionProps) {
  return (
    <Card id="asset-profile-accounting" className="panel-surface scroll-mt-6 border border-border/70">
      <CardHeader>
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div>
            <div className="eyebrow-label">Security Master</div>
            <CardTitle className="mt-2 flex items-center gap-2 text-base">
              <GitBranch className="h-4 w-4 text-primary" />
              {vm.assetProfileGovernancePanel.title}
            </CardTitle>
            <CardDescription className="mt-2">{vm.assetProfileGovernancePanel.summary}</CardDescription>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <SettingsChip label="Approved" value={vm.assetProfileGovernancePanel.approvedCountLabel} />
            <SettingsChip label="Declared projected" value={vm.assetProfileGovernancePanel.projectedFieldCountLabel} />
            <SettingsChip label="Close IDs" value={vm.assetProfileGovernancePanel.closeIdentifierCountLabel} />
            <SeverityBadge
              status={toSeverity(vm.assetProfileGovernancePanel.statusVariant)}
              label={vm.assetProfileGovernancePanel.statusLabel}
            />
          </div>
        </div>
      </CardHeader>
      <CardContent className="grid gap-4">
        <div className="grid gap-3 xl:grid-cols-5" role="list" aria-label={vm.assetProfileGovernancePanel.listLabel}>
          {vm.assetProfileGovernancePanel.rows.map((row) => (
            <div
              key={`${row.profileId}-${row.versionLabel}`}
              role="listitem"
              className={cn("grid gap-3 rounded-md border px-3 py-3", diagnosticToneClass[capabilityTone(row.statusVariant)])}
            >
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <h3 className="text-sm font-semibold text-foreground">{row.name}</h3>
                  <p className="mt-1 break-words font-mono text-[11px] text-muted-foreground">{row.profileId}</p>
                </div>
                <Badge variant={row.statusVariant}>{row.versionLabel}</Badge>
              </div>
              <dl className="grid gap-2">
                <SettingsFieldRow label="Category" value={row.categoryLabel} tone="default" />
                <SettingsFieldRow label="Fields" value={row.fieldCountLabel} tone="muted" />
                <SettingsFieldRow label="Projection" value={row.projectedFieldLabel} tone="muted" />
                <SettingsFieldRow label="Close ID" value={row.requiredCloseIdentifierLabel} tone="warning" />
              </dl>
              <p className="text-xs leading-5 text-foreground/75">{row.accountingImpactLabel}</p>
            </div>
          ))}
        </div>

        <form
          className="grid gap-3 rounded-md border border-border/70 bg-background/35 px-3 py-3"
          onSubmit={submitAssetProfileDraft}
          aria-label="Draft asset profile"
        >
          <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
            <div className="min-w-0">
              <h3 className="text-sm font-semibold text-foreground">Draft asset profile</h3>
              <p className="mt-1 text-xs leading-5 text-muted-foreground">
                Copy approved no-code fields, close identifiers, lifecycle states, and accounting hints into a governed draft.
              </p>
            </div>
            <Badge variant={selectedDraftStarterProfile ? "warning" : "outline"}>
              {assetProfileDraft.busyAction ? "Working" : selectedDraftStarterProfile ? "Draft ready" : "Unavailable"}
            </Badge>
          </div>
          <div className="grid gap-3 lg:grid-cols-[minmax(0,1.1fr)_minmax(0,0.9fr)_minmax(0,1fr)_minmax(0,0.8fr)]">
            <FilterSelect
              label="Starter"
              value={assetProfileDraft.starterProfileId}
              onChange={selectAssetProfileStarter}
              options={approvedAssetProfiles.map((profile) => ({ value: profile.profileId, label: profile.name }))}
              disabled={!selectedDraftStarterProfile || assetProfileDraft.busyAction !== null}
            />
            <label className="grid gap-1 text-xs font-medium text-muted-foreground">
              Profile id
              <Input
                value={assetProfileDraft.profileId}
                onChange={(event) => setAssetProfileDraft((current) => ({ ...current, profileId: event.target.value, message: null }))}
                disabled={!selectedDraftStarterProfile || assetProfileDraft.busyAction !== null}
                aria-label="Asset profile id"
              />
            </label>
            <label className="grid gap-1 text-xs font-medium text-muted-foreground">
              Name
              <Input
                value={assetProfileDraft.name}
                onChange={(event) => setAssetProfileDraft((current) => ({ ...current, name: event.target.value, message: null }))}
                disabled={!selectedDraftStarterProfile || assetProfileDraft.busyAction !== null}
                aria-label="Asset profile name"
              />
            </label>
            <label className="grid gap-1 text-xs font-medium text-muted-foreground">
              Category
              <Input
                value={assetProfileDraft.category}
                onChange={(event) => setAssetProfileDraft((current) => ({ ...current, category: event.target.value, message: null }))}
                disabled={!selectedDraftStarterProfile || assetProfileDraft.busyAction !== null}
                aria-label="Asset profile category"
              />
            </label>
          </div>
          <div className="grid gap-3 lg:grid-cols-[minmax(0,0.75fr)_minmax(0,1fr)_auto_auto_auto]">
            <label className="grid gap-1 text-xs font-medium text-muted-foreground">
              Subtype
              <Input
                value={assetProfileDraft.subType}
                onChange={(event) => setAssetProfileDraft((current) => ({ ...current, subType: event.target.value, message: null }))}
                disabled={!selectedDraftStarterProfile || assetProfileDraft.busyAction !== null}
                aria-label="Asset profile subtype"
              />
            </label>
            <label className="grid gap-1 text-xs font-medium text-muted-foreground">
              Rationale
              <Input
                value={assetProfileDraft.rationale}
                onChange={(event) => setAssetProfileDraft((current) => ({ ...current, rationale: event.target.value, message: null }))}
                disabled={!selectedDraftStarterProfile || assetProfileDraft.busyAction !== null}
                aria-label="Asset profile rationale"
              />
            </label>
            <div className="flex items-end">
              <Button type="submit" size="sm" disabled={!selectedDraftStarterProfile || assetProfileDraft.busyAction !== null} busy={assetProfileDraft.busyAction === "draft"} busyLabel="Saving draft">
                <Save className="h-3.5 w-3.5" aria-hidden="true" />
                Save draft
              </Button>
            </div>
            <div className="flex items-end">
              <Button type="button" variant="outline" size="sm" onClick={approveAssetProfileDraft} disabled={assetProfileDraft.busyAction !== null || !assetProfileDraft.result} busy={assetProfileDraft.busyAction === "approve"} busyLabel="Approving">
                <ShieldCheck className="h-3.5 w-3.5" aria-hidden="true" />
                Approve
              </Button>
            </div>
            <div className="flex items-end">
              <Button type="button" variant="outline" size="sm" onClick={loadAssetProfileLineage} disabled={!selectedDraftStarterProfile || assetProfileDraft.busyAction !== null} busy={assetProfileDraft.busyAction === "lineage"} busyLabel="Loading lineage">
                <RefreshCcw className="h-3.5 w-3.5" aria-hidden="true" />
                Lineage
              </Button>
            </div>
          </div>
          <div className="grid gap-3 lg:grid-cols-[minmax(0,0.35fr)_auto_minmax(0,1fr)]">
            <label className="grid gap-1 text-xs font-medium text-muted-foreground">
              Rollback version
              <Input
                type="number"
                min={1}
                value={String(assetProfileDraft.rollbackTargetVersion)}
                onChange={(event) => setAssetProfileDraft((current) => ({
                  ...current,
                  rollbackTargetVersion: Math.max(1, Number.parseInt(event.target.value, 10) || 1),
                  message: null
                }))}
                disabled={assetProfileDraft.busyAction !== null}
                aria-label="Asset profile rollback target version"
              />
            </label>
            <div className="flex items-end">
              <Button
                type="button"
                variant="destructive"
                size="sm"
                onClick={() => setMutationConfirmation({
                  id: "rollback-asset-profile",
                  title: "Confirm asset profile rollback",
                  description: `Rollback ${selectedDraftStarterProfile?.name ?? "the selected asset profile"} to version ${assetProfileDraft.rollbackTargetVersion}. This changes the active governed profile and retains a lineage record.`,
                  confirmLabel: "Confirm rollback",
                  confirmAriaLabel: `Confirm asset profile rollback to version ${assetProfileDraft.rollbackTargetVersion}`,
                  destructive: true,
                  run: rollbackAssetProfile
                })}
                disabled={!selectedDraftStarterProfile || assetProfileDraft.busyAction !== null}
                busy={assetProfileDraft.busyAction === "rollback"}
                busyLabel="Rolling back"
              >
                <GitBranch className="h-3.5 w-3.5" aria-hidden="true" />
                Rollback
              </Button>
            </div>
            <div className="flex items-end text-xs text-muted-foreground">
              {assetProfileDraft.lineage ? `${assetProfileDraft.lineage.versions.length} lineage version${assetProfileDraft.lineage.versions.length === 1 ? "" : "s"}` : "Lineage not loaded"}
            </div>
          </div>
          {assetProfileDraft.message ? (
            <StatusBanner
              role={assetProfileDraft.tone === "danger" ? "alert" : "status"}
              tone={settingsBannerTone(assetProfileDraft.tone)}
              title={assetProfileDraft.message}
              detail={assetProfileDraft.details.length > 0 ? (
                <ul className="list-disc space-y-1 pl-5">
                  {assetProfileDraft.details.map((detail) => <li key={detail}>{detail}</li>)}
                </ul>
              ) : null}
            />
          ) : null}
        </form>

        <form
          className="grid gap-3 rounded-md border border-border/70 bg-background/35 px-3 py-3"
          onSubmit={submitProfileBackedSecurity}
          aria-label="Create profile-backed security"
        >
          <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
            <div className="min-w-0">
              <h3 className="text-sm font-semibold text-foreground">Create profile-backed security</h3>
              <p className="mt-1 text-xs leading-5 text-muted-foreground">
                Pin the Security Master record to the approved profile version used at creation.
              </p>
            </div>
            <Badge variant={selectedProfileBackedSecurityProfile ? "success" : "outline"}>
              {selectedProfileBackedSecurityProfile ? `v${selectedProfileBackedSecurityProfile.version}` : "No profile"}
            </Badge>
          </div>
          <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_minmax(0,0.75fr)_minmax(0,0.45fr)]">
            <FilterSelect
              label="Profile"
              value={profileBackedSecurity.profileId}
              onChange={selectProfileBackedSecurityProfile}
              options={approvedAssetProfiles.map((profile) => ({ value: profile.profileId, label: profile.name }))}
              disabled={!vm.assetProfileGovernancePanel.canCreateSecurity || profileBackedSecurity.busy}
            />
            <label className="grid gap-1 text-xs font-medium text-muted-foreground">
              Display name
              <Input
                value={profileBackedSecurity.displayName}
                onChange={(event) => setProfileBackedSecurity((current) => ({ ...current, displayName: event.target.value, message: null }))}
                disabled={!selectedProfileBackedSecurityProfile || profileBackedSecurity.busy}
                aria-label="Profile-backed security display name"
              />
            </label>
            <label className="grid gap-1 text-xs font-medium text-muted-foreground">
              Internal code
              <Input
                value={profileBackedSecurity.internalCode}
                onChange={(event) => setProfileBackedSecurity((current) => ({ ...current, internalCode: event.target.value, message: null }))}
                disabled={!selectedProfileBackedSecurityProfile || profileBackedSecurity.busy}
                aria-label="Profile-backed security internal code"
              />
            </label>
            <label className="grid gap-1 text-xs font-medium text-muted-foreground">
              Currency
              <Input
                value={profileBackedSecurity.currency}
                onChange={(event) => setProfileBackedSecurity((current) => ({ ...current, currency: event.target.value.toUpperCase(), message: null }))}
                disabled={!selectedProfileBackedSecurityProfile || profileBackedSecurity.busy}
                aria-label="Profile-backed security currency"
              />
            </label>
          </div>
          {additionalRequiredIdentifierPreferences.length > 0 ? (
            <div className="grid gap-3 lg:grid-cols-3">
              {additionalRequiredIdentifierPreferences.map((preference) => (
                <label key={preference.kind} className="grid gap-1 text-xs font-medium text-muted-foreground">
                  {`${preference.kind} identifier`}
                  <Input
                    value={profileBackedSecurity.identifierValues[preference.kind] ?? ""}
                    onChange={(event) => setProfileBackedSecurity((current) => ({
                      ...current,
                      identifierValues: { ...current.identifierValues, [preference.kind]: event.target.value },
                      message: null
                    }))}
                    disabled={profileBackedSecurity.busy}
                    aria-label={`Profile-backed security ${preference.kind} identifier`}
                  />
                  {preference.kind === "ProviderSymbol" ? (
                    <Input
                      value={profileBackedSecurity.identifierProviders[preference.kind] ?? ""}
                      onChange={(event) => setProfileBackedSecurity((current) => ({
                        ...current,
                        identifierProviders: { ...current.identifierProviders, [preference.kind]: event.target.value },
                        message: null
                      }))}
                      placeholder="Provider namespace (e.g. bloomberg)"
                      disabled={profileBackedSecurity.busy}
                      aria-label={`Profile-backed security ${preference.kind} provider namespace`}
                    />
                  ) : null}
                </label>
              ))}
            </div>
          ) : null}
          {selectedProfileBackedSecurityProfile ? (
            <div className="grid gap-3 lg:grid-cols-3">
              {selectedProfileBackedSecurityProfile.fields.map((field) => (
                <AssetProfileFieldInput
                  key={field.key}
                  field={field}
                  value={profileBackedSecurity.fieldValues[field.key] ?? ""}
                  disabled={profileBackedSecurity.busy}
                  onChange={(value) => updateProfileBackedSecurityField(field.key, value)}
                />
              ))}
            </div>
          ) : null}
          <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_auto]">
            <label className="grid gap-1 text-xs font-medium text-muted-foreground">
              Rationale
              <Input
                value={profileBackedSecurity.rationale}
                onChange={(event) => setProfileBackedSecurity((current) => ({ ...current, rationale: event.target.value, message: null }))}
                disabled={!selectedProfileBackedSecurityProfile || profileBackedSecurity.busy}
                aria-label="Profile-backed security rationale"
              />
            </label>
            <div className="flex items-end">
              <Button
                type="submit"
                size="sm"
                disabled={!selectedProfileBackedSecurityProfile || profileBackedSecurity.busy}
                busy={profileBackedSecurity.busy}
                busyLabel="Creating security"
                disabledReason={vm.assetProfileGovernancePanel.createDisabledReason ?? undefined}
              >
                <Save className="h-3.5 w-3.5" aria-hidden="true" />
                Create security
              </Button>
            </div>
          </div>
          {profileBackedSecurity.message ? (
            <StatusBanner
              role={profileBackedSecurity.tone === "danger" ? "alert" : "status"}
              tone={settingsBannerTone(profileBackedSecurity.tone)}
              title={profileBackedSecurity.message}
              detail={profileBackedSecurity.details.length > 0 ? (
                <ul className="list-disc space-y-1 pl-5">
                  {profileBackedSecurity.details.map((detail) => <li key={detail}>{detail}</li>)}
                </ul>
              ) : null}
            />
          ) : null}
        </form>
      </CardContent>
    </Card>
  );
}
