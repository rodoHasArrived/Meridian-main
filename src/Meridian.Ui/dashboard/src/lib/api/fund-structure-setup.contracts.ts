export interface FundStructureSetupDraft {
  organization: { organizationId?: string | null; code: string; name: string; baseCurrency: string; description?: string | null };
  businessLane: { businessId?: string | null; businessKind: string; code: string; name: string; baseCurrency: string; description?: string | null };
  clientOrFund: { clientId?: string | null; fundId?: string | null; createClient: boolean; code: string; name: string; baseCurrency: string; description?: string | null; clientSegmentKind?: string };
  legalEntity: {
    entityId?: string | null;
    entityType: string;
    code: string;
    name: string;
    jurisdiction: string;
    baseCurrency: string;
    description?: string | null;
    legalForm?: string;
    lifecycleStatus?: string;
    registrationNumber?: string | null;
    beneficialOwners?: Array<{
      ownerName: string;
      ownershipPercent?: number | null;
      ownerIdentifier?: string | null;
      isControlPerson?: boolean;
      effectiveFrom?: string | null;
      effectiveTo?: string | null;
      notes?: string | null;
    }>;
    initialLifecycleEventKind?: string;
    initialLifecycleEventSummary?: string | null;
    initialLifecycleEvidenceReference?: string | null;
  };
  vehicle: { vehicleId?: string | null; code: string; name: string; baseCurrency: string; description?: string | null };
  investmentPortfolio: { investmentPortfolioId?: string | null; code: string; name: string; baseCurrency: string; description?: string | null };
  accountHandoff: { accountCode: string; displayName: string; accountType: string; baseCurrency: string; institution?: string | null; ledgerReference?: string | null; notes?: string | null };
  initialOwnershipLinks?: Array<{ ownershipLinkId?: string | null; parent: string; child: string; relationshipType: string; ownershipPercent?: number | null; isPrimary?: boolean; notes?: string | null }>;
  effectiveFrom?: string | null;
  requestedBy?: string | null;
}

export interface FundStructureNodePreview {
  nodeId: string;
  kind: string;
  code: string;
  name: string;
  description?: string | null;
  isActive: boolean;
  effectiveFrom: string;
  effectiveTo?: string | null;
}

export interface FundStructureSetupValidationIssue {
  code: string;
  message: string;
  fieldPath: string;
  isBlocking: boolean;
}

export interface FundStructureSetupValidationSummary {
  isValid: boolean;
  issues: FundStructureSetupValidationIssue[];
}

export interface FundStructureSetupPreview {
  nodes: FundStructureNodePreview[];
  ownershipLinks: Array<{ parent: string; child: string; relationshipType: string; ownershipPercent?: number | null; isPrimary: boolean; notes?: string | null }>;
  validationSummary: FundStructureSetupValidationSummary;
}

export interface FundStructureSetupResult {
  organization: { organizationId: string; code: string; name: string };
  businessLane: { businessId: string; code: string; name: string };
  client?: { clientId: string; code: string; name: string } | null;
  fund?: { fundId: string; code: string; name: string } | null;
  legalEntity: { entityId: string; code: string; name: string; legalForm?: string; lifecycleStatus?: string; registrationNumber?: string | null };
  vehicle: { vehicleId: string; code: string; name: string };
  investmentPortfolio: { investmentPortfolioId: string; code: string; name: string };
  ownershipLinks: unknown[];
  accountHandoffAssignment: unknown;
  graph: { nodes: FundStructureNodePreview[]; ownershipLinks: unknown[] };
  validationSummary: FundStructureSetupValidationSummary;
}
