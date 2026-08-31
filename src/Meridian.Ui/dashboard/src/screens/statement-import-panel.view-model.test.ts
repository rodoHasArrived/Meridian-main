import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api-errors";
import {
  buildStatementMappingProfileDraft,
  statementColumnConfidenceBadgeVariant,
  statementMappingProfileFromDraft,
  useStatementImportPanelViewModel,
  validateStatementImportCommitForm,
  buildStatementImportPreviewRequirements,
  DEFAULT_STATEMENT_IMPORT_COMMIT_FORM,
  type StatementImportPanelServices
} from "@/screens/statement-import-panel.view-model";
import type {
  StatementConnectorDescriptor,
  StatementImportCommitResult,
  StatementImportPreview,
  StatementMappingProfile
} from "@/types";

const fixtureConnectors: StatementConnectorDescriptor[] = [
  {
    connectorId: "generic-csv",
    displayName: "Generic CSV",
    fileExtensions: [".csv"],
    supportsFileImport: true,
    supportsRemoteFetch: false,
    requiresMappingProfile: false,
    defaultProfileId: "builtin-generic"
  },
  {
    connectorId: "ofx",
    displayName: "OFX",
    fileExtensions: [".ofx"],
    supportsFileImport: true,
    supportsRemoteFetch: true,
    requiresMappingProfile: false,
    defaultProfileId: null
  }
];

const fixtureProfiles: StatementMappingProfile[] = [
  {
    schemaVersion: 1,
    profileId: "builtin-generic",
    displayName: "Built-in generic",
    format: "csv",
    csv: { delimiter: ",", quote: "\"", hasHeader: true },
    culture: "en-US",
    dateFormats: ["yyyy-MM-dd"],
    fields: [
      { canonicalField: "symbol", sourceColumn: "Symbol", aliases: ["Ticker"], required: true }
    ],
    activityCodes: [{ sourceCode: "BUY", canonicalActivityType: "Buy" }],
    lastAcceptedFingerprint: null,
    isBuiltIn: true,
    notes: null
  },
  {
    schemaVersion: 1,
    profileId: "custom-ibkr",
    displayName: "Custom IBKR",
    format: "csv",
    csv: { delimiter: ";", quote: "\"", hasHeader: true },
    culture: null,
    dateFormats: null,
    fields: [],
    activityCodes: [],
    lastAcceptedFingerprint: "abc123",
    isBuiltIn: false,
    notes: "Operator maintained."
  }
];

const fixturePreview: StatementImportPreview = {
  connectorId: "generic-csv",
  connectorDisplayName: "Generic CSV",
  profileId: "builtin-generic",
  fileName: "statement.csv",
  fileSizeBytes: 128,
  detectedColumns: ["Symbol", "Qty"],
  columnMappings: [
    { sourceColumn: "Symbol", canonicalField: "symbol", confidence: "Exact", score: 1, rationale: "Exact header match." },
    { sourceColumn: "Qty", canonicalField: "quantity", confidence: "Alias", score: 0.9, rationale: "Alias match." }
  ],
  recordCount: 2,
  kindSummaries: [
    {
      kind: "Transaction",
      recordCount: 2,
      sampleRecords: [
        {
          kind: "Transaction",
          account: "U-100",
          symbol: "AAPL",
          quantity: 10,
          price: 187.25,
          cashAmount: -1872.5,
          activityType: "Buy",
          tradeDate: "2026-06-01",
          settlementDate: null,
          currency: "USD",
          feesCommission: null,
          externalTransactionId: "TXN-1"
        }
      ]
    }
  ],
  issues: [
    { code: "FORMAT_DRIFT", severity: "Warning", rowNumber: null, field: null, message: "Header layout drifted." }
  ],
  profileSuggestions: [{ profileId: "custom-ibkr", displayName: "Custom IBKR", score: 0.82 }],
  status: "ReadyToImport",
  nextAction: "Commit the import to create a reconciliation run."
};

const fixtureCommitResult: StatementImportCommitResult = {
  runId: "stmt-run-77",
  duplicate: false,
  recordCount: 2,
  kindSummaries: fixturePreview.kindSummaries,
  breakCount: 1,
  caseCount: 0,
  retainedSourcePath: "retained/source/statement.csv",
  retainedCanonicalPath: "retained/canonical/statement.json",
  status: "Committed",
  nextAction: "Review breaks in the reconciliation queue."
};

function makeServices(overrides: Partial<StatementImportPanelServices> = {}): StatementImportPanelServices {
  return {
    getConnectors: vi.fn(async () => fixtureConnectors),
    listMappingProfiles: vi.fn(async () => fixtureProfiles),
    upsertMappingProfile: vi.fn(async (profile: StatementMappingProfile) => profile),
    previewImport: vi.fn(async () => fixturePreview),
    commitImport: vi.fn(async () => fixtureCommitResult),
    ...overrides
  };
}

function makeFile(name = "statement.csv"): File {
  return new File(["Symbol,Qty\nAAPL,10"], name, { type: "text/csv" });
}

function fillCommitForm(result: { current: ReturnType<typeof useStatementImportPanelViewModel> }) {
  act(() => result.current.updateCommitForm("sourceInstitution", "Interactive Brokers"));
  act(() => result.current.updateCommitForm("fundAccountId", "FUND-A"));
  act(() => result.current.updateCommitForm("externalAccountId", "U-100"));
  act(() => result.current.updateCommitForm("periodStart", "2026-06-01"));
  act(() => result.current.updateCommitForm("periodEnd", "2026-06-30"));
}

describe("validateStatementImportCommitForm", () => {
  it("requires a file and all commit fields", () => {
    const errors = validateStatementImportCommitForm(DEFAULT_STATEMENT_IMPORT_COMMIT_FORM, null);
    expect(errors.file).toBeDefined();
    expect(errors.sourceInstitution).toBeDefined();
    expect(errors.fundAccountId).toBeDefined();
    expect(errors.externalAccountId).toBeDefined();
    expect(errors.periodStart).toBeDefined();
    expect(errors.periodEnd).toBeDefined();
  });

  it("requires periodStart <= periodEnd and returns empty for a valid form", () => {
    const validForm = {
      ...DEFAULT_STATEMENT_IMPORT_COMMIT_FORM,
      sourceInstitution: "IBKR",
      fundAccountId: "FUND-A",
      externalAccountId: "U-100",
      periodStart: "2026-06-01",
      periodEnd: "2026-06-30"
    };
    expect(validateStatementImportCommitForm(validForm, makeFile())).toEqual({});
    expect(
      validateStatementImportCommitForm({ ...validForm, periodStart: "2026-07-01" }, makeFile()).periodEnd
    ).toBeDefined();
  });
});

describe("buildStatementImportPreviewRequirements", () => {
  const completeForm = {
    ...DEFAULT_STATEMENT_IMPORT_COMMIT_FORM,
    sourceInstitution: "IBKR",
    fundAccountId: "FUND-A",
    externalAccountId: "U-100",
    periodStart: "2026-06-01",
    periodEnd: "2026-06-30"
  };

  it("stays quiet until a file is chosen, so an untouched panel does not nag", () => {
    const requirements = buildStatementImportPreviewRequirements(DEFAULT_STATEMENT_IMPORT_COMMIT_FORM, null);

    expect(requirements.blocked).toBe(false);
    expect(requirements.detail).toBe("");
  });

  it("names every field the preview is waiting on once a file is chosen", () => {
    const requirements = buildStatementImportPreviewRequirements(DEFAULT_STATEMENT_IMPORT_COMMIT_FORM, makeFile());

    expect(requirements.blocked).toBe(true);
    expect(requirements.missingFields).toEqual([
      "sourceInstitution",
      "fundAccountId",
      "externalAccountId",
      "periodStart",
      "periodEnd"
    ]);
    expect(requirements.detail).toContain("Source institution");
    expect(requirements.detail).toContain("Period end");
    expect(requirements.detail).toContain("Commit import");
  });

  it("names the statement so the message is about the file the user just dropped", () => {
    const requirements = buildStatementImportPreviewRequirements(
      DEFAULT_STATEMENT_IMPORT_COMMIT_FORM,
      makeFile("june-statement.csv")
    );

    expect(requirements.detail).toContain("june-statement.csv");
  });

  it("narrows to the one field still outstanding", () => {
    const requirements = buildStatementImportPreviewRequirements(
      { ...completeForm, periodEnd: "" },
      makeFile()
    );

    expect(requirements.missingLabels).toEqual(["Period end"]);
    expect(requirements.detail).toContain("complete Period end");
  });

  it("reports a filled-but-invalid field too, since the preview is equally blocked", () => {
    const requirements = buildStatementImportPreviewRequirements(
      { ...completeForm, periodStart: "2026-07-01" },
      makeFile()
    );

    expect(requirements.blocked).toBe(true);
    expect(requirements.missingLabels).toEqual(["Period end"]);
  });

  it("clears once the form is complete", () => {
    const requirements = buildStatementImportPreviewRequirements(completeForm, makeFile());

    expect(requirements.blocked).toBe(false);
    expect(requirements.missingFields).toEqual([]);
    expect(requirements.title).toBe("");
  });
});

describe("statement mapping profile drafts", () => {
  it("round-trips a profile through the editable draft", () => {
    const draft = buildStatementMappingProfileDraft(fixtureProfiles[1]);
    expect(draft.csvDelimiter).toBe(";");
    expect(draft.isBuiltIn).toBe(false);

    const profile = statementMappingProfileFromDraft({ ...draft, dateFormatsText: "yyyy-MM-dd, MM/dd/yyyy" });
    expect(profile.profileId).toBe("custom-ibkr");
    expect(profile.dateFormats).toEqual(["yyyy-MM-dd", "MM/dd/yyyy"]);
    expect(profile.lastAcceptedFingerprint).toBe("abc123");
  });

  it("clones built-ins into an editable draft with a fresh profile id", () => {
    const draft = buildStatementMappingProfileDraft(fixtureProfiles[0], { clone: true });
    expect(draft.profileId).toBe("builtin-generic-copy");
    expect(draft.isBuiltIn).toBe(false);
    expect(draft.isClone).toBe(true);
    expect(draft.lastAcceptedFingerprint).toBeNull();
    expect(draft.fields[0].aliasesText).toBe("Ticker");
  });
});

describe("statementColumnConfidenceBadgeVariant", () => {
  it("maps confidences onto badge variants", () => {
    expect(statementColumnConfidenceBadgeVariant("Exact")).toBe("success");
    expect(statementColumnConfidenceBadgeVariant("Alias")).toBe("paper");
    expect(statementColumnConfidenceBadgeVariant("Fuzzy")).toBe("warning");
    expect(statementColumnConfidenceBadgeVariant("Unmapped")).toBe("danger");
  });
});

describe("useStatementImportPanelViewModel", () => {
  it("loads connectors and mapping profiles on mount", async () => {
    const services = makeServices();
    const { result } = renderHook(() => useStatementImportPanelViewModel({ services }));

    expect(result.current.loading).toBe(true);
    await waitFor(() => expect(result.current.loading).toBe(false));

    expect(services.getConnectors).toHaveBeenCalledTimes(1);
    expect(services.listMappingProfiles).toHaveBeenCalledTimes(1);
    expect(result.current.connectors).toHaveLength(2);
    expect(result.current.profiles).toHaveLength(2);
    expect(result.current.loadError).toBeNull();
    expect(result.current.fileAccept).toBe(".csv,.ofx");
  });

  it("fills Source institution from the connector the user picked", async () => {
    // One of the five fields that hold the preview back answers itself: the connector is the
    // institution, so the user should not have to retype what they just selected.
    const services = makeServices();
    const { result } = renderHook(() => useStatementImportPanelViewModel({ services }));
    await waitFor(() => expect(result.current.loading).toBe(false));

    act(() => result.current.selectConnector("generic-csv"));

    expect(result.current.commitForm.sourceInstitution).toBe("Generic CSV");
  });

  it("never overwrites a Source institution the user typed", async () => {
    const services = makeServices();
    const { result } = renderHook(() => useStatementImportPanelViewModel({ services }));
    await waitFor(() => expect(result.current.loading).toBe(false));
    act(() => result.current.updateCommitForm("sourceInstitution", "My custodian"));

    act(() => result.current.selectConnector("generic-csv"));

    expect(result.current.commitForm.sourceInstitution).toBe("My custodian");
  });

  it("explains what the preview is waiting on, and stops once the form is complete", async () => {
    const services = makeServices();
    const { result } = renderHook(() => useStatementImportPanelViewModel({ services }));
    await waitFor(() => expect(result.current.loading).toBe(false));

    act(() => result.current.selectFile(makeFile()));

    // Previously this state was silent: no preview, no error, and a commit button asking for a
    // preview the panel was refusing to run.
    expect(result.current.previewRequirements.blocked).toBe(true);
    expect(result.current.previewRequirements.missingLabels).toContain("Fund account");
    expect(result.current.commitDisabledReason).toContain("Fund account");
    expect(result.current.commitDisabledReason).not.toContain("Preview the statement before committing");

    fillCommitForm(result);
    await waitFor(() => expect(result.current.previewRequirements.blocked).toBe(false));
  });

  it("surfaces a load error when the connector library fails", async () => {
    const services = makeServices({
      getConnectors: vi.fn(async () => {
        throw new ApiError({ path: "/api/x", status: 500, detail: "boom" });
      })
    });
    const { result } = renderHook(() => useStatementImportPanelViewModel({ services }));

    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.loadError).not.toBeNull();
    expect(result.current.connectors).toHaveLength(0);
  });

  it("previews a selected file and populates mappings, kinds, and issues", async () => {
    const services = makeServices();
    const { result } = renderHook(() => useStatementImportPanelViewModel({ services }));
    await waitFor(() => expect(result.current.loading).toBe(false));
    fillCommitForm(result);

    await act(async () => {
      result.current.selectFile(makeFile());
    });

    await waitFor(() => expect(result.current.preview).not.toBeNull());
    expect(services.previewImport).toHaveBeenCalledTimes(1);
    expect(services.previewImport).toHaveBeenCalledWith(expect.objectContaining({
      connectorId: undefined,
      mappingProfileId: undefined
    }));
    expect(result.current.preview?.columnMappings).toHaveLength(2);
    expect(result.current.kindSummaries.map((summary) => summary.kind)).toEqual(["Transaction"]);
    expect(result.current.selectedKind).toBe("Transaction");
    expect(result.current.selectedKindSummary?.sampleRecords[0].symbol).toBe("AAPL");
    expect(result.current.issues.map((issue) => issue.code)).toContain("FORMAT_DRIFT");
    expect(result.current.previewError).toBeNull();
  });

  it("surfaces preview failures without keeping stale preview data", async () => {
    const services = makeServices({
      previewImport: vi.fn(async () => {
        throw new ApiError({ path: "/api/preview", status: 400, detail: "Unparseable statement." });
      })
    });
    const { result } = renderHook(() => useStatementImportPanelViewModel({ services }));
    await waitFor(() => expect(result.current.loading).toBe(false));
    fillCommitForm(result);

    await act(async () => {
      result.current.selectFile(makeFile());
    });

    await waitFor(() => expect(result.current.previewError).not.toBeNull());
    expect(result.current.preview).toBeNull();
    expect(result.current.previewBusy).toBe(false);
    expect(result.current.previewError?.summary).toContain("Unparseable statement.");
  });

  it("re-runs the preview when the mapping profile changes", async () => {
    const services = makeServices();
    const { result } = renderHook(() => useStatementImportPanelViewModel({ services }));
    await waitFor(() => expect(result.current.loading).toBe(false));
    fillCommitForm(result);

    await act(async () => {
      result.current.selectFile(makeFile());
    });
    await waitFor(() => expect(result.current.preview).not.toBeNull());

    await act(async () => {
      result.current.selectProfile("custom-ibkr");
    });

    await waitFor(() => expect(services.previewImport).toHaveBeenCalledTimes(2));
    expect(services.previewImport).toHaveBeenLastCalledWith(expect.objectContaining({
      mappingProfileId: "custom-ibkr"
    }));
    expect(result.current.selectedProfileId).toBe("custom-ibkr");
  });

  it("does not re-preview on profile change while no file is selected", async () => {
    const services = makeServices();
    const { result } = renderHook(() => useStatementImportPanelViewModel({ services }));
    await waitFor(() => expect(result.current.loading).toBe(false));

    await act(async () => {
      result.current.selectProfile("custom-ibkr");
    });

    expect(services.previewImport).not.toHaveBeenCalled();
    expect(result.current.selectedProfileId).toBe("custom-ibkr");
  });

  it("saves a profile draft, refreshes the profile list, and re-previews", async () => {
    const services = makeServices();
    const { result } = renderHook(() => useStatementImportPanelViewModel({ services }));
    await waitFor(() => expect(result.current.loading).toBe(false));
    fillCommitForm(result);

    await act(async () => {
      result.current.selectFile(makeFile());
    });
    await waitFor(() => expect(result.current.preview).not.toBeNull());

    act(() => result.current.editProfile("custom-ibkr"));
    expect(result.current.profileDraft?.profileId).toBe("custom-ibkr");

    act(() => result.current.updateProfileDraft({ displayName: "Custom IBKR v2" }));
    await act(async () => {
      await result.current.saveProfileDraft();
    });

    expect(services.upsertMappingProfile).toHaveBeenCalledTimes(1);
    expect(services.upsertMappingProfile).toHaveBeenCalledWith(expect.objectContaining({
      profileId: "custom-ibkr",
      displayName: "Custom IBKR v2",
      isBuiltIn: false
    }));
    expect(services.listMappingProfiles).toHaveBeenCalledTimes(2);
    await waitFor(() => expect(services.previewImport).toHaveBeenCalledTimes(2));
    expect(services.previewImport).toHaveBeenLastCalledWith(expect.objectContaining({
      mappingProfileId: "custom-ibkr"
    }));
    expect(result.current.profileSaveMessage).toContain("Custom IBKR v2");
  });

  it("refuses to save built-in profiles but allows cloning them", async () => {
    const services = makeServices();
    const { result } = renderHook(() => useStatementImportPanelViewModel({ services }));
    await waitFor(() => expect(result.current.loading).toBe(false));

    act(() => result.current.editProfile("builtin-generic"));
    await act(async () => {
      await result.current.saveProfileDraft();
    });
    expect(services.upsertMappingProfile).not.toHaveBeenCalled();
    expect(result.current.profileDraftError).toContain("read-only");

    act(() => result.current.cloneProfile("builtin-generic"));
    expect(result.current.profileDraft).toMatchObject({
      profileId: "builtin-generic-copy",
      isBuiltIn: false,
      isClone: true
    });
  });

  it("commits the import with the validated form fields", async () => {
    const services = makeServices();
    const { result } = renderHook(() => useStatementImportPanelViewModel({ services }));
    await waitFor(() => expect(result.current.loading).toBe(false));
    fillCommitForm(result);

    await act(async () => {
      result.current.selectFile(makeFile());
    });
    await waitFor(() => expect(result.current.preview).not.toBeNull());

    act(() => result.current.selectProfile("custom-ibkr"));
    await waitFor(() => expect(services.previewImport).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(result.current.canCommit).toBe(true));

    await act(async () => {
      await result.current.commit();
    });

    expect(services.commitImport).toHaveBeenCalledTimes(1);
    expect(services.commitImport).toHaveBeenCalledWith(expect.objectContaining({
      mappingProfileId: "custom-ibkr",
      externalAccountId: "U-100",
      sourceKind: "broker",
      sourceInstitution: "Interactive Brokers",
      fundAccountId: "FUND-A",
      periodStart: "2026-06-01",
      periodEnd: "2026-06-30"
    }));
    expect(result.current.commitResult?.runId).toBe("stmt-run-77");
    expect(result.current.commitOutcome).toBe("committed");
    expect(result.current.commitError).toBeNull();
  });

  it("blocks commit when preview errors remain", async () => {
    const blockedPreview: StatementImportPreview = {
      ...fixturePreview,
      status: "NeedsAttention",
      issues: [
        { code: "MISSING_ACCOUNT", severity: "Error", rowNumber: 2, field: "account", message: "Account is required." }
      ]
    };
    const services = makeServices({
      previewImport: vi.fn(async () => blockedPreview)
    });
    const { result } = renderHook(() => useStatementImportPanelViewModel({ services }));
    await waitFor(() => expect(result.current.loading).toBe(false));
    fillCommitForm(result);

    await act(async () => {
      result.current.selectFile(makeFile());
    });
    await waitFor(() => expect(result.current.preview?.status).toBe("NeedsAttention"));

    expect(result.current.canCommit).toBe(false);
    expect(result.current.commitDisabledReason).toContain("Resolve preview errors");

    await act(async () => {
      await result.current.commit();
    });

    expect(services.commitImport).not.toHaveBeenCalled();
    expect(result.current.commitError?.summary).toContain("Resolve preview errors");
  });

  it("reports duplicate commits distinctly", async () => {
    const services = makeServices({
      commitImport: vi.fn(async () => ({ ...fixtureCommitResult, duplicate: true }))
    });
    const { result } = renderHook(() => useStatementImportPanelViewModel({ services }));
    await waitFor(() => expect(result.current.loading).toBe(false));
    fillCommitForm(result);

    await act(async () => {
      result.current.selectFile(makeFile());
    });
    await waitFor(() => expect(result.current.preview).not.toBeNull());

    await act(async () => {
      await result.current.commit();
    });

    expect(result.current.commitOutcome).toBe("duplicate");
    expect(result.current.commitResult?.duplicate).toBe(true);
  });

  it("blocks commit and surfaces field errors when required fields are missing", async () => {
    const services = makeServices();
    const { result } = renderHook(() => useStatementImportPanelViewModel({ services }));
    await waitFor(() => expect(result.current.loading).toBe(false));

    await act(async () => {
      await result.current.commit();
    });

    expect(services.commitImport).not.toHaveBeenCalled();
    expect(result.current.commitFormErrors.file).toBeDefined();
    expect(result.current.commitFormErrors.sourceInstitution).toBeDefined();
    expect(result.current.commitFormErrors.periodStart).toBeDefined();
    expect(result.current.commitError?.summary).toContain("Fix the highlighted fields");
  });

  it("surfaces commit failures from the API", async () => {
    const services = makeServices({
      commitImport: vi.fn(async () => {
        throw new ApiError({ path: "/api/commit", status: 400, detail: "Blocking parse errors remain." });
      })
    });
    const { result } = renderHook(() => useStatementImportPanelViewModel({ services }));
    await waitFor(() => expect(result.current.loading).toBe(false));
    fillCommitForm(result);

    await act(async () => {
      result.current.selectFile(makeFile());
    });
    await waitFor(() => expect(result.current.preview).not.toBeNull());

    await act(async () => {
      await result.current.commit();
    });

    expect(result.current.commitResult).toBeNull();
    expect(result.current.commitOutcome).toBeNull();
    expect(result.current.commitError?.summary).toContain("Blocking parse errors remain.");
  });
});
