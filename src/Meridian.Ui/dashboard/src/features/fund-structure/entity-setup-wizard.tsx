import { useMemo, useState, type ChangeEvent, type ChangeEventHandler } from "react";
import { CheckCircle2, GitBranch, LoaderCircle, Network, ShieldAlert } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { Badge } from "@/components/ui/badge";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { createFundStructureSetupDraft, validateFundStructureSetupDraft, type FundStructureSetupDraft, type FundStructureSetupPreview, type FundStructureSetupResult } from "@/lib/api";

const legalEntityTypes = ["Fund", "ManagementCompany", "GeneralPartner", "LimitedPartner", "Vehicle", "Custodian", "Broker", "Counterparty", "Other"] as const;
const legalForms = ["LimitedPartnership", "LimitedLiabilityCompany", "SeriesLimitedLiabilityCompany", "SegregatedPortfolioCompany", "StatutoryTrust", "Corporation", "Trust", "Foundation", "Other"] as const;
const lifecycleStatuses = ["Forming", "Active", "AmendmentPending", "Restructuring", "WindingDown", "Dissolved", "Merged"] as const;
const lifecycleEventKinds = ["Formation", "Amendment", "Restructure", "SpvRollup", "BeneficialOwnershipChange", "LegalFormChange", "JurisdictionChange", "Other"] as const;

interface DraftFields {
  organizationCode: string;
  organizationName: string;
  businessCode: string;
  businessName: string;
  clientOrFundCode: string;
  clientOrFundName: string;
  legalEntityType: string;
  legalForm: string;
  lifecycleStatus: string;
  legalEntityCode: string;
  legalEntityName: string;
  jurisdiction: string;
  registrationNumber: string;
  beneficialOwnerName: string;
  beneficialOwnerPercent: string;
  lifecycleEventKind: string;
  lifecycleEventSummary: string;
  vehicleCode: string;
  vehicleName: string;
  portfolioCode: string;
  portfolioName: string;
  accountCode: string;
  accountDisplayName: string;
  baseCurrency: string;
  requestedBy: string;
}

const initialFields: DraftFields = {
  organizationCode: "ORG",
  organizationName: "New Organization",
  businessCode: "INV",
  businessName: "Investment Management",
  clientOrFundCode: "FUND",
  clientOrFundName: "Flagship Fund",
  legalEntityType: "Fund",
  legalForm: "LimitedPartnership",
  lifecycleStatus: "Active",
  legalEntityCode: "LE",
  legalEntityName: "Flagship LP",
  jurisdiction: "US-DE",
  registrationNumber: "DE-FUND-001",
  beneficialOwnerName: "Flagship GP",
  beneficialOwnerPercent: "100",
  lifecycleEventKind: "Formation",
  lifecycleEventSummary: "Initial formation and operating setup.",
  vehicleCode: "VEH",
  vehicleName: "Flagship Vehicle",
  portfolioCode: "PORT",
  portfolioName: "Core Portfolio",
  accountCode: "ACCT",
  accountDisplayName: "Primary brokerage handoff",
  baseCurrency: "USD",
  requestedBy: "browser-operator"
};

export function EntitySetupWizard() {
  const [fields, setFields] = useState(initialFields);
  const [preview, setPreview] = useState<FundStructureSetupPreview | null>(null);
  const [result, setResult] = useState<FundStructureSetupResult | null>(null);
  const [status, setStatus] = useState("Validate the setup draft before review and create.");
  const [busy, setBusy] = useState(false);
  const [validatedDraftSignature, setValidatedDraftSignature] = useState<string | null>(null);
  const draft = useMemo(() => buildDraft(fields), [fields]);
  const draftSignature = useMemo(() => JSON.stringify(draft), [draft]);
  const blockingCount = preview?.validationSummary.issues.filter((issue) => issue.isBlocking).length ?? 0;
  const validationIsCurrent = preview?.validationSummary.isValid === true && validatedDraftSignature === draftSignature;
  const createDisabledReason = busy
    ? "Validation or creation is in progress."
    : !preview
      ? "Validate the current draft before creating the entity graph."
      : validatedDraftSignature !== draftSignature
        ? "The draft changed after validation. Validate it again before creating records."
        : !preview.validationSummary.isValid
          ? "Resolve blocking validation issues before creating records."
          : null;

  const update = (key: keyof DraftFields) => (event: ChangeEvent<HTMLInputElement>) => {
    setFields((current) => ({ ...current, [key]: event.target.value }));
    setPreview(null);
    setResult(null);
    setValidatedDraftSignature(null);
    setStatus("Draft changed. Validate it again before review and create.");
  };
  const updateSelect = (key: keyof DraftFields) => (event: ChangeEvent<HTMLSelectElement>) => {
    setFields((current) => ({ ...current, [key]: event.target.value }));
    setPreview(null);
    setResult(null);
    setValidatedDraftSignature(null);
    setStatus("Draft changed. Validate it again before review and create.");
  };

  async function validate() {
    setBusy(true);
    try {
      const response = await validateFundStructureSetupDraft(draft);
      setPreview(response);
      setValidatedDraftSignature(response.validationSummary.isValid ? draftSignature : null);
      setStatus(response.validationSummary.isValid ? "Draft is valid. Review the graph preview." : `Resolve ${response.validationSummary.issues.length} setup issue(s).`);
    } catch {
      setPreview(null);
      setValidatedDraftSignature(null);
      setStatus("Validation could not be completed. No records can be created until the shared setup workflow responds.");
    } finally {
      setBusy(false);
    }
  }

  async function create() {
    if (!validationIsCurrent) {
      setStatus("Validate the current draft before creating records.");
      return;
    }

    setBusy(true);
    try {
      const response = await createFundStructureSetupDraft(draft);
      setResult(response);
      setPreview({ nodes: response.graph.nodes, ownershipLinks: [], validationSummary: response.validationSummary });
      setStatus(`Created ${response.businessLane.name} and ${response.investmentPortfolio.name}.`);
    } catch {
      setResult(null);
      setStatus("Creation failed. The validated draft remains available for review and retry.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="space-y-4" aria-labelledby="entity-setup-title">
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <CardTitle id="entity-setup-title" className="flex items-center gap-2 text-base">
                <Network className="h-4 w-4" aria-hidden="true" /> Entity setup wizard
              </CardTitle>
              <CardDescription>Shared /api/fund-structure workflow for organization, lane, fund, entity, vehicle, portfolio, ownership, and account handoff setup.</CardDescription>
            </div>
            <Badge variant={!preview ? "outline" : blockingCount > 0 ? "warning" : "success"}>
              {!preview ? "Not validated" : `${blockingCount} blockers`}
            </Badge>
          </div>
        </CardHeader>
        <CardContent className="space-y-4 p-4">
          <fieldset className="space-y-3 rounded-sm border border-border/70 p-3">
            <legend className="px-1 text-sm font-semibold text-foreground">Organization and fund</legend>
            <div className="grid gap-3 md:grid-cols-3">
              <Field label="Organization code" value={fields.organizationCode} onChange={update("organizationCode")} />
              <Field label="Organization name" value={fields.organizationName} onChange={update("organizationName")} />
              <Field label="Base currency" value={fields.baseCurrency} onChange={update("baseCurrency")} />
              <Field label="Business code" value={fields.businessCode} onChange={update("businessCode")} />
              <Field label="Business name" value={fields.businessName} onChange={update("businessName")} />
              <Field label="Requested by" value={fields.requestedBy} onChange={update("requestedBy")} />
              <Field label="Fund code" value={fields.clientOrFundCode} onChange={update("clientOrFundCode")} />
              <Field label="Fund name" value={fields.clientOrFundName} onChange={update("clientOrFundName")} />
            </div>
          </fieldset>
          <fieldset className="space-y-3 rounded-sm border border-border/70 p-3">
            <legend className="px-1 text-sm font-semibold text-foreground">Legal entity</legend>
            <div className="grid gap-3 md:grid-cols-3">
              <Field label="Jurisdiction" value={fields.jurisdiction} onChange={update("jurisdiction")} />
              <SelectField label="Entity type" value={fields.legalEntityType} values={legalEntityTypes} onChange={updateSelect("legalEntityType")} />
              <SelectField label="Legal form" value={fields.legalForm} values={legalForms} onChange={updateSelect("legalForm")} />
              <SelectField label="Lifecycle status" value={fields.lifecycleStatus} values={lifecycleStatuses} onChange={updateSelect("lifecycleStatus")} />
              <Field label="Legal entity code" value={fields.legalEntityCode} onChange={update("legalEntityCode")} />
              <Field label="Legal entity name" value={fields.legalEntityName} onChange={update("legalEntityName")} />
              <Field label="Registration number" value={fields.registrationNumber} onChange={update("registrationNumber")} />
              <Field label="Beneficial owner" value={fields.beneficialOwnerName} onChange={update("beneficialOwnerName")} />
              <Field label="Owner percent" value={fields.beneficialOwnerPercent} onChange={update("beneficialOwnerPercent")} />
            </div>
          </fieldset>
          <TechnicalDetails label="Advanced structure and handoff">
            <div className="grid gap-3 md:grid-cols-3">
              <SelectField label="Lifecycle event" value={fields.lifecycleEventKind} values={lifecycleEventKinds} onChange={updateSelect("lifecycleEventKind")} />
              <Field label="Lifecycle summary" value={fields.lifecycleEventSummary} onChange={update("lifecycleEventSummary")} />
              <Field label="Vehicle code" value={fields.vehicleCode} onChange={update("vehicleCode")} />
              <Field label="Vehicle name" value={fields.vehicleName} onChange={update("vehicleName")} />
              <Field label="Portfolio code" value={fields.portfolioCode} onChange={update("portfolioCode")} />
              <Field label="Portfolio name" value={fields.portfolioName} onChange={update("portfolioName")} />
              <Field label="Account handoff code" value={fields.accountCode} onChange={update("accountCode")} />
              <Field label="Account handoff name" value={fields.accountDisplayName} onChange={update("accountDisplayName")} />
            </div>
          </TechnicalDetails>
          <div className="flex flex-wrap gap-2">
            <Button type="button" variant="secondary" onClick={validate} disabled={busy}>{busy ? <LoaderCircle className="h-4 w-4 animate-spin" /> : <ShieldAlert className="h-4 w-4" />} Validate draft</Button>
            <Button type="button" onClick={create} disabled={createDisabledReason !== null} disabledReason={createDisabledReason ?? undefined}>{busy ? <LoaderCircle className="h-4 w-4 animate-spin" /> : <CheckCircle2 className="h-4 w-4" />} Review and create</Button>
          </div>
          <p className="text-sm text-muted-foreground" role="status">{status}</p>
        </CardContent>
      </Card>

      <div className="grid gap-4 lg:grid-cols-[minmax(0,0.9fr)_minmax(0,1.1fr)]">
        <Card>
          <CardHeader>
            <CardTitle>Draft validation summary</CardTitle>
            <CardDescription>Blocking issues come from the shared setup workflow.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-2 p-4">
            {preview?.validationSummary.issues.length ? preview.validationSummary.issues.map((issue) => (
              <div key={`${issue.fieldPath}-${issue.code}`} className="rounded-sm border border-warning/40 bg-warning/10 p-2 text-sm">
                <div className="font-medium">{issue.message}</div>
                <div className="font-mono text-xs text-muted-foreground">{issue.fieldPath}</div>
              </div>
            )) : <p className="text-sm text-muted-foreground">No validation issues loaded yet.</p>}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2"><GitBranch className="h-4 w-4" aria-hidden="true" /> Resulting graph / ownership preview</CardTitle>
            <CardDescription>Review the entity graph before creating the durable fund-structure records.</CardDescription>
          </CardHeader>
          <CardContent className="space-y-3 p-4">
            {(preview?.nodes ?? []).map((node) => (
              <div key={node.nodeId} className="rounded-sm border border-border/70 p-2 text-sm">
                <div className="font-medium">{node.kind}: {node.code}</div>
                <div className="text-muted-foreground">{node.name}</div>
              </div>
            ))}
            {(preview?.ownershipLinks ?? []).map((link, index) => (
              <div key={`${link.parent}-${link.child}-${index}`} className="font-mono text-xs text-muted-foreground">
                {link.parent} → {link.child} ({link.relationshipType})
              </div>
            ))}
            {result ? <div className="rounded-sm border border-success/50 bg-success/10 p-3 text-sm">Created graph with {result.graph.nodes.length} nodes and {result.graph.ownershipLinks.length} ownership links.</div> : null}
          </CardContent>
        </Card>
      </div>
    </section>
  );
}

function Field({ label, value, onChange }: { label: string; value: string; onChange: ChangeEventHandler<HTMLInputElement> }) {
  return <label className="space-y-1 text-xs font-medium text-muted-foreground"><span>{label}</span><Input value={value} onChange={onChange} /></label>;
}

function SelectField<T extends readonly string[]>({ label, value, values, onChange }: { label: string; value: string; values: T; onChange: ChangeEventHandler<HTMLSelectElement> }) {
  return (
    <label className="space-y-1 text-xs font-medium text-muted-foreground">
      <span>{label}</span>
      <Select value={value} onChange={onChange}>
        {values.map((item) => <option key={item} value={item}>{formatEnumLabel(item)}</option>)}
      </Select>
    </label>
  );
}

function buildDraft(fields: DraftFields): FundStructureSetupDraft {
  const beneficialOwnerPercent = Number.parseFloat(fields.beneficialOwnerPercent);
  const beneficialOwners = fields.beneficialOwnerName.trim()
    ? [{
        ownerName: fields.beneficialOwnerName.trim(),
        ownershipPercent: Number.isFinite(beneficialOwnerPercent) ? beneficialOwnerPercent : null,
        isControlPerson: true
      }]
    : [];

  return {
    organization: { code: fields.organizationCode, name: fields.organizationName, baseCurrency: fields.baseCurrency },
    businessLane: { businessKind: "FundManager", code: fields.businessCode, name: fields.businessName, baseCurrency: fields.baseCurrency },
    clientOrFund: { createClient: false, code: fields.clientOrFundCode, name: fields.clientOrFundName, baseCurrency: fields.baseCurrency, clientSegmentKind: "Unspecified" },
    legalEntity: {
      entityType: fields.legalEntityType,
      legalForm: fields.legalForm,
      lifecycleStatus: fields.lifecycleStatus,
      code: fields.legalEntityCode,
      name: fields.legalEntityName,
      jurisdiction: fields.jurisdiction,
      baseCurrency: fields.baseCurrency,
      registrationNumber: fields.registrationNumber || null,
      beneficialOwners,
      initialLifecycleEventKind: fields.lifecycleEventKind,
      initialLifecycleEventSummary: fields.lifecycleEventSummary || null
    },
    vehicle: { code: fields.vehicleCode, name: fields.vehicleName, baseCurrency: fields.baseCurrency },
    investmentPortfolio: { code: fields.portfolioCode, name: fields.portfolioName, baseCurrency: fields.baseCurrency },
    accountHandoff: { accountCode: fields.accountCode, displayName: fields.accountDisplayName, accountType: "Brokerage", baseCurrency: fields.baseCurrency },
    initialOwnershipLinks: [],
    requestedBy: fields.requestedBy
  };
}

function formatEnumLabel(value: string) {
  return value.replace(/([a-z0-9])([A-Z])/g, "$1 $2");
}
