import { describe, expect, it } from "vitest";
import {
  CONFIGURE_SECTION_LINKS,
  appendChartPathSegment,
  buildChartPathSegments,
  buildConfigureActivationSummary,
  buildConfigureChangePreview,
  buildConfigureOperationalReadinessSummary,
  detectChartPathSeparator,
  filterConfigureSearch,
  isConfigureSectionAnchor,
  parseConfigureKeyValuePairs,
  resolveConfigureAnchorForLabel,
  serializeConfigureKeyValuePairs
} from "@/screens/accounting-screen.configure-panel.view-model";

describe("configure key/value parsing", () => {
  it("parses newline key=value text and ignores blank lines", () => {
    const pairs = parseConfigureKeyValuePairs("fundId=fund-alpha\n\nbookId=book-primary\n");
    expect(pairs.map((pair) => ({ key: pair.key, value: pair.value }))).toEqual([
      { key: "fundId", value: "fund-alpha" },
      { key: "bookId", value: "book-primary" }
    ]);
  });

  it("splits only on the first equals so colon-keyed mappings round-trip", () => {
    const text = "Meridian:Account=external-account-id";
    const pairs = parseConfigureKeyValuePairs(text);
    expect(pairs).toHaveLength(1);
    expect(pairs[0].key).toBe("Meridian:Account");
    expect(pairs[0].value).toBe("external-account-id");
    expect(serializeConfigureKeyValuePairs(pairs)).toBe(text);
  });

  it("drops fully empty rows but keeps keyed rows with empty values", () => {
    expect(serializeConfigureKeyValuePairs([
      { id: "a", key: "", value: "" },
      { id: "b", key: "fundId", value: "" }
    ])).toBe("fundId=");
  });

  it("round-trips multi-line content", () => {
    const text = "Class=fund-alpha\nBook=book-primary\nDepartment=fund-accounting";
    expect(serializeConfigureKeyValuePairs(parseConfigureKeyValuePairs(text))).toBe(text);
  });
});

describe("configure section anchors", () => {
  it("recognizes known section anchors", () => {
    for (const section of CONFIGURE_SECTION_LINKS) {
      expect(isConfigureSectionAnchor(section.anchorId)).toBe(true);
    }
    expect(isConfigureSectionAnchor("configure-section-unknown")).toBe(false);
  });

  it("routes labels to the section that fixes them", () => {
    expect(resolveConfigureAnchorForLabel("Chart setup incomplete")).toBe("configure-section-chart");
    expect(resolveConfigureAnchorForLabel("Posting rule promotion")).toBe("configure-section-rules");
    expect(resolveConfigureAnchorForLabel("External GL mapping missing")).toBe("configure-section-mappings");
    expect(resolveConfigureAnchorForLabel("Ledger book rollout")).toBe("configure-section-books");
    expect(resolveConfigureAnchorForLabel("Migration certification")).toBe("configure-section-activation");
    expect(resolveConfigureAnchorForLabel("Something else")).toBe("configure-section-setup");
  });
});

describe("configure search", () => {
  it("returns nothing for an empty query", () => {
    expect(filterConfigureSearch("")).toEqual([]);
    expect(filterConfigureSearch("   ")).toEqual([]);
  });

  it("matches labels and keywords across all terms", () => {
    const results = filterConfigureSearch("external export");
    expect(results.some((entry) => entry.anchorId === "configure-section-mappings")).toBe(true);
  });

  it("finds the rules section by keyword", () => {
    const results = filterConfigureSearch("dry run");
    expect(results.map((entry) => entry.anchorId)).toContain("configure-section-rules");
  });
});

describe("activation summary", () => {
  it("summarizes readiness rows and blockers with a deep-link anchor", () => {
    const summary = buildConfigureActivationSummary({
      canActivate: false,
      setupReadinessRows: [
        { id: "chart", label: "Chart coverage", value: "2 of 5", detail: "", tone: "warning" },
        { id: "books", label: "Ledger books", value: "1 active", detail: "", tone: "success" }
      ],
      productionReadiness: {
        blockerIssues: [
          { id: "gl", label: "External GL mapping", message: "", suggestedAction: "Map accounts", evidenceLabel: "", tone: "danger" }
        ]
      } as never
    });

    expect(summary.blockerCount).toBe(1);
    expect(summary.readyCount).toBe(1);
    expect(summary.tone).toBe("danger");
    const glItem = summary.items.find((item) => item.label === "External GL mapping");
    expect(glItem?.anchorId).toBe("configure-section-mappings");
    expect(summary.summaryLabel).toContain("blocker");
  });

  it("reports ready when activation is allowed", () => {
    const summary = buildConfigureActivationSummary({
      canActivate: true,
      setupReadinessRows: [],
      productionReadiness: { blockerIssues: [] } as never
    });
    expect(summary.tone).toBe("success");
    expect(summary.summaryLabel).toBe("Ready to activate");
  });
});

describe("change preview", () => {
  const editorWithSave = (canSave: boolean) => ({ canSave, saveDisabledReason: canSave ? null : "Nothing to save" });

  it("flags editors with unsaved changes and reports activation outlook", () => {
    const preview = buildConfigureChangePreview({
      chartAccountEditor: editorWithSave(true) as never,
      productionCertificationProfile: editorWithSave(false) as never,
      tenantAdministrationProfile: editorWithSave(false) as never,
      externalGlMappingProfile: editorWithSave(true) as never,
      canActivate: false,
      activateDisabledReason: "Resolve blockers first",
      dryRunPreview: null
    });

    expect(preview.pendingCount).toBe(2);
    expect(preview.headline).toContain("2 editors");
    expect(preview.activationTone).toBe("warning");
    expect(preview.currentVersionLabel).toBe("Current saved version: Not active");
    expect(preview.activationBadgeLabel).toBe("Draft activation blocked");
    expect(preview.activationLabel).toBe("Resolve blockers first");
  });

  it("notes an available dry-run preview when activation-ready", () => {
    const preview = buildConfigureChangePreview({
      chartAccountEditor: editorWithSave(false) as never,
      productionCertificationProfile: editorWithSave(false) as never,
      tenantAdministrationProfile: editorWithSave(false) as never,
      externalGlMappingProfile: editorWithSave(false) as never,
      canActivate: true,
      activateDisabledReason: null,
      dryRunPreview: { title: "x" } as never
    });
    expect(preview.pendingCount).toBe(0);
    expect(preview.activationTone).toBe("success");
    expect(preview.activationBadgeLabel).toBe("Draft ready to activate");
    expect(preview.activationLabel).toContain("dry-run preview");
  });

  it("separates the current active version from unsaved draft editor changes", () => {
    const preview = buildConfigureChangePreview({
      chartAccountEditor: editorWithSave(true) as never,
      productionCertificationProfile: editorWithSave(true) as never,
      tenantAdministrationProfile: editorWithSave(false) as never,
      externalGlMappingProfile: editorWithSave(false) as never,
      canActivate: false,
      activateDisabledReason: "Current saved accounting configuration is already active.",
      dryRunPreview: null
    });

    expect(preview).toMatchObject({
      pendingCount: 2,
      currentVersionLabel: "Current saved version: Active",
      activationBadgeLabel: "Draft promotion blocked",
      activationTone: "warning"
    });
    expect(preview.headline).toBe("2 editors with unsaved draft changes");
    expect(preview.activationLabel).toContain("current saved version remains active");
    expect(preview.activationLabel).toContain("pending draft changes");
  });

  it("does not show a blocked draft when the current version is active and no draft exists", () => {
    const preview = buildConfigureChangePreview({
      chartAccountEditor: editorWithSave(false) as never,
      productionCertificationProfile: editorWithSave(false) as never,
      tenantAdministrationProfile: editorWithSave(false) as never,
      externalGlMappingProfile: editorWithSave(false) as never,
      canActivate: false,
      activateDisabledReason: "Current saved accounting configuration is already active.",
      dryRunPreview: null
    });

    expect(preview).toMatchObject({
      pendingCount: 0,
      currentVersionLabel: "Current saved version: Active",
      activationBadgeLabel: "No draft to activate",
      activationTone: "default",
      headline: "No unsaved draft changes"
    });
  });
});

describe("overall configure readiness", () => {
  const editorWithSave = (canSave: boolean) => ({ canSave });

  it("fails closed when a nominal shared score omits evidence and adjacent gates are unresolved", () => {
    const summary = buildConfigureOperationalReadinessSummary({
      productionReadiness: {
        statusLabel: "Ready",
        scoreLabel: "100/100",
        ledgerBookRolloutLabel: "Ledger-book rollout evidence unavailable",
        tenantAdministrationLabel: "Tenant administration evidence unavailable",
        dimensionalReportingLabel: "Dimensional reporting evidence unavailable",
        externalGlLabel: "2 providers | 1 certified mapping | live posting disabled",
        components: [],
        blockerIssues: [],
        loading: false,
        errorText: null
      } as never,
      chartAccountEditor: editorWithSave(true) as never,
      productionCertificationProfile: editorWithSave(true) as never,
      tenantAdministrationProfile: editorWithSave(true) as never,
      externalGlMappingProfile: editorWithSave(true) as never,
      ruleTestSuite: null,
      canActivate: false,
      activateDisabledReason: "Resolve readiness gates before activation."
    });

    expect(summary).toMatchObject({
      statusLabel: "Review required",
      tone: "danger",
      gateLabel: "8 review items"
    });
    expect(summary.issueSummaryLabel).not.toContain("0");
    expect(summary.reviewItems).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "component-checks-unavailable", tone: "danger" }),
      expect.objectContaining({ id: "ledger-rollout-evidence", tone: "danger" }),
      expect.objectContaining({ id: "tenant-administration-evidence", tone: "danger" }),
      expect.objectContaining({ id: "dimensional-reporting-evidence", tone: "warning" }),
      expect.objectContaining({ id: "external-gl-guarded-mode", tone: "warning" }),
      expect.objectContaining({ id: "pending-editor-changes", label: "4 editors have unsaved changes" }),
      expect.objectContaining({ id: "rule-suite-not-run" }),
      expect.objectContaining({ id: "activation-gate-closed", tone: "danger" })
    ]));
  });

  it("reports ready only when evidence, tests, edits, and the activation gate are clear", () => {
    const summary = buildConfigureOperationalReadinessSummary({
      productionReadiness: {
        statusLabel: "Ready",
        scoreLabel: "96/100",
        ledgerBookRolloutLabel: "2 books | 1 open period | 0 rollout blockers | 10/10 workflow controls",
        tenantAdministrationLabel: "23/23 admin controls",
        dimensionalReportingLabel: "9/9 ledger/query/report/export dimension controls",
        externalGlLabel: "2 providers | 2 certified mappings | live posting enabled",
        components: [{ id: "rules", tone: "success" }],
        blockerIssues: [],
        loading: false,
        errorText: null
      } as never,
      chartAccountEditor: editorWithSave(false) as never,
      productionCertificationProfile: editorWithSave(false) as never,
      tenantAdministrationProfile: editorWithSave(false) as never,
      externalGlMappingProfile: editorWithSave(false) as never,
      ruleTestSuite: { statusTone: "success", summaryLabel: "All tests passed" } as never,
      canActivate: true,
      activateDisabledReason: null
    });

    expect(summary).toMatchObject({
      statusLabel: "Ready",
      tone: "success",
      gateLabel: "All gates clear",
      issueSummaryLabel: "All readiness evidence and activation gates are clear",
      reviewItems: []
    });
  });

  it("does not treat an already-active version as an activation blocker by itself", () => {
    const summary = buildConfigureOperationalReadinessSummary({
      productionReadiness: {
        statusLabel: "Ready",
        scoreLabel: "100/100",
        ledgerBookRolloutLabel: "1 book | 10/10 workflow controls",
        tenantAdministrationLabel: "23/23 admin controls",
        dimensionalReportingLabel: "9/9 dimension controls",
        externalGlLabel: "Guarded export enabled",
        components: [{ id: "rules", tone: "success" }],
        blockerIssues: [],
        loading: false,
        errorText: null
      } as never,
      chartAccountEditor: editorWithSave(false) as never,
      productionCertificationProfile: editorWithSave(false) as never,
      tenantAdministrationProfile: editorWithSave(false) as never,
      externalGlMappingProfile: editorWithSave(false) as never,
      ruleTestSuite: { statusTone: "success", summaryLabel: "All tests passed" } as never,
      canActivate: false,
      activateDisabledReason: "Current saved accounting configuration is already active."
    });

    expect(summary.statusLabel).toBe("Ready");
    expect(summary.reviewItems.some((item) => item.id === "activation-gate-closed")).toBe(false);
  });
});

describe("chart path builder helpers", () => {
  it("detects the separator and defaults to dot", () => {
    expect(detectChartPathSeparator("1200.Investments")).toBe(".");
    expect(detectChartPathSeparator("assets/current")).toBe("/");
    expect(detectChartPathSeparator("root")).toBe(".");
  });

  it("builds cumulative breadcrumb segments", () => {
    const segments = buildChartPathSegments("1000.Assets.Cash");
    expect(segments.map((segment) => segment.path)).toEqual(["1000", "1000.Assets", "1000.Assets.Cash"]);
    expect(segments.map((segment) => segment.label)).toEqual(["1000", "Assets", "Cash"]);
  });

  it("appends a segment using the existing separator", () => {
    expect(appendChartPathSegment("1000.Assets", "Cash")).toBe("1000.Assets.Cash");
    expect(appendChartPathSegment("assets/current", "cash")).toBe("assets/current/cash");
    expect(appendChartPathSegment("", "Assets")).toBe("Assets");
    expect(appendChartPathSegment("1000", "  ")).toBe("1000");
  });
});
