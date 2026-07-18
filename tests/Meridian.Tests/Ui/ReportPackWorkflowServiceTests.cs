using System.Net;
using System.Net.Http.Json;
using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Application.Services;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Reporting;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

/// <summary>
/// Scenario tests guard the controller close-reporting path where an operator submits a freshly created W4 report pack for review before approval and publication.
/// </summary>
public sealed class ReportPackWorkflowServiceTests
{
    private const string TestTenantId = "tenant-a";
    private const string TestCompanyId = "company-a";

    [Fact]
    public void Transition_FreshDraftSubmitApprovePublish_CompletesW4LifecycleWithoutLegacyIntermediateState()
    {
        var svc = new ReportPackWorkflowService();
        var created = svc.Create("fund-a", "acct-1", "2026-03", new VersionedReportTemplateIdDto("board-pack", 1), "author");

        var submitted = svc.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        var approved = svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
        var published = svc.Publish(
            created.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            [new ReportPackEvidenceLinkDto("report-pack-1", "Report pack manifest", "/evidence/report-pack-1", "reporting")]);

        submitted.State.Should().Be(ReportPackWorkflowStateDto.InReview);
        approved.State.Should().Be(ReportPackWorkflowStateDto.Approved);
        published.State.Should().Be(ReportPackWorkflowStateDto.Published);
        published.Publication.Should().NotBeNull();
        published.Publication!.SignedOffBy.Should().Be("controller");
        published.Publication.EvidenceHash.Should().Be("sha256:abc123");
        published.AuditTrail.Should().HaveCount(4);
        published.AuditTrail.Should().ContainSingle(e =>
            e.Action == "create"
            && e.Actor == "author"
            && e.FromState == ReportPackWorkflowStateDto.Draft
            && e.ToState == ReportPackWorkflowStateDto.Draft);
        published.AuditTrail.Should().ContainSingle(e =>
            e.Action == "inreview"
            && e.Actor == "reviewer"
            && e.FromState == ReportPackWorkflowStateDto.Draft
            && e.ToState == ReportPackWorkflowStateDto.InReview);
    }


    [Fact]
    public void Transition_ToPublished_RequiresGovernedPublicationMetadata()
    {
        var svc = new ReportPackWorkflowService();
        var created = svc.Create("fund-a", "acct-1", "2026-03", new VersionedReportTemplateIdDto("board-pack", 1), "author");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");

        Action act = () => svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Published, "publisher", "publisher");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Report pack publication requires sign-off, evidence hash, and retained manifest metadata.");
    }

    [Fact]
    public void TemplateRegistry_CreateSubmitApproveRevision_TracksVersionApprovalAndBlocksDraftRender()
    {
        var svc = new ReportTemplateRegistryService();

        svc.List().Should().Contain(record =>
            record.Definition.TemplateId.Name == "investor-monthly-statement" &&
            record.Status == ReportTemplateLifecycleStatusDto.Approved &&
            record.IsBuiltIn);

        var draft = svc.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "investor-monthly-statement",
                "Investor Monthly Statement v2",
                ["cover", "performance", "positions", "fees"],
                [new ReportTemplateParameterDefinitionDto("period", Required: true)],
                Family: "InvestorStatement",
                BasedOnVersion: 1,
                Rationale: "Add management fee detail"),
            "report.author");

        draft.Status.Should().Be(ReportTemplateLifecycleStatusDto.Draft);
        draft.Definition.TemplateId.Version.Should().Be(2);
        draft.BasedOnTemplateId.Should().Be(new VersionedReportTemplateIdDto("investor-monthly-statement", 1));
        draft.ValidationIssues.Should().BeEmpty();
        svc.Get(draft.Definition.TemplateId).Should().BeNull();

        var submitted = svc.Submit(draft.Definition.TemplateId, "report.author", "ready for controller review");
        var approved = svc.Approve(
            draft.Definition.TemplateId,
            new ReportTemplateDecisionRequestDto("Controller approved fee disclosure", "APP-TPL-2"),
            "controller.admin");

        submitted.Status.Should().Be(ReportTemplateLifecycleStatusDto.InReview);
        approved.Status.Should().Be(ReportTemplateLifecycleStatusDto.Approved);
        approved.IsLatestApproved.Should().BeTrue();
        approved.ApprovalReference.Should().Be("APP-TPL-2");
        approved.AuditTrail.Select(entry => entry.Action).Should().ContainInOrder("draft", "submit", "approve");

        var builtIn = svc.List(includeSuperseded: true).Single(record =>
            record.Definition.TemplateId == new VersionedReportTemplateIdDto("investor-monthly-statement", 1));
        builtIn.Status.Should().Be(ReportTemplateLifecycleStatusDto.Approved);
        builtIn.IsLatestApproved.Should().BeFalse();

        var rendered = svc.Render(new RenderReportTemplateRequestDto(
            approved.Definition.TemplateId,
            new Dictionary<string, string> { ["period"] = "2026-05" }));
        rendered.MissingRequiredParameters.Should().BeEmpty();
        rendered.RenderedContent.Should().Contain("sections=cover,fees,performance,positions");
    }

    [Fact]
    public void TemplateRegistry_InvalidDraft_CannotSubmitForApproval()
    {
        var svc = new ReportTemplateRegistryService();
        var draft = svc.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "custom-board-pack",
                "Custom Board Pack",
                [],
                [],
                Rationale: "Missing section setup"),
            "report.author");

        Action act = () => svc.Submit(draft.Definition.TemplateId, "report.author");

        draft.ValidationIssues.Should().Contain("At least one report section or report writer grid is required.");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Template custom-board-pack@v1 is not ready for review: At least one report section or report writer grid is required.");
    }

    [Fact]
    public void TemplateRegistry_ReportWriterFormulaValidation_BlocksUnknownAndCircularReferences()
    {
        var svc = new ReportTemplateRegistryService();
        var draft = svc.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "invalid-formula-grid",
                "Invalid Formula Grid",
                [],
                [],
                Family: "CustomReport",
                Rationale: "Validate no-code formula dependencies",
                Grids:
                [
                    new ReportWriterGridDefinitionDto(
                        "formula-grid",
                        "Formula Grid",
                        ReportWriterGridKindDto.Pivot,
                        RowFields: ["sector"],
                        Metrics:
                        [
                            new ReportWriterMetricDefinitionDto("marketValue", "marketValue")
                        ],
                        Formulas:
                        [
                            new ReportWriterFormulaDefinitionDto("badReference", "{pnl} / {marketValue}"),
                            new ReportWriterFormulaDefinitionDto("selfReference", "{selfReference} + 1"),
                            new ReportWriterFormulaDefinitionDto("unsupportedTotal", "total(unconfiguredAmount)")
                        ])
                ]),
            "report.author");

        Action act = () => svc.Submit(draft.Definition.TemplateId, "report.author", "ready");

        draft.ValidationIssues.Should().Contain("Report writer grid 'formula-grid' formula 'badReference' references unknown metric or formula 'pnl'.");
        draft.ValidationIssues.Should().Contain("Report writer grid 'formula-grid' formula 'selfReference' cannot reference itself.");
        draft.ValidationIssues.Should().Contain("Report writer grid 'formula-grid' formula 'unsupportedTotal' total field 'unconfiguredAmount' is not a configured metric or metric source field.");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Template invalid-formula-grid@v1 is not ready for review: Report writer grid 'formula-grid' formula 'badReference' references unknown metric or formula 'pnl'.*");
    }

    [Fact]
    public void TemplateRegistry_ReportWriterFormulaValidation_AllowsPriorFormulaAndConfiguredTotals()
    {
        var svc = new ReportTemplateRegistryService();
        var draft = svc.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "valid-formula-grid",
                "Valid Formula Grid",
                [],
                [],
                Family: "CustomReport",
                Rationale: "Validate no-code formula dependencies",
                Grids:
                [
                    new ReportWriterGridDefinitionDto(
                        "formula-grid",
                        "Formula Grid",
                        ReportWriterGridKindDto.Pivot,
                        RowFields: ["sector"],
                        Metrics:
                        [
                            new ReportWriterMetricDefinitionDto("marketValue", "market_value"),
                            new ReportWriterMetricDefinitionDto("pnl", "pnl")
                        ],
                        Formulas:
                        [
                            new ReportWriterFormulaDefinitionDto("returnPct", "{pnl} / {marketValue} * 100"),
                            new ReportWriterFormulaDefinitionDto("fundWeightPct", "{marketValue} / total(market_value) * 100"),
                            new ReportWriterFormulaDefinitionDto("score", "{returnPct} + {fundWeightPct}")
                        ])
                ]),
            "report.author");

        draft.ValidationIssues.Should().BeEmpty();
        svc.Submit(draft.Definition.TemplateId, "report.author", "ready");
        var approved = svc.Approve(
            draft.Definition.TemplateId,
            new ReportTemplateDecisionRequestDto("Controller approved formula dependencies", "APP-FORMULA-1"),
            "controller.admin");

        approved.Status.Should().Be(ReportTemplateLifecycleStatusDto.Approved);
    }

    [Fact]
    public void TemplateRegistry_RenderFormulaGridWithReportingMathFunctions_ReturnsRoundedPercentAndBasisPoints()
    {
        var svc = new ReportTemplateRegistryService();
        var draft = svc.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "reporting-math-formula-grid",
                "Reporting Math Formula Grid",
                [],
                [],
                Family: "CustomReport",
                Rationale: "Allow no-code finance formulas without scripting",
                Grids:
                [
                    new ReportWriterGridDefinitionDto(
                        "strategy-reporting-math",
                        "Strategy Reporting Math",
                        ReportWriterGridKindDto.Pivot,
                        RowFields: ["strategy"],
                        Metrics:
                        [
                            new ReportWriterMetricDefinitionDto("marketValue", "marketValue"),
                            new ReportWriterMetricDefinitionDto("pnl", "pnl")
                        ],
                        Formulas:
                        [
                            new ReportWriterFormulaDefinitionDto("returnPct", "round(percent({pnl}, {marketValue}), 2)"),
                            new ReportWriterFormulaDefinitionDto("fundWeightPct", "round(percent({marketValue}, total(marketValue)), 1)"),
                            new ReportWriterFormulaDefinitionDto("returnBps", "round(basisPoints({pnl}, {marketValue}), 0)")
                        ])
                ]),
            "report.author");

        draft.ValidationIssues.Should().BeEmpty();
        svc.Submit(draft.Definition.TemplateId, "report.author", "ready");
        svc.Approve(
            draft.Definition.TemplateId,
            new ReportTemplateDecisionRequestDto("Controller approved no-code reporting math functions", "APP-FORMULA-MATH"),
            "controller.admin");

        var rendered = svc.Render(new RenderReportTemplateRequestDto(
            draft.Definition.TemplateId,
            new Dictionary<string, string>(),
            [
                new Dictionary<string, string> { ["strategy"] = "Core", ["marketValue"] = "300", ["pnl"] = "20" },
                new Dictionary<string, string> { ["strategy"] = "Credit", ["marketValue"] = "100", ["pnl"] = "-5" }
            ]));

        var grid = rendered.Grids.Should().ContainSingle().Subject;
        grid.Warnings.Should().BeEmpty();
        grid.Columns.Select(static column => column.Key).Should().ContainInOrder(
            "strategy",
            "marketValue",
            "pnl",
            "returnPct",
            "fundWeightPct",
            "returnBps");
        grid.Rows.Should().Contain(row =>
            row.Values["strategy"] == "Core" &&
            row.Values["returnPct"] == "6.67" &&
            row.Values["fundWeightPct"] == "75" &&
            row.Values["returnBps"] == "667");
        grid.Rows.Should().Contain(row =>
            row.Values["strategy"] == "Credit" &&
            row.Values["returnPct"] == "-5" &&
            row.Values["fundWeightPct"] == "25" &&
            row.Values["returnBps"] == "-500");
        grid.Lineage!.Formulas.Should().Contain(formula =>
            formula.Name == "returnPct" &&
            formula.SourceFields.Contains("pnl") &&
            formula.SourceFields.Contains("marketValue"));
        grid.Lineage.Formulas.Should().Contain(formula =>
            formula.Name == "fundWeightPct" &&
            formula.SourceFields.Contains("marketValue"));
    }

    [Fact]
    public void TemplateRegistry_RenderFormulaGridWithInvalidRoundScale_ReturnsGridWarning()
    {
        var svc = new ReportTemplateRegistryService();
        var draft = svc.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "invalid-round-scale-grid",
                "Invalid Round Scale Grid",
                [],
                [],
                Family: "CustomReport",
                Rationale: "Keep bad no-code formula math isolated to grid warnings",
                Grids:
                [
                    new ReportWriterGridDefinitionDto(
                        "invalid-round-scale",
                        "Invalid Round Scale",
                        ReportWriterGridKindDto.Detail,
                        Metrics: [new ReportWriterMetricDefinitionDto("pnl", "pnl")],
                        Formulas: [new ReportWriterFormulaDefinitionDto("badRound", "round({pnl}, 9)")])
                ]),
            "report.author");

        draft.ValidationIssues.Should().BeEmpty();
        svc.Submit(draft.Definition.TemplateId, "report.author", "ready");
        svc.Approve(
            draft.Definition.TemplateId,
            new ReportTemplateDecisionRequestDto("Controller approved warning isolation", "APP-FORMULA-WARN"),
            "controller.admin");

        var rendered = svc.Render(new RenderReportTemplateRequestDto(
            draft.Definition.TemplateId,
            new Dictionary<string, string>(),
            [new Dictionary<string, string> { ["pnl"] = "12.34" }]));

        var grid = rendered.Grids.Should().ContainSingle().Subject;
        grid.Rows.Should().ContainSingle().Which.Values["badRound"].Should().BeEmpty();
        grid.Warnings.Should().Contain("Formula 'badRound' could not be evaluated: round scale must be a whole number between 0 and 8.");
        rendered.Warnings.Should().Contain("Formula 'badRound' could not be evaluated: round scale must be a whole number between 0 and 8.");
    }

    [Fact]
    public void TemplateRegistry_ReportWriterContributionValidation_ReservesGeneratedContributionFields()
    {
        var svc = new ReportTemplateRegistryService();
        var draft = svc.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "invalid-contribution-grid",
                "Invalid Contribution Grid",
                [],
                [],
                Family: "CustomReport",
                Rationale: "Validate generated contribution fields",
                Grids:
                [
                    new ReportWriterGridDefinitionDto(
                        "strategy-contribution",
                        "Strategy Contribution",
                        ReportWriterGridKindDto.Contribution,
                        RowFields: ["strategy"],
                        Metrics:
                        [
                            new ReportWriterMetricDefinitionDto("pnl", "pnl"),
                            new ReportWriterMetricDefinitionDto("contributionPercent", "pnl")
                        ],
                        Formulas:
                        [
                            new ReportWriterFormulaDefinitionDto("contributionAbsPercent", "{pnl}")
                        ])
                ]),
            "report.author");

        Action act = () => svc.Submit(draft.Definition.TemplateId, "report.author", "ready");

        draft.ValidationIssues.Should().Contain("Report writer grid 'strategy-contribution' metric 'contributionPercent' uses reserved contribution field 'contributionPercent'.");
        draft.ValidationIssues.Should().Contain("Report writer grid 'strategy-contribution' formula 'contributionAbsPercent' uses reserved contribution field 'contributionAbsPercent'.");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Template invalid-contribution-grid@v1 is not ready for review: Report writer grid 'strategy-contribution' metric 'contributionPercent' uses reserved contribution field 'contributionPercent'.*");
    }

    [Fact]
    public void TemplateRegistry_RenderContributionGridWithOffsettingPnl_ReturnsSignedAndAbsoluteBreakdown()
    {
        var svc = new ReportTemplateRegistryService();
        var draft = svc.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "offsetting-contribution-grid",
                "Offsetting Contribution Grid",
                [],
                [],
                Family: "CustomReport",
                Rationale: "Render signed and absolute percentage-of-P&L breakdowns",
                Grids:
                [
                    new ReportWriterGridDefinitionDto(
                        "strategy-contribution",
                        "Strategy Contribution",
                        ReportWriterGridKindDto.Contribution,
                        RowFields: ["strategy"],
                        Metrics: [new ReportWriterMetricDefinitionDto("pnl", "pnl")],
                        Formulas:
                        [
                            new ReportWriterFormulaDefinitionDto("signedCheck", "{contributionPercent}"),
                            new ReportWriterFormulaDefinitionDto("absCheck", "{contributionAbsPercent}")
                        ])
                ]),
            "report.author");

        draft.ValidationIssues.Should().BeEmpty();
        svc.Submit(draft.Definition.TemplateId, "report.author", "ready");
        svc.Approve(
            draft.Definition.TemplateId,
            new ReportTemplateDecisionRequestDto("Controller approved offsetting contribution grid", "APP-CONTRIB-1"),
            "controller.admin");

        var rendered = svc.Render(new RenderReportTemplateRequestDto(
            draft.Definition.TemplateId,
            new Dictionary<string, string>(),
            [
                new Dictionary<string, string> { ["strategy"] = "Core", ["pnl"] = "150" },
                new Dictionary<string, string> { ["strategy"] = "Credit", ["pnl"] = "-50" },
                new Dictionary<string, string> { ["strategy"] = "Macro", ["pnl"] = "0" }
            ]));

        var grid = rendered.Grids.Should().ContainSingle().Subject;
        grid.Columns.Select(static column => column.Key).Should().ContainInOrder(
            "strategy",
            "pnl",
            "contributionPercent",
            "contributionAbsPercent",
            "signedCheck",
            "absCheck");
        grid.Rows.Select(static row => row.Values["strategy"]).Should().Equal("Core", "Credit", "Macro");
        grid.Rows[0].Values["contributionPercent"].Should().Be("75");
        grid.Rows[0].Values["contributionAbsPercent"].Should().Be("75");
        grid.Rows[0].Values["signedCheck"].Should().Be("75");
        grid.Rows[0].Values["absCheck"].Should().Be("75");
        grid.Rows[1].Values["contributionPercent"].Should().Be("-25");
        grid.Rows[1].Values["contributionAbsPercent"].Should().Be("25");
        grid.Rows[1].Values["signedCheck"].Should().Be("-25");
        grid.Rows[1].Values["absCheck"].Should().Be("25");
        grid.Lineage.Should().NotBeNull();
        grid.Lineage!.FilteredInputRowCount.Should().Be(3);
        grid.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void TemplateRegistry_RenderApprovedReportWriterGridTemplate_ReturnsStructuredGridResults()
    {
        var svc = new ReportTemplateRegistryService();
        var draft = svc.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "custom-exposure-grid",
                "Custom Exposure Grid",
                [],
                [new ReportTemplateParameterDefinitionDto("period", Required: true)],
                Family: "CustomReport",
                Rationale: "No-code exposure writer",
                Grids:
                [
                    new ReportWriterGridDefinitionDto(
                        "sector-pivot",
                        "Sector Pivot",
                        ReportWriterGridKindDto.Pivot,
                        RowFields: ["sector"],
                        Metrics:
                        [
                            new ReportWriterMetricDefinitionDto("marketValue", "marketValue"),
                            new ReportWriterMetricDefinitionDto("pnl", "pnl")
                        ],
                        Formulas: [new ReportWriterFormulaDefinitionDto("returnPct", "{pnl} / {marketValue} * 100")])
                ]),
            "report.author");

        draft.ValidationIssues.Should().BeEmpty();
        svc.Submit(draft.Definition.TemplateId, "report.author", "ready");
        svc.Approve(
            draft.Definition.TemplateId,
            new ReportTemplateDecisionRequestDto("Controller approved no-code grid", "APP-GRID-1"),
            "controller.admin");

        var rendered = svc.Render(new RenderReportTemplateRequestDto(
            draft.Definition.TemplateId,
            new Dictionary<string, string> { ["period"] = "2026-05" },
            [
                new Dictionary<string, string> { ["sector"] = "Technology", ["marketValue"] = "100", ["pnl"] = "10" },
                new Dictionary<string, string> { ["sector"] = "Technology", ["marketValue"] = "50", ["pnl"] = "5" },
                new Dictionary<string, string> { ["sector"] = "Rates", ["marketValue"] = "50", ["pnl"] = "-2" }
            ]));

        rendered.MissingRequiredParameters.Should().BeEmpty();
        rendered.RenderedContent.Should().Contain("grids=sector-pivot:2r");
        rendered.Grids.Should().ContainSingle(grid => grid.GridId == "sector-pivot");
        var grid = rendered.Grids!.Single();
        grid.Columns.Select(column => column.Key).Should().ContainInOrder("sector", "marketValue", "pnl", "returnPct");
        grid.Rows.Should().Contain(row =>
            row.Values["sector"] == "Technology" &&
            row.Values["marketValue"] == "150" &&
            row.Values["pnl"] == "15" &&
            row.Values["returnPct"] == "10");
        grid.Lineage.Should().NotBeNull();
        grid.Lineage!.InputRowCount.Should().Be(3);
        grid.Lineage.OutputRowCount.Should().Be(2);
        grid.Lineage.SourceFields.Should().Equal("marketValue", "pnl", "sector");
        grid.Lineage.Metrics.Should().Contain(metric =>
            metric.Name == "pnl" &&
            metric.SourceField == "pnl" &&
            metric.Function == ReportWriterAggregateFunctionDto.Sum.ToString());
        grid.Lineage.Formulas.Should().Contain(formula =>
            formula.Name == "returnPct" &&
            formula.SourceFields.Count == 2 &&
            formula.SourceFields[0] == "marketValue" &&
            formula.SourceFields[1] == "pnl");
    }

    [Fact]
    public void TemplateRegistry_ReportWriterGridNormalization_PreservesAuthoredLayoutOrder()
    {
        var svc = new ReportTemplateRegistryService();
        var draft = svc.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "ordered-report-writer-grid",
                "Ordered Report Writer Grid",
                [],
                [],
                Family: "CustomReport",
                Rationale: "Preserve drag-and-drop report writer layout order",
                Grids:
                [
                    new ReportWriterGridDefinitionDto(
                        "ordered-grid",
                        "Ordered Grid",
                        ReportWriterGridKindDto.Pivot,
                        RowFields: ["strategy", "sector", "strategy"],
                        ColumnFields: ["region", "security"],
                        Metrics:
                        [
                            new ReportWriterMetricDefinitionDto("pnl", "pnl", Label: "P&L"),
                            new ReportWriterMetricDefinitionDto("marketValue", "marketValue", Label: "Market value")
                        ],
                        Formulas:
                        [
                            new ReportWriterFormulaDefinitionDto("returnPct", "{pnl} / {marketValue} * 100"),
                            new ReportWriterFormulaDefinitionDto("weightPct", "{marketValue} / total(marketValue) * 100")
                        ],
                        Filters:
                        [
                            new ReportWriterFilterDefinitionDto("strategy", ReportWriterFilterOperatorDto.Equals, "Core"),
                            new ReportWriterFilterDefinitionDto("region", ReportWriterFilterOperatorDto.NotEquals, "Closed")
                        ])
                ]),
            "report.author");
        svc.Submit(draft.Definition.TemplateId, "report.author", "ready");
        svc.Approve(
            draft.Definition.TemplateId,
            new ReportTemplateDecisionRequestDto("Controller approved ordered grid", "APP-GRID-ORDER"),
            "controller.admin");

        var rendered = svc.Render(new RenderReportTemplateRequestDto(
            draft.Definition.TemplateId,
            new Dictionary<string, string>(),
            [
                new Dictionary<string, string>
                {
                    ["strategy"] = "Core",
                    ["sector"] = "Technology",
                    ["region"] = "US",
                    ["security"] = "ABC",
                    ["pnl"] = "12",
                    ["marketValue"] = "100"
                }
            ]));

        var stored = svc.Get(draft.Definition.TemplateId);
        stored.Should().NotBeNull();
        var storedGrid = stored!.Grids.Should().ContainSingle().Subject;
        storedGrid.RowFields.Should().Equal("strategy", "sector");
        storedGrid.ColumnFields.Should().Equal("region", "security");
        storedGrid.Metrics!.Select(static metric => metric.Name).Should().Equal("pnl", "marketValue");
        storedGrid.Formulas!.Select(static formula => formula.Name).Should().Equal("returnPct", "weightPct");
        storedGrid.Filters!.Select(static filter => filter.Field).Should().Equal("strategy", "region");

        var grid = rendered.Grids.Should().ContainSingle().Subject;
        grid.Columns.Select(static column => column.Key).Should().Equal(
            "strategy",
            "sector",
            "US|ABC:pnl",
            "US|ABC:marketValue",
            "returnPct",
            "weightPct");
        grid.Lineage!.Metrics.Select(static metric => metric.Name).Should().Equal("pnl", "marketValue");
        grid.Lineage.Formulas.Select(static formula => formula.Name).Should().Equal("returnPct", "weightPct");
        grid.Lineage.Filters!.Select(static filter => filter.Field).Should().Equal("strategy", "region");
    }

    [Fact]
    public void TemplateRegistry_RenderPivotWithColumnFields_ReturnsCrosstabColumns()
    {
        var svc = new ReportTemplateRegistryService();
        var draft = svc.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "custom-sector-region-crosstab",
                "Custom Sector Region Crosstab",
                [],
                [],
                Family: "CustomReport",
                Rationale: "No-code crosstab writer",
                Grids:
                [
                    new ReportWriterGridDefinitionDto(
                        "sector-region-pivot",
                        "Sector Region Pivot",
                        ReportWriterGridKindDto.Pivot,
                        RowFields: ["sector"],
                        ColumnFields: ["region"],
                        Metrics:
                        [
                            new ReportWriterMetricDefinitionDto("marketValue", "marketValue"),
                            new ReportWriterMetricDefinitionDto("pnl", "pnl")
                        ],
                        Formulas:
                        [
                            new ReportWriterFormulaDefinitionDto("returnPct", "{pnl} / {marketValue} * 100")
                        ])
                ]),
            "report.author");
        svc.Submit(draft.Definition.TemplateId, "report.author", "ready");
        svc.Approve(
            draft.Definition.TemplateId,
            new ReportTemplateDecisionRequestDto("Controller approved crosstab grid", "APP-GRID-CROSSTAB"),
            "controller.admin");

        var rendered = svc.Render(new RenderReportTemplateRequestDto(
            draft.Definition.TemplateId,
            new Dictionary<string, string>(),
            [
                new Dictionary<string, string> { ["sector"] = "Technology", ["region"] = "US", ["marketValue"] = "100", ["pnl"] = "10" },
                new Dictionary<string, string> { ["sector"] = "Technology", ["region"] = "EU", ["marketValue"] = "50", ["pnl"] = "5" },
                new Dictionary<string, string> { ["sector"] = "Rates", ["region"] = "US", ["marketValue"] = "40", ["pnl"] = "-2" }
            ]));

        rendered.RenderedContent.Should().Contain("grids=sector-region-pivot:2r");
        var grid = rendered.Grids.Should().ContainSingle().Subject;
        grid.Columns.Select(static column => column.Key).Should().Equal(
            "sector",
            "US:marketValue",
            "US:pnl",
            "EU:marketValue",
            "EU:pnl",
            "returnPct");
        grid.Rows.Should().HaveCount(2);
        grid.Rows.Should().Contain(row =>
            row.Values["sector"] == "Technology" &&
            row.Values["US:marketValue"] == "100" &&
            row.Values["US:pnl"] == "10" &&
            row.Values["EU:marketValue"] == "50" &&
            row.Values["EU:pnl"] == "5" &&
            row.Values["returnPct"] == "10");
        grid.Rows.Should().Contain(row =>
            row.Values["sector"] == "Rates" &&
            row.Values["US:marketValue"] == "40" &&
            row.Values["US:pnl"] == "-2" &&
            row.Values["EU:marketValue"] == "0" &&
            row.Values["EU:pnl"] == "0" &&
            row.Values["returnPct"] == "-5");
        grid.Lineage.Should().NotBeNull();
        grid.Lineage!.SourceFields.Should().Equal("marketValue", "pnl", "region", "sector");
    }

    [Fact]
    public void TemplateRegistry_RenderWithRequestGridOverride_DoesNotMutateApprovedTemplate()
    {
        var svc = new ReportTemplateRegistryService();
        var draft = svc.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "previewable-exposure-grid",
                "Previewable Exposure Grid",
                [],
                [],
                Family: "CustomReport",
                Rationale: "No-code preview writer",
                Grids:
                [
                    new ReportWriterGridDefinitionDto(
                        "saved-sector-pivot",
                        "Saved Sector Pivot",
                        ReportWriterGridKindDto.Pivot,
                        RowFields: ["sector"],
                        Metrics: [new ReportWriterMetricDefinitionDto("marketValue", "marketValue")])
                ]),
            "report.author");
        svc.Submit(draft.Definition.TemplateId, "report.author", "ready");
        svc.Approve(
            draft.Definition.TemplateId,
            new ReportTemplateDecisionRequestDto("Controller approved saved grid", "APP-GRID-2"),
            "controller.admin");

        var rendered = svc.Render(new RenderReportTemplateRequestDto(
            draft.Definition.TemplateId,
            new Dictionary<string, string>(),
            [
                new Dictionary<string, string> { ["security"] = "ABC", ["pnl"] = "25" },
                new Dictionary<string, string> { ["security"] = "XYZ", ["pnl"] = "10" }
            ],
            Grids:
            [
                new ReportWriterGridDefinitionDto(
                    "preview-topn",
                    "Preview Top-N",
                    ReportWriterGridKindDto.TopN,
                    RowFields: ["security"],
                    Metrics: [new ReportWriterMetricDefinitionDto("pnl", "pnl")],
                    TopN: 1,
                    SortBy: "pnl")
            ]));

        rendered.RenderedContent.Should().Contain("grids=preview-topn:1r");
        rendered.RenderedContent.Should().NotContain("saved-sector-pivot");
        rendered.Grids.Should().ContainSingle(grid => grid.GridId == "preview-topn");
        rendered.Grids!.Single().Rows.Should().ContainSingle().Which.Values["security"].Should().Be("ABC");

        var stored = svc.Get(draft.Definition.TemplateId);
        stored.Should().NotBeNull();
        stored!.Grids.Should().ContainSingle().Which.GridId.Should().Be("saved-sector-pivot");
    }

    [Fact]
    public async Task ReportingOrchestration_ApprovedCustomReportWriterTemplate_RetainsGridArtifacts()
    {
        var registry = new ReportTemplateRegistryService();
        var draft = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "custom-report-writer-run",
                "Custom Report Writer Run",
                [],
                [new ReportTemplateParameterDefinitionDto("period", Required: true)],
                Family: "CustomReport",
                Rationale: "Run approved no-code grids",
                Grids:
                [
                    new ReportWriterGridDefinitionDto(
                        "sector-pivot",
                        "Sector Pivot",
                        ReportWriterGridKindDto.Pivot,
                        RowFields: ["sector"],
                        Metrics:
                        [
                            new ReportWriterMetricDefinitionDto("marketValue", "marketValue"),
                            new ReportWriterMetricDefinitionDto("pnl", "pnl")
                        ],
                        Formulas: [new ReportWriterFormulaDefinitionDto("returnPct", "{pnl} / {marketValue} * 100")]),
                    new ReportWriterGridDefinitionDto(
                        "security-topn",
                        "Security Top-N",
                        ReportWriterGridKindDto.TopN,
                        RowFields: ["security"],
                        Metrics: [new ReportWriterMetricDefinitionDto("pnl", "pnl")],
                        TopN: 5,
                        SortBy: "pnl"),
                    new ReportWriterGridDefinitionDto(
                        "strategy-contribution",
                        "Strategy Contribution",
                        ReportWriterGridKindDto.Contribution,
                        RowFields: ["strategy"],
                        Metrics: [new ReportWriterMetricDefinitionDto("pnl", "pnl")],
                        Formulas:
                        [
                            new ReportWriterFormulaDefinitionDto("weightCheck", "{contributionPercent}"),
                            new ReportWriterFormulaDefinitionDto("absWeightCheck", "{contributionAbsPercent}")
                        ])
                ]),
            "report.author");
        registry.Submit(draft.Definition.TemplateId, "report.author", "ready");
        registry.Approve(
            draft.Definition.TemplateId,
            new ReportTemplateDecisionRequestDto("Approved no-code grid package", "APP-RW-RUN-1"),
            "controller.admin");
        var catalog = new GovernedReportingTemplateCatalog(new DefaultReportingTemplateCatalog(), registry);
        var orchestration = new ReportingOrchestrationService(
            catalog,
            new DeterministicReportingSectionRenderer(),
            () => new DateTimeOffset(2026, 5, 6, 9, 0, 0, TimeSpan.Zero));

        var manifest = await orchestration.ExecuteAsync(
            new ReportingJobContract(
                "adhoc-report-writer",
                draft.Definition.TemplateId.Name,
                new DateOnly(2026, 5, 6),
                ReportingRunTrigger.AdHoc,
                0,
                "report.author",
                new DateTimeOffset(2026, 5, 6, 9, 0, 0, TimeSpan.Zero)),
            CancellationToken.None);

        manifest.TemplateId.Should().Be(draft.Definition.TemplateId.Name);
        manifest.Sections.Select(static section => section.SectionId).Should().Contain(
            "sector-pivot",
            "security-topn",
            "strategy-contribution",
            "__VACUITY_PROBE__");
        manifest.Artifacts.Should().Contain($"report-writer://{manifest.RunId}/grids/sector-pivot");
        manifest.Artifacts.Should().Contain($"report-writer://{manifest.RunId}/grids/security-topn");
        manifest.Artifacts.Should().Contain($"report-writer://{manifest.RunId}/grids/strategy-contribution");
        orchestration.GetAudit(manifest.RunId).Should().Contain(entry =>
            entry.Action == "RunGenerated" &&
            entry.Notes.Contains("reportWriterGrids=3", StringComparison.Ordinal));
    }

    [Fact]
    public void TemplateRegistry_ReloadsCustomDraftsAndApprovedRevisionsFromStore()
    {
        var snapshotPath = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"), "report-templates.json");
        var store = new FileReportTemplateGovernanceStore(
            new ReportTemplateGovernanceStoreOptions(snapshotPath),
            NullLogger<FileReportTemplateGovernanceStore>.Instance);
        var svc = new ReportTemplateRegistryService(store);
        var draft = svc.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "investor-monthly-statement",
                "Investor Monthly Statement v2",
                ["cover", "performance", "positions", "fees"],
                [new ReportTemplateParameterDefinitionDto("period", Required: true)],
                Family: "InvestorStatement",
                BasedOnVersion: 1,
                Rationale: "Add fee disclosure"),
            "report.author");
        svc.Submit(draft.Definition.TemplateId, "report.author", "ready");
        svc.Approve(
            draft.Definition.TemplateId,
            new ReportTemplateDecisionRequestDto("Approved", "APP-TPL-2"),
            "controller.admin");

        var reloaded = new ReportTemplateRegistryService(store);
        var records = reloaded.List(includeSuperseded: true);

        records.Should().Contain(record =>
            record.Definition.TemplateId == new VersionedReportTemplateIdDto("investor-monthly-statement", 2) &&
            record.Status == ReportTemplateLifecycleStatusDto.Approved &&
            record.ApprovalReference == "APP-TPL-2");
        records.Should().Contain(record =>
            record.Definition.TemplateId == new VersionedReportTemplateIdDto("investor-monthly-statement", 1) &&
            record.IsBuiltIn &&
            !record.IsLatestApproved);
        reloaded.Render(new RenderReportTemplateRequestDto(
                new VersionedReportTemplateIdDto("investor-monthly-statement", 2),
                new Dictionary<string, string> { ["period"] = "2026-05" }))
            .RenderedContent.Should().Contain("fees");
    }

    [Fact]
    public async Task Endpoint_TemplateDraftSubmitApprove_ListExposesGovernedLifecycle()
    {
        await using var app = await CreateFundStructureAppAsync(UserRole.Admin);
        var client = app.GetTestClient();
        var draftRequest = new ReportTemplateDraftRequestDto(
            "investor-monthly-statement",
            "Investor Monthly Statement v2",
            ["cover", "performance", "positions", "fees"],
            [new ReportTemplateParameterDefinitionDto("period", Required: true)],
            Family: "InvestorStatement",
            BasedOnVersion: 1,
            Rationale: "Add fee disclosure");

        var draftResponse = await client.PostAsJsonAsync("/api/fund-structure/reporting/templates/drafts", draftRequest, ServerJsonOptions);
        draftResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var draft = await draftResponse.Content.ReadFromJsonAsync<ReportTemplateGovernanceRecordDto>(ServerJsonOptions);
        draft.Should().NotBeNull();
        draft!.Status.Should().Be(ReportTemplateLifecycleStatusDto.Draft);

        var renderDraftResponse = await client.PostAsJsonAsync(
            "/api/fund-structure/reporting/templates/render",
            new RenderReportTemplateRequestDto(draft.Definition.TemplateId, new Dictionary<string, string> { ["period"] = "2026-05" }),
            ServerJsonOptions);
        renderDraftResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var submitResponse = await client.PostAsJsonAsync(
            $"/api/fund-structure/reporting/templates/{draft.Definition.TemplateId.Name}/versions/{draft.Definition.TemplateId.Version}/submit",
            new ReportTemplateDecisionRequestDto("Ready for controller review"),
            ServerJsonOptions);
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<ReportTemplateGovernanceRecordDto>(ServerJsonOptions);
        submitted.Should().NotBeNull();
        submitted!.Status.Should().Be(ReportTemplateLifecycleStatusDto.InReview);

        client.DefaultRequestHeaders.Add("X-Meridian-Test-User", "independent.controller");
        var approveResponse = await client.PostAsJsonAsync(
            $"/api/fund-structure/reporting/templates/{draft.Definition.TemplateId.Name}/versions/{draft.Definition.TemplateId.Version}/approve",
            new ReportTemplateDecisionRequestDto("Controller approved fee disclosure", "APP-TPL-2"),
            ServerJsonOptions);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var approved = await approveResponse.Content.ReadFromJsonAsync<ReportTemplateGovernanceRecordDto>(ServerJsonOptions);
        approved.Should().NotBeNull();
        approved!.Status.Should().Be(ReportTemplateLifecycleStatusDto.Approved);
        approved.ApprovalReference.Should().Be("APP-TPL-2");

        var renderApprovedResponse = await client.PostAsJsonAsync(
            "/api/fund-structure/reporting/templates/render",
            new RenderReportTemplateRequestDto(
                approved.Definition.TemplateId,
                new Dictionary<string, string> { ["period"] = "2026-05" },
                DatasetRows:
                [
                    new Dictionary<string, string> { ["sector"] = "Technology", ["marketValue"] = "100", ["pnl"] = "10" },
                    new Dictionary<string, string> { ["sector"] = "Technology", ["marketValue"] = "50", ["pnl"] = "5" },
                    new Dictionary<string, string> { ["sector"] = "Rates", ["marketValue"] = "75", ["pnl"] = "-2" }
                ],
                Grids:
                [
                    new ReportWriterGridDefinitionDto(
                        "endpoint-sector-pivot",
                        "Endpoint Sector Pivot",
                        ReportWriterGridKindDto.Pivot,
                        RowFields: ["sector"],
                        Metrics:
                        [
                            new ReportWriterMetricDefinitionDto("marketValue", "marketValue"),
                            new ReportWriterMetricDefinitionDto("pnl", "pnl")
                        ],
                        Formulas: [new ReportWriterFormulaDefinitionDto("returnPct", "{pnl} / {marketValue} * 100")])
                ]),
            ServerJsonOptions);
        renderApprovedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var rendered = await renderApprovedResponse.Content.ReadFromJsonAsync<RenderReportTemplateResponseDto>(ServerJsonOptions);
        rendered.Should().NotBeNull();
        var renderedGrid = rendered!.Grids.Should().ContainSingle().Subject;
        renderedGrid.Lineage.Should().NotBeNull();
        renderedGrid.Lineage!.InputRowCount.Should().Be(3);
        renderedGrid.Lineage.OutputRowCount.Should().Be(2);
        renderedGrid.Lineage.SourceFields.Should().Equal("marketValue", "pnl", "sector");

        var listResponse = await client.GetAsync("/api/fund-structure/reporting/templates?includeSuperseded=true");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var records = await listResponse.Content.ReadFromJsonAsync<List<ReportTemplateGovernanceRecordDto>>(ServerJsonOptions);
        records.Should().NotBeNull();
        records!.Should().Contain(record =>
            record.Definition.TemplateId == new VersionedReportTemplateIdDto("investor-monthly-statement", 2) &&
            record.Status == ReportTemplateLifecycleStatusDto.Approved &&
            !record.IsBuiltIn);
        records.Should().Contain(record =>
            record.Definition.TemplateId == new VersionedReportTemplateIdDto("investor-monthly-statement", 1) &&
            record.IsBuiltIn &&
            !record.IsLatestApproved);
    }

    [Fact]
    public void Publish_RejectsReviewedAutomationOriginBeforePublication()
    {
        var svc = new ReportPackWorkflowService();
        var approved = CreateApprovedPack(
            svc,
            [CompleteLineProvenance("trial-balance.cash", "ledger-evidence-1")]);

        Action act = () => svc.Publish(
            approved.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            CompleteLineProvenanceEvidenceLinks("ledger-evidence-1"),
            actionOrigin: OperationsActionOriginDto.AssistantDraft);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Reviewed automation cannot publish reports; a human operator approval is required.");

        var record = svc.GetRecord(approved.ReportId);
        record.Should().NotBeNull();
        record!.State.Should().Be(ReportPackWorkflowStateDto.Approved);
        record.Publication.Should().BeNull();
    }

    [Fact]
    public void Transition_RejectsReviewedAutomationApprovalOriginBeforeMutation()
    {
        var svc = new ReportPackWorkflowService();
        var created = svc.Create("fund-a", "acct-1", "2026-03", new VersionedReportTemplateIdDto("board-pack", 1), "author");
        var inReview = svc.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");

        Action act = () => svc.Transition(
            created.ReportId,
            ReportPackWorkflowStateDto.Approved,
            "reviewed-automation",
            "approver",
            actionOrigin: OperationsActionOriginDto.AssistantDraft);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Reviewed automation cannot approve reports; a human operator approval is required.");

        var record = svc.GetRecord(created.ReportId);
        record.Should().NotBeNull();
        record!.State.Should().Be(ReportPackWorkflowStateDto.InReview);
        record.AuditTrail.Should().HaveCount(inReview.AuditTrail.Count);
    }

    [Fact]
    public void Publish_RejectsLineProvenanceEvidenceMissingFromRetainedManifest()
    {
        var svc = new ReportPackWorkflowService();
        var created = svc.Create(
            "fund-a",
            "acct-1",
            "2026-03",
            new VersionedReportTemplateIdDto("board-pack", 1),
            "author",
            [CompleteLineProvenance("trial-balance.cash", "ledger-evidence-1")]);
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");

        Action act = () => svc.Publish(
            created.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            [new ReportPackEvidenceLinkDto("other-evidence", "Other evidence", "/evidence/other", "reporting")]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Report pack publication has orphan evidence: ledger-evidence-1.");
    }

    [Fact]
    public void Publish_RejectsLineProvenancePointersMissingFromRetainedManifest()
    {
        var svc = new ReportPackWorkflowService();
        var approved = CreateApprovedPack(
            svc,
            [CompleteLineProvenance("trial-balance.cash", "ledger-evidence-1")]);

        Action act = () => svc.Publish(
            approved.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            [new ReportPackEvidenceLinkDto("ledger-evidence-1", "Line evidence", "/evidence/ledger-evidence-1", "reporting")]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Report pack publication has orphan provenance pointers: approval-1, case-1, definition-1, ledger-entry-1, provider-event-1, provider-session-1, recon-run-1, run-1, security-1.");
    }

    [Fact]
    public void Publish_RequiresReportValueAndSourcePointerForLineProvenance()
    {
        var svc = new ReportPackWorkflowService();
        var missingValue = svc.Create(
            "fund-a",
            "acct-1",
            "2026-03",
            new VersionedReportTemplateIdDto("board-pack", 1),
            "author",
            [new ReportPackLineProvenanceDto("trial-balance.cash", "ledger", "ledger-entry-1", "ledger-evidence-1", LedgerEntryId: "ledger-entry-1")]);
        svc.Transition(missingValue.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        svc.Transition(missingValue.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");

        Action missingValuePublish = () => svc.Publish(
            missingValue.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            [new ReportPackEvidenceLinkDto("ledger-evidence-1", "Ledger evidence", "/evidence/ledger-evidence-1", "ledger")]);

        missingValuePublish.Should().Throw<InvalidOperationException>()
            .WithMessage("Report pack line provenance requires report values for: trial-balance.cash.");

        var missingPointer = svc.Create(
            "fund-a",
            "acct-1",
            "2026-03",
            new VersionedReportTemplateIdDto("board-pack", 1),
            "author",
            [new ReportPackLineProvenanceDto("trial-balance.nav", "report", "nav-line-1", "nav-evidence-1", ReportValue: "125.00")]);
        svc.Transition(missingPointer.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        svc.Transition(missingPointer.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");

        Action missingPointerPublish = () => svc.Publish(
            missingPointer.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-2",
            "vault/report-packs/manifest-2.json",
            [new ReportPackEvidenceLinkDto("nav-evidence-1", "NAV evidence", "/evidence/nav-evidence-1", "reporting")]);

        missingPointerPublish.Should().Throw<InvalidOperationException>()
            .WithMessage("Report pack line provenance requires run, session, ledger, or reconciliation source pointers for: trial-balance.nav.");
    }

    [Fact]
    public void Publish_RequiresLedgerProviderSecurityReconciliationAndApprovalPointersForLineProvenance()
    {
        var svc = new ReportPackWorkflowService();
        var missingLedger = CreateApprovedPack(
            svc,
            [CompleteLineProvenance("trial-balance.cash", "ledger-evidence-1") with { LedgerEntryId = null, RunId = "run-1" }]);

        Action missingLedgerPublish = () => PublishWithLedgerEvidence(svc, missingLedger.ReportId);

        missingLedgerPublish.Should().Throw<InvalidOperationException>()
            .WithMessage("Report pack line provenance requires ledger entries for: trial-balance.cash.");

        var missingProvider = CreateApprovedPack(
            svc,
            [CompleteLineProvenance("trial-balance.income", "income-evidence-1") with { ProviderEventId = null }]);

        Action missingProviderPublish = () => PublishWithLedgerEvidence(svc, missingProvider.ReportId, "income-evidence-1");

        missingProviderPublish.Should().Throw<InvalidOperationException>()
            .WithMessage("Report pack line provenance requires provider events for: trial-balance.income.");

        var missingSecurityMaster = CreateApprovedPack(
            svc,
            [CompleteLineProvenance("trial-balance.position", "position-evidence-1") with { SecurityMasterId = null, SecurityDefinitionId = null }]);

        Action missingSecurityMasterPublish = () => PublishWithLedgerEvidence(svc, missingSecurityMaster.ReportId, "position-evidence-1");

        missingSecurityMasterPublish.Should().Throw<InvalidOperationException>()
            .WithMessage("Report pack line provenance requires Security Master definitions for: trial-balance.position.");

        var missingReconciliation = CreateApprovedPack(
            svc,
            [CompleteLineProvenance("trial-balance.nav", "nav-evidence-1") with { ReconciliationRunId = null, ReconciliationCaseId = null, ReconciliationOutcome = null }]);

        Action missingReconciliationPublish = () => PublishWithLedgerEvidence(svc, missingReconciliation.ReportId, "nav-evidence-1");

        missingReconciliationPublish.Should().Throw<InvalidOperationException>()
            .WithMessage("Report pack line provenance requires reconciliation outcomes for: trial-balance.nav.");

        var missingApproval = CreateApprovedPack(
            svc,
            [CompleteLineProvenance("trial-balance.fees", "fees-evidence-1") with { ApprovalId = null }]);

        Action missingApprovalPublish = () => PublishWithLedgerEvidence(svc, missingApproval.ReportId, "fees-evidence-1");

        missingApprovalPublish.Should().Throw<InvalidOperationException>()
            .WithMessage("Report pack line provenance requires approval references for: trial-balance.fees.");
    }

    [Fact]
    public void Publish_AllowsCompleteReportLineProvenanceForRetainedReportPack()
    {
        var svc = new ReportPackWorkflowService();
        var approved = CreateApprovedPack(
            svc,
            [CompleteLineProvenance("trial-balance.cash", "ledger-evidence-1")]);

        var published = svc.Publish(
            approved.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            CompleteLineProvenanceEvidenceLinks("ledger-evidence-1"));

        var line = published.LineProvenance.Should().ContainSingle().Subject;
        line.LedgerEntryId.Should().Be("ledger-entry-1");
        line.ProviderEventId.Should().Be("provider-event-1");
        line.SecurityMasterId.Should().Be("security-1");
        line.SecurityDefinitionId.Should().Be("definition-1");
        line.ReconciliationRunId.Should().Be("recon-run-1");
        line.ReconciliationOutcome.Should().Be("matched");
        line.ApprovalId.Should().Be("approval-1");
        published.State.Should().Be(ReportPackWorkflowStateDto.Published);
    }

    [Fact]
    public void WorkflowStore_ReloadsPublishedAndRestatedReportPackRecords()
    {
        var snapshotPath = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"), "report-pack-workflows.json");
        var store = new FileReportPackWorkflowRecordStore(
            new ReportPackWorkflowRecordStoreOptions(snapshotPath),
            NullLogger<FileReportPackWorkflowRecordStore>.Instance);
        var svc = new ReportPackWorkflowService(store);
        var approved = CreateApprovedPack(
            svc,
            [CompleteLineProvenance("trial-balance.cash", "ledger-evidence-1")]);
        var published = svc.Publish(
            approved.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            CompleteLineProvenanceEvidenceLinks("ledger-evidence-1"));

        var restated = svc.Restate(
            published.ReportId,
            "approver",
            "approver",
            "NAV_CORRECTION",
            "controller",
            published.ReportId,
            [
                new ReportPackChangedLineDto(
                    "trial-balance.cash",
                    "100.00",
                    "101.00",
                    [new ReportPackEvidenceLinkDto("cash-restatement-1", "Cash restatement", "/evidence/cash-restatement-1", "reporting")])
            ]);

        var reloaded = new ReportPackWorkflowService(store);
        var record = reloaded.ListRecords().Should().ContainSingle(item => item.ReportId == published.ReportId).Subject;
        record.State.Should().Be(ReportPackWorkflowStateDto.Restated);
        record.Version.Should().Be(restated.Version);
        record.Publication!.ManifestId.Should().Be("manifest-1");
        record.Restatement!.ReasonCode.Should().Be("NAV_CORRECTION");
        record.AuditTrail.Select(static entry => entry.Action).Should().ContainInOrder("create", "inreview", "approved", "published", "restated");
    }

    [Fact]
    public void ReportPackRunReadService_ProjectsReportWriterDatasetSources()
    {
        var workflow = new ReportPackWorkflowService();
        var published = workflow.Create(
            "fund-alpha",
            "acct-main",
            "2026-05",
            new VersionedReportTemplateIdDto("shadow-nav-pack", 1),
            "report.author",
            [
                SourcePortfolioReportingLine("portfolio.gross-exposure", "evidence-gross", "2500000", "/evidence/gross"),
                SourcePortfolioReportingLine("portfolio.realized-pnl", "evidence-realized", "35000", "/evidence/realized"),
                SourcePortfolioReportingLine("portfolio.unrealized-pnl", "evidence-unrealized", "25000", "/evidence/unrealized"),
                SourcePortfolioReportingLine("portfolio.shadow-nav", "evidence-shadow-nav", "2935000", "/evidence/shadow-nav"),
                SourcePortfolioReportingLine("portfolio.reported-nav", "evidence-reported-nav", "2875000", "/evidence/reported-nav")
            ]);
        workflow.Transition(published.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        workflow.Transition(published.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
        workflow.Publish(
            published.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:dataset-source",
            "manifest-dataset-source",
            "vault/report-packs/manifest-dataset-source.json",
            CompleteLineProvenanceEvidenceLinks(
                "evidence-gross",
                "evidence-realized",
                "evidence-unrealized",
                "evidence-shadow-nav",
                "evidence-reported-nav"));

        var payload = new ReportPackRunReadService(new DefaultReportingTemplateCatalog(), workflowService: workflow).BuildPayload();

        payload.ReportWriterDatasetSources.Should().NotBeNull();
        payload.ReportWriterDatasetSources!.Select(static source => source.SourceId).Should().Contain(
            [
                "retained-reporting-rows",
                "portfolio-reporting-cuts",
                "topn-contribution-analytics",
                "cross-fund-consolidation",
                "certified-operational-data-mart"
            ]);

        var source = payload.ReportWriterDatasetSources!.Should()
            .ContainSingle(static item => item.SourceId == "retained-reporting-rows")
            .Subject;
        source.SourceId.Should().Be("retained-reporting-rows");
        source.RowCount.Should().Be(source.Rows.Count);
        source.Rows.Any(IsExpectedPortfolioRow)
            .Should().BeTrue("the retained reporting-row dataset should include the portfolio aggregate row");
        var fieldNames = source.Fields.Select(static field => field.Name).ToArray();
        fieldNames.Should().Contain("dataset");
        fieldNames.Should().Contain("grossExposure");
        fieldNames.Should().Contain("totalPnl");
        fieldNames.Should().Contain("shadowNav");
        source.Tags.Should().NotBeNull();
        source.Tags!.Should().Contain("portfolio-cuts");
        source.Tags.Should().Contain("top-n");
        source.Tags.Should().Contain("contribution");
        source.Tags.Should().Contain("cross-fund");

        var portfolioSource = payload.ReportWriterDatasetSources!.Should()
            .ContainSingle(static item => item.SourceId == "portfolio-reporting-cuts")
            .Subject;
        portfolioSource.Rows.All(static row => HasDataset(row, "portfolio-cut")).Should().BeTrue();
        portfolioSource.Tags.Should().Contain("shadow-nav");

        var analyticsSource = payload.ReportWriterDatasetSources!.Should()
            .ContainSingle(static item => item.SourceId == "topn-contribution-analytics")
            .Subject;
        analyticsSource.Rows.All(static row => HasDataset(row, "portfolio-analytics")).Should().BeTrue();
        analyticsSource.Fields.Select(static field => field.Name).Should().Contain("contributionPercent");

        var crossFundSource = payload.ReportWriterDatasetSources!.Should()
            .ContainSingle(static item => item.SourceId == "cross-fund-consolidation")
            .Subject;
        crossFundSource.Rows.All(static row => HasDataset(row, "cross-fund-consolidation")).Should().BeTrue();
        crossFundSource.Tags.Should().Contain("consolidation");

        var certifiedMartSource = payload.ReportWriterDatasetSources!.Should()
            .ContainSingle(static item => item.SourceId == "certified-operational-data-mart")
            .Subject;
        certifiedMartSource.CertificationState.Should().Be("SourceBacked");
        certifiedMartSource.ValidationState.Should().Be("Passed");
        certifiedMartSource.ReconciliationState.Should().Be("Linked");
        certifiedMartSource.LineageManifest.Should().Contain("datasetSourceId=certified-operational-data-mart");
        certifiedMartSource.SourceRunIds.Should().NotBeNullOrEmpty();
        certifiedMartSource.PermittedConsumers.Should().Contain("DataWarehouse");
        certifiedMartSource.RowLineageKeyField.Should().Be("rowLineageKey");
        certifiedMartSource.EvidenceIndexField.Should().Be("evidenceIndex");
        certifiedMartSource.Fields.Select(static field => field.Name).Should().Contain(
            [
                "sourceRunIds",
                "rowLineageKey",
                "lineageManifest",
                "evidenceIndex",
                "validationState",
                "reconciliationState",
                "certificationState",
                "permittedConsumers"
            ]);
        var hasCertifiedMartEvidenceRow = certifiedMartSource.Rows.Any(static row =>
            row.TryGetValue("rowLineageKey", out var rowLineageKey) &&
            rowLineageKey.Contains("certified-operational-data-mart:portfolio-cut:", StringComparison.Ordinal) &&
            row.TryGetValue("evidenceIndex", out var evidenceIndex) &&
            evidenceIndex.Contains("/api/workstation/evidence/search", StringComparison.Ordinal) &&
            row.TryGetValue("certificationState", out var certificationState) &&
            certificationState == "SourceBacked");
        hasCertifiedMartEvidenceRow.Should().BeTrue("certified operational data mart rows should retain lineage and evidence search metadata");

        static bool HasDataset(IReadOnlyDictionary<string, string> row, string expectedDataset) =>
            row.TryGetValue("dataset", out var dataset) && dataset == expectedDataset;

        static bool IsExpectedPortfolioRow(IReadOnlyDictionary<string, string> row)
        {
            return row.TryGetValue("dataset", out var dataset) && dataset == "portfolio-cut" &&
                   row.TryGetValue("grossExposure", out var grossExposure) && grossExposure == "2500000" &&
                   row.TryGetValue("totalPnl", out var totalPnl) && totalPnl == "60000";
        }
    }

    [Fact]
    public void ReportWriterDatasetSourceService_ResolvesKnownEmptySourcesWithoutTreatingThemAsUnknown()
    {
        var service = new ReportWriterDatasetSourceService(new ReportPackWorkflowService());

        service.BuildDatasetRowsForSource("portfolio-reporting-cuts").Should().BeEmpty();
        service.BuildDatasetRowsForSource("topn-contribution-analytics").Should().BeEmpty();
        service.BuildDatasetRowsForSource("cross-fund-consolidation").Should().BeEmpty();
        service.BuildDatasetRowsForSource("certified-operational-data-mart").Should().BeEmpty();
    }

    [Fact]
    public void ReportWriterDatasetSourceService_RejectsUnknownDatasetSourceIds()
    {
        var service = new ReportWriterDatasetSourceService(new ReportPackWorkflowService());

        var act = () => service.BuildDatasetRowsForSource("not-a-reporting-source");

        act.Should().Throw<ArgumentException>()
            .WithMessage("Unknown report-writer dataset source 'not-a-reporting-source'.*");
    }

    [Fact]
    public async Task ReportPackRunReadService_UnifiesGenericRunsAndGovernedWorkflowRecords()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var runStore = new FileReportingRunStore(
            new ReportingRunStoreOptions(Path.Combine(root, "reporting-runs")),
            NullLogger<FileReportingRunStore>.Instance);
        var orchestration = new ReportingOrchestrationService(
            new DefaultReportingTemplateCatalog(),
            new DeterministicReportingSectionRenderer(),
            () => new DateTimeOffset(2026, 5, 3, 12, 0, 0, TimeSpan.Zero),
            runStore);
        var manifest = await orchestration.ExecuteAsync(
            new ReportingJobContract(
                "sched-shadow",
                "shadow-nav-daily-pack",
                new DateOnly(2026, 5, 3),
                ReportingRunTrigger.Scheduled,
                0,
                "scheduler",
                new DateTimeOffset(2026, 5, 3, 12, 0, 0, TimeSpan.Zero),
                ScheduleId: "sched-shadow"),
            CancellationToken.None);

        var workflow = new ReportPackWorkflowService();
        var approved = CreateApprovedPack(
            workflow,
            [CompleteLineProvenance("trial-balance.cash", "ledger-evidence-1")]);
        var published = workflow.Publish(
            approved.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            CompleteLineProvenanceEvidenceLinks("ledger-evidence-1"),
            signedOffRole: nameof(UserRole.Controller),
            signOffReason: "Approved by controller.",
            signOffContext: "Authenticated actor 'publisher' with role 'Controller' approved publication.");
        workflow.Restate(
            published.ReportId,
            "approver",
            "approver",
            "NAV_CORRECTION",
            "controller",
            published.ReportId,
            [
                new ReportPackChangedLineDto(
                    "trial-balance.cash",
                    "100.00",
                    "101.00",
                    [new ReportPackEvidenceLinkDto("cash-restatement-1", "Cash restatement", "/evidence/cash-restatement-1", "reporting")])
            ]);

        var payload = new ReportPackRunReadService(new DefaultReportingTemplateCatalog(), runStore, workflow).BuildPayload();

        payload.SelectedFundProfileId.Should().Be(published.FundProfileId);
        payload.RecentRuns.Select(static run => run.RunId).Should().Contain(manifest.RunId);
        payload.RecentRuns.Select(static run => run.RunId).Should().NotContain("investor-monthly-statement-20260501");
        payload.RecentRuns.Select(static run => run.RunId).Should().NotContain("shadow-nav-daily-pack-20260503");
        var genericRun = payload.RecentRuns.Single(run => run.RunId == manifest.RunId);
        genericRun.AsOfDate.Should().Be("2026-05-03");
        genericRun.Artifacts.Should().Contain("schedule:sched-shadow");
        genericRun.DrilldownLinks.Should().Contain(link =>
            link.Kind == "schedule" &&
            link.Href == "schedule:sched-shadow" &&
            !link.IsBrowserNavigable);
        genericRun.DrilldownLinks.Should().Contain(link =>
            link.Kind == "audit" &&
            link.Href == $"/api/fund-structure/reporting/runs/{manifest.RunId}/audit" &&
            link.IsBrowserNavigable);
        genericRun.NextActions.Should().ContainSingle(action =>
            action.Kind == "migration-required" &&
            action.Method == "GET" &&
            action.Href == string.Empty &&
            !action.IsEnabled &&
            !action.IsBrowserNavigable);
        var workflowRun = payload.RecentRuns.Single(run => run.RunId == $"report-pack:{published.ReportId:D}");
        workflowRun.Family.Should().Be("GovernedReportPack");
        workflowRun.AsOfDate.Should().Be(published.Period);
        workflowRun.Status.Should().Be(ReportPackWorkflowStateDto.Restated.ToString());
        workflowRun.Artifacts.Should().Contain($"/api/fund-structure/report-packs/{published.ReportId:D}");
        workflowRun.Artifacts.Should().Contain("publication-manifest:manifest-1");
        workflowRun.Artifacts.Should().Contain("restatement:NAV_CORRECTION");
        workflowRun.Artifacts.Should().Contain("/evidence/cash-restatement-1");
        workflowRun.DrilldownLinks.Should().Contain(link =>
            link.Kind == "ledger-provenance" &&
            link.Href.Contains("/ledger-provenance", StringComparison.OrdinalIgnoreCase) &&
            link.IsBrowserNavigable);
        workflowRun.DrilldownLinks.Should().Contain(link =>
            link.Kind == "publication-evidence" &&
            link.Label == "Line evidence" &&
            link.Href == "/evidence/ledger-evidence-1");
        workflowRun.DrilldownLinks.Should().Contain(link =>
            link.Kind == "restatement-evidence" &&
            link.Label == "Cash restatement" &&
            link.Href == "/evidence/cash-restatement-1");
        workflowRun.NextActions.Should().ContainSingle(action =>
            action.Kind == "migration-required" &&
            action.Method == "GET" &&
            !action.IsEnabled &&
            !action.IsBrowserNavigable &&
            action.DisabledReason!.Contains("no immutable canonical governed-run binding", StringComparison.Ordinal));
        workflowRun.NextActions.Should().OnlyContain(action =>
            !action.Href.Contains("/reporting/packs/", StringComparison.OrdinalIgnoreCase));
        workflowRun.AuditActions.Should().Contain("published");
        workflowRun.AuditActions.Should().Contain("restated");
        payload.ReportPackDistributions.Should().NotBeEmpty();
        payload.ReportPackDistributions.Should().AllSatisfy(distribution =>
        {
            distribution.Recipient.Should().NotBeNullOrWhiteSpace();
            distribution.Channel.Should().NotBeNullOrWhiteSpace();
            distribution.PendingSummary.Should().Contain(distribution.Recipient);
        });
        payload.ReportPackDistributions.Should().Contain(distribution =>
            distribution.Recipient == "Board reporting committee" &&
            distribution.State == "Pending delivery" &&
            distribution.PendingItems == 1);
    }

    [Fact]
    public async Task ReportPackRunReadService_UsesCanonicalRunOnlyForExactImmutableBinding()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var runStore = new FileReportingRunStore(
            new ReportingRunStoreOptions(Path.Combine(root, "reporting-runs")),
            NullLogger<FileReportingRunStore>.Instance);
        var workflow = new ReportPackWorkflowService();
        var record = workflow.Create(
            "fund-alpha",
            "acct-main",
            "2026-05",
            new VersionedReportTemplateIdDto("shadow-nav-pack", 1),
            "report.author",
            accessContext: new ReportAccessQueryContext(
                ActorPrincipalId: "report.author",
                CompanyId: "company-a",
                TenantId: "tenant-a",
                RequireBoundScope: true));
        var runId = record.ReportId.ToString("D", CultureInfo.InvariantCulture);
        var scope = new ReportingOperationalScope(
            "tenant-a",
            "organization-a",
            "company-a",
            "fund-alpha",
            "book-main",
            "2026-05");
        var access = new ReportingAccessScope(
            "legacy-pack-binding",
            "1",
            ReportingGovernanceAccessMode.CompanyWide,
            "report.author",
            AllowOwnerAccess: true,
            Principals: System.Collections.Immutable.ImmutableArray.Create(
                new ReportingAccessPrincipalScope(ReportingAccessPrincipalKind.User, "report.author")),
            record.AccessPolicySnapshotHash!);
        var asOfDate = new DateOnly(2026, 5, 31);
        var capturedAtUtc = new DateTimeOffset(2026, 5, 31, 23, 0, 0, TimeSpan.Zero);
        var template = new VersionedReportTemplateIdDto("shadow-nav-daily-pack", 1);
        var parameters = new ReportingRunParametersDto(
            new ReportingRunScopeDto(scope.FundId!),
            scope.PeriodId,
            asOfDate,
            new ReportingLedgerBookSelectionDto(LedgerBookCode: scope.BookId),
            ReportingAccountingBasisDto.Gaap,
            "USD",
            ReportingConsolidationLevelDto.Fund,
            ReportingOutputFormatDto.Pdf,
            ReportingFinalityDto.Draft,
            IncludeSupportingSchedules: true,
            IncludeEvidenceAppendix: true);
        var parametersCanonicalJson = JsonSerializer.Serialize(new
        {
            scope = new
            {
                fundProfileId = scope.FundId,
                entityScopeKind = ReportingEntityScopeKindDto.AllEntities.ToString(),
                entityId = (string?)null,
                portfolioId = (string?)null,
                investorId = (string?)null,
                dimensions = (object?)null
            },
            periodId = scope.PeriodId,
            asOfDate = asOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ledgerBookId = (string?)null,
            ledgerBookCode = scope.BookId,
            accountingBasis = parameters.AccountingBasis.ToString(),
            presentationCurrency = parameters.PresentationCurrency,
            consolidationLevel = parameters.ConsolidationLevel.ToString(),
            outputFormat = parameters.OutputFormat.ToString(),
            finality = parameters.Finality.ToString(),
            includeSupportingSchedules = parameters.IncludeSupportingSchedules,
            includeEvidenceAppendix = parameters.IncludeEvidenceAppendix,
            templateParameters = new Dictionary<string, string>()
        });
        var parametersHash = ComputeSha256(parametersCanonicalJson);
        const string sourceCheckpointId = "checkpoint-pack-bound";
        var sourceCheckpointHash = new string('c', 64);
        const string reconciliationCheckpointId = "reconciliation-pack-bound";
        var reconciliationCheckpointHash = new string('f', 64);
        var readiness = new ReportingRunReadinessDto(
            "readiness-pack-bound",
            capturedAtUtc.AddMinutes(-1),
            template,
            parameters,
            ReportingRunReadinessStatusDto.Ready,
            CanGenerateDraft: true,
            CanGenerateFinal: true,
            Checks:
            [
                new ReportingRunReadinessCheckDto(
                    "accounting-close",
                    "Accounting close",
                    ReportingRunReadinessStatusDto.Ready,
                    "Exact close evidence is retained.",
                    0,
                    BlocksDraft: true,
                    BlocksFinal: true,
                    EvidenceReferences: ["evidence-reconciliation"])
            ],
            BlockingReasons: [],
            EvidenceHash: new string('d', 64));
        var certifiedRows = System.Collections.Immutable.ImmutableArray.Create<IReadOnlyDictionary<string, string>>(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["account"] = "cash",
                ["amount"] = "100.00"
            });
        var snapshotHash = ComputeSha256(JsonSerializer.Serialize(new
        {
            template = new { template.Name, template.Version },
            scope,
            access,
            parametersHash,
            sourceCheckpointId,
            sourceCheckpointHash,
            reconciliationId = reconciliationCheckpointId,
            reconciliationHash = reconciliationCheckpointHash,
            readinessHash = readiness.EvidenceHash,
            certifiedDatasetHash = FileReportingRunStore.ComputeCertifiedRowsHash(certifiedRows)
        }));
        var snapshot = new ReportingCertifiedSnapshotScope(
            scope.TenantId,
            scope.OrganizationId,
            scope.CompanyId,
            scope.FundId,
            scope.BookId,
            scope.PeriodId,
            "snapshot-pack-bound",
            snapshotHash,
            reconciliationCheckpointId,
            capturedAtUtc,
            SourceCheckpointId: sourceCheckpointId,
            SourceCheckpointHash: sourceCheckpointHash,
            ReconciliationCheckpointHash: reconciliationCheckpointHash,
            ParametersCanonicalJson: parametersCanonicalJson,
            ParametersHash: parametersHash);
        var source = new ReportingAuthoritativeSourceCheckpoint(
            "LedgerJournal",
            "ledger-journal-pack-bound",
            scope.TenantId,
            scope.OrganizationId,
            scope.CompanyId,
            scope.FundId!,
            scope.BookId,
            scope.PeriodId,
            ReportingAccountingBasisDto.Gaap.ToString(),
            asOfDate,
            capturedAtUtc,
            42,
            1,
            certifiedRows.Length,
            sourceCheckpointId,
            sourceCheckpointHash,
            capturedAtUtc,
            System.Collections.Immutable.ImmutableArray.Create(
                $"reporting-source-checkpoint:{sourceCheckpointId}:{sourceCheckpointHash}",
                "evidence-source"));
        var section = new ReportingSectionManifest(
            "summary",
            snapshot.SnapshotId,
            snapshot.ReconciliationCheckpointId,
            new string('e', 64),
            new ReportingLineageReference(
                "summary",
                snapshot.SnapshotId,
                snapshot.SnapshotHash,
                snapshot.ReconciliationCheckpointId,
                snapshot.CapturedAtUtc));
        var manifest = new ReportingOutputManifest(
            runId,
            "shadow-nav-daily-pack",
            asOfDate,
            ReportingRunStatus.Draft,
            System.Collections.Immutable.ImmutableArray.Create(section),
            System.Collections.Immutable.ImmutableArray.Create($"artifact://{runId}/summary.pdf"),
            1,
            ReportingRunTrigger.AdHoc,
            RunSeriesId: runId,
            RunAttemptOrdinal: 1,
            ResolvedTemplate: template,
            ResolvedParameters: parameters,
            Readiness: readiness,
            OperationalScope: scope,
            ImmutableAccessScope: access,
            CertifiedSnapshot: snapshot,
            AuthoritativeSource: source,
            CertifiedDatasetRows: certifiedRows);
        await runStore.SaveAsync(manifest, []);

        var payload = new ReportPackRunReadService(
            new DefaultReportingTemplateCatalog(),
            runStore,
            workflow).BuildPayload();

        var workflowRun = payload.RecentRuns.Single(run => run.RunId == $"report-pack:{record.ReportId:D}");
        workflowRun.NextActions.Should().ContainSingle(action =>
            action.Kind == "governed-run" &&
            action.Method == "GET" &&
            action.IsEnabled &&
            action.IsBrowserNavigable &&
            action.Href == $"/api/fund-structure/reporting/runs/{runId}");
        workflowRun.NextActions.Should().OnlyContain(action =>
            !action.Href.Contains("/reporting/packs/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReportPackRunReadService_GenericRunActionRequiresExactCertifiedImmutableBinding()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var runStore = new FileReportingRunStore(
            new ReportingRunStoreOptions(Path.Combine(root, "reporting-runs")),
            NullLogger<FileReportingRunStore>.Instance);
        var asOfDate = new DateOnly(2026, 5, 31);
        var capturedAtUtc = new DateTimeOffset(2026, 5, 31, 23, 0, 0, TimeSpan.Zero);
        var scope = new ReportingOperationalScope(
            "tenant-a",
            "organization-a",
            "company-a",
            "fund-alpha",
            "book-main",
            "2026-05");
        var access = new ReportingAccessScope(
            "policy-company-a",
            "1",
            ReportingGovernanceAccessMode.CompanyWide,
            "report.author",
            AllowOwnerAccess: true,
            Principals: System.Collections.Immutable.ImmutableArray.Create(
                new ReportingAccessPrincipalScope(ReportingAccessPrincipalKind.User, "report.author")),
            PolicyHash: new string('a', 64));
        var template = new VersionedReportTemplateIdDto("shadow-nav-daily-pack", 1);
        var parameters = new ReportingRunParametersDto(
            new ReportingRunScopeDto(scope.FundId!),
            scope.PeriodId,
            asOfDate,
            new ReportingLedgerBookSelectionDto(LedgerBookCode: scope.BookId),
            ReportingAccountingBasisDto.Gaap,
            "USD",
            ReportingConsolidationLevelDto.Fund,
            ReportingOutputFormatDto.Pdf,
            ReportingFinalityDto.Draft,
            IncludeSupportingSchedules: true,
            IncludeEvidenceAppendix: true);
        var parametersCanonicalJson = JsonSerializer.Serialize(new
        {
            scope = new
            {
                fundProfileId = scope.FundId,
                entityScopeKind = ReportingEntityScopeKindDto.AllEntities.ToString(),
                entityId = (string?)null,
                portfolioId = (string?)null,
                investorId = (string?)null,
                dimensions = (object?)null
            },
            periodId = scope.PeriodId,
            asOfDate = asOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ledgerBookId = (string?)null,
            ledgerBookCode = scope.BookId,
            accountingBasis = parameters.AccountingBasis.ToString(),
            presentationCurrency = parameters.PresentationCurrency,
            consolidationLevel = parameters.ConsolidationLevel.ToString(),
            outputFormat = parameters.OutputFormat.ToString(),
            finality = parameters.Finality.ToString(),
            includeSupportingSchedules = parameters.IncludeSupportingSchedules,
            includeEvidenceAppendix = parameters.IncludeEvidenceAppendix,
            templateParameters = new Dictionary<string, string>()
        });
        var parametersHash = ComputeSha256(parametersCanonicalJson);
        const string sourceCheckpointId = "checkpoint-bound";
        var sourceCheckpointHash = new string('c', 64);
        const string reconciliationCheckpointId = "reconciliation-bound";
        var reconciliationCheckpointHash = new string('f', 64);
        var readiness = new ReportingRunReadinessDto(
            "readiness-bound",
            capturedAtUtc.AddMinutes(-1),
            template,
            parameters,
            ReportingRunReadinessStatusDto.Ready,
            CanGenerateDraft: true,
            CanGenerateFinal: true,
            Checks:
            [
                new ReportingRunReadinessCheckDto(
                    "accounting-close",
                    "Accounting close",
                    ReportingRunReadinessStatusDto.Ready,
                    "Exact close evidence is retained.",
                    0,
                    BlocksDraft: true,
                    BlocksFinal: true,
                    EvidenceReferences: ["evidence-reconciliation"])
            ],
            BlockingReasons: [],
            EvidenceHash: new string('d', 64));
        var certifiedRows = System.Collections.Immutable.ImmutableArray.Create<IReadOnlyDictionary<string, string>>(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["account"] = "cash",
                ["amount"] = "100.00"
            });
        var snapshotHash = ComputeSha256(JsonSerializer.Serialize(new
        {
            template = new { template.Name, template.Version },
            scope,
            access,
            parametersHash,
            sourceCheckpointId,
            sourceCheckpointHash,
            reconciliationId = reconciliationCheckpointId,
            reconciliationHash = reconciliationCheckpointHash,
            readinessHash = readiness.EvidenceHash,
            certifiedDatasetHash = FileReportingRunStore.ComputeCertifiedRowsHash(certifiedRows)
        }));
        var snapshot = new ReportingCertifiedSnapshotScope(
            scope.TenantId,
            scope.OrganizationId,
            scope.CompanyId,
            scope.FundId,
            scope.BookId,
            scope.PeriodId,
            "snapshot-bound",
            snapshotHash,
            reconciliationCheckpointId,
            capturedAtUtc,
            SourceCheckpointId: sourceCheckpointId,
            SourceCheckpointHash: sourceCheckpointHash,
            ReconciliationCheckpointHash: reconciliationCheckpointHash,
            ParametersCanonicalJson: parametersCanonicalJson,
            ParametersHash: parametersHash);
        var source = new ReportingAuthoritativeSourceCheckpoint(
            "LedgerJournal",
            "ledger-journal-bound",
            scope.TenantId,
            scope.OrganizationId,
            scope.CompanyId,
            scope.FundId!,
            scope.BookId,
            scope.PeriodId,
            ReportingAccountingBasisDto.Gaap.ToString(),
            asOfDate,
            capturedAtUtc,
            42,
            1,
            certifiedRows.Length,
            sourceCheckpointId,
            sourceCheckpointHash,
            capturedAtUtc,
            System.Collections.Immutable.ImmutableArray.Create(
                $"reporting-source-checkpoint:{sourceCheckpointId}:{sourceCheckpointHash}",
                "evidence-source"));
        var section = new ReportingSectionManifest(
            "summary",
            snapshot.SnapshotId,
            snapshot.ReconciliationCheckpointId,
            new string('e', 64),
            new ReportingLineageReference(
                "summary",
                snapshot.SnapshotId,
                snapshot.SnapshotHash,
                snapshot.ReconciliationCheckpointId,
                snapshot.CapturedAtUtc));
        var boundRunId = "certified-bound-run-20260531";
        var bound = new ReportingOutputManifest(
            boundRunId,
            template.Name,
            asOfDate,
            ReportingRunStatus.Draft,
            System.Collections.Immutable.ImmutableArray.Create(section),
            System.Collections.Immutable.ImmutableArray.Create("artifact://certified-bound-run-20260531/summary.pdf"),
            1,
            ReportingRunTrigger.AdHoc,
            RunSeriesId: "certified-bound-series",
            RunAttemptOrdinal: 1,
            ResolvedTemplate: template,
            ResolvedParameters: parameters,
            Readiness: readiness,
            OperationalScope: scope,
            ImmutableAccessScope: access,
            CertifiedSnapshot: snapshot,
            AuthoritativeSource: source,
            CertifiedDatasetRows: certifiedRows);
        var driftedRunId = "certified-drifted-run-20260531";
        var drifted = bound with
        {
            RunId = driftedRunId,
            RunSeriesId = "certified-drifted-series",
            ResolvedTemplate = null,
            ResolvedParameters = null,
            Readiness = null,
            OperationalScope = null,
            ImmutableAccessScope = null,
            CertifiedSnapshot = null,
            AuthoritativeSource = null,
            CertifiedDatasetRows = System.Collections.Immutable.ImmutableArray<IReadOnlyDictionary<string, string>>.Empty
        };
        await runStore.SaveAsync(bound, []);
        await runStore.SaveAsync(drifted, []);

        var coordinatorValidation = () =>
            ReportingGovernanceCoordinatorService.ValidateManifestScope(bound);
        coordinatorValidation.Should().NotThrow();

        var payload = new ReportPackRunReadService(
            new DefaultReportingTemplateCatalog(),
            runStore).BuildPayload();

        payload.RecentRuns.Single(run => run.RunId == boundRunId).NextActions.Should().ContainSingle(action =>
            action.Kind == "governed-run" &&
            action.Method == "GET" &&
            action.Href == $"/api/fund-structure/reporting/runs/{boundRunId}" &&
            action.IsEnabled &&
            action.IsBrowserNavigable);
        payload.RecentRuns.Single(run => run.RunId == driftedRunId).NextActions.Should().ContainSingle(action =>
            action.Kind == "migration-required" &&
            action.Method == "GET" &&
            action.Href == string.Empty &&
            !action.IsEnabled &&
            !action.IsBrowserNavigable &&
            action.DisabledReason != null &&
            action.DisabledReason.Contains("exact certified tenant, company, scope, access-policy, and run binding", StringComparison.Ordinal));
    }

    [Fact]
    public void ReportPackDeliveryService_RejectsReviewedAutomationOriginBeforePackageCreation()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var workflow = new ReportPackWorkflowService();
        var approved = CreateApprovedPack(
            workflow,
            [CompleteLineProvenance("trial-balance.cash", "ledger-evidence-1")]);
        var published = workflow.Publish(
            approved.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            CompleteLineProvenanceEvidenceLinks("ledger-evidence-1"));
        var store = new FileReportPackDeliveryRecordStore(
            new ReportPackDeliveryStoreOptions(Path.Combine(root, "report-pack-deliveries.json")),
            NullLogger<FileReportPackDeliveryRecordStore>.Instance);
        var service = new ReportPackDeliveryService(workflow, store);

        Action act = () => service.Deliver(
            published.ReportId,
            new ReportPackDeliveryRequestDto(
                "board-reporting-committee",
                Actor: "reviewed-automation",
                ActionOrigin: OperationsActionOriginDto.AutomationAssistant),
            fallbackActor: "fallback");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Reviewed automation cannot publish reports; a human operator approval is required.");
        service.GetHistory(published.ReportId).Should().BeEmpty();
    }

    [Fact]
    public void ReportPackDeliveryService_RejectsReviewedAutomationOriginBeforeFailureRecording()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var workflow = new ReportPackWorkflowService();
        var approved = CreateApprovedPack(
            workflow,
            [CompleteLineProvenance("trial-balance.cash", "ledger-evidence-1")]);
        var published = workflow.Publish(
            approved.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            CompleteLineProvenanceEvidenceLinks("ledger-evidence-1"));
        var store = new FileReportPackDeliveryRecordStore(
            new ReportPackDeliveryStoreOptions(Path.Combine(root, "report-pack-deliveries.json")),
            NullLogger<FileReportPackDeliveryRecordStore>.Instance);
        var service = new ReportPackDeliveryService(workflow, store);

        Action act = () => service.RecordFailure(
            published.ReportId,
            new ReportPackDeliveryFailureRequestDto(
                "board-reporting-committee",
                "Assistant classified recipient portal as failed.",
                Actor: "reviewed-automation",
                ActionOrigin: OperationsActionOriginDto.AssistantDraft),
            fallbackActor: "fallback");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Reviewed automation cannot record delivery failures; a human operator approval is required.");
        service.GetHistory(published.ReportId).Should().BeEmpty();
    }

    [Fact]
    public void ReportPackDeliveryService_PersistsAttemptsAndUpdatesDistributionState()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var workflow = new ReportPackWorkflowService();
        var recordAccess = new ReportAccessQueryContext(
            ActorPrincipalId: "author",
            CompanyId: "company-a",
            TenantId: "tenant-a",
            RequireBoundScope: true);
        var queryAccess = recordAccess with { ActorPrincipalId = "report-viewer" };
        var approved = CreateApprovedPack(
            workflow,
            [CompleteLineProvenance("trial-balance.cash", "ledger-evidence-1")],
            accessPolicy: new ReportAccessPolicyDto(ReportAccessModeDto.CompanyWide, CompanyId: "company-a"),
            accessContext: recordAccess);
        var published = workflow.Publish(
            approved.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            CompleteLineProvenanceEvidenceLinks("ledger-evidence-1"),
            signedOffRole: nameof(UserRole.Controller),
            signOffReason: "Approved by controller.",
            signOffContext: "Authenticated actor 'publisher' with role 'Controller' approved publication.");
        var restated = workflow.Restate(
            published.ReportId,
            "approver",
            "approver",
            "NAV_CORRECTION",
            "controller",
            published.ReportId,
            [
                new ReportPackChangedLineDto(
                    "trial-balance.cash",
                    "100.00",
                    "101.00",
                    [new ReportPackEvidenceLinkDto("cash-restatement-1", "Cash restatement", "/evidence/cash-restatement-1", "reporting")])
            ]);
        var store = new FileReportPackDeliveryRecordStore(
            new ReportPackDeliveryStoreOptions(Path.Combine(root, "report-pack-deliveries.json")),
            NullLogger<FileReportPackDeliveryRecordStore>.Instance);

        var service = new ReportPackDeliveryService(workflow, store);
        var attempt = service.Deliver(
            restated.ReportId,
            new ReportPackDeliveryRequestDto(
                "board-reporting-committee",
                Actor: "fund-controller",
                DeliveryReference: "board-portal:packet-1",
                Note: "Delivered after restatement.",
                EvidenceLinks: [new ReportPackEvidenceLinkDto("delivery-evidence-1", "Board portal receipt", "/evidence/board-portal-receipt", "delivery")],
                Formats:
                [
                    GovernanceReportArtifactFormatDto.Pdf,
                    GovernanceReportArtifactFormatDto.Xlsx,
                    GovernanceReportArtifactFormatDto.Csv
                ],
                DeliveryMode: ReportPackDeliveryModeDto.EmailLink),
            fallbackActor: "fallback");

        attempt.State.Should().Be(ReportPackDeliveryStateDto.Delivered);
        attempt.AttemptNumber.Should().Be(1);
        attempt.Recipient.Should().Be("Board reporting committee");
        attempt.DeliveryReference.Should().Be("board-portal:packet-1");
        attempt.Package.Should().NotBeNull();
        attempt.Package!.DeliveryMode.Should().Be(ReportPackDeliveryModeDto.EmailLink);
        var emailNotification = attempt.Package.Notifications.Should().ContainSingle().Subject;
        emailNotification.NotificationId.Should().Be($"delivery-notification:{attempt.Package.PackageId}:email-link");
        emailNotification.Channel.Should().Be("EmailLink");
        emailNotification.Subject.Should().Be("Report package available for Board reporting committee");
        emailNotification.Status.Should().Be("ReadyToSend");
        emailNotification.RequiresToken.Should().BeTrue();
        emailNotification.Href.Should().Be(attempt.Package.SecureLink);
        emailNotification.ExpiresAtUtc.Should().Be(attempt.Package.AccessExpiresAtUtc);
        attempt.Package.Formats.Should().Equal(
            [
                GovernanceReportArtifactFormatDto.Pdf,
                GovernanceReportArtifactFormatDto.Xlsx,
                GovernanceReportArtifactFormatDto.Csv
            ]);
        attempt.Package.Artifacts.Should().Contain(artifact =>
            artifact.Format == GovernanceReportArtifactFormatDto.Pdf &&
            artifact.ContentType == "application/pdf" &&
            artifact.RetainedPath.Contains("/board-reporting-committee/", StringComparison.OrdinalIgnoreCase));
        attempt.Package.Artifacts.Should().Contain(artifact =>
            artifact.Format == GovernanceReportArtifactFormatDto.Xlsx &&
            artifact.ContentType == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        attempt.Package.Artifacts.Should().Contain(artifact =>
            artifact.Format == GovernanceReportArtifactFormatDto.Csv &&
            artifact.ContentType == "text/csv");
        attempt.Package.SecureLink.Should().Contain("/package?token=");
        attempt.Package.AccessLinks.Should().NotBeNull();
        attempt.Package.AccessLinks!.Should().Contain(link =>
            link.Kind == "email-link" &&
            link.Label == "Email link package" &&
            link.Href == attempt.Package.SecureLink &&
            link.RequiresToken &&
            link.ExpiresAtUtc == attempt.Package.AccessExpiresAtUtc);
        attempt.Package.AccessLinks.Should().Contain(link =>
            link.Kind == "manifest" &&
            link.Href == attempt.Package.RetainedManifestPath &&
            !link.RequiresToken);
        attempt.Package.AccessLinks.Count(link => link.Kind.StartsWith("artifact-", StringComparison.Ordinal)).Should().Be(4);
        attempt.Package.AccessLinks.Should().Contain(link =>
            link.Kind == "artifact-xls" &&
            link.Label == "XLS compatibility download" &&
            link.Href.Contains("format=xls", StringComparison.Ordinal) &&
            link.RequiresToken);
        attempt.Package.DeliveryAccessSummary.Should().Contain("Email-link package is available through the token-gated route");
        attempt.Package.DeliveryAccessSummary.Should().Contain(attempt.Package.SecureLink);
        attempt.Package.DeliveryChannelSummary.Should().Be("EmailLink delivery to Board reporting committee via Board portal.");
        attempt.Package.DownloadSummary.Should().Contain("3 artifact(s) retained as Csv/Pdf/Xlsx");
        attempt.Package.DownloadSummary.Should().Contain(attempt.Package.RetainedManifestPath);
        attempt.Package.AccessExpiresAtUtc.Should().BeAfter(attempt.Package.CreatedAtUtc);
        attempt.Package.AccessExpiresAtUtc.Should().BeCloseTo(attempt.Package.CreatedAtUtc.AddDays(14), TimeSpan.FromMinutes(1));
        attempt.Package.PublicationManifestId.Should().Be("manifest-1");
        attempt.Package.PublicationRetainedManifestPath.Should().Be("vault/report-packs/manifest-1.json");
        attempt.Package.PublicationSignedOffBy.Should().Be("controller");
        attempt.Package.PublicationSignedOffRole.Should().Be(nameof(UserRole.Controller));
        attempt.Package.PublicationSignOffReason.Should().Be("Approved by controller.");
        attempt.Package.PublicationSignOffContext.Should().Be("Authenticated actor 'publisher' with role 'Controller' approved publication.");
        attempt.Package.PublicationEvidenceLinks.Should().NotBeNull().And.HaveCount(10);
        attempt.Package.LineProvenance.Should().NotBeNull().And.ContainSingle(line =>
            line.LineKey == "trial-balance.cash" &&
            line.EvidenceId == "ledger-evidence-1" &&
            line.ReportValue == "100.00");
        attempt.Package.RestatementReasonCode.Should().Be("NAV_CORRECTION");
        attempt.Package.RestatementPriorVersionReportId.Should().Be(published.ReportId);
        attempt.Package.RestatementApprover.Should().Be("controller");
        attempt.Package.RestatementChangedLines.Should().NotBeNull().And.ContainSingle(line =>
            line.LineKey == "trial-balance.cash" &&
            line.PreviousValue == "100.00" &&
            line.CurrentValue == "101.00");
        attempt.Package.RestatementEvidenceLinks.Should().NotBeNull().And.ContainSingle(link =>
            link.EvidenceId == "cash-restatement-1");
        attempt.Package.DeliveryEvidencePacket.Should().NotBeNull();
        var packet = attempt.Package.DeliveryEvidencePacket!;
        packet.PacketKind.Should().Be("StakeholderDeliveryRestatement");
        packet.PackageId.Should().Be(attempt.Package.PackageId);
        packet.FundProfileId.Should().Be("fund-a");
        packet.FundAccountId.Should().Be("acct-1");
        packet.Period.Should().Be("2026-03");
        packet.PackageContents.Should().Contain(item => item.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));
        packet.PackageContents.Should().Contain("report-line:trial-balance.cash");
        packet.PackageContents.Should().Contain("restatement-line:trial-balance.cash");
        packet.SupportEvidenceIds.Should().Contain("ledger-evidence-1");
        packet.SupportEvidenceIds.Should().Contain("delivery-evidence-1");
        packet.SupportEvidenceIds.Should().Contain("cash-restatement-1");
        packet.RecipientList.Should().ContainSingle(recipient =>
            recipient.DistributionId == "board-reporting-committee" &&
            recipient.Recipient == "Board reporting committee" &&
            recipient.Channel == "Board portal");
        packet.EntitlementScope.Should().Be("CompanyWide company=company-a");
        packet.ApprovalChain.Should().Contain(step => step.Action == "published");
        packet.DatasetVersion.Should().Be("manifest-1");
        packet.TemplateVersion.Should().Be("board-pack@v1");
        packet.DeliveryChannel.Should().Be("EmailLink via Board portal");
        packet.DeliveryEvidence.Should().Contain(link => link.EvidenceId == "delivery-evidence-1");
        packet.DeliveryEvidence.Should().Contain(link => link.Source == "report-pack-delivery");
        packet.RequestHistory.Should().Contain(item => item.Contains("delivery-request:board-reporting-committee", StringComparison.Ordinal));
        packet.AmendmentReason.Should().Be("NAV_CORRECTION");
        packet.RestatementLineage.Should().Contain("reason=NAV_CORRECTION");
        packet.AuditEventReferences.Should().NotBeNull().And.HaveCountGreaterThan(0);
        packet.BlockedDownstreamOutputs.Should().BeEmpty();
        attempt.EvidenceLinks.Should().Contain(link => link.Source == "report-pack-delivery");
        var token = attempt.Package.SecureLink.Split("token=", 2, StringSplitOptions.None)[1];
        service.GetPackage(published.ReportId, attempt.AttemptId, token).PackageId.Should().Be(attempt.Package.PackageId);
        service.GetPortalPackage(attempt.Package.PackageId, token).ReportId.Should().Be(published.ReportId);
        var csvArtifact = attempt.Package.Artifacts.Single(artifact => artifact.Format == GovernanceReportArtifactFormatDto.Csv);
        var csv = System.Text.Encoding.UTF8.GetString(service.GetArtifact(published.ReportId, attempt.AttemptId, csvArtifact.ArtifactName, token).Content);
        csv.Should().Contain("publicationManifestId,manifest-1");
        csv.Should().Contain("publicationSignedOffBy,controller");
        csv.Should().Contain($"publicationSignedOffRole,{nameof(UserRole.Controller)}");
        csv.Should().Contain("publicationSignOffReason,Approved by controller.");
        csv.Should().Contain("publicationSignOffContext,Authenticated actor 'publisher' with role 'Controller' approved publication.");
        csv.Should().Contain("publicationEvidenceLinkCount,10");
        csv.Should().Contain("lineProvenanceCount,1");
        csv.Should().Contain("lineProvenance[0].lineKey,trial-balance.cash");
        csv.Should().Contain("lineProvenance[0].reportValue,100.00");
        csv.Should().Contain("lineProvenance[0].financialRecordExplorerId,ledger");
        csv.Should().Contain("lineProvenance[0].financialRecordHref,/api/workstation/financial-record-explorers/ledger");
        csv.Should().Contain("publicationEvidenceLinks[0].evidenceId,ledger-evidence-1");
        csv.Should().Contain("restatementReasonCode,NAV_CORRECTION");
        csv.Should().Contain("restatementChangedLineCount,1");
        csv.Should().Contain("restatementChangedLines[0].lineKey,trial-balance.cash");
        csv.Should().Contain("restatementChangedLines[0].currentValue,101.00");
        csv.Should().Contain("restatementEvidenceLinks[0].evidenceId,cash-restatement-1");
        service.Invoking(item => item.GetPackage(published.ReportId, attempt.AttemptId, "bad-token"))
            .Should().Throw<UnauthorizedAccessException>()
            .WithMessage("A valid package token is required.");

        var reloaded = new ReportPackDeliveryService(workflow, store);
        reloaded.GetHistory(published.ReportId).Should().ContainSingle(item =>
            item.DistributionId == "board-reporting-committee" &&
            item.DeliveryReference == "board-portal:packet-1" &&
            item.Package != null &&
            item.Package.DeliveryMode == ReportPackDeliveryModeDto.EmailLink &&
            item.Package.DeliveryEvidencePacket != null &&
            item.Package.DeliveryEvidencePacket.SupportEvidenceIds.Contains("ledger-evidence-1"));

        var payload = new ReportPackRunReadService(
            new DefaultReportingTemplateCatalog(),
            workflowService: workflow,
            deliveryService: reloaded).BuildPayload(queryAccess);
        payload.DeliveryAttempts.Should().ContainSingle(item =>
            item.AttemptId == attempt.AttemptId &&
            item.Package != null &&
            item.Package.Artifacts.Count == 3);
        JsonSerializer.Serialize(payload)
            .Contains("token=", StringComparison.OrdinalIgnoreCase)
            .Should().BeFalse();
        payload.ReportPackDistributions.Should().Contain(distribution =>
            distribution.DistributionId == "board-reporting-committee" &&
            distribution.State == "Pending delivery" &&
            distribution.PendingItems == 1 &&
            distribution.LastSentAtUtc == null);
    }

    [Fact]
    public async Task Endpoint_EmailLinkDeliveryPackage_ReturnsGoneWithoutLeakingQueryToken()
    {
        await using var app = await CreateFundStructureAppAsync(UserRole.Admin);
        var client = app.GetTestClient();
        const string token = "caller-token-must-not-be-returned";

        var response = await client.GetAsync($"/portal/reporting/packages/legacy-package?token={token}");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
        body.Should().Contain("/portal/reporting/access-grants/{grantId}/exchange");
        body.Should().NotContain(token);
    }

    [Fact]
    public void ReportPackDeliveryService_RejectsExpiredPackageTokens()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var workflow = new ReportPackWorkflowService();
        var approved = CreateApprovedPack(
            workflow,
            [CompleteLineProvenance("trial-balance.cash", "ledger-evidence-1")]);
        var published = workflow.Publish(
            approved.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            CompleteLineProvenanceEvidenceLinks("ledger-evidence-1"));
        var store = new FileReportPackDeliveryRecordStore(
            new ReportPackDeliveryStoreOptions(Path.Combine(root, "report-pack-deliveries.json")),
            NullLogger<FileReportPackDeliveryRecordStore>.Instance);
        var service = new ReportPackDeliveryService(workflow, store);
        var attempt = service.Deliver(
            published.ReportId,
            new ReportPackDeliveryRequestDto(
                "board-reporting-committee",
                Actor: "fund-controller",
                DeliveryMode: ReportPackDeliveryModeDto.SecurePortal),
            fallbackActor: "fallback");
        var token = attempt.Package!.SecureLink.Split("token=", 2, StringSplitOptions.None)[1];

        var expiredAttempt = attempt with
        {
            Package = attempt.Package with { AccessExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1) }
        };
        store.Save([expiredAttempt]);
        var reloaded = new ReportPackDeliveryService(workflow, store);

        reloaded.Invoking(item => item.GetPackage(published.ReportId, attempt.AttemptId, token))
            .Should().Throw<UnauthorizedAccessException>()
            .WithMessage("The package token has expired.");
        reloaded.Invoking(item => item.GetArtifact(
                published.ReportId,
                attempt.AttemptId,
                attempt.Package.Artifacts[0].ArtifactName,
                token))
            .Should().Throw<UnauthorizedAccessException>()
            .WithMessage("The package token has expired.");
    }

    [Fact]
    public async Task Endpoint_DeliveryPackageAndArtifactQueryTokens_ReturnGoneWithoutDisclosure()
    {
        await using var app = await CreateFundStructureAppAsync(UserRole.Admin);
        var client = app.GetTestClient();
        var reportId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        const string token = "legacy-query-token-secret";

        var packageResponse = await client.GetAsync(
            $"/api/fund-structure/reporting/packs/{reportId:D}/deliveries/{attemptId:D}/package?token={token}&format=json");
        var artifactResponse = await client.GetAsync(
            $"/api/fund-structure/reporting/packs/{reportId:D}/deliveries/{attemptId:D}/artifacts/board-pack.pdf?token={token}");
        var packageBody = await packageResponse.Content.ReadAsStringAsync();
        var artifactBody = await artifactResponse.Content.ReadAsStringAsync();

        packageResponse.StatusCode.Should().Be(HttpStatusCode.Gone);
        artifactResponse.StatusCode.Should().Be(HttpStatusCode.Gone);
        packageBody.Should().Contain("/portal/reporting/access-grants/{grantId}/exchange");
        artifactBody.Should().Contain(
            "/api/fund-structure/reporting/distribution/packages/{runId}/artifacts/{artifactId}");
        packageBody.Should().NotContain(token);
        artifactBody.Should().NotContain(token);
    }


    [Fact]
    public async Task Endpoint_StructuredReportingExport_ReturnsJsonCsvAndXlsxWhenFormatRequested()
    {
        await using var app = await CreateFundStructureAppAsync(
            UserRole.Admin,
            username: "controller.admin",
            workspaceService: CreateStructuredExportWorkspaceService(),
            roleProfileName: "reporting-ops",
            companyId: "company-alpha");
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/fund-structure/reporting/structured-exports/regulatory-trial-balance?fundProfileId=fund-alpha&asOf=2026-04-11T16%3A00%3A00Z&currency=USD&format=csv");
        var xlsxResponse = await client.GetAsync("/api/fund-structure/reporting/structured-exports/investment-portfolio-cuts?fundProfileId=fund-alpha&asOf=2026-04-11T16%3A00%3A00Z&currency=USD&format=xlsx");
        var xlsAliasResponse = await client.GetAsync("/api/fund-structure/reporting/structured-exports/investment-portfolio-cuts?fundProfileId=fund-alpha&asOf=2026-04-11T16%3A00%3A00Z&currency=USD&format=xls");
        var analyticsJsonResponse = await client.GetAsync("/api/fund-structure/reporting/structured-exports/investment-topn-contribution-analytics?fundProfileId=fund-alpha&asOf=2026-04-11T16%3A00%3A00Z&currency=USD");
        var analyticsCsvResponse = await client.GetAsync("/api/fund-structure/reporting/structured-exports/investment-topn-contribution-analytics?fundProfileId=fund-alpha&asOf=2026-04-11T16%3A00%3A00Z&currency=USD&format=csv");
        var crossFundJsonResponse = await client.GetAsync("/api/fund-structure/reporting/structured-exports/cross-fund-consolidation?fundProfileId=fund-alpha&asOf=2026-04-11T16%3A00%3A00Z&currency=USD");
        var crossFundXlsxResponse = await client.GetAsync("/api/fund-structure/reporting/structured-exports/cross-fund-consolidation?fundProfileId=fund-alpha&asOf=2026-04-11T16%3A00%3A00Z&currency=USD&format=xlsx");
        var warehouseJsonResponse = await client.GetAsync("/api/fund-structure/reporting/structured-exports/warehouse-ledger-facts?fundProfileId=fund-alpha&asOf=2026-04-11T16%3A00%3A00Z&currency=USD");
        var warehouseJsonDownloadResponse = await client.GetAsync("/api/fund-structure/reporting/structured-exports/warehouse-ledger-facts?fundProfileId=fund-alpha&asOf=2026-04-11T16%3A00%3A00Z&currency=USD&format=json");
        var warehouseCsvResponse = await client.GetAsync("/api/fund-structure/reporting/structured-exports/warehouse-ledger-facts?fundProfileId=fund-alpha&asOf=2026-04-11T16%3A00%3A00Z&currency=USD&format=csv");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/csv");
        response.Content.Headers.ContentDisposition?.FileName.Should().Be("regulatory-trial-balance-20260411160000.csv");
        AssertStructuredExportAuditHeaders(response);
        var csv = await response.Content.ReadAsStringAsync();
        csv.Should().StartWith("accountName,accountType,symbol,financialAccountId,fundId,entityId,sleeveId,strategyId,investorId,capitalAccountId,instrumentId,taxLotId,costCenterId,counterpartyId,organizationId,portfolioId,bookId,accountId,customerId,vendorId,projectId,externalGlDimensionsJson,currency,balance,entryCount,securityId,securityDisplayName,sourceAsOfUtc");
        csv.Should().NotContain("\"exportId\"");
        xlsxResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        xlsxResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        xlsxResponse.Content.Headers.ContentDisposition?.FileName.Should().Be("investment-portfolio-cuts-20260411160000.xlsx");
        var workbook = await xlsxResponse.Content.ReadAsByteArrayAsync();
        workbook.Should().StartWith([0x50, 0x4B]);
        workbook.Length.Should().BeGreaterThan(1000);
        using (var structuredWorkbook = new ZipArchive(new MemoryStream(workbook), ZipArchiveMode.Read))
        {
            structuredWorkbook.GetEntry("xl/worksheets/sheet1.xml").Should().NotBeNull();
            structuredWorkbook.GetEntry("xl/worksheets/sheet2.xml").Should().NotBeNull();
            structuredWorkbook.GetEntry("xl/worksheets/sheet3.xml").Should().NotBeNull();
            structuredWorkbook.GetEntry("xl/worksheets/sheet4.xml").Should().NotBeNull();
            structuredWorkbook.GetEntry("xl/worksheets/sheet5.xml").Should().NotBeNull();
            using var workbookXmlReader = new StreamReader(structuredWorkbook.GetEntry("xl/workbook.xml")!.Open());
            var workbookXml = workbookXmlReader.ReadToEnd();
            workbookXml.Should().Contain("portfolio-reporting-cuts");
            workbookXml.Should().Contain("Metadata");
            workbookXml.Should().Contain("DataDictionary");
            workbookXml.Should().Contain("Validation");
            workbookXml.Should().Contain("RowLineage");
            using var sharedStringsReader = new StreamReader(structuredWorkbook.GetEntry("xl/sharedStrings.xml")!.Open());
            var sharedStringsXml = sharedStringsReader.ReadToEnd();
            sharedStringsXml.Should().Contain("generatedByPrincipalId");
            sharedStringsXml.Should().Contain("controller.admin");
            sharedStringsXml.Should().Contain("generatedForCompanyId");
            sharedStringsXml.Should().Contain("company-alpha");
            sharedStringsXml.Should().Contain("generatedForGroups");
            sharedStringsXml.Should().Contain("reporting-ops");
            sharedStringsXml.Should().Contain("rowLineageCount");
        }
        xlsAliasResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        xlsAliasResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        xlsAliasResponse.Content.Headers.ContentDisposition?.FileName.Should().Be("investment-portfolio-cuts-20260411160000.xlsx");
        var xlsAliasWorkbook = await xlsAliasResponse.Content.ReadAsByteArrayAsync();
        xlsAliasWorkbook.Should().StartWith([0x50, 0x4B]);
        using (var xlsAliasArchive = new ZipArchive(new MemoryStream(xlsAliasWorkbook), ZipArchiveMode.Read))
        {
            xlsAliasArchive.GetEntry("xl/workbook.xml").Should().NotBeNull();
            xlsAliasArchive.GetEntry("xl/worksheets/sheet1.xml").Should().NotBeNull();
        }
        analyticsJsonResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var analyticsPayload = await analyticsJsonResponse.Content.ReadFromJsonAsync<StructuredReportingExportPayloadDto>(ServerJsonOptions);
        analyticsPayload.Should().NotBeNull();
        analyticsPayload!.Export.ExportId.Should().Be("investment-topn-contribution-analytics");
        analyticsPayload.GeneratedByPrincipalId.Should().Be("controller.admin");
        analyticsPayload.GeneratedForCompanyId.Should().Be("company-alpha");
        analyticsPayload.GeneratedForGroupPrincipalIds.Should().Contain("reporting-ops");
        analyticsPayload.RowLineage.Should().NotBeNull();
        analyticsPayload.RowLineage!.Should().Contain(lineage =>
            lineage.RowKey == analyticsPayload.Rows[0]["analyticsId"] &&
            lineage.RowHashSha256.Length == 64);
        analyticsPayload.Export.RowLineageCount.Should().Be(analyticsPayload.RowLineage.Count);
        analyticsPayload.Export.Dataset.Should().Be("portfolio-topn-contribution-analytics");
        analyticsPayload.Export.Purpose.Should().Be(StructuredReportingExportPurposeDto.InvestmentDecision);
        analyticsPayload.Columns.Select(static column => column.Name).Should().ContainInOrder(
            "analyticsId",
            "kind",
            "scope",
            "rank",
            "label",
            "symbol",
            "classification",
            "realizedPnl",
            "unrealizedPnl",
            "totalPnl",
            "contributionPercent",
            "heatMapIntensity");
        var analyticsRow = analyticsPayload.Rows.Should().ContainSingle().Subject;
        analyticsRow.Should().ContainKey("kind");
        analyticsRow.Should().ContainKey("contributionPercent");
        decimal.Parse(analyticsRow["contributionPercent"]!, CultureInfo.InvariantCulture).Should().BeGreaterThanOrEqualTo(0m);
        analyticsCsvResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        analyticsCsvResponse.Content.Headers.ContentType?.MediaType.Should().Be("text/csv");
        analyticsCsvResponse.Content.Headers.ContentDisposition?.FileName.Should().Be("investment-topn-contribution-analytics-20260411160000.csv");
        AssertStructuredExportAuditHeaders(analyticsCsvResponse);
        var analyticsCsv = await analyticsCsvResponse.Content.ReadAsStringAsync();
        analyticsCsv.Should().StartWith("analyticsId,kind,scope,rank,label,symbol,classification,currency,realizedPnl,unrealizedPnl,totalPnl,contributionPercent,heatMapIntensity,sourceCount,asOfUtc,readinessSummary,versionStamp,tags");
        analyticsCsv.Should().Contain("contributionPercent");
        crossFundJsonResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var crossFundPayload = await crossFundJsonResponse.Content.ReadFromJsonAsync<StructuredReportingExportPayloadDto>(ServerJsonOptions);
        crossFundPayload.Should().NotBeNull();
        crossFundPayload!.Export.ExportId.Should().Be("cross-fund-consolidation");
        crossFundPayload.Export.Dataset.Should().Be("cross-fund-reporting-consolidation");
        crossFundPayload.Export.Purpose.Should().Be(StructuredReportingExportPurposeDto.InvestmentDecision);
        crossFundPayload.Columns.Select(static column => column.Name).Should().ContainInOrder(
            "consolidationId",
            "label",
            "scope",
            "currency",
            "grossExposure",
            "netExposure",
            "shadowNav",
            "shadowNavVariance");
        var crossFundRow = crossFundPayload.Rows.Should().ContainSingle().Subject;
        crossFundRow.Should().ContainKey("consolidationId");
        crossFundRow.Should().ContainKey("shadowNav");
        decimal.Parse(crossFundRow["shadowNav"]!, CultureInfo.InvariantCulture).Should().BeGreaterThanOrEqualTo(0m);
        crossFundXlsxResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        crossFundXlsxResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        crossFundXlsxResponse.Content.Headers.ContentDisposition?.FileName.Should().Be("cross-fund-consolidation-20260411160000.xlsx");
        var crossFundWorkbook = await crossFundXlsxResponse.Content.ReadAsByteArrayAsync();
        crossFundWorkbook.Should().StartWith([0x50, 0x4B]);
        warehouseJsonResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        warehouseJsonResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        var warehousePayload = await warehouseJsonResponse.Content.ReadFromJsonAsync<StructuredReportingExportPayloadDto>(ServerJsonOptions);
        warehousePayload.Should().NotBeNull();
        warehousePayload!.Export.ExportId.Should().Be("warehouse-ledger-facts");
        warehousePayload.Export.Purpose.Should().Be(StructuredReportingExportPurposeDto.DataWarehouse);
        warehousePayload.Export.RetainedManifestPath.Should().Be("exports/reporting/fund-alpha/20260411160000/warehouse-ledger-facts.manifest.json");
        warehousePayload.Export.IntegrityHashSha256.Should().MatchRegex("^[a-f0-9]{64}$");
        warehousePayload.Export.IntegritySummary.Should().Contain("SHA-256");
        warehousePayload.Export.IntegritySummary.Should().Contain(warehousePayload.Export.IntegrityHashSha256);
        warehousePayload.RowLineage.Should().NotBeNull();
        warehousePayload.RowLineage!.Should().OnlyContain(lineage => lineage.RowHashSha256.Length == 64);
        warehousePayload.Export.RowLineageCount.Should().Be(warehousePayload.RowLineage.Count);
        warehousePayload.DataDictionary.Should().NotBeNull();
        warehousePayload.DataDictionary!.Should().Contain(field =>
            field.Name == "ledgerEntryCount" &&
            field.DataType == "integer" &&
            field.Ordinal == 26);
        warehousePayload.DataDictionary.Should().Contain(field =>
            field.Name == "fundId" &&
            field.DataType == "string" &&
            field.Ordinal == 6);
        warehousePayload.ValidationChecks.Should().NotBeNull();
        warehousePayload.ValidationChecks!.Should().Contain(check =>
            check.CheckId == "source-count" &&
            check.Status == "Warning");
        warehousePayload.Columns.Select(static column => column.Name).Should().ContainInOrder(
            "scope",
            "accountName",
            "accountType",
            "symbol",
            "financialAccountId",
            "fundId",
            "entityId",
            "sleeveId",
            "strategyId",
            "investorId",
            "capitalAccountId",
            "instrumentId",
            "taxLotId",
            "costCenterId",
            "counterpartyId",
            "organizationId",
            "portfolioId",
            "bookId",
            "accountId",
            "customerId",
            "vendorId",
            "projectId",
            "externalGlDimensionsJson",
            "balance",
            "journalEntryCount",
            "ledgerEntryCount",
            "sourceAsOfUtc");
        warehouseJsonDownloadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        warehouseJsonDownloadResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        warehouseJsonDownloadResponse.Content.Headers.ContentDisposition?.FileName.Should().Be("warehouse-ledger-facts-20260411160000.json");
        AssertStructuredExportAuditHeaders(warehouseJsonDownloadResponse);
        var warehouseJsonDownload = await warehouseJsonDownloadResponse.Content.ReadAsStringAsync();
        warehouseJsonDownload.Should().Contain("\"exportId\":\"warehouse-ledger-facts\"");
        warehouseJsonDownload.Should().Contain("\"generatedByPrincipalId\":\"controller.admin\"");
        warehouseJsonDownload.Should().Contain("\"generatedForCompanyId\":\"company-alpha\"");
        warehouseJsonDownload.Should().Contain("\"rows\"");
        warehouseCsvResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        warehouseCsvResponse.Content.Headers.ContentType?.MediaType.Should().Be("text/csv");
        warehouseCsvResponse.Content.Headers.ContentDisposition?.FileName.Should().Be("warehouse-ledger-facts-20260411160000.csv");
        AssertStructuredExportAuditHeaders(warehouseCsvResponse);
        var warehouseCsv = await warehouseCsvResponse.Content.ReadAsStringAsync();
        warehouseCsv.Should().StartWith("scope,accountName,accountType,symbol,financialAccountId,fundId,entityId,sleeveId,strategyId,investorId,capitalAccountId,instrumentId,taxLotId,costCenterId,counterpartyId,organizationId,portfolioId,bookId,accountId,customerId,vendorId,projectId,externalGlDimensionsJson,balance,journalEntryCount,ledgerEntryCount,sourceAsOfUtc");

        static void AssertStructuredExportAuditHeaders(HttpResponseMessage exportResponse)
        {
            exportResponse.Headers.TryGetValues("X-Meridian-Export-Id", out var exportIds).Should().BeTrue();
            exportIds.Should().ContainSingle(value => !string.IsNullOrWhiteSpace(value));
            exportResponse.Headers.TryGetValues("X-Meridian-Export-Generated-At", out var generatedAtValues).Should().BeTrue();
            var generatedAt = generatedAtValues.Should().ContainSingle().Subject;
            DateTimeOffset.TryParse(generatedAt, out _).Should().BeTrue();
            exportResponse.Headers.GetValues("X-Meridian-Export-Generated-By").Should().Contain("controller.admin");
            exportResponse.Headers.GetValues("X-Meridian-Export-Company").Should().Contain("company-alpha");
            exportResponse.Headers.GetValues("X-Meridian-Export-Groups").Should().Contain(value => value.Contains("reporting-ops", StringComparison.Ordinal));
            exportResponse.Headers.GetValues("X-Meridian-Export-Version").Should().ContainSingle(value => !string.IsNullOrWhiteSpace(value));
            exportResponse.Headers.GetValues("X-Meridian-Export-Sha256").Should().ContainSingle(value => Regex.IsMatch(value, "^[a-f0-9]{64}$"));
        }
    }

    [Fact]
    public async Task ReportingScheduleService_PersistsCanonicalBoundScheduleAndFailsClosedWithoutCertification()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var accessContext = BoundReportingScheduleAuthority("fund-controller");
        var fallbackCatalog = new DefaultReportingTemplateCatalog();
        var governedCatalog = new GovernedReportingTemplateCatalog(
            fallbackCatalog,
            new ReportTemplateRegistryService());
        var runStore = new FileReportingRunStore(
            new ReportingRunStoreOptions(Path.Combine(root, "reporting-runs")),
            NullLogger<FileReportingRunStore>.Instance);
        var orchestration = new ReportingOrchestrationService(
            governedCatalog,
            new DeterministicReportingSectionRenderer(),
            () => new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero),
            runStore);
        var scheduleStore = new FileReportingScheduleStore(
            new ReportingScheduleStoreOptions(Path.Combine(root, "reporting-schedules.json")),
            NullLogger<FileReportingScheduleStore>.Instance);
        var schedules = new ReportingScheduleService(
            orchestration,
            scheduleStore,
            governedTemplateCatalog: governedCatalog);
        var parameters = BuildCanonicalScheduleRunParameters(
            new DateOnly(2026, 5, 1),
            "2026-05");

        var created = await schedules.UpsertAsync(new ReportingScheduleUpsertRequestDto(
            "sched-investor",
            "investor-monthly-statement",
            "0 8 1 * *",
            new DateOnly(2026, 5, 1),
            new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero),
            2,
            "fund-controller",
            "Monthly investor statement close packet.",
            RunParameters: parameters),
            accessContext,
            CancellationToken.None);

        created.State.Should().Be(ReportingScheduleStateDto.Active);
        created.Template.Should().Be(new VersionedReportTemplateIdDto("investor-monthly-statement", 1));
        created.RunParameters.Should().BeEquivalentTo(parameters);
        created.AccessPolicySnapshot.Should().NotBeNull();
        ReportingScheduleService.HasValidAccessPolicySnapshot(created).Should().BeTrue();

        var reloaded = new ReportingScheduleService(
            orchestration,
            scheduleStore,
            governedTemplateCatalog: governedCatalog);
        reloaded.ListSchedules(accessContext).Should().ContainSingle(schedule =>
            schedule.ScheduleId == "sched-investor" &&
            schedule.TenantId == "tenant-a" &&
            schedule.CompanyId == "company-a" &&
            schedule.RunParameters != null &&
            schedule.RunCount == 0);

        var dueRun = () => reloaded.RunDueAsync(
            new DateTimeOffset(2026, 5, 1, 8, 5, 0, TimeSpan.Zero),
            accessContext,
            CancellationToken.None);
        await dueRun.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*readiness is unavailable*");
    }

    [Fact]
    public void Scenario_NewFundWorkspace_EmergingManagerStarterKitSeedsEditableReportingDesk()
    {
        var templateCatalog = new DefaultReportingTemplateCatalog();
        var governedCatalog = new GovernedReportingTemplateCatalog(
            templateCatalog,
            new ReportTemplateRegistryService());
        var accessContext = BoundAccessContext("fund-controller");
        var orchestration = new ReportingOrchestrationService(
            governedCatalog,
            new DeterministicReportingSectionRenderer(),
            () => new DateTimeOffset(2026, 7, 6, 16, 0, 0, TimeSpan.Zero));
        var schedules = new ReportingScheduleService(
            orchestration,
            new InMemoryReportingScheduleStore([]),
            governedTemplateCatalog: governedCatalog);
        var starterKits = new ReportingStarterKitService(
            new DefaultReportingStarterKitCatalog(),
            templateCatalog,
            schedules,
            clock: () => new DateTimeOffset(2026, 7, 6, 16, 0, 0, TimeSpan.Zero));

        var result = starterKits.Provision(
            "emerging-manager",
            "fund-controller",
            accessContext);
        var reporting = new ReportPackRunReadService(
                templateCatalog,
                scheduleService: schedules,
                starterKitService: starterKits)
            .BuildPayload(accessContext);

        result.State.IsProvisioned.Should().BeTrue();
        result.State.SelectedKitId.Should().Be("emerging-manager");
        result.State.EnabledTemplateIds.Should().BeEquivalentTo(
            "investor-monthly-statement",
            "capital-account-statement",
            "shadow-nav-daily-pack");
        result.SeededSchedules.Should().HaveCount(2);
        result.SeededSchedules.Should().OnlyContain(schedule => schedule.State == ReportingScheduleStateDto.Draft);
        result.SeededSchedules.Should().Contain(schedule =>
            schedule.ScheduleId == "starter-emerging-manager-investor-monthly" &&
            schedule.TemplateId == "investor-monthly-statement" &&
            schedule.RequestedBy == "fund-controller");
        schedules.ListSchedules(accessContext).Select(static schedule => schedule.ScheduleId).Should().BeEquivalentTo(result.State.SeedScheduleIds);
        reporting.StarterKitState.Should().NotBeNull();
        reporting.StarterKitState!.SelectedKitId.Should().Be("emerging-manager");
        reporting.Templates.Select(static template => template.TemplateId).Should().BeEquivalentTo(result.State.EnabledTemplateIds);
    }

    [Fact]
    public async Task ReportingScheduleService_PersistsTypedTargetsAndRetainsCanonicalReleaseHandoffs()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var accessContext = BoundReportingScheduleAuthority("fund-controller");
        var fallbackCatalog = new DefaultReportingTemplateCatalog();
        var governedCatalog = new GovernedReportingTemplateCatalog(
            fallbackCatalog,
            new ReportTemplateRegistryService());
        var destinationResolver = new ConfiguredReportingRecipientDestinationResolver(
        [
            new ReportingRecipientDestinationBinding(
                "tenant-a",
                "company-a",
                "fund-controller",
                "http-relay",
                "fund-controller@example.test")
        ]);
        var runStore = new FileReportingRunStore(
            new ReportingRunStoreOptions(Path.Combine(root, "reporting-runs")),
            NullLogger<FileReportingRunStore>.Instance);
        var orchestration = new ReportingOrchestrationService(
            governedCatalog,
            new DeterministicReportingSectionRenderer(),
            () => new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero),
            runStore);
        var scheduleStore = new FileReportingScheduleStore(
            new ReportingScheduleStoreOptions(Path.Combine(root, "reporting-schedules.json")),
            NullLogger<FileReportingScheduleStore>.Instance);
        var schedules = new ReportingScheduleService(
            orchestration,
            scheduleStore,
            governedTemplateCatalog: governedCatalog,
            destinationResolver: destinationResolver);
        var parameters = BuildCanonicalScheduleRunParameters(
            new DateOnly(2026, 5, 1),
            "2026-05");

        var created = await schedules.UpsertAsync(new ReportingScheduleUpsertRequestDto(
            "sched-board-distribution",
            "holdings-board-report",
            "0 8 1 * *",
            new DateOnly(2026, 5, 1),
            new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero),
            1,
            "fund-controller",
            "Monthly board packet with secure delivery.",
            DeliveryTargets:
            [
                new ReportingScheduleDeliveryTargetDto(
                    "board-reporting-committee",
                    [GovernanceReportArtifactFormatDto.Pdf],
                    ReportPackDeliveryModeDto.SecurePortal,
                    "Board portal delivery.",
                    "fund-controller",
                    ReportAccessPrincipalKindDto.User),
                new ReportingScheduleDeliveryTargetDto(
                    "investor-relations",
                    [GovernanceReportArtifactFormatDto.Pdf],
                    ReportPackDeliveryModeDto.EmailLink,
                    "Investor email-link delivery.",
                    "fund-controller",
                    ReportAccessPrincipalKindDto.User)
            ],
            RunParameters: parameters),
            accessContext,
            CancellationToken.None);

        created.DeliveryTargets.Should().HaveCount(2);
        created.DeliveryTargets.Should().OnlyContain(target =>
            target.RecipientPrincipalId == "fund-controller" &&
            target.RecipientPrincipalKind == ReportAccessPrincipalKindDto.User);
        ReportingScheduleService.HasValidDeliveryTargetsSnapshot(created).Should().BeTrue();

        var restarted = new ReportingScheduleService(
            orchestration,
            scheduleStore,
            governedTemplateCatalog: governedCatalog,
            destinationResolver: destinationResolver);
        var retained = restarted.ListSchedules(accessContext).Should().ContainSingle().Subject;
        retained.DeliveryTargets.Should().BeEquivalentTo(created.DeliveryTargets);
        ReportingScheduleService.HasValidDeliveryTargetsSnapshot(retained).Should().BeTrue();

        const string runId = "sched-board-distribution-20260501";
        var handoffs = ReportingScheduleService.BuildReleaseDeliveryHandoffs(
            retained,
            BuildCanonicalScheduledManifest(retained, runId));
        handoffs.Should().HaveCount(2).And.OnlyContain(handoff =>
            handoff.State == ReportingScheduledReleaseHandoffStateDto.PendingRelease &&
            handoff.RecipientPrincipalId == "fund-controller" &&
            handoff.RecipientPrincipalKind == ReportingAccessPrincipalKind.User.ToString() &&
            handoff.EnqueuedDeliveryJobId == null);
        handoffs.Should().Contain(handoff =>
            handoff.TargetDistributionId == "board-reporting-committee" &&
            handoff.TransportId == "secure-portal");
        handoffs.Should().Contain(handoff =>
            handoff.TargetDistributionId == "investor-relations" &&
            handoff.TransportId == "http-relay");

        scheduleStore.Save([retained with { ReleaseDeliveryHandoffs = handoffs }]);
        var reloaded = new ReportingScheduleService(
            orchestration,
            scheduleStore,
            governedTemplateCatalog: governedCatalog,
            destinationResolver: destinationResolver);
        reloaded.GetPendingReleaseHandoffs(runId, accessContext)
            .Should().BeEquivalentTo(handoffs);
    }

    [Fact]
    public void ReportingScheduleDeliveryPlans_SurfaceModeAndArtifactReadinessBlockers()
    {
        var reportId = Guid.NewGuid();
        var schedule = new ReportingScheduleRecordDto(
            "sched-board-readiness",
            "holdings-board-report",
            "0 8 1 * *",
            new DateOnly(2026, 5, 1),
            DateTimeOffset.UtcNow.AddHours(-2),
            1,
            "fund-controller",
            ReportingScheduleStateDto.Active,
            DateTimeOffset.UtcNow.AddDays(-2),
            DateTimeOffset.UtcNow.AddDays(-1),
            DeliveryTargets:
            [
                new ReportingScheduleDeliveryTargetDto(
                    "board-reporting-committee",
                    [GovernanceReportArtifactFormatDto.Pdf, GovernanceReportArtifactFormatDto.Csv],
                    ReportPackDeliveryModeDto.EvidenceVault,
                    "Invalid board vault delivery.")
            ]);
        var latestAttempt = new ReportPackDeliveryAttemptDto(
            Guid.NewGuid(),
            reportId,
            "board-reporting-committee",
            "Board reporting committee",
            "Board",
            "Board portal",
            ReportPackDeliveryStateDto.Delivered,
            DateTimeOffset.UtcNow.AddMinutes(-30),
            "fund-controller",
            1,
            "schedule:holdings-board-report:run-1:board-reporting-committee",
            Package: new ReportPackDeliveryPackageDto(
                "package-1",
                reportId,
                "board-reporting-committee",
                ReportPackDeliveryModeDto.EvidenceVault,
                "/api/fund-structure/reporting/packs/package?token=t",
                "/portal/reporting/packages/package-1",
                [GovernanceReportArtifactFormatDto.Pdf],
                [
                    new ReportPackDeliveryArtifactDto(
                        GovernanceReportArtifactFormatDto.Pdf,
                        "package-1.pdf",
                        "application/pdf",
                        "vault/report-packs/package-1.pdf",
                        128,
                        "artifact-pdf",
                        "sha256:pdf")
                ],
                DateTimeOffset.UtcNow.AddMinutes(-30),
                "vault/report-packs/package-1.json",
                IntegritySummary: "1 artifact retained."));

        var plan = ReportPackRunReadService.BuildScheduleDeliveryPlans([schedule], [latestAttempt])
            .Should()
            .ContainSingle()
            .Subject;

        plan.IsReady.Should().BeFalse();
        plan.ReadinessBlockers.Should().NotBeNull();
        plan.ReadinessBlockers!.Should().Contain("Delivery mode EvidenceVault is not compatible with Board portal for Board reporting committee.");
        plan.ReadinessBlockers.Should().Contain(blocker =>
            blocker.Contains("Latest delivery package for Board reporting committee is incomplete:", StringComparison.Ordinal) &&
            blocker.Contains("Artifact package-1.pdf requires a version stamp.", StringComparison.Ordinal) &&
            blocker.Contains("Delivery evidence packet is required.", StringComparison.Ordinal));
        plan.ReadinessBlockers.Should().Contain("Latest delivery package for Board reporting committee is missing requested artifact format(s): Csv.");
        plan.ReadinessSummary.Should().Contain("Delivery mode EvidenceVault is not compatible with Board portal");
        plan.LastDeliveryArtifactCount.Should().Be(1);
        plan.LastDeliveryIntegritySummary.Should().Be("1 artifact retained.");
    }

    [Fact]
    public void ReportPackRunReadService_ProjectsDailyCockpitWorkItems()
    {
        var now = DateTimeOffset.UtcNow;
        var workflow = new ReportPackWorkflowService();
        var companyPolicy = new ReportAccessPolicyDto(
            ReportAccessModeDto.CompanyWide,
            CompanyId: "company-a");
        var recordAccess = new ReportAccessQueryContext(
            ActorPrincipalId: "fund-accountant",
            CompanyId: "company-a",
            TenantId: "tenant-a",
            RequireBoundScope: true);
        var approval = workflow.Create(
            "fund-alpha",
            "acct-main",
            "2026-06",
            new VersionedReportTemplateIdDto("board-pack", 1),
            "fund-accountant",
            [CompleteLineProvenance("trial-balance.cash", "cash-evidence")],
            companyPolicy,
            recordAccess);
        workflow.Transition(approval.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        var blocked = workflow.Create(
            "fund-alpha",
            "acct-main",
            "2026-06",
            new VersionedReportTemplateIdDto("investor-statement", 1),
            "fund-accountant",
            [CompleteLineProvenance("trial-balance.nav", "nav-evidence")],
            companyPolicy,
            recordAccess);
        workflow.Transition(blocked.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        workflow.Reject(blocked.ReportId, "NAV tie-out evidence is missing.", "fund-controller", nameof(UserRole.Controller));

        var schedule = new ReportingScheduleRecordDto(
            "sched-board-daily",
            "board-pack",
            "0 8 * * *",
            new DateOnly(2026, 6, 30),
            now.AddHours(-1),
            1,
            "fund-accountant",
            ReportingScheduleStateDto.Active,
            now.AddDays(-2),
            now.AddDays(-1),
            DeliveryTargets:
            [
                new ReportingScheduleDeliveryTargetDto(
                    "unknown-distribution",
                    [GovernanceReportArtifactFormatDto.Pdf],
                    ReportPackDeliveryModeDto.SecurePortal)
            ],
            TenantId: "tenant-a",
            CompanyId: "company-a",
            AccessPolicySnapshot: companyPolicy,
            AccessPolicySnapshotHash: ReportingScheduleService.ComputeAccessPolicySnapshotHash(companyPolicy));
        var deliveryAttempt = new ReportPackDeliveryAttemptDto(
            Guid.NewGuid(),
            blocked.ReportId,
            "board-reporting-committee",
            "Board reporting committee",
            "Board",
            "Board portal",
            ReportPackDeliveryStateDto.Failed,
            now.AddMinutes(-30),
            "delivery-operator",
            2,
            "delivery-ref-2",
            FailureReason: "Portal upload rejected the retained package.");
        var deliveryService = new ReportPackDeliveryService(
            workflow,
            new InMemoryReportPackDeliveryRecordStore([deliveryAttempt]));
        var scheduleService = new ReportingScheduleService(
            new ReportingOrchestrationService(new DefaultReportingTemplateCatalog()),
            new InMemoryReportingScheduleStore([schedule]));

        var payload = new ReportPackRunReadService(
            new DefaultReportingTemplateCatalog(),
            workflowService: workflow,
            deliveryService: deliveryService,
            scheduleService: scheduleService)
            .BuildPayload(recordAccess with { ActorPrincipalId = "report-viewer" });

        payload.DailyWork.Should().NotBeNullOrEmpty();
        payload.DailyWork!.Should().Contain(item =>
            item.Kind == "approval-needed" &&
            item.Title.Contains("board-pack", StringComparison.OrdinalIgnoreCase));
        payload.DailyWork.Should().Contain(item =>
            item.Kind == "blocked-package" &&
            item.Detail == "NAV tie-out evidence is missing.");
        payload.DailyWork.Should().Contain(item =>
            item.Kind == "delivery-failure" &&
            item.Detail == "Portal upload rejected the retained package.");
        payload.DailyWork.Should().Contain(item =>
            item.Kind == "due-package" &&
            item.WorkItemId == "due-package:sched-board-daily");
        payload.DailyWork.Should().Contain(item =>
            item.Kind == "evidence-gap" &&
            item.Title == "Readiness warning: board-pack" &&
            item.EvidenceGaps != null &&
            item.EvidenceGaps.Count > 0);
    }

    [Fact]
    public void ReportingPayload_SourceBackedPortfolioCuts_SurfaceExposureCashPnlAndShadowNav()
    {
        var workflow = new ReportPackWorkflowService();
        var created = workflow.Create(
            "fund-alpha",
            "acct-main",
            "2026-05",
            new VersionedReportTemplateIdDto("shadow-nav-pack", 1),
            "report.author",
            [
                PortfolioReportingLine("portfolio.gross-exposure", "evidence-gross", "2500000", "/evidence/gross"),
                PortfolioReportingLine("portfolio.net-exposure", "evidence-net", "1800000"),
                PortfolioReportingLine("portfolio.long-market-value", "evidence-long", "2200000"),
                PortfolioReportingLine("portfolio.short-market-value", "evidence-short", "-400000"),
                PortfolioReportingLine("portfolio.cash", "evidence-cash", "375000"),
                PortfolioReportingLine("portfolio.pending-settlement", "evidence-settlement", "12500"),
                PortfolioReportingLine("portfolio.realized-pnl", "evidence-realized", "42000"),
                PortfolioReportingLine("portfolio.unrealized-pnl", "evidence-unrealized", "18000"),
                PortfolioReportingLine("portfolio.shadow-nav", "evidence-shadow-nav", "2935000"),
                PortfolioReportingLine("portfolio.reported-nav", "evidence-reported-nav", "2920000")
            ]);
        workflow.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        workflow.Transition(created.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
        workflow.Publish(
            created.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:portfolio-cut",
            "manifest-portfolio",
            "vault/report-packs/manifest-portfolio.json",
            CompleteLineProvenanceEvidenceLinks(
                "evidence-gross",
                "evidence-net",
                "evidence-long",
                "evidence-short",
                "evidence-cash",
                "evidence-settlement",
                "evidence-realized",
                "evidence-unrealized",
                "evidence-shadow-nav",
                "evidence-reported-nav"));

        var payload = new ReportPackRunReadService(
            new DefaultReportingTemplateCatalog(),
            workflowService: workflow).BuildPayload();

        payload.PortfolioCuts.Should().NotBeNull().And.ContainSingle();
        var cut = payload.PortfolioCuts!.Single();
        cut.CutId.Should().Be("reporting-portfolio-cut:fund-alpha");
        cut.GrossExposure.Should().Be(2_500_000m);
        cut.NetExposure.Should().Be(1_800_000m);
        cut.LongMarketValue.Should().Be(2_200_000m);
        cut.ShortMarketValue.Should().Be(-400_000m);
        cut.TotalCash.Should().Be(375_000m);
        cut.PendingSettlement.Should().Be(12_500m);
        cut.RealizedPnl.Should().Be(42_000m);
        cut.UnrealizedPnl.Should().Be(18_000m);
        cut.TotalPnl.Should().Be(60_000m);
        cut.ShadowNav.Should().Be(2_935_000m);
        cut.ShadowNavVariance.Should().Be(15_000m);
        cut.SourceCount.Should().Be(10);
        cut.EvidenceRoute.Should().Be("/evidence/gross");
        cut.Tags.Should().Contain("fund:fund-alpha");
        cut.Tags.Should().Contain("template:shadow-nav-pack");

        payload.LivePortfolioViews.Should().NotBeNull().And.ContainSingle();
        var liveView = payload.LivePortfolioViews!.Single();
        liveView.State.Should().Be(PortfolioReportingLiveViewStateDto.SourceBacked);
        liveView.TotalCash.Should().Be(375_000m);
        liveView.ShadowNav.Should().Be(2_935_000m);
        liveView.IsMarketTickLinked.Should().BeFalse();
        liveView.MarketDataProvider.Should().Be("retained-portfolio-snapshot");
        liveView.MarketTickAsOfUtc.Should().NotBeNull();
        liveView.MarketTickSequence.Should().Be(liveView.MarketTickAsOfUtc!.Value.ToUnixTimeMilliseconds());
        liveView.MarketTickAgeSeconds.Should().BeGreaterThanOrEqualTo(0);
        liveView.TickFreshnessSummary.Should().Contain("Source-backed tick snapshot");
        liveView.FreshnessPolicy.Should().NotBeNull();
        liveView.FreshnessPolicy!.PolicyName.Should().Be("RetainedReportPackProvenance");
        liveView.FreshnessPolicy.SourceAgeSeconds.Should().Be(0);
        liveView.FreshnessPolicy.IsWithinLiveLinkWindow.Should().BeFalse();
        liveView.FreshnessPolicy.IsBeyondStaleWindow.Should().BeFalse();
        liveView.FreshnessPolicy.Reason.Should().Contain("not classified as live-linked market telemetry");

        payload.CrossFundConsolidations.Should().NotBeNull().And.ContainSingle();
        payload.CrossFundConsolidations!.Single().FundCount.Should().Be(1);
        payload.CrossFundConsolidations.Single().TotalPnl.Should().Be(60_000m);

        payload.PnlSlices.Should().NotBeNull().And.HaveCount(4);
        payload.PnlSlices!.Should().Contain(slice =>
            slice.Period == PortfolioReportingPnlSlicePeriodDto.Daily &&
            slice.RealizedPnl == 42_000m &&
            slice.UnrealizedPnl == 18_000m &&
            slice.TotalPnl == 60_000m);

        payload.AnalyticsRows.Should().NotBeNull();
        payload.AnalyticsRows!.Should().Contain(row =>
            row.Kind == PortfolioReportingAnalyticsKindDto.TopWinner &&
            row.Scope == PortfolioReportingAnalyticsScopeDto.Strategy &&
            row.TotalPnl == 60_000m &&
            row.ContributionPercent == 100m);
        payload.AnalyticsRows.Should().Contain(row =>
            row.Kind == PortfolioReportingAnalyticsKindDto.Contribution &&
            row.HeatMapIntensity == 100m &&
            row.Tags.Contains("analytics:Contribution"));

        payload.BrandingThemes.Should().NotBeNull();
        payload.BrandingThemes!.Select(static theme => theme.ThemeId).Should().Contain("meridian-standard");
        payload.StructuredExports.Should().NotBeNull();
        payload.StructuredExports!.Should().Contain(export =>
            export.ExportId == "investment-portfolio-cuts" &&
            export.Purpose == StructuredReportingExportPurposeDto.InvestmentDecision &&
            export.RowCount == 1 &&
            export.SourceCount == 10 &&
            export.IsReady &&
            export.Route.Contains("/api/workstation/reporting/structured-exports/investment-portfolio-cuts", StringComparison.Ordinal));
        payload.StructuredExports.Should().Contain(export =>
            export.ExportId == "investment-topn-contribution-analytics" &&
            export.RowCount == payload.AnalyticsRows.Count &&
            export.Tags!.Contains("top-n"));
        payload.StructuredExports.Should().Contain(export =>
            export.ExportId == "regulatory-trial-balance" &&
            export.Purpose == StructuredReportingExportPurposeDto.Regulatory &&
            export.IsReady);

        static ReportPackLineProvenanceDto PortfolioReportingLine(
            string lineKey,
            string evidenceId,
            string reportValue,
            string? href = null) =>
            new(
                lineKey,
                "portfolio",
                "run-1",
                evidenceId,
                RunId: "run-1",
                LedgerEntryId: "ledger-entry-1",
                ReconciliationCaseId: "case-1",
                ReportValue: reportValue,
                SourceSessionId: "provider-session-1",
                ReconciliationRunId: "recon-run-1",
                ProviderEventId: "provider-event-1",
                SecurityMasterId: "security-1",
                SecurityDefinitionId: "definition-1",
                ReconciliationOutcome: "matched",
                ApprovalId: "approval-1",
                FinancialRecordHref: href);
    }

    [Fact]
    public async Task ReportingScheduleService_PersistsGeneratedReportWriterConfigurationWithoutInlineDelivery()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var authorScope = BoundReportingScheduleAuthority(
            "report-owner",
            ["investor-relations"]);
        var approverScope = BoundReportingScheduleAuthority(
            "ops-lead",
            isAdmin: true);
        var registry = new ReportTemplateRegistryService();
        var draft = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "scheduled-report-writer-pack",
                "Scheduled Report Writer Pack",
                Sections: [],
                Parameters: [],
                Family: ReportingTemplateFamily.CustomReport.ToString(),
                Rationale: "No-code custom report pack for scheduled investor delivery.",
                AccessPolicy: new ReportAccessPolicyDto(
                    ReportAccessModeDto.Restricted,
                    Principals:
                    [
                        new ReportAccessPrincipalDto(
                            ReportAccessPrincipalKindDto.Group,
                            "investor-relations")
                    ]),
                Grids:
                [
                    new ReportWriterGridDefinitionDto(
                        "sector-pivot",
                        "Sector Pivot",
                        ReportWriterGridKindDto.Pivot,
                        RowFields: ["sector"],
                        Metrics:
                        [
                            new ReportWriterMetricDefinitionDto(
                                "marketValue",
                                "marketValue",
                                ReportWriterAggregateFunctionDto.Sum,
                                "Market value")
                        ])
                ]),
            "report-owner",
            "company-a",
            tenantId: "tenant-a");
        registry.Submit(
            draft.Definition.TemplateId,
            "report-owner",
            "Ready for scheduled delivery.",
            authorScope);
        registry.Approve(
            draft.Definition.TemplateId,
            new ReportTemplateDecisionRequestDto(
                "Approved for scheduled investor distribution.",
                "approval:schedule-report-writer"),
            "ops-lead",
            approverScope);

        var catalog = new GovernedReportingTemplateCatalog(
            new DefaultReportingTemplateCatalog(),
            registry);
        var orchestration = new ReportingOrchestrationService(
            catalog,
            new DeterministicReportingSectionRenderer(),
            () => new DateTimeOffset(2026, 5, 2, 8, 0, 0, TimeSpan.Zero));
        var scheduleStore = new FileReportingScheduleStore(
            new ReportingScheduleStoreOptions(Path.Combine(root, "reporting-schedules.json")),
            NullLogger<FileReportingScheduleStore>.Instance);
        var destinationResolver = new ConfiguredReportingRecipientDestinationResolver(
        [
            new ReportingRecipientDestinationBinding(
                "tenant-a",
                "company-a",
                "investor-relations",
                "http-relay",
                "investor-relations@example.test",
                ReportingAccessPrincipalKind.Group)
        ]);
        var schedules = new ReportingScheduleService(
            orchestration,
            scheduleStore,
            governedTemplateCatalog: catalog,
            destinationResolver: destinationResolver);
        var brandingTheme = new ReportBrandingThemeDto(
            "allocator-quarterly",
            "Allocator Quarterly",
            "Northstar Capital",
            "#123456",
            "#C99700",
            "#111827",
            "#FFFFFF",
            "https://assets.example/northstar.svg",
            "Generated for Northstar Capital investors.",
            "Confidential investor reporting pack.",
            IsBuiltIn: false);
        var parameters = BuildCanonicalScheduleRunParameters(
            new DateOnly(2026, 5, 2),
            "2026-05");

        var created = await schedules.UpsertAsync(
            new ReportingScheduleUpsertRequestDto(
                "sched-custom-writer",
                draft.Definition.TemplateId.Name,
                "0 8 1 * *",
                new DateOnly(2026, 5, 2),
                new DateTimeOffset(2026, 5, 2, 8, 0, 0, TimeSpan.Zero),
                0,
                "report-owner",
                "Scheduled no-code report writer pack.",
                DeliveryTargets:
                [
                    new ReportingScheduleDeliveryTargetDto(
                        "investor-relations",
                        [GovernanceReportArtifactFormatDto.Pdf],
                        ReportPackDeliveryModeDto.EmailLink,
                        "Investor email-link package from generated report run.",
                        "investor-relations",
                        ReportAccessPrincipalKindDto.Group)
                ],
                DatasetSourceId: "portfolio-reporting-cuts",
                BrandingThemeId: brandingTheme.ThemeId,
                BrandingThemeOverride: brandingTheme,
                Template: draft.Definition.TemplateId,
                RunParameters: parameters),
            authorScope,
            CancellationToken.None);

        created.DatasetRows.Should().BeNull();
        created.DatasetSourceId.Should().Be("portfolio-reporting-cuts");
        created.BrandingThemeOverride.Should().BeEquivalentTo(brandingTheme);
        created.DeliveryTargets.Should().ContainSingle(target =>
            target.RecipientPrincipalId == "investor-relations"
            && target.RecipientPrincipalKind == ReportAccessPrincipalKindDto.Group);
        ReportingScheduleService.HasValidDeliveryTargetsSnapshot(created).Should().BeTrue();
        created.ReleaseDeliveryHandoffs.Should().BeNullOrEmpty(
            "scheduled generation must retain a post-release handoff rather than deliver inline");

        var reloaded = new ReportingScheduleService(
            orchestration,
            scheduleStore,
            governedTemplateCatalog: catalog,
            destinationResolver: destinationResolver)
            .ListSchedules(authorScope)
            .Should().ContainSingle().Subject;
        reloaded.Template.Should().Be(draft.Definition.TemplateId);
        reloaded.RunParameters.Should().BeEquivalentTo(parameters);
        reloaded.DatasetSourceId.Should().Be("portfolio-reporting-cuts");
        reloaded.DeliveryTargets.Should().BeEquivalentTo(created.DeliveryTargets);
        ReportingScheduleService.HasValidAccessPolicySnapshot(reloaded).Should().BeTrue();
        ReportingScheduleService.HasValidDeliveryTargetsSnapshot(reloaded).Should().BeTrue();
    }
    [Fact]
    public async Task ReportingRunCommandService_RejectsRestatementAuthorizationOutsideGovernedWorkflow()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var runStore = new FileReportingRunStore(
            new ReportingRunStoreOptions(Path.Combine(root, "reporting-runs")),
            NullLogger<FileReportingRunStore>.Instance);
        var catalog = new DefaultReportingTemplateCatalog();
        var orchestration = new ReportingOrchestrationService(
            catalog,
            new DeterministicReportingSectionRenderer(),
            () => new DateTimeOffset(2026, 5, 4, 9, 0, 0, TimeSpan.Zero),
            runStore);
        var service = new ReportingRunCommandService(
            orchestration,
            catalog,
            readinessService: LegacyDraftReadiness(catalog));

        var initial = await service.RunAsync(
            new ReportingRunRequestDto("investor-monthly-statement", new DateOnly(2026, 5, 4), JobId: "adhoc-restate"),
            "fund-controller",
            CancellationToken.None);
        (await orchestration.TransitionApprovalAsync(initial.Run.RunId, ReportingRunStatus.InReview, "rev", "Reviewer", "review", CancellationToken.None)).Should().BeTrue();
        (await orchestration.TransitionApprovalAsync(initial.Run.RunId, ReportingRunStatus.Approved, "cmp", "ComplianceOfficer", "approve", CancellationToken.None)).Should().BeTrue();
        (await orchestration.TransitionApprovalAsync(initial.Run.RunId, ReportingRunStatus.Released, "ops", "OperationsLead", "release", CancellationToken.None)).Should().BeTrue();

        // Default request (no restatement authorization) must be rejected once the series is released.
        var blocked = async () => await service.RunAsync(
            new ReportingRunRequestDto("investor-monthly-statement", new DateOnly(2026, 5, 4), JobId: "adhoc-restate"),
            "fund-controller",
            CancellationToken.None);
        await blocked.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Released manifest*");

        // Ordinary report generation cannot authorize restatement; callers must use the governed request workflow.
        var restatement = async () => await service.RunAsync(
            new ReportingRunRequestDto(
                "investor-monthly-statement",
                new DateOnly(2026, 5, 4),
                JobId: "adhoc-restate",
                RetryReason: "Q2 NAV correction",
                AllowRestatement: true),
            "fund-controller",
            CancellationToken.None);

        await restatement.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*governed restatement-request workflow*");
    }

    [Fact]
    public async Task ReportingRunCommandService_RunsAdHocReportsOnDemand()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var runStore = new FileReportingRunStore(
            new ReportingRunStoreOptions(Path.Combine(root, "reporting-runs")),
            NullLogger<FileReportingRunStore>.Instance);
        var catalog = new DefaultReportingTemplateCatalog();
        var orchestration = new ReportingOrchestrationService(
            catalog,
            new DeterministicReportingSectionRenderer(),
            () => new DateTimeOffset(2026, 5, 4, 9, 0, 0, TimeSpan.Zero),
            runStore);
        var service = new ReportingRunCommandService(
            orchestration,
            catalog,
            readinessService: LegacyDraftReadiness(catalog));

        var result = await service.RunAsync(
            new ReportingRunRequestDto(
                "investor-monthly-statement",
                new DateOnly(2026, 5, 4),
                JobId: "adhoc-investor"),
            "fund-controller",
            CancellationToken.None);

        result.Run.RunId.Should().Be("adhoc-investor-20260504");
        result.Run.TemplateId.Should().Be("investor-monthly-statement");
        result.Run.Trigger.Should().Be(ReportingRunTrigger.AdHoc.ToString());
        result.Run.AsOfDate.Should().Be("2026-05-04");
        result.Run.Status.Should().Be(ReportingRunStatus.Draft.ToString());
        result.Run.NextActions.Should().ContainSingle(action =>
            action.Kind == "migration-required" &&
            action.Method == "GET" &&
            action.Href == string.Empty &&
            !action.IsEnabled &&
            !action.IsBrowserNavigable);
        runStore.GetManifest(result.Run.RunId)!.Trigger.Should().Be(ReportingRunTrigger.AdHoc);
        runStore.GetAudit(result.Run.RunId).Select(static audit => audit.Action).Should().Contain("RunGenerated");
    }

    [Fact]
    public async Task ReportingRunCommandService_ResolvesSourceBackedDatasetRowsForReportWriterRuns()
    {
        var workflow = new ReportPackWorkflowService();
        var published = workflow.Create(
            "fund-alpha",
            "acct-main",
            "2026-05",
            new VersionedReportTemplateIdDto("shadow-nav-pack", 1),
            "report.author",
            [
                SourcePortfolioReportingLine("portfolio.gross-exposure", "evidence-gross", "2500000", "/evidence/gross"),
                SourcePortfolioReportingLine("portfolio.net-exposure", "evidence-net", "1800000"),
                SourcePortfolioReportingLine("portfolio.cash", "evidence-cash", "375000"),
                SourcePortfolioReportingLine("portfolio.realized-pnl", "evidence-realized", "42000"),
                SourcePortfolioReportingLine("portfolio.unrealized-pnl", "evidence-unrealized", "18000"),
                SourcePortfolioReportingLine("portfolio.shadow-nav", "evidence-shadow-nav", "2935000"),
                SourcePortfolioReportingLine("portfolio.reported-nav", "evidence-reported-nav", "2920000")
            ]);
        workflow.Transition(published.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        workflow.Transition(published.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
        workflow.Publish(
            published.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:source-backed",
            "manifest-source-backed",
            "vault/report-packs/manifest-source-backed.json",
            CompleteLineProvenanceEvidenceLinks(
                "evidence-gross",
                "evidence-net",
                "evidence-cash",
                "evidence-realized",
                "evidence-unrealized",
                "evidence-shadow-nav",
                "evidence-reported-nav"));

        var registry = new ReportTemplateRegistryService();
        var draft = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "source-backed-portfolio-grid",
                "Source-backed Portfolio Grid",
                Sections: [],
                Parameters: [],
                Family: ReportingTemplateFamily.CustomReport.ToString(),
                Rationale: "Render retained portfolio reporting rows without pasted data.",
                Grids:
                [
                    new ReportWriterGridDefinitionDto(
                        "portfolio-source-grid",
                        "Portfolio Source Grid",
                        ReportWriterGridKindDto.Pivot,
                        RowFields: ["kind"],
                        Metrics:
                        [
                            new ReportWriterMetricDefinitionDto("grossExposure", "grossExposure", ReportWriterAggregateFunctionDto.Sum, "Gross exposure"),
                            new ReportWriterMetricDefinitionDto("totalPnl", "totalPnl", ReportWriterAggregateFunctionDto.Sum, "Total P&L"),
                            new ReportWriterMetricDefinitionDto("shadowNav", "shadowNav", ReportWriterAggregateFunctionDto.Sum, "Shadow NAV")
                        ],
                        Formulas:
                        [
                            new ReportWriterFormulaDefinitionDto("pnlOnGrossPct", "{totalPnl} / total(grossExposure) * 100", "P&L / gross %")
                        ],
                        Filters:
                        [
                            new ReportWriterFilterDefinitionDto("dataset", ReportWriterFilterOperatorDto.Equals, "portfolio-cut", "Portfolio cuts only")
                        ])
                ]),
            "report.author");
        registry.Submit(draft.Definition.TemplateId, "report.author", "Ready for source-backed run.");
        registry.Approve(
            draft.Definition.TemplateId,
            new ReportTemplateDecisionRequestDto("Approved for retained portfolio reporting rows.", "approval:source-backed-grid"),
            "ops-lead");

        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var catalog = new GovernedReportingTemplateCatalog(new DefaultReportingTemplateCatalog(), registry);
        var runStore = new FileReportingRunStore(
            new ReportingRunStoreOptions(Path.Combine(root, "reporting-runs")),
            NullLogger<FileReportingRunStore>.Instance);
        var orchestration = new ReportingOrchestrationService(
            catalog,
            new DeterministicReportingSectionRenderer(),
            () => new DateTimeOffset(2026, 5, 6, 12, 0, 0, TimeSpan.Zero),
            runStore);
        var datasetSource = new ReportWriterDatasetSourceService(workflow);
        var commandService = new ReportingRunCommandService(
            orchestration,
            catalog,
            catalog,
            datasetSource,
            readinessService: LegacyDraftReadiness(catalog, catalog));

        var result = await commandService.RunAsync(
            new ReportingRunRequestDto(
                "source-backed-portfolio-grid",
                new DateOnly(2026, 5, 6),
                JobId: "adhoc-source-backed",
                DatasetSourceId: "portfolio-reporting-cuts"),
            "report.author",
            CancellationToken.None);

        result.Run.RunId.Should().Be("adhoc-source-backed-20260506");
        result.Run.ReportWriterDatasetSourceId.Should().Be("portfolio-reporting-cuts");
        result.Run.ReportWriterDatasetSourceLabel.Should().Be("Portfolio reporting cuts");
        result.Run.ReportWriterDatasetRowCount.Should().Be(1);
        var manifest = runStore.GetManifest(result.Run.RunId);
        manifest.Should().NotBeNull();
        manifest!.ReportWriterDatasetSourceId.Should().Be("portfolio-reporting-cuts");
        manifest.ReportWriterDatasetSourceLabel.Should().Be("Portfolio reporting cuts");
        manifest.ReportWriterDatasetRowCount.Should().Be(1);
        var renderedGrid = manifest.RenderedReportWriterGrids.Should().ContainSingle().Subject;
        renderedGrid.GridId.Should().Be("portfolio-source-grid");
        renderedGrid.Lineage.Should().NotBeNull();
        renderedGrid.Lineage!.FilteredInputRowCount.Should().Be(1);
        renderedGrid.Lineage.InputRowCount.Should().Be(1);
        renderedGrid.Warnings.Should().NotContain(warning => warning.Contains("no dataset rows", StringComparison.OrdinalIgnoreCase));
        var renderedRow = renderedGrid.Rows.Should().ContainSingle().Subject;
        renderedRow.Values["kind"].Should().Be(PortfolioReportingCutKindDto.Fund.ToString());
        renderedRow.Values["grossExposure"].Should().Be("2500000");
        renderedRow.Values["totalPnl"].Should().Be("60000");
        renderedRow.Values["shadowNav"].Should().Be("2935000");
        renderedRow.Values["pnlOnGrossPct"].Should().Be("2.4");
    }
    [Fact]
    public async Task ReportingScheduleService_PersistsBoundSourceBackedDatasetSelection()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var authorScope = BoundReportingScheduleAuthority("report.author");
        var approverScope = BoundReportingScheduleAuthority("ops-lead", isAdmin: true);
        var workflow = new ReportPackWorkflowService();
        var sourcePack = workflow.Create(
            "fund-alpha",
            "acct-main",
            "2026-05",
            new VersionedReportTemplateIdDto("shadow-nav-pack", 1),
            "report.author",
            [
                SourcePortfolioReportingLine(
                    "portfolio.gross-exposure",
                    "evidence-gross",
                    "1250000",
                    "/evidence/gross"),
                SourcePortfolioReportingLine(
                    "portfolio.realized-pnl",
                    "evidence-realized",
                    "15000"),
                SourcePortfolioReportingLine(
                    "portfolio.unrealized-pnl",
                    "evidence-unrealized",
                    "10000"),
                SourcePortfolioReportingLine(
                    "portfolio.shadow-nav",
                    "evidence-shadow-nav",
                    "1260000"),
                SourcePortfolioReportingLine(
                    "portfolio.reported-nav",
                    "evidence-reported-nav",
                    "1250000")
            ],
            accessContext: authorScope);
        workflow.Transition(
            sourcePack.ReportId,
            ReportPackWorkflowStateDto.InReview,
            "reviewer",
            "reviewer");
        workflow.Transition(
            sourcePack.ReportId,
            ReportPackWorkflowStateDto.Approved,
            "approver",
            "approver");
        workflow.Publish(
            sourcePack.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:scheduled-source-backed",
            "manifest-scheduled-source-backed",
            "vault/report-packs/manifest-scheduled-source-backed.json",
            CompleteLineProvenanceEvidenceLinks(
                "evidence-gross",
                "evidence-realized",
                "evidence-unrealized",
                "evidence-shadow-nav",
                "evidence-reported-nav"));

        var registry = new ReportTemplateRegistryService();
        var draft = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "scheduled-source-backed-grid",
                "Scheduled Source-backed Grid",
                Sections: [],
                Parameters: [],
                Family: ReportingTemplateFamily.CustomReport.ToString(),
                Rationale: "Retain a server-owned portfolio dataset selection for a scheduled grid.",
                Grids:
                [
                    new ReportWriterGridDefinitionDto(
                        "scheduled-portfolio-source-grid",
                        "Scheduled Portfolio Source Grid",
                        ReportWriterGridKindDto.Pivot,
                        RowFields: ["kind"],
                        Metrics:
                        [
                            new ReportWriterMetricDefinitionDto("grossExposure", "grossExposure", ReportWriterAggregateFunctionDto.Sum, "Gross exposure"),
                            new ReportWriterMetricDefinitionDto("totalPnl", "totalPnl", ReportWriterAggregateFunctionDto.Sum, "Total P&L"),
                            new ReportWriterMetricDefinitionDto("shadowNav", "shadowNav", ReportWriterAggregateFunctionDto.Sum, "Shadow NAV")
                        ])
                ]),
            "report.author",
            "company-a",
            tenantId: "tenant-a");
        registry.Submit(
            draft.Definition.TemplateId,
            "report.author",
            "Ready for scheduled source-backed run.",
            authorScope);
        registry.Approve(
            draft.Definition.TemplateId,
            new ReportTemplateDecisionRequestDto(
                "Approved for retained scheduled portfolio rows.",
                "approval:scheduled-source-grid"),
            "ops-lead",
            approverScope);

        var catalog = new GovernedReportingTemplateCatalog(
            new DefaultReportingTemplateCatalog(),
            registry);
        var orchestration = new ReportingOrchestrationService(
            catalog,
            new DeterministicReportingSectionRenderer(),
            () => new DateTimeOffset(2026, 5, 7, 8, 0, 0, TimeSpan.Zero));
        var scheduleStore = new FileReportingScheduleStore(
            new ReportingScheduleStoreOptions(Path.Combine(root, "reporting-schedules.json")),
            NullLogger<FileReportingScheduleStore>.Instance);
        var schedules = new ReportingScheduleService(
            orchestration,
            scheduleStore,
            governedTemplateCatalog: catalog,
            datasetSourceService: new ReportWriterDatasetSourceService(workflow));
        var parameters = BuildCanonicalScheduleRunParameters(
            new DateOnly(2026, 5, 7),
            "2026-05");

        var created = await schedules.UpsertAsync(
            new ReportingScheduleUpsertRequestDto(
                "sched-source-backed",
                draft.Definition.TemplateId.Name,
                "0 8 1 * *",
                new DateOnly(2026, 5, 7),
                new DateTimeOffset(2026, 5, 7, 8, 0, 0, TimeSpan.Zero),
                0,
                "report.author",
                "Scheduled source-backed report-writer run.",
                DeliveryTargets:
                [
                    new ReportingScheduleDeliveryTargetDto(
                        "fund-operations",
                        [GovernanceReportArtifactFormatDto.Pdf],
                        ReportPackDeliveryModeDto.SecurePortal,
                        "Retained internal operations target.",
                        "report.author",
                        ReportAccessPrincipalKindDto.User)
                ],
                DatasetSourceId: "portfolio-reporting-cuts",
                Template: draft.Definition.TemplateId,
                RunParameters: parameters),
            authorScope,
            CancellationToken.None);

        created.DatasetRows.Should().BeNull();
        created.DatasetSourceId.Should().Be("portfolio-reporting-cuts");
        created.DeliveryTargets.Should().ContainSingle(target =>
            target.RecipientPrincipalId == "report.author"
            && target.RecipientPrincipalKind == ReportAccessPrincipalKindDto.User);
        ReportingScheduleService.HasValidAccessPolicySnapshot(created).Should().BeTrue();
        ReportingScheduleService.HasValidDeliveryTargetsSnapshot(created).Should().BeTrue();

        var reloaded = new ReportingScheduleService(
            orchestration,
            scheduleStore,
            governedTemplateCatalog: catalog,
            datasetSourceService: new ReportWriterDatasetSourceService(workflow))
            .ListSchedules(authorScope)
            .Should().ContainSingle().Subject;
        reloaded.Template.Should().Be(draft.Definition.TemplateId);
        reloaded.RunParameters.Should().BeEquivalentTo(parameters);
        reloaded.DatasetRows.Should().BeNull();
        reloaded.DatasetSourceId.Should().Be("portfolio-reporting-cuts");
        reloaded.DeliveryTargets.Should().BeEquivalentTo(created.DeliveryTargets);
    }
    [Fact]
    public void ReportPackRunReadService_ListsRegistryTemplateDraftsAndApprovals()
    {
        var registry = new ReportTemplateRegistryService();
        var draft = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "custom-board-pack",
                "Custom Board Pack",
                ["cover", "exposures"],
                [new ReportTemplateParameterDefinitionDto("period", Required: true)],
                Family: "BoardPack",
                Rationale: "Controller-specific exposure section"),
            "report.author");
        var revision = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "investor-monthly-statement",
                "Investor Monthly Statement v2",
                ["cover", "performance", "positions", "fees"],
                [new ReportTemplateParameterDefinitionDto("period", Required: true)],
                Family: "InvestorStatement",
                BasedOnVersion: 1,
                Rationale: "Add fee disclosure"),
            "report.author");
        registry.Submit(revision.Definition.TemplateId, "report.author", "ready for controller review");
        registry.Approve(
            revision.Definition.TemplateId,
            new ReportTemplateDecisionRequestDto("Controller approved fee disclosure", "APP-TPL-2"),
            "controller.admin");

        var payload = new ReportPackRunReadService(
            new DefaultReportingTemplateCatalog(),
            templateRegistry: registry).BuildPayload();

        payload.Templates.Should().Contain(template =>
            template.TemplateId == draft.Definition.TemplateId.Name &&
            template.Version == "1" &&
            template.LifecycleStatus == ReportTemplateLifecycleStatusDto.Draft.ToString() &&
            !template.IsBuiltIn &&
            !template.IsLatestApproved &&
            template.AuthoringRoute == "/api/fund-structure/reporting/templates/custom-board-pack/versions/1");
        var approvedRevision = payload.Templates.Should().ContainSingle(template =>
            template.TemplateId == "investor-monthly-statement" &&
            template.Version == "2" &&
            template.Family == "InvestorStatement" &&
            template.LifecycleStatus == ReportTemplateLifecycleStatusDto.Approved.ToString() &&
            !template.IsBuiltIn &&
            template.IsLatestApproved &&
            template.ApprovalSummary.Contains("APP-TPL-2", StringComparison.Ordinal)).Which;
        approvedRevision.BasedOnTemplateId.Should().Be(new VersionedReportTemplateIdDto("investor-monthly-statement", 1));
        approvedRevision.CreatedBy.Should().Be("report.author");
        approvedRevision.SubmittedBy.Should().Be("report.author");
        approvedRevision.ApprovedBy.Should().Be("controller.admin");
        approvedRevision.DecisionRationale.Should().Be("Controller approved fee disclosure");
        approvedRevision.ApprovalReference.Should().Be("APP-TPL-2");
        approvedRevision.ValidationIssues.Should().BeEmpty();
        approvedRevision.AuditTrail.Should().NotBeNull();
        approvedRevision.AuditTrail!.Select(static entry => entry.Action).Should().ContainInOrder("draft", "submit", "approve");
        payload.Templates.Should().Contain(template =>
            template.TemplateId == "investor-monthly-statement" &&
            template.Version == "1" &&
            template.IsBuiltIn &&
            !template.IsLatestApproved);
    }

    [Fact]
    public void ReportPackRunReadService_ProjectsReportWriterGridMetadataForCustomTemplates()
    {
        var workflow = new ReportPackWorkflowService();
        var sourceBackedPack = workflow.Create(
            "fund-alpha",
            "acct-main",
            "2026-05",
            new VersionedReportTemplateIdDto("shadow-nav-pack", 1),
            "report.author",
            [
                SourcePortfolioReportingLine("portfolio.gross-exposure", "evidence-gross", "2500000", "/evidence/gross"),
                SourcePortfolioReportingLine("portfolio.realized-pnl", "evidence-realized", "42000"),
                SourcePortfolioReportingLine("portfolio.unrealized-pnl", "evidence-unrealized", "18000"),
                SourcePortfolioReportingLine("portfolio.shadow-nav", "evidence-shadow-nav", "2935000"),
                SourcePortfolioReportingLine("portfolio.reported-nav", "evidence-reported-nav", "2920000")
            ]);
        workflow.Transition(sourceBackedPack.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        workflow.Transition(sourceBackedPack.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
        workflow.Publish(
            sourceBackedPack.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:source-backed",
            "manifest-source-backed",
            "vault/report-packs/manifest-source-backed.json",
            CompleteLineProvenanceEvidenceLinks(
                "evidence-gross",
                "evidence-realized",
                "evidence-unrealized",
                "evidence-shadow-nav",
                "evidence-reported-nav"));
        var registry = new ReportTemplateRegistryService();
        var draft = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "custom-exposure-grid",
                "Custom Exposure Grid",
                ["exposures"],
                [new ReportTemplateParameterDefinitionDto("period", Required: true)],
                Family: "CustomReport",
                Rationale: "Expose no-code grid",
                Grids:
                [
                    new ReportWriterGridDefinitionDto(
                        "strategy-contribution",
                        "Strategy Contribution",
                        ReportWriterGridKindDto.Contribution,
                        RowFields: ["strategy"],
                        Metrics: [new ReportWriterMetricDefinitionDto("marketValue", "marketValue")],
                        Formulas: [new ReportWriterFormulaDefinitionDto("weightCheck", "{contributionPercent}")],
                        Filters:
                        [
                            new ReportWriterFilterDefinitionDto(
                                "strategy",
                                ReportWriterFilterOperatorDto.Equals,
                                "Core",
                                "Core strategy")
                        ]
                    )
                ]),
            "report.author");

        var payload = new ReportPackRunReadService(
            new DefaultReportingTemplateCatalog(),
            workflowService: workflow,
            templateRegistry: registry).BuildPayload();

        var template = payload.Templates.Single(template => template.TemplateId == draft.Definition.TemplateId.Name);
        template.ReportWriterGrids.Should().ContainSingle();
        template.ReportWriterGrids![0].GridId.Should().Be("strategy-contribution");
        template.ReportWriterGrids[0].Kind.Should().Be(ReportWriterGridKindDto.Contribution.ToString());
        template.ReportWriterGrids[0].DimensionCount.Should().Be(1);
        template.ReportWriterGrids[0].MetricCount.Should().Be(1);
        template.ReportWriterGrids[0].FormulaCount.Should().Be(1);
        template.ReportWriterGrids[0].RowFields.Should().ContainSingle().Which.Should().Be("strategy");
        template.ReportWriterGrids[0].ColumnFields.Should().BeEmpty();
        template.ReportWriterGrids[0].Metrics.Should().ContainSingle(metric =>
            metric.Name == "marketValue" &&
            metric.SourceField == "marketValue" &&
            metric.Function == ReportWriterAggregateFunctionDto.Sum.ToString());
        template.ReportWriterGrids[0].Formulas.Should().ContainSingle(formula =>
            formula.Name == "weightCheck" &&
            formula.Expression == "{contributionPercent}");
        template.ReportWriterGrids[0].Filters.Should().ContainSingle(filter =>
            filter.Field == "strategy" &&
            filter.Operator == ReportWriterFilterOperatorDto.Equals.ToString() &&
            filter.Value == "Core" &&
            filter.Label == "Core strategy");
        template.ReportWriterGrids[0].SourceFields.Should().NotBeNull();
        template.ReportWriterGrids[0].SourceFields!.Should().Contain(field =>
            field.Name == "grossExposure" &&
            field.Label == "Gross exposure" &&
            field.Role == "metric" &&
            field.DataType == "decimal" &&
            field.Dataset == "Portfolio cuts");
        template.ReportWriterGrids[0].SourceFields.Should().Contain(field =>
            field.Name == "totalPnl" &&
            field.Role == "metric" &&
            field.DataType == "decimal");
        template.ReportWriterGrids[0].SourceFields.Should().Contain(field =>
            field.Name == "shadowNav" &&
            field.Role == "metric" &&
            field.DataType == "decimal");
        template.ReportWriterGrids[0].SourceFields.Should().Contain(field =>
            field.Name == "contributionPercent" &&
            field.Role == "generated" &&
            field.DataType == "decimal");
    }

    [Fact]
    public void ReportAccessPolicyEvaluator_MatchesUserGroupCompanyAndOwnerPrincipals()
    {
        var policy = new ReportAccessPolicyDto(
            ReportAccessModeDto.Restricted,
            OwnerPrincipalId: "owner.user",
            Principals:
            [
                new ReportAccessPrincipalDto(ReportAccessPrincipalKindDto.User, "report.user"),
                new ReportAccessPrincipalDto(ReportAccessPrincipalKindDto.Group, "ops-control"),
                new ReportAccessPrincipalDto(ReportAccessPrincipalKindDto.Company, "company-a")
            ]);

        ReportAccessPolicyEvaluator.Evaluate(policy, new ReportAccessQueryContext("owner.user")).IsAccessible.Should().BeTrue();
        ReportAccessPolicyEvaluator.Evaluate(policy, new ReportAccessQueryContext("report.user")).IsAccessible.Should().BeTrue();
        ReportAccessPolicyEvaluator.Evaluate(policy, new ReportAccessQueryContext("viewer.user", ["ops-control"])).IsAccessible.Should().BeTrue();
        ReportAccessPolicyEvaluator.Evaluate(policy, new ReportAccessQueryContext("viewer.user", CompanyId: "company-a")).IsAccessible.Should().BeTrue();
        ReportAccessPolicyEvaluator.Evaluate(policy, new ReportAccessQueryContext("viewer.user", ["unrelated"])).IsAccessible.Should().BeFalse();
    }

    [Fact]
    public async Task ReportPackRunReadService_FiltersTemplatesAndPacksByAccessPolicy()
    {
        var ownerContext = BoundReportingScheduleAuthority("owner.user", ["ops-control"]);
        var registry = new ReportTemplateRegistryService();
        var privateTemplate = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "owner-only-pack",
                "Owner Only Pack",
                ["summary"],
                [new ReportTemplateParameterDefinitionDto("period", Required: true)],
                Family: "CustomReport",
                Rationale: "User-locked report",
                AccessPolicy: new ReportAccessPolicyDto(ReportAccessModeDto.Private, OwnerPrincipalId: "owner.user")),
            "owner.user",
            "company-a",
            tenantId: "tenant-a");
        var groupTemplate = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "ops-control-pack",
                "Ops Control Pack",
                ["summary"],
                [new ReportTemplateParameterDefinitionDto("period", Required: true)],
                Family: "CustomReport",
                Rationale: "Ops control report",
                AccessPolicy: new ReportAccessPolicyDto(
                    ReportAccessModeDto.Restricted,
                    Principals: [new ReportAccessPrincipalDto(ReportAccessPrincipalKindDto.Group, "ops-control")])),
            "owner.user",
            "company-a",
            tenantId: "tenant-a");
        var workflow = new ReportPackWorkflowService();
        var privatePack = workflow.Create(
            "fund-a",
            "acct-1",
            "2026-03",
            privateTemplate.Definition.TemplateId,
            "owner.user",
            accessPolicy: new ReportAccessPolicyDto(ReportAccessModeDto.Private, OwnerPrincipalId: "owner.user"),
            accessContext: ownerContext);
        var groupPack = workflow.Create(
            "fund-a",
            "acct-1",
            "2026-03",
            groupTemplate.Definition.TemplateId,
            "owner.user",
            accessPolicy: new ReportAccessPolicyDto(
                ReportAccessModeDto.Restricted,
                Principals: [new ReportAccessPrincipalDto(ReportAccessPrincipalKindDto.Group, "ops-control")]),
            accessContext: ownerContext);
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var governedCatalog = new GovernedReportingTemplateCatalog(
            new DefaultReportingTemplateCatalog(),
            registry);
        var scheduleStore = new FileReportingScheduleStore(
            new ReportingScheduleStoreOptions(Path.Combine(root, "reporting-schedules.json")),
            NullLogger<FileReportingScheduleStore>.Instance);
        var destinationResolver = new ConfiguredReportingRecipientDestinationResolver(
        [
            new ReportingRecipientDestinationBinding(
                "tenant-a",
                "company-a",
                "owner.user",
                "http-relay",
                "owner.user@example.test")
        ]);
        var schedules = new ReportingScheduleService(
            new ReportingOrchestrationService(governedCatalog),
            scheduleStore,
            governedTemplateCatalog: governedCatalog,
            destinationResolver: destinationResolver);
        var parameters = BuildCanonicalScheduleRunParameters(
            new DateOnly(2026, 4, 1),
            "2026-03");
        await schedules.UpsertAsync(
            new ReportingScheduleUpsertRequestDto(
                "sched-owner-only",
                privateTemplate.Definition.TemplateId.Name,
                "0 8 1 * *",
                new DateOnly(2026, 4, 1),
                new DateTimeOffset(2026, 4, 1, 8, 0, 0, TimeSpan.Zero),
                0,
                "owner.user",
                Description: "Private owner schedule.",
                DeliveryTargets:
                [
                    new ReportingScheduleDeliveryTargetDto(
                        "investor-relations",
                        [GovernanceReportArtifactFormatDto.Pdf],
                        ReportPackDeliveryModeDto.EmailLink,
                        "Owner delivery.",
                        "owner.user",
                        ReportAccessPrincipalKindDto.User)
                ],
                Template: privateTemplate.Definition.TemplateId,
                RunParameters: parameters),
            ownerContext,
            CancellationToken.None);
        await schedules.UpsertAsync(
            new ReportingScheduleUpsertRequestDto(
                "sched-ops-control",
                groupTemplate.Definition.TemplateId.Name,
                "0 8 1 * *",
                new DateOnly(2026, 4, 1),
                new DateTimeOffset(2026, 4, 1, 8, 0, 0, TimeSpan.Zero),
                0,
                "owner.user",
                Description: "Restricted operations schedule.",
                DeliveryTargets:
                [
                    new ReportingScheduleDeliveryTargetDto(
                        "board-reporting-committee",
                        [GovernanceReportArtifactFormatDto.Pdf],
                        ReportPackDeliveryModeDto.SecurePortal,
                        "Operations control delivery.",
                        "ops-control",
                        ReportAccessPrincipalKindDto.Group)
                ],
                Template: groupTemplate.Definition.TemplateId,
                RunParameters: parameters),
            ownerContext,
            CancellationToken.None);
        var groupDeliveryAttemptId = Guid.NewGuid();
        var privateDeliveryAttemptId = Guid.NewGuid();
        var genericRunDeliveryAttemptId = Guid.NewGuid();
        var genericRunReportId = Guid.NewGuid();
        var deliveryStore = new FileReportPackDeliveryRecordStore(
            new ReportPackDeliveryStoreOptions(Path.Combine(root, "report-pack-deliveries.json")),
            NullLogger<FileReportPackDeliveryRecordStore>.Instance);
        deliveryStore.Save(
        [
            new ReportPackDeliveryAttemptDto(
                privateDeliveryAttemptId,
                privatePack.ReportId,
                "investor-relations",
                "Investor relations",
                "Investor communications",
                "Investor portal",
                ReportPackDeliveryStateDto.Delivered,
                new DateTimeOffset(2026, 3, 21, 9, 0, 0, TimeSpan.Zero),
                "owner.user",
                1,
                "delivery:private-owner-only"),
            new ReportPackDeliveryAttemptDto(
                groupDeliveryAttemptId,
                groupPack.ReportId,
                "board-reporting-committee",
                "Board reporting committee",
                "Board",
                "Board portal",
                ReportPackDeliveryStateDto.Delivered,
                new DateTimeOffset(2026, 3, 21, 9, 5, 0, TimeSpan.Zero),
                "owner.user",
                1,
                "delivery:ops-control"),
            new ReportPackDeliveryAttemptDto(
                genericRunDeliveryAttemptId,
                genericRunReportId,
                "board-reporting-committee",
                "Board reporting committee",
                "Board",
                "Board portal",
                ReportPackDeliveryStateDto.Delivered,
                new DateTimeOffset(2026, 3, 21, 9, 10, 0, TimeSpan.Zero),
                "owner.user",
                1,
                $"schedule:{groupTemplate.Definition.TemplateId.Name}:sched-ops-control-20260401:board-reporting-committee",
                Package: new ReportPackDeliveryPackageDto(
                    "pkg-generic-ops-control",
                    genericRunReportId,
                    "board-reporting-committee",
                    ReportPackDeliveryModeDto.SecurePortal,
                    "/portal/reporting/packages/pkg-generic-ops-control?token=token",
                    "/reporting/runs/sched-ops-control-20260401/packages/pkg-generic-ops-control",
                    [GovernanceReportArtifactFormatDto.Pdf],
                    [],
                    new DateTimeOffset(2026, 3, 21, 9, 10, 0, TimeSpan.Zero),
                    "workstation/reporting/runs/sched-ops-control-20260401/deliveries/board-reporting-committee/1/manifest.json",
                    IntegritySummary: "1 artifact retained.",
                    ReportingRunId: "sched-ops-control-20260401",
                    ReportingTemplateId: groupTemplate.Definition.TemplateId.Name,
                    ReportingScheduleId: "sched-ops-control",
                    SourceArtifacts: ["sched-ops-control-20260401.manifest.json"]))
        ]);
        var delivery = new ReportPackDeliveryService(workflow, deliveryStore);

        var readService = new ReportPackRunReadService(
            new DefaultReportingTemplateCatalog(),
            workflowService: workflow,
            templateRegistry: registry,
            deliveryService: delivery,
            scheduleService: schedules);
        var groupPayload = readService.BuildPayload(BoundReportingScheduleAuthority("viewer.user", ["ops-control"]));
        var strangerPayload = readService.BuildPayload(BoundReportingScheduleAuthority("viewer.user"));

        groupPayload.Templates.Select(static template => template.TemplateId).Should().Contain(groupTemplate.Definition.TemplateId.Name);
        groupPayload.Templates.Select(static template => template.TemplateId).Should().NotContain(privateTemplate.Definition.TemplateId.Name);
        groupPayload.RecentRuns.Select(static run => run.RunId).Should().Contain($"report-pack:{groupPack.ReportId:D}");
        groupPayload.RecentRuns.Select(static run => run.RunId).Should().NotContain($"report-pack:{privatePack.ReportId:D}");
        groupPayload.Templates.Single(template => template.TemplateId == groupTemplate.Definition.TemplateId.Name).AccessMode.Should().Be(ReportAccessModeDto.Restricted.ToString());
        groupPayload.Schedules.Should().NotBeNull();
        groupPayload.Schedules!.Select(static schedule => schedule.ScheduleId).Should().Contain("sched-ops-control");
        groupPayload.Schedules.Select(static schedule => schedule.ScheduleId).Should().NotContain("sched-owner-only");
        groupPayload.ScheduleDeliveryPlans.Should().NotBeNull();
        groupPayload.ScheduleDeliveryPlans!.Select(static plan => plan.ScheduleId).Should().Contain("sched-ops-control");
        groupPayload.ScheduleDeliveryPlans.Select(static plan => plan.ScheduleId).Should().NotContain("sched-owner-only");
        groupPayload.DeliveryAttempts.Should().NotBeNull().And.BeEmpty(
            "template visibility alone cannot establish tenant, company, and immutable run authority");
        System.Text.Json.JsonSerializer.Serialize(groupPayload)
            .Should().NotContain("token=");
        System.Text.Json.JsonSerializer.Serialize(groupPayload)
            .Should().NotContain("/portal/reporting/packages/");
        groupPayload.ScheduleDeliveryPlans.Single(plan => plan.ScheduleId == "sched-ops-control").LastDeliveryAttemptId.Should().BeNull();
        groupPayload.AccessAudit.Should().NotBeNull();
        groupPayload.AccessAudit!.EvaluationScope.Should().Be("CallerScoped");
        groupPayload.AccessAudit.PrincipalScopes.Should().Contain("user:viewer.user");
        groupPayload.AccessAudit.PrincipalScopes.Should().Contain("group:ops-control");
        groupPayload.AccessAudit.VisibleTemplateCount.Should().Be(groupPayload.Templates.Count);
        groupPayload.AccessAudit.HiddenTemplateCount.Should().Be(1);
        groupPayload.AccessAudit.VisibleReportPackCount.Should().Be(1);
        groupPayload.AccessAudit.HiddenReportPackCount.Should().Be(1);
        groupPayload.AccessAudit.VisibleScheduleCount.Should().Be(1);
        groupPayload.AccessAudit.HiddenScheduleCount.Should().Be(1);
        groupPayload.AccessAudit.VisibleDeliveryAttemptCount.Should().Be(0);
        groupPayload.AccessAudit.HiddenDeliveryAttemptCount.Should().Be(0);
        groupPayload.AccessAudit.DenialReasons.Should().Contain(reason =>
            reason.Contains("report templates", StringComparison.OrdinalIgnoreCase));
        groupPayload.StructuredExports.Should().NotBeNull();
        groupPayload.StructuredExports!.Should().Contain(export =>
            export.ExportId == "regulatory-trial-balance" &&
            export.IsReady &&
            export.Route.Contains("/api/workstation/reporting/structured-exports/regulatory-trial-balance", StringComparison.Ordinal));
        groupPayload.StructuredExports.Should().Contain(export =>
            export.ExportId == "warehouse-ledger-facts" &&
            export.IsReady);

        strangerPayload.Templates.Select(static template => template.TemplateId).Should().NotContain(groupTemplate.Definition.TemplateId.Name);
        strangerPayload.Templates.Select(static template => template.TemplateId).Should().NotContain(privateTemplate.Definition.TemplateId.Name);
        strangerPayload.RecentRuns.Select(static run => run.RunId).Should().NotContain($"report-pack:{groupPack.ReportId:D}");
        strangerPayload.RecentRuns.Select(static run => run.RunId).Should().NotContain($"report-pack:{privatePack.ReportId:D}");
        strangerPayload.Schedules.Should().NotBeNull().And.BeEmpty();
        strangerPayload.ScheduleDeliveryPlans.Should().NotBeNull().And.BeEmpty();
        strangerPayload.DeliveryAttempts.Should().NotBeNull().And.BeEmpty();
        strangerPayload.StructuredExports.Should().NotBeNull().And.BeEmpty();
        strangerPayload.AccessAudit.Should().NotBeNull();
        strangerPayload.AccessAudit!.VisibleTemplateCount.Should().Be(strangerPayload.Templates.Count);
        strangerPayload.AccessAudit.HiddenTemplateCount.Should().Be(2);
        strangerPayload.AccessAudit.VisibleReportPackCount.Should().Be(0);
        strangerPayload.AccessAudit.HiddenReportPackCount.Should().Be(2);
        strangerPayload.AccessAudit.VisibleScheduleCount.Should().Be(0);
        strangerPayload.AccessAudit.HiddenScheduleCount.Should().Be(2);
        strangerPayload.AccessAudit.VisibleDeliveryAttemptCount.Should().Be(0);
        strangerPayload.AccessAudit.HiddenDeliveryAttemptCount.Should().Be(0);
        strangerPayload.AccessAudit.VisibleStructuredExportCount.Should().Be(0);
        strangerPayload.AccessAudit.HiddenStructuredExportCount.Should().BeGreaterThan(0);

        var securityMaster = new NullSecurityMasterQueryService();
        var workspaceService = new FundOperationsWorkspaceReadService(
            new InMemoryFundAccountService(),
            new StrategyRunStore(),
            new PortfolioReadService(),
            new NavAttributionService(securityMaster),
            new ReportGenerationService(securityMaster),
            reportPackWorkflowService: workflow,
            reportPackDeliveryService: delivery,
            reportingScheduleService: schedules,
            reportPackRunReadService: readService);
        var groupWorkspace = await workspaceService.GetWorkspaceAsync(
            new FundOperationsWorkspaceQuery("fund-a", new DateTimeOffset(2026, 3, 21, 10, 0, 0, TimeSpan.Zero), "USD"),
            new ReportAccessQueryContext("viewer.user", ["ops-control"]));
        var strangerWorkspace = await workspaceService.GetWorkspaceAsync(
            new FundOperationsWorkspaceQuery("fund-a", new DateTimeOffset(2026, 3, 21, 10, 0, 0, TimeSpan.Zero), "USD"),
            new ReportAccessQueryContext("viewer.user"));

        groupWorkspace.Reporting.Schedules.Should().NotBeNull();
        groupWorkspace.Reporting.Schedules!.Select(static schedule => schedule.ScheduleId).Should().Contain("sched-ops-control");
        groupWorkspace.Reporting.Schedules.Select(static schedule => schedule.ScheduleId).Should().NotContain("sched-owner-only");
        groupWorkspace.Reporting.DeliveryAttempts.Should().NotBeNull().And.BeEmpty();
        groupWorkspace.Reporting.ScheduleDeliveryPlans.Should().NotBeNull();
        groupWorkspace.Reporting.ScheduleDeliveryPlans!.Select(static plan => plan.ScheduleId).Should().Contain("sched-ops-control");
        groupWorkspace.Reporting.AccessAudit.Should().NotBeNull();
        groupWorkspace.Reporting.AccessAudit!.VisibleTemplateCount.Should().Be(groupPayload.AccessAudit.VisibleTemplateCount);
        groupWorkspace.Reporting.AccessAudit.HiddenTemplateCount.Should().Be(1);
        groupWorkspace.Reporting.AccessAudit.HiddenDeliveryAttemptCount.Should().Be(0);
        strangerWorkspace.Reporting.Schedules.Should().NotBeNull().And.BeEmpty();
        strangerWorkspace.Reporting.DeliveryAttempts.Should().NotBeNull().And.BeEmpty();
        strangerWorkspace.Reporting.ScheduleDeliveryPlans.Should().NotBeNull().And.BeEmpty();
        strangerWorkspace.Reporting.AccessAudit.Should().NotBeNull();
        strangerWorkspace.Reporting.AccessAudit!.VisibleTemplateCount.Should().Be(strangerPayload.AccessAudit.VisibleTemplateCount);
        strangerWorkspace.Reporting.AccessAudit.HiddenTemplateCount.Should().Be(2);
    }

    [Fact]
    public void CreateDraft_WithCallerContext_DefaultsCompanyAndReportGroupPrincipals()
    {
        var registry = new ReportTemplateRegistryService();

        var companyWide = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "company-default-pack",
                "Company Default Pack",
                ["summary"],
                [new ReportTemplateParameterDefinitionDto("period", Required: true)],
                Family: "CustomReport",
                Rationale: "Company default report"),
            "owner.user",
            companyId: "company-alpha",
            reportGroupPrincipalIds: ["reporting-ops"],
            tenantId: "tenant-alpha");
        var restricted = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "group-default-pack",
                "Group Default Pack",
                ["summary"],
                [new ReportTemplateParameterDefinitionDto("period", Required: true)],
                Family: "CustomReport",
                Rationale: "Group default report",
                AccessPolicy: new ReportAccessPolicyDto(ReportAccessModeDto.Restricted)),
            "owner.user",
            companyId: "company-alpha",
            reportGroupPrincipalIds: ["reporting-ops"],
            tenantId: "tenant-alpha");

        companyWide.Definition.AccessPolicy!.OwnerPrincipalId.Should().Be("owner.user");
        companyWide.Definition.AccessPolicy.CompanyId.Should().Be("company-alpha");
        restricted.Definition.AccessPolicy!.CompanyId.Should().Be("company-alpha");
        restricted.Definition.AccessPolicy.Principals.Should().ContainSingle(principal =>
            principal.Kind == ReportAccessPrincipalKindDto.Group &&
            principal.PrincipalId == "reporting-ops");
    }

    [Fact]
    public async Task Endpoint_RenderPrivateTemplate_WhenCallerIsNotOwner_ReturnsForbidden()
    {
        await using var app = await CreateFundStructureAppAsync(UserRole.Analysis, "viewer.user");
        var registry = app.Services.GetRequiredService<ReportTemplateRegistryService>();
        var draft = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "private-investor-pack",
                "Private Investor Pack",
                ["summary"],
                [new ReportTemplateParameterDefinitionDto("period", Required: true)],
                Family: "InvestorStatement",
                Rationale: "Private report test",
                AccessPolicy: new ReportAccessPolicyDto(ReportAccessModeDto.Private, OwnerPrincipalId: "owner.user")),
            "owner.user",
            companyId: TestCompanyId,
            tenantId: TestTenantId);
        registry.Submit(draft.Definition.TemplateId, "owner.user", "ready", BoundAccessContext("owner.user"));
        registry.Approve(draft.Definition.TemplateId, new ReportTemplateDecisionRequestDto("approved", "APP-PRIVATE-1"), "controller.admin", BoundAccessContext("controller.admin"));
        var client = app.GetTestClient();

        var listResponse = await client.GetAsync("/api/fund-structure/reporting/templates");
        var renderResponse = await client.PostAsJsonAsync(
            "/api/fund-structure/reporting/templates/render",
            new RenderReportTemplateRequestDto(
                draft.Definition.TemplateId,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["period"] = "2026-03" }),
            ServerJsonOptions);

        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var records = await listResponse.Content.ReadFromJsonAsync<IReadOnlyList<ReportTemplateGovernanceRecordDto>>(ServerJsonOptions);
        records.Should().NotBeNull();
        records!.Select(static record => record.Definition.TemplateId.Name).Should().NotContain(draft.Definition.TemplateId.Name);
        renderResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Endpoint_RenderRestrictedTemplate_WhenRoleProfileMatchesGroup_AllowsRender()
    {
        await using var app = await CreateFundStructureAppAsync(
            UserRole.Analysis,
            "viewer.user",
            roleProfileName: "ops-control");
        var registry = app.Services.GetRequiredService<ReportTemplateRegistryService>();
        var draft = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "ops-control-template",
                "Ops Control Template",
                ["summary"],
                [new ReportTemplateParameterDefinitionDto("period", Required: true)],
                Family: "CustomReport",
                Rationale: "Restricted group report test",
                AccessPolicy: new ReportAccessPolicyDto(
                    ReportAccessModeDto.Restricted,
                    Principals: [new ReportAccessPrincipalDto(ReportAccessPrincipalKindDto.Group, "ops-control")])),
            "owner.user",
            companyId: TestCompanyId,
            tenantId: TestTenantId);
        registry.Submit(draft.Definition.TemplateId, "owner.user", "ready", BoundAccessContext("owner.user"));
        registry.Approve(draft.Definition.TemplateId, new ReportTemplateDecisionRequestDto("approved", "APP-GROUP-1"), "controller.admin", BoundAccessContext("controller.admin"));
        var client = app.GetTestClient();

        var listResponse = await client.GetAsync("/api/fund-structure/reporting/templates");
        var renderResponse = await client.PostAsJsonAsync(
            "/api/fund-structure/reporting/templates/render",
            new RenderReportTemplateRequestDto(
                draft.Definition.TemplateId,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["period"] = "2026-03" }),
            ServerJsonOptions);

        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var records = await listResponse.Content.ReadFromJsonAsync<IReadOnlyList<ReportTemplateGovernanceRecordDto>>(ServerJsonOptions);
        records.Should().NotBeNull();
        records!.Select(static record => record.Definition.TemplateId.Name).Should().Contain(draft.Definition.TemplateId.Name);
        renderResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Endpoint_RenderRestrictedTemplate_WhenCompanyMatchesSession_AllowsRender()
    {
        await using var app = await CreateFundStructureAppAsync(
            UserRole.Analysis,
            "viewer.user",
            companyId: "company-alpha");
        var registry = app.Services.GetRequiredService<ReportTemplateRegistryService>();
        var draft = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "company-alpha-template",
                "Company Alpha Template",
                ["summary"],
                [new ReportTemplateParameterDefinitionDto("period", Required: true)],
                Family: "CustomReport",
                Rationale: "Restricted company report test",
                AccessPolicy: new ReportAccessPolicyDto(
                    ReportAccessModeDto.Restricted,
                    Principals: [new ReportAccessPrincipalDto(ReportAccessPrincipalKindDto.Company, "company-alpha")])),
            "owner.user",
            companyId: "company-alpha",
            tenantId: TestTenantId);
        registry.Submit(draft.Definition.TemplateId, "owner.user", "ready", BoundAccessContext("owner.user", "company-alpha"));
        registry.Approve(draft.Definition.TemplateId, new ReportTemplateDecisionRequestDto("approved", "APP-COMPANY-1"), "controller.admin", BoundAccessContext("controller.admin", "company-alpha"));
        var client = app.GetTestClient();

        var listResponse = await client.GetAsync("/api/fund-structure/reporting/templates");
        var renderResponse = await client.PostAsJsonAsync(
            "/api/fund-structure/reporting/templates/render",
            new RenderReportTemplateRequestDto(
                draft.Definition.TemplateId,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["period"] = "2026-03" }),
            ServerJsonOptions);

        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var records = await listResponse.Content.ReadFromJsonAsync<IReadOnlyList<ReportTemplateGovernanceRecordDto>>(ServerJsonOptions);
        records.Should().NotBeNull();
        records!.Select(static record => record.Definition.TemplateId.Name).Should().Contain(draft.Definition.TemplateId.Name);
        renderResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Endpoint_RunRestrictedCustomTemplate_WhenRoleProfileMatchesGroup_CreatesAdHocRun()
    {
        await using var app = await CreateFundStructureAppAsync(
            UserRole.ReportingAnalyst,
            "viewer.user",
            roleProfileName: "ops-control");
        var registry = app.Services.GetRequiredService<ReportTemplateRegistryService>();
        var draft = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "ops-control-run-template",
                "Ops Control Run Template",
                ["summary"],
                [new ReportTemplateParameterDefinitionDto("period", Required: true)],
                Family: "CustomReport",
                Rationale: "Restricted group run test",
                Grids:
                [
                    new ReportWriterGridDefinitionDto(
                        "strategy-pivot",
                        "Strategy Pivot",
                        ReportWriterGridKindDto.Pivot,
                        RowFields: ["strategy"],
                        Metrics: [new ReportWriterMetricDefinitionDto("marketValue", "marketValue")])
                ],
                AccessPolicy: new ReportAccessPolicyDto(
                    ReportAccessModeDto.Restricted,
                    Principals: [new ReportAccessPrincipalDto(ReportAccessPrincipalKindDto.Group, "ops-control")])),
            "owner.user",
            companyId: TestCompanyId,
            tenantId: TestTenantId);
        registry.Submit(draft.Definition.TemplateId, "owner.user", "ready", BoundAccessContext("owner.user"));
        registry.Approve(draft.Definition.TemplateId, new ReportTemplateDecisionRequestDto("approved", "APP-RUN-1"), "controller.admin", BoundAccessContext("controller.admin"));
        var client = app.GetTestClient();

        var runResponse = await client.PostAsJsonAsync(
            "/api/fund-structure/reporting/runs",
            new ReportingRunRequestDto(
                draft.Definition.TemplateId.Name,
                new DateOnly(2026, 5, 5),
                JobId: "adhoc-custom-grid"),
            ServerJsonOptions);

        runResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await runResponse.Content.ReadFromJsonAsync<ReportingRunResultDto>(ServerJsonOptions);
        result.Should().NotBeNull();
        result!.Run.RunId.Should().Be("adhoc-custom-grid-20260505");
        result.Run.TemplateId.Should().Be(draft.Definition.TemplateId.Name);
        result.Run.Family.Should().Be(ReportingTemplateFamily.CustomReport.ToString());
        result.Run.Trigger.Should().Be(ReportingRunTrigger.AdHoc.ToString());
        result.Run.SectionCount.Should().BeGreaterThan(0);
        result.Run.LineageLinkedSections.Should().Be(result.Run.SectionCount);
    }

    [Fact]
    public async Task Endpoint_ReportingRunAuditTrail_ReturnsRetainedActorTimestampAndNotesForAccessibleRun()
    {
        await using var app = await CreateFundStructureAppAsync(UserRole.Admin);
        var registry = app.Services.GetRequiredService<ReportTemplateRegistryService>();
        var draft = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "audit-visible-template",
                "Audit Visible Template",
                ["summary"],
                [],
                Family: "CustomReport",
                Rationale: "Expose retained run audit trail details",
                Grids:
                [
                    new ReportWriterGridDefinitionDto(
                        "audit-source-grid",
                        "Audit Source Grid",
                        ReportWriterGridKindDto.Pivot,
                        RowFields: ["kind"],
                        Metrics:
                        [
                            new ReportWriterMetricDefinitionDto("grossExposure", "grossExposure", ReportWriterAggregateFunctionDto.Sum, "Gross exposure")
                        ])
                ]),
            "report.author");
        registry.Submit(draft.Definition.TemplateId, "report.author", "ready");
        registry.Approve(draft.Definition.TemplateId, new ReportTemplateDecisionRequestDto("approved", "APP-RUN-AUDIT-1"), "controller.admin");
        var client = app.GetTestClient();

        var runResponse = await client.PostAsJsonAsync(
            "/api/fund-structure/reporting/runs",
            new ReportingRunRequestDto(
                draft.Definition.TemplateId.Name,
                new DateOnly(2026, 5, 5),
                JobId: "audit-visible-run",
                DatasetSourceId: "portfolio-reporting-cuts"),
            ServerJsonOptions);

        runResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var run = await runResponse.Content.ReadFromJsonAsync<ReportingRunResultDto>(ServerJsonOptions);
        run.Should().NotBeNull();
        run!.Run.DrilldownLinks.Should().Contain(link =>
            link.Kind == "audit" &&
            link.Href == "/api/fund-structure/reporting/runs/audit-visible-run-20260505/audit" &&
            link.IsBrowserNavigable);

        var auditResponse = await client.GetAsync($"/api/fund-structure/reporting/runs/{run.Run.RunId}/audit");

        auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var audit = await auditResponse.Content.ReadFromJsonAsync<ReportingRunAuditTrailDto>(ServerJsonOptions);
        audit.Should().NotBeNull();
        audit!.RunId.Should().Be("audit-visible-run-20260505");
        audit.TemplateId.Should().Be(draft.Definition.TemplateId.Name);
        audit.AsOfDate.Should().Be("2026-05-05");
        audit.Status.Should().Be(ReportingRunStatus.Draft.ToString());
        audit.Trigger.Should().Be(ReportingRunTrigger.AdHoc.ToString());
        audit.AttemptCount.Should().Be(1);
        audit.ReportWriterDatasetSourceId.Should().Be("portfolio-reporting-cuts");
        audit.ReportWriterDatasetSourceLabel.Should().Be("Portfolio reporting cuts");
        audit.ReportWriterDatasetRowCount.Should().Be(0);
        audit.Entries.Should().ContainSingle(entry =>
            entry.RunId == audit.RunId &&
            entry.Action == "RunGenerated" &&
            entry.Actor == "controller.admin" &&
            entry.TimestampUtc == new DateTimeOffset(2026, 5, 5, 9, 0, 0, TimeSpan.Zero) &&
            entry.Notes.Contains("trigger=AdHoc", StringComparison.Ordinal) &&
            entry.Notes.Contains("reportWriterDatasetSource=portfolio-reporting-cuts", StringComparison.Ordinal) &&
            entry.Notes.Contains("lineageSections=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Endpoint_ReportingRunAuditTrail_WhenCallerCannotAccessTemplate_ReturnsForbidden()
    {
        await using var app = await CreateFundStructureAppAsync(UserRole.ReportingAnalyst, "viewer.user");
        var registry = app.Services.GetRequiredService<ReportTemplateRegistryService>();
        var draft = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "private-audit-run-template",
                "Private Audit Run Template",
                ["summary"],
                [],
                Family: "CustomReport",
                Rationale: "Retained run audit trails must inherit template access policy",
                AccessPolicy: new ReportAccessPolicyDto(ReportAccessModeDto.Private, OwnerPrincipalId: "owner.user")),
            "owner.user");
        registry.Submit(draft.Definition.TemplateId, "owner.user", "ready");
        registry.Approve(draft.Definition.TemplateId, new ReportTemplateDecisionRequestDto("approved", "APP-RUN-AUDIT-PRIVATE-1"), "controller.admin");

        var orchestration = app.Services.GetRequiredService<IReportingOrchestrationService>();
        var manifest = await orchestration.ExecuteAsync(
            new ReportingJobContract(
                "private-audit-run",
                draft.Definition.TemplateId.Name,
                new DateOnly(2026, 5, 5),
                ReportingRunTrigger.AdHoc,
                0,
                "owner.user",
                new DateTimeOffset(2026, 5, 5, 9, 0, 0, TimeSpan.Zero)),
            CancellationToken.None);
        var client = app.GetTestClient();

        var auditResponse = await client.GetAsync($"/api/fund-structure/reporting/runs/{manifest.RunId}/audit");

        auditResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Endpoint_ReportWriterGridArtifact_ReturnsJsonCsvPdfAndXlsxForRetainedRunGrid()
    {
        await using var app = await CreateFundStructureAppAsync(UserRole.Admin);
        var registry = app.Services.GetRequiredService<ReportTemplateRegistryService>();
        var draft = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "retained-grid-artifact-template",
                "Retained Grid Artifact Template",
                ["summary"],
                [],
                Family: "CustomReport",
                Rationale: "Expose retained no-code grid artifacts",
                Grids:
                [
                    new ReportWriterGridDefinitionDto(
                        "sector-pnl",
                        "Sector P&L",
                        ReportWriterGridKindDto.Pivot,
                        RowFields: ["sector"],
                        Metrics:
                        [
                            new ReportWriterMetricDefinitionDto("pnl", "pnl"),
                            new ReportWriterMetricDefinitionDto("marketValue", "marketValue")
                        ],
                        Formulas: [new ReportWriterFormulaDefinitionDto("returnPct", "{pnl} / {marketValue} * 100")])
                ]),
            "report.author",
            companyId: TestCompanyId,
            tenantId: TestTenantId);
        registry.Submit(draft.Definition.TemplateId, "report.author", "ready", BoundAccessContext("report.author"));
        registry.Approve(draft.Definition.TemplateId, new ReportTemplateDecisionRequestDto("approved", "APP-GRID-ARTIFACT-1"), "controller.admin", BoundAccessContext("controller.admin"));
        var client = app.GetTestClient();

        var runResponse = await client.PostAsJsonAsync(
            "/api/fund-structure/reporting/runs",
            new ReportingRunRequestDto(
                draft.Definition.TemplateId.Name,
                new DateOnly(2026, 5, 5),
                JobId: "retained-grid",
                DatasetRows:
                [
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["sector"] = "Technology",
                        ["pnl"] = "250",
                        ["marketValue"] = "10000"
                    },
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["sector"] = "Credit",
                        ["pnl"] = "-25",
                        ["marketValue"] = "5000"
                    }
                ]),
            ServerJsonOptions);

        runResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var run = await runResponse.Content.ReadFromJsonAsync<ReportingRunResultDto>(ServerJsonOptions);
        run.Should().NotBeNull();
        run!.Run.GeneratedReportWriterGrids.Should().ContainSingle(grid => grid.GridId == "sector-pnl");

        var jsonResponse = await client.GetAsync($"/api/fund-structure/reporting/runs/{run.Run.RunId}/report-writer-grids/sector-pnl");
        var csvResponse = await client.GetAsync($"/api/fund-structure/reporting/runs/{run.Run.RunId}/report-writer-grids/sector-pnl?format=csv");
        var xlsAliasResponse = await client.GetAsync($"/api/fund-structure/reporting/runs/{run.Run.RunId}/report-writer-grids/sector-pnl?format=xls");
        var xlsxResponse = await client.GetAsync($"/api/fund-structure/reporting/runs/{run.Run.RunId}/report-writer-grids/sector-pnl?format=xlsx");
        var pdfResponse = await client.GetAsync($"/api/fund-structure/reporting/runs/{run.Run.RunId}/report-writer-grids/sector-pnl?format=pdf");

        jsonResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        jsonResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        var grid = await jsonResponse.Content.ReadFromJsonAsync<ReportWriterGridRenderDto>(ServerJsonOptions);
        grid.Should().NotBeNull();
        grid!.GridId.Should().Be("sector-pnl");
        grid.Rows.Should().Contain(row =>
            row.Values["sector"] == "Technology"
            && row.Values["pnl"] == "250"
            && row.Values["returnPct"] == "2.5");
        grid.DataDictionary.Should().NotBeNull();
        grid.DataDictionary!.Should().Contain(field =>
            field.Key == "pnl"
            && field.SourceField == "pnl"
            && field.DataType == "decimal"
            && !field.IsGenerated);
        grid.DataDictionary.Should().Contain(field =>
            field.Key == "returnPct"
            && field.Role == "formula"
            && field.IsGenerated);
        grid.ValidationChecks.Should().NotBeNull();
        grid.ValidationChecks!.Should().Contain(check =>
            check.CheckId == "row-count"
            && check.Status == "Passed");
        grid.ValidationChecks.Should().Contain(check =>
            check.CheckId == "source-field-lineage"
            && check.Status == "Passed");

        csvResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        csvResponse.Content.Headers.ContentType?.MediaType.Should().Be("text/csv");
        csvResponse.Content.Headers.ContentDisposition?.FileName.Should().Be("retained-grid-20260505-sector-pnl.csv");
        var csv = await csvResponse.Content.ReadAsStringAsync();
        csv.Should().StartWith("sector,pnl,marketValue,returnPct");
        csv.Should().Contain("Technology,250,10000,2.5");

        xlsAliasResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        xlsAliasResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        xlsAliasResponse.Content.Headers.ContentDisposition?.FileName.Should().Be("retained-grid-20260505-sector-pnl.xlsx");
        var xlsAliasWorkbook = await xlsAliasResponse.Content.ReadAsByteArrayAsync();
        xlsAliasWorkbook.Should().StartWith([0x50, 0x4B]);
        using (var xlsAliasArchive = new ZipArchive(new MemoryStream(xlsAliasWorkbook), ZipArchiveMode.Read))
        {
            xlsAliasArchive.GetEntry("xl/workbook.xml").Should().NotBeNull();
            xlsAliasArchive.GetEntry("xl/worksheets/sheet1.xml").Should().NotBeNull();
        }

        xlsxResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        xlsxResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        xlsxResponse.Content.Headers.ContentDisposition?.FileName.Should().Be("retained-grid-20260505-sector-pnl.xlsx");
        var workbook = await xlsxResponse.Content.ReadAsByteArrayAsync();
        workbook.Should().StartWith([0x50, 0x4B]);
        workbook.Length.Should().BeGreaterThan(1000);
        using var archive = new ZipArchive(new MemoryStream(workbook), ZipArchiveMode.Read);
        using var workbookReader = new StreamReader(archive.GetEntry("xl/workbook.xml")!.Open());
        var workbookXml = await workbookReader.ReadToEndAsync();
        workbookXml.Should().Contain("DataDictionary");
        workbookXml.Should().Contain("Validation");

        pdfResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        pdfResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
        pdfResponse.Content.Headers.ContentDisposition?.FileName.Should().Be("retained-grid-20260505-sector-pnl.pdf");
        var pdf = await pdfResponse.Content.ReadAsByteArrayAsync();
        pdf.Should().StartWith(System.Text.Encoding.ASCII.GetBytes("%PDF-1.4"));
        System.Text.Encoding.ASCII.GetString(pdf).Should().Contain("Sector P&L");
        System.Text.Encoding.ASCII.GetString(pdf).Should().Contain("Technology | 250 | 10000 | 2.5");
    }

    [Fact]
    public async Task Endpoint_ReportWriterGridArtifact_WhenCallerCannotAccessTemplate_ReturnsForbidden()
    {
        await using var app = await CreateFundStructureAppAsync(UserRole.ReportingAnalyst, "viewer.user");
        var registry = app.Services.GetRequiredService<ReportTemplateRegistryService>();
        var draft = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "private-retained-grid-template",
                "Private Retained Grid Template",
                ["summary"],
                [],
                Family: "CustomReport",
                Rationale: "Retained grid artifacts must inherit template access policy",
                Grids:
                [
                    new ReportWriterGridDefinitionDto(
                        "private-sector-pnl",
                        "Private Sector P&L",
                        ReportWriterGridKindDto.Pivot,
                        RowFields: ["sector"],
                        Metrics: [new ReportWriterMetricDefinitionDto("pnl", "pnl")])
                ],
                AccessPolicy: new ReportAccessPolicyDto(ReportAccessModeDto.Private, OwnerPrincipalId: "owner.user")),
            "owner.user");
        registry.Submit(draft.Definition.TemplateId, "owner.user", "ready");
        registry.Approve(draft.Definition.TemplateId, new ReportTemplateDecisionRequestDto("approved", "APP-GRID-PRIVATE-1"), "controller.admin");

        var orchestration = app.Services.GetRequiredService<IReportingOrchestrationService>();
        var manifest = await orchestration.ExecuteAsync(
            new ReportingJobContract(
                "private-retained-grid",
                draft.Definition.TemplateId.Name,
                new DateOnly(2026, 5, 5),
                ReportingRunTrigger.AdHoc,
                0,
                "owner.user",
                new DateTimeOffset(2026, 5, 5, 9, 0, 0, TimeSpan.Zero),
                DatasetRows:
                [
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["sector"] = "Technology",
                        ["pnl"] = "250"
                    }
                ]),
            CancellationToken.None);
        manifest.RenderedReportWriterGrids.Should().ContainSingle(grid => grid.GridId == "private-sector-pnl");
        var client = app.GetTestClient();

        var response = await client.GetAsync($"/api/fund-structure/reporting/runs/{manifest.RunId}/report-writer-grids/private-sector-pnl");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Endpoint_RunPrivateTemplate_WhenCallerIsNotOwner_ReturnsForbidden()
    {
        await using var app = await CreateFundStructureAppAsync(UserRole.ReportingAnalyst, "viewer.user");
        var registry = app.Services.GetRequiredService<ReportTemplateRegistryService>();
        var draft = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "private-run-template",
                "Private Run Template",
                ["summary"],
                [new ReportTemplateParameterDefinitionDto("period", Required: true)],
                Family: "CustomReport",
                Rationale: "Private run test",
                AccessPolicy: new ReportAccessPolicyDto(ReportAccessModeDto.Private, OwnerPrincipalId: "owner.user")),
            "owner.user",
            companyId: TestCompanyId,
            tenantId: TestTenantId);
        registry.Submit(draft.Definition.TemplateId, "owner.user", "ready", BoundAccessContext("owner.user"));
        registry.Approve(draft.Definition.TemplateId, new ReportTemplateDecisionRequestDto("approved", "APP-RUN-PRIVATE-1"), "controller.admin", BoundAccessContext("controller.admin"));
        var client = app.GetTestClient();

        var runResponse = await client.PostAsJsonAsync(
            "/api/fund-structure/reporting/runs",
            new ReportingRunRequestDto(
                draft.Definition.TemplateId.Name,
                new DateOnly(2026, 5, 5),
                JobId: "adhoc-private-grid"),
            ServerJsonOptions);

        runResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Endpoint_ScheduleRestrictedCustomTemplate_WhenRoleProfileMatchesGroup_CreatesBoundScheduleAndFailsClosedWithoutCertification()
    {
        await using var app = await CreateFundStructureAppAsync(
            UserRole.ReportingAnalyst,
            "viewer.user",
            roleProfileName: "ops-control");
        var registry = app.Services.GetRequiredService<ReportTemplateRegistryService>();
        var draft = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "ops-control-scheduled-template",
                "Ops Control Scheduled Template",
                [],
                [new ReportTemplateParameterDefinitionDto("period", Required: true)],
                Family: "CustomReport",
                Rationale: "Restricted group schedule test",
                Grids:
                [
                    new ReportWriterGridDefinitionDto(
                        "strategy-contribution",
                        "Strategy Contribution",
                        ReportWriterGridKindDto.Contribution,
                        RowFields: ["strategy"],
                        Metrics: [new ReportWriterMetricDefinitionDto("pnl", "pnl")])
                ],
                AccessPolicy: new ReportAccessPolicyDto(
                    ReportAccessModeDto.Restricted,
                    Principals: [new ReportAccessPrincipalDto(ReportAccessPrincipalKindDto.Group, "ops-control")])),
            "owner.user",
            companyId: TestCompanyId,
            tenantId: TestTenantId);
        registry.Submit(draft.Definition.TemplateId, "owner.user", "ready", BoundAccessContext("owner.user"));
        registry.Approve(draft.Definition.TemplateId, new ReportTemplateDecisionRequestDto("approved", "APP-SCHED-GROUP-1"), "controller.admin", BoundAccessContext("controller.admin"));
        var client = app.GetTestClient();

        var scheduleResponse = await client.PostAsJsonAsync(
            "/api/fund-structure/reporting/schedules",
            new ReportingScheduleUpsertRequestDto(
                "sched-ops-control-custom",
                draft.Definition.TemplateId.Name,
                "0 8 1 * *",
                new DateOnly(2026, 5, 5),
                new DateTimeOffset(2026, 5, 5, 8, 0, 0, TimeSpan.Zero),
                0,
                "spoofed-requester",
                "Ops control custom report schedule.",
                RunParameters: BuildEndpointScheduleRunParameters()),
            ServerJsonOptions);
        var runResponse = await client.PostAsync("/api/fund-structure/reporting/schedules/sched-ops-control-custom/run", null);

        scheduleResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var schedule = await scheduleResponse.Content.ReadFromJsonAsync<ReportingScheduleRecordDto>(ServerJsonOptions);
        schedule.Should().NotBeNull();
        schedule!.TemplateId.Should().Be(draft.Definition.TemplateId.Name);
        schedule.RequestedBy.Should().Be("viewer.user");
        schedule.RunParameters.Should().BeEquivalentTo(BuildEndpointScheduleRunParameters());
        runResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "the minimal endpoint harness intentionally has no server readiness, certification, or canonical governance dependencies");
    }

    [Fact]
    public async Task Endpoint_ActiveScheduleWithoutImmutableParameters_ReturnsBadRequestForCreateAndResume()
    {
        await using var app = await CreateFundStructureAppAsync(UserRole.ReportingAnalyst, "viewer.user");
        var client = app.GetTestClient();
        var incomplete = new ReportingScheduleUpsertRequestDto(
            "sched-incomplete-bound",
            "investor-monthly-statement",
            "0 8 1 * *",
            new DateOnly(2026, 5, 5),
            new DateTimeOffset(2026, 5, 5, 8, 0, 0, TimeSpan.Zero),
            0,
            "spoofed-requester",
            "Bound schedule requires exact immutable parameters.");

        var activeResponse = await client.PostAsJsonAsync(
            "/api/fund-structure/reporting/schedules",
            incomplete,
            ServerJsonOptions);
        activeResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var draftResponse = await client.PostAsJsonAsync(
            "/api/fund-structure/reporting/schedules",
            incomplete with { State = ReportingScheduleStateDto.Draft },
            ServerJsonOptions);
        draftResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var resumeResponse = await client.PostAsync(
            "/api/fund-structure/reporting/schedules/sched-incomplete-bound/resume",
            null);
        resumeResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var retained = app.Services.GetRequiredService<ReportingScheduleService>()
            .ListSchedules(new ReportAccessQueryContext(
                ActorPrincipalId: "viewer.user",
                CompanyId: "company-test",
                TenantId: "company-test",
                RequireBoundScope: true))
            .Should().ContainSingle().Subject;
        retained.State.Should().Be(ReportingScheduleStateDto.Draft);
        retained.RunParameters.Should().BeNull();
    }

    [Fact]
    public async Task Endpoint_ProvisionReportingStarterKit_WhenAnalystSelectsEmergingManager_ReturnsSeededDraftSchedules()
    {
        await using var app = await CreateFundStructureAppAsync(UserRole.ReportingAnalyst, "controller.admin");
        var client = app.GetTestClient();

        var catalogResponse = await client.GetAsync("/api/fund-structure/reporting/starter-kits");
        var provisionResponse = await client.PostAsync("/api/fund-structure/reporting/starter-kits/emerging-manager/provision", null);

        catalogResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var kits = await catalogResponse.Content.ReadFromJsonAsync<IReadOnlyList<ReportingStarterKitDto>>(ServerJsonOptions);
        kits.Should().NotBeNull();
        kits!.Select(static kit => kit.KitId).Should().Contain("emerging-manager");
        provisionResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await provisionResponse.Content.ReadFromJsonAsync<ReportingStarterKitProvisionResultDto>(ServerJsonOptions);
        result.Should().NotBeNull();
        result!.Kit.KitId.Should().Be("emerging-manager");
        result.State.ProvisionedBy.Should().Be("controller.admin");
        result.State.EnabledTemplateIds.Should().BeEquivalentTo(
            "investor-monthly-statement",
            "capital-account-statement",
            "shadow-nav-daily-pack");
        result.SeededSchedules.Should().HaveCount(2);
        result.SeededSchedules.Should().OnlyContain(static schedule => schedule.State == ReportingScheduleStateDto.Draft);
        var scheduleService = app.Services.GetRequiredService<ReportingScheduleService>();
        scheduleService.ListSchedules(BoundAccessContext("controller.admin"))
            .Select(static schedule => schedule.ScheduleId)
            .Should()
            .BeEquivalentTo(result.State.SeedScheduleIds);
    }

    [Fact]
    public async Task Endpoint_SchedulePrivateTemplate_WhenCallerIsNotOwner_ReturnsForbiddenForCreateAndRun()
    {
        await using var app = await CreateFundStructureAppAsync(UserRole.ReportingAnalyst, "viewer.user");
        var registry = app.Services.GetRequiredService<ReportTemplateRegistryService>();
        var draft = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "private-scheduled-template",
                "Private Scheduled Template",
                ["summary"],
                [new ReportTemplateParameterDefinitionDto("period", Required: true)],
                Family: "CustomReport",
                Rationale: "Private schedule test",
                AccessPolicy: new ReportAccessPolicyDto(ReportAccessModeDto.Private, OwnerPrincipalId: "owner.user")),
            "owner.user",
            companyId: TestCompanyId,
            tenantId: TestTenantId);
        registry.Submit(draft.Definition.TemplateId, "owner.user", "ready", BoundAccessContext("owner.user"));
        var approved = registry.Approve(draft.Definition.TemplateId, new ReportTemplateDecisionRequestDto("approved", "APP-SCHED-PRIVATE-1"), "controller.admin", BoundAccessContext("controller.admin"));
        ReportAccessPolicyEvaluator.Evaluate(approved.Definition.AccessPolicy, new ReportAccessQueryContext("owner.user")).IsAccessible.Should().BeTrue();
        ReportAccessPolicyEvaluator.Evaluate(approved.Definition.AccessPolicy, new ReportAccessQueryContext("viewer.user")).IsAccessible.Should().BeFalse();
        var scheduleService = app.Services.GetRequiredService<ReportingScheduleService>();
        scheduleService.Upsert(
            new ReportingScheduleUpsertRequestDto(
                "sched-private-custom",
                draft.Definition.TemplateId.Name,
                "0 8 1 * *",
                new DateOnly(2026, 5, 5),
                new DateTimeOffset(2026, 5, 5, 8, 0, 0, TimeSpan.Zero),
                0,
                "owner.user",
                "Owner-created private report schedule.",
                RunParameters: BuildEndpointScheduleRunParameters()),
            BoundAccessContext("owner.user"));
        var client = app.GetTestClient();

        var scheduleResponse = await client.PostAsJsonAsync(
            "/api/fund-structure/reporting/schedules",
            new ReportingScheduleUpsertRequestDto(
                "sched-private-blocked",
                draft.Definition.TemplateId.Name,
                "0 8 1 * *",
                new DateOnly(2026, 5, 5),
                new DateTimeOffset(2026, 5, 5, 8, 0, 0, TimeSpan.Zero),
                0,
                "viewer.user",
                "Unauthorized private report schedule.",
                RunParameters: BuildEndpointScheduleRunParameters()),
            ServerJsonOptions);
        var runResponse = await client.PostAsync("/api/fund-structure/reporting/schedules/sched-private-custom/run", null);

        scheduleResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        runResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Endpoint_CreateDraft_WithSessionCompanyAndRoleProfile_DefaultsTenantAwareAccessPolicy()
    {
        await using var app = await CreateFundStructureAppAsync(
            UserRole.ReportingAnalyst,
            "viewer.user",
            roleProfileName: "reporting-ops",
            companyId: "company-alpha");
        var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            "/api/fund-structure/reporting/templates/drafts",
            new ReportTemplateDraftRequestDto(
                "tenant-aware-template",
                "Tenant Aware Template",
                ["summary"],
                [new ReportTemplateParameterDefinitionDto("period", Required: true)],
                Family: "CustomReport",
                Rationale: "Tenant-aware draft test"),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var draft = await response.Content.ReadFromJsonAsync<ReportTemplateGovernanceRecordDto>(ServerJsonOptions);
        draft.Should().NotBeNull();
        draft!.CreatedBy.Should().Be("viewer.user");
        draft.Definition.AccessPolicy!.OwnerPrincipalId.Should().Be("viewer.user");
        draft.Definition.AccessPolicy.CompanyId.Should().Be("company-alpha");
    }

    [Fact]
    public async Task Endpoint_LegacyDeliveryMutations_ReturnGoneWithoutCreatingAttempt()
    {
        await using var app = await CreateFundStructureAppAsync(UserRole.Admin, "ops.lead");
        var delivery = app.Services.GetRequiredService<ReportPackDeliveryService>();
        var client = app.GetTestClient();
        var reportId = Guid.NewGuid();
        var request = new ReportPackDeliveryRequestDto(
            "board-reporting-committee",
            Actor: "caller-supplied-actor",
            DeliveryReference: "legacy-delivery",
            DeliveryMode: ReportPackDeliveryModeDto.SecurePortal);

        var deliveryResponse = await client.PostAsJsonAsync(
            $"/api/fund-structure/reporting/packs/{reportId:D}/deliveries",
            request,
            ServerJsonOptions);
        var failureResponse = await client.PostAsJsonAsync(
            $"/api/fund-structure/reporting/packs/{reportId:D}/deliveries/failures",
            new ReportPackDeliveryFailureRequestDto(
                "board-reporting-committee",
                "caller-supplied failure",
                Actor: "caller-supplied-actor"),
            ServerJsonOptions);

        deliveryResponse.StatusCode.Should().Be(HttpStatusCode.Gone);
        failureResponse.StatusCode.Should().Be(HttpStatusCode.Gone);
        (await deliveryResponse.Content.ReadAsStringAsync()).Should()
            .NotContain("caller-supplied-actor");
        (await failureResponse.Content.ReadAsStringAsync()).Should()
            .NotContain("caller-supplied-actor");
        delivery.ListAttempts().Should().BeEmpty();
    }


    [Fact]
    public void Create_NormalizesReportValueSessionAndReconciliationRunLineProvenance()
    {
        var svc = new ReportPackWorkflowService();

        var created = svc.Create(
            "fund-a",
            "acct-1",
            "2026-03",
            new VersionedReportTemplateIdDto("board-pack", 1),
            "author",
            [
                new ReportPackLineProvenanceDto(
                    " trial-balance.nav ",
                    " paper-session ",
                    " session-1 ",
                    " session-evidence-1 ",
                    RunId: " run-1 ",
                    ReportValue: " 125.00 ",
                    SourceSessionId: " paper-session-1 ",
                    ReconciliationRunId: " recon-run-1 ",
                    ProviderEventId: " provider-event-1 ",
                    SecurityMasterId: " security-1 ",
                    SecurityDefinitionId: " definition-1 ",
                    ReconciliationOutcome: " matched ",
                    ApprovalId: " approval-1 ")
            ]);

        created.LineProvenance.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ReportPackLineProvenanceDto(
                "trial-balance.nav",
                "paper-session",
                "session-1",
                "session-evidence-1",
                RunId: "run-1",
                ReportValue: "125.00",
                SourceSessionId: "paper-session-1",
                ReconciliationRunId: "recon-run-1",
                ProviderEventId: "provider-event-1",
                SecurityMasterId: "security-1",
                SecurityDefinitionId: "definition-1",
                ReconciliationOutcome: "matched",
                ApprovalId: "approval-1",
                FinancialRecordExplorerId: "ledger",
                FinancialRecordHref: "/api/workstation/financial-record-explorers/ledger?lineKey=trial-balance.nav&sourceId=session-1&evidenceId=session-evidence-1&runId=run-1"));
    }

    [Fact]
    public void Create_RoutesSecurityOnlyLineProvenanceToSecurityInstrumentExplorer()
    {
        var svc = new ReportPackWorkflowService();

        var created = svc.Create(
            "fund-a",
            "acct-1",
            "2026-03",
            new VersionedReportTemplateIdDto("board-pack", 1),
            "author",
            [
                new ReportPackLineProvenanceDto(
                    "security-master.bdc-alpha",
                    "security-master",
                    "security-1",
                    "security-evidence-1",
                    SecurityMasterId: "security-1",
                    SecurityDefinitionId: "definition-1")
            ]);

        var line = created.LineProvenance.Should().ContainSingle().Which;
        line.FinancialRecordExplorerId.Should().Be("security-instrument");
        line.FinancialRecordHref.Should().Be(
            "/api/workstation/financial-record-explorers/security-instrument?lineKey=security-master.bdc-alpha&sourceId=security-1&evidenceId=security-evidence-1");
    }

    [Fact]
    public void Transition_RejectsInvalidRole()
    {
        var svc = new ReportPackWorkflowService();
        var created = svc.Create("fund-a", "acct-1", "2026-03", new VersionedReportTemplateIdDto("board-pack", 1), "author");

        Action act = () => svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Approved, "user", "reviewer");
        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Fact]
    public void Reject_AllowsReviewStateAndRecordsReasonMetadata()
    {
        var svc = new ReportPackWorkflowService();
        var created = svc.Create("fund-a", "acct-1", "2026-03", new VersionedReportTemplateIdDto("board-pack", 1), "author");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");

        var rejected = svc.Reject(
            created.ReportId,
            "NAV tie-out variance exceeds tolerance",
            "senior-reviewer",
            "reviewer",
            [new ReportPackEvidenceLinkDto("tie-out-1", "Tie-out variance", "/evidence/tie-out-1", "reconciliation")]);

        rejected.State.Should().Be(ReportPackWorkflowStateDto.Rejected);
        rejected.Rejection.Should().NotBeNull();
        rejected.Rejection!.Reason.Should().Be("NAV tie-out variance exceeds tolerance");
        rejected.Rejection.Actor.Should().Be("senior-reviewer");
        rejected.Rejection.ActorRole.Should().Be("reviewer");
        rejected.Rejection.EvidenceLinks.Should().ContainSingle(link =>
            link.EvidenceId == "tie-out-1" &&
            link.Label == "Tie-out variance" &&
            link.Route == "/evidence/tie-out-1" &&
            link.Source == "reconciliation");
    }

    [Theory]
    [InlineData(ReportPackWorkflowStateDto.Draft)]
    [InlineData(ReportPackWorkflowStateDto.Published)]
    public void Reject_RejectsDraftAndPublishedStates(ReportPackWorkflowStateDto startingState)
    {
        var svc = new ReportPackWorkflowService();
        var created = svc.Create("fund-a", "acct-1", "2026-03", new VersionedReportTemplateIdDto("board-pack", 1), "author");

        if (startingState == ReportPackWorkflowStateDto.Published)
        {
            svc.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
            svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
            svc.Publish(
                created.ReportId,
                "publisher",
                "publisher",
                "controller",
                "sha256:abc123",
                "manifest-1",
                "vault/report-packs/manifest-1.json",
                [new ReportPackEvidenceLinkDto("report-pack-1", "Report pack manifest", "/evidence/report-pack-1", "reporting")]);
        }

        Action act = () => svc.Reject(created.ReportId, "needs reviewer remediation", "senior-reviewer", "reviewer");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"invalid transition {startingState} -> {ReportPackWorkflowStateDto.Rejected}");
    }

    [Fact]
    public void Reject_RejectsInvalidRole()
    {
        var svc = new ReportPackWorkflowService();
        var created = svc.Create("fund-a", "acct-1", "2026-03", new VersionedReportTemplateIdDto("board-pack", 1), "author");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");

        Action act = () => svc.Reject(created.ReportId, "needs reviewer remediation", "operator", "operator");

        act.Should().Throw<UnauthorizedAccessException>()
            .WithMessage("Role 'operator' cannot transition to Rejected.");
    }

    [Fact]
    public void Reject_AppendsAuditTrailContents()
    {
        var svc = new ReportPackWorkflowService();
        var created = svc.Create("fund-a", "acct-1", "2026-03", new VersionedReportTemplateIdDto("board-pack", 1), "author");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");

        var rejected = svc.Reject(created.ReportId, "missing controller sign-off evidence", "senior-reviewer", "reviewer");

        rejected.AuditTrail.Should().ContainSingle(entry =>
            entry.Actor == "senior-reviewer" &&
            entry.Action == "rejected" &&
            entry.FromState == ReportPackWorkflowStateDto.InReview &&
            entry.ToState == ReportPackWorkflowStateDto.Rejected &&
            entry.Note == "missing controller sign-off evidence");
    }

    [Fact]
    public void Publish_RejectsRejectedRecordsUntilResubmittedThroughApprovalLifecycle()
    {
        var svc = new ReportPackWorkflowService();
        var created = svc.Create("fund-a", "acct-1", "2026-03", new VersionedReportTemplateIdDto("board-pack", 1), "author");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        svc.Reject(created.ReportId, "missing controller sign-off evidence", "senior-reviewer", "reviewer");

        Action publishRejected = () => svc.Publish(
            created.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            [new ReportPackEvidenceLinkDto("report-pack-1", "Report pack manifest", "/evidence/report-pack-1", "reporting")]);

        publishRejected.Should().Throw<InvalidOperationException>()
            .WithMessage("invalid transition Rejected -> Published");

        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Draft, "author", "operator");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
        var published = svc.Publish(
            created.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            [new ReportPackEvidenceLinkDto("report-pack-1", "Report pack manifest", "/evidence/report-pack-1", "reporting")]);

        published.State.Should().Be(ReportPackWorkflowStateDto.Published);
    }

    [Fact]
    public void Restate_RequiresLineageAndReasonMetadata()
    {
        var svc = new ReportPackWorkflowService();
        var created = svc.Create("fund-a", "acct-1", "2026-03", new VersionedReportTemplateIdDto("board-pack", 1), "author");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
        svc.Publish(
            created.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            [new ReportPackEvidenceLinkDto("report-pack-1", "Report pack manifest", "/evidence/report-pack-1", "reporting")]);

        var restated = svc.Restate(created.ReportId, "approver", "approver", "pricing-correction", "chief-approver", created.ReportId,
            [new ReportPackChangedLineDto("line-1", "100", "125", [new ReportPackEvidenceLinkDto("pricing-evidence-1", "Pricing correction", "/evidence/pricing-evidence-1", "pricing")])]);

        restated.State.Should().Be(ReportPackWorkflowStateDto.Restated);
        restated.Restatement.Should().NotBeNull();
        restated.Restatement!.ChangedLines.Should().ContainSingle();
        restated.Restatement.EvidenceLinks.Should().ContainSingle(link => link.EvidenceId == "pricing-evidence-1");
        restated.Version.Should().Be(2);
    }

    [Fact]
    public void Restate_RejectsChangedLinesWithoutEvidenceLinks()
    {
        var svc = new ReportPackWorkflowService();
        var created = svc.Create("fund-a", "acct-1", "2026-03", new VersionedReportTemplateIdDto("board-pack", 1), "author");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
        svc.Publish(
            created.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            [new ReportPackEvidenceLinkDto("report-pack-1", "Report pack manifest", "/evidence/report-pack-1", "reporting")]);

        Action act = () => svc.Restate(
            created.ReportId,
            "approver",
            "approver",
            "pricing-correction",
            "chief-approver",
            created.ReportId,
            [new ReportPackChangedLineDto("line-1", "100", "125")]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Restatement changed lines require evidence links: line-1.");
    }

    [Fact]
    public void Restate_RejectsReviewedAutomationOriginBeforeMutation()
    {
        var svc = new ReportPackWorkflowService();
        var created = svc.Create("fund-a", "acct-1", "2026-03", new VersionedReportTemplateIdDto("board-pack", 1), "author");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
        var published = svc.Publish(
            created.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            [new ReportPackEvidenceLinkDto("report-pack-1", "Report pack manifest", "/evidence/report-pack-1", "reporting")]);

        Action act = () => svc.Restate(
            created.ReportId,
            "reviewed-automation",
            "approver",
            "pricing-correction",
            "chief-approver",
            created.ReportId,
            [new ReportPackChangedLineDto("line-1", "100", "125", [new ReportPackEvidenceLinkDto("pricing-evidence-1", "Pricing correction", "/evidence/pricing-evidence-1", "pricing")])],
            OperationsActionOriginDto.AssistantDraft);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Reviewed automation cannot restate reports; a human operator approval is required.");

        var record = svc.GetRecord(created.ReportId);
        record.Should().NotBeNull();
        record!.State.Should().Be(ReportPackWorkflowStateDto.Published);
        record.Version.Should().Be(published.Version);
        record.Restatement.Should().BeNull();
    }

    [Fact]
    public void Transition_ArchivesPublishedOrRestatedPacksOnly()
    {
        var svc = new ReportPackWorkflowService();
        var created = svc.Create("fund-a", "acct-1", "2026-03", new VersionedReportTemplateIdDto("board-pack", 1), "author");

        Action earlyArchive = () => svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Archived, "records", "records-manager");
        earlyArchive.Should().Throw<InvalidOperationException>()
            .WithMessage("invalid transition Draft -> Archived");

        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
        svc.Publish(
            created.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            [new ReportPackEvidenceLinkDto("report-pack-1", "Report pack manifest", "/evidence/report-pack-1", "reporting")]);

        var archived = svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Archived, "records", "records-manager");

        archived.State.Should().Be(ReportPackWorkflowStateDto.Archived);
        archived.AuditTrail.Should().ContainSingle(entry =>
            entry.Action == "archived" &&
            entry.FromState == ReportPackWorkflowStateDto.Published &&
            entry.ToState == ReportPackWorkflowStateDto.Archived);
    }

    private static ReportPackWorkflowRecordDto CreateApprovedPack(
        ReportPackWorkflowService svc,
        IReadOnlyList<ReportPackLineProvenanceDto> lineProvenance,
        string templateName = "board-pack",
        ReportAccessPolicyDto? accessPolicy = null,
        ReportAccessQueryContext? accessContext = null)
    {
        var created = svc.Create(
            "fund-a",
            "acct-1",
            "2026-03",
            new VersionedReportTemplateIdDto(templateName, 1),
            "author",
            lineProvenance,
            accessPolicy,
            accessContext);
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        return svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
    }

    private static ReportAccessQueryContext BoundAccessContext(
        string actor,
        string companyId = TestCompanyId,
        IReadOnlyList<string>? groups = null) =>
        new(actor, groups, companyId, TenantId: TestTenantId, RequireBoundScope: true);

    private static ReportingRunReadinessService LegacyDraftReadiness(
        IReportingTemplateCatalog catalog,
        GovernedReportingTemplateCatalog? governedCatalog = null) =>
        new(
            catalog,
            governedCatalog,
            options: new ReportingRunReadinessOptions(
                AllowLegacyUnscopedDrafts: true,
                AllowDraftWhenDependencyEvaluatorIsUnavailable: true));

    private static void PublishWithLedgerEvidence(ReportPackWorkflowService svc, Guid reportId, string evidenceId = "ledger-evidence-1") =>
        svc.Publish(
            reportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            [new ReportPackEvidenceLinkDto(evidenceId, "Line evidence", $"/evidence/{evidenceId}", "reporting")]);

    private static IReadOnlyList<ReportPackEvidenceLinkDto> CompleteLineProvenanceEvidenceLinks(params string[] evidenceIds) =>
        evidenceIds
            .DefaultIfEmpty("line-evidence-1")
            .Select(static evidenceId => new ReportPackEvidenceLinkDto(evidenceId, "Line evidence", $"/evidence/{evidenceId}", "reporting"))
            .Concat(
            [
                new ReportPackEvidenceLinkDto("ledger-entry-1", "Ledger entry", "/evidence/ledger-entry-1", "ledger"),
                new ReportPackEvidenceLinkDto("provider-event-1", "Provider event", "/evidence/provider-event-1", "provider"),
                new ReportPackEvidenceLinkDto("security-1", "Security Master identity", "/evidence/security-1", "security-master"),
                new ReportPackEvidenceLinkDto("definition-1", "Security definition", "/evidence/definition-1", "security-master"),
                new ReportPackEvidenceLinkDto("case-1", "Reconciliation case", "/evidence/case-1", "reconciliation"),
                new ReportPackEvidenceLinkDto("recon-run-1", "Reconciliation run", "/evidence/recon-run-1", "reconciliation"),
                new ReportPackEvidenceLinkDto("approval-1", "Approval", "/evidence/approval-1", "approval"),
                new ReportPackEvidenceLinkDto("run-1", "Strategy run", "/evidence/run-1", "strategy"),
                new ReportPackEvidenceLinkDto("provider-session-1", "Provider source session", "/evidence/provider-session-1", "provider")
            ])
            .GroupBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static ReportAccessQueryContext BoundReportingScheduleAuthority(
        string actorPrincipalId,
        IReadOnlyList<string>? groupPrincipalIds = null,
        bool isAdmin = false) =>
        new(
            actorPrincipalId,
            groupPrincipalIds ?? [],
            CompanyId: "company-a",
            HasGlobalOverride: isAdmin,
            TenantId: "tenant-a",
            RequireBoundScope: true);

    private static ReportingRunParametersDto BuildCanonicalScheduleRunParameters(
        DateOnly asOfDate,
        string periodId) =>
        new(
            new ReportingRunScopeDto("fund-a"),
            periodId,
            asOfDate,
            new ReportingLedgerBookSelectionDto(LedgerBookCode: "MAIN"),
            ReportingAccountingBasisDto.Gaap,
            "USD",
            ReportingConsolidationLevelDto.Fund,
            ReportingOutputFormatDto.Pdf,
            ReportingFinalityDto.Draft,
            IncludeSupportingSchedules: false,
            IncludeEvidenceAppendix: true,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["period"] = periodId
            });

    private static ReportingOutputManifest BuildCanonicalScheduledManifest(
        ReportingScheduleRecordDto schedule,
        string runId)
    {
        var parameters = schedule.RunParameters
            ?? throw new InvalidOperationException("The test schedule requires canonical run parameters.");
        var policy = ReportAccessPolicyEvaluator.Normalize(schedule.AccessPolicySnapshot);
        var accessMode = policy.Mode switch
        {
            ReportAccessModeDto.Private => ReportingGovernanceAccessMode.Private,
            ReportAccessModeDto.Restricted => ReportingGovernanceAccessMode.Restricted,
            _ => ReportingGovernanceAccessMode.CompanyWide
        };
        var principals = (policy.Principals ?? [])
            .Select(static principal => new ReportingAccessPrincipalScope(
                principal.Kind switch
                {
                    ReportAccessPrincipalKindDto.Group => ReportingAccessPrincipalKind.Group,
                    ReportAccessPrincipalKindDto.Company => ReportingAccessPrincipalKind.Company,
                    _ => ReportingAccessPrincipalKind.User
                },
                principal.PrincipalId))
            .ToImmutableArray();
        var scope = new ReportingOperationalScope(
            schedule.TenantId!,
            "organization-a",
            schedule.CompanyId,
            parameters.Scope.FundProfileId,
            parameters.LedgerBook.LedgerBookId?.ToString("D", CultureInfo.InvariantCulture)
                ?? parameters.LedgerBook.LedgerBookCode,
            parameters.PeriodId);
        var access = new ReportingAccessScope(
            $"schedule:{schedule.ScheduleId}",
            "1",
            accessMode,
            policy.OwnerPrincipalId,
            policy.AllowOwnerAccess,
            principals,
            schedule.AccessPolicySnapshotHash!);
        return new ReportingOutputManifest(
            runId,
            schedule.TemplateId,
            parameters.AsOfDate,
            ReportingRunStatus.Draft,
            ImmutableArray<ReportingSectionManifest>.Empty,
            ImmutableArray.Create($"{runId}.pdf"),
            1,
            ReportingRunTrigger.Scheduled,
            ScheduleId: schedule.ScheduleId,
            ResolvedTemplate: schedule.Template,
            ResolvedParameters: parameters,
            OperationalScope: scope,
            ImmutableAccessScope: access);
    }

    private static ReportPackLineProvenanceDto CompleteLineProvenance(string lineKey, string evidenceId) =>
        new(
            lineKey,
            "ledger",
            "ledger-entry-1",
            evidenceId,
            RunId: "run-1",
            LedgerEntryId: "ledger-entry-1",
            ReconciliationCaseId: "case-1",
            ReportValue: "100.00",
            SourceSessionId: "provider-session-1",
            ReconciliationRunId: "recon-run-1",
            ProviderEventId: "provider-event-1",
            SecurityMasterId: "security-1",
            SecurityDefinitionId: "definition-1",
            ReconciliationOutcome: "matched",
            ApprovalId: "approval-1");

    private static ReportPackLineProvenanceDto SourcePortfolioReportingLine(
        string lineKey,
        string evidenceId,
        string reportValue,
        string? href = null) =>
        new(
            lineKey,
            "ledger",
            "ledger-entry-1",
            evidenceId,
            RunId: "run-1",
            LedgerEntryId: "ledger-entry-1",
            ReconciliationCaseId: "case-1",
            ReportValue: reportValue,
            SourceSessionId: "provider-session-1",
            ReconciliationRunId: "recon-run-1",
            ProviderEventId: "provider-event-1",
            SecurityMasterId: "security-1",
            SecurityDefinitionId: "definition-1",
            ReconciliationOutcome: "matched",
            ApprovalId: "approval-1",
            FinancialRecordHref: href);

    private static readonly JsonSerializerOptions ServerJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static ReportingRunParametersDto BuildEndpointScheduleRunParameters() =>
        new(
            new ReportingRunScopeDto("fund-a"),
            "2026-05",
            new DateOnly(2026, 5, 5),
            new ReportingLedgerBookSelectionDto(LedgerBookCode: "primary"),
            ReportingAccountingBasisDto.Gaap,
            "USD",
            ReportingConsolidationLevelDto.Fund,
            ReportingOutputFormatDto.Pdf,
            ReportingFinalityDto.Draft,
            IncludeSupportingSchedules: true,
            IncludeEvidenceAppendix: true);

    private static async Task<WebApplication> CreateFundStructureAppAsync(
        UserRole role,
        string username = "controller.admin",
        FundOperationsWorkspaceReadService? workspaceService = null,
        string? roleProfileName = null,
        string? companyId = TestCompanyId,
        string? tenantId = TestTenantId)
    {
        var resolvedCompanyId = string.IsNullOrWhiteSpace(companyId)
            ? "company-test"
            : companyId.Trim();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ReportTemplateRegistryService>();
        builder.Services.AddSingleton<DefaultReportingTemplateCatalog>();
        builder.Services.AddSingleton<IReportingStarterKitCatalog, DefaultReportingStarterKitCatalog>();
        builder.Services.AddSingleton(sp =>
            new GovernedReportingTemplateCatalog(
                sp.GetRequiredService<DefaultReportingTemplateCatalog>(),
                sp.GetRequiredService<ReportTemplateRegistryService>()));
        builder.Services.AddSingleton<IReportingTemplateCatalog>(sp =>
            sp.GetRequiredService<GovernedReportingTemplateCatalog>());
        builder.Services.AddSingleton<IReportingOrchestrationService>(sp =>
            new ReportingOrchestrationService(
                sp.GetRequiredService<IReportingTemplateCatalog>(),
                new DeterministicReportingSectionRenderer(),
                () => new DateTimeOffset(2026, 5, 5, 9, 0, 0, TimeSpan.Zero)));
        builder.Services.AddSingleton<ReportWriterDatasetSourceService>();
        builder.Services.AddSingleton<ReportWriterGridArtifactService>();
        builder.Services.AddSingleton<IReportingAuthoritativeSource, FundStructureAuthoritativeSource>();
        builder.Services.AddSingleton<IReportingReconciliationEvidenceSource, FundStructureReconciliationEvidenceSource>();
        builder.Services.AddSingleton<IReportingGovernanceEndpointCoordinator>(sp =>
            new FundStructureGovernanceCoordinator(sp.GetRequiredService<IReportingOrchestrationService>()));
        builder.Services.AddSingleton(sp =>
            LegacyDraftReadiness(
                sp.GetRequiredService<IReportingTemplateCatalog>(),
                sp.GetRequiredService<GovernedReportingTemplateCatalog>()));
        builder.Services.AddSingleton<ReportingRunCertificationService>();
        builder.Services.AddSingleton<ReportingRunCommandService>();
        builder.Services.AddSingleton(sp =>
            new ReportingScheduleService(
                sp.GetRequiredService<IReportingOrchestrationService>(),
                datasetSourceService: sp.GetRequiredService<ReportWriterDatasetSourceService>(),
                governedTemplateCatalog: sp.GetRequiredService<GovernedReportingTemplateCatalog>(),
                readinessService: sp.GetRequiredService<ReportingRunReadinessService>(),
                certificationService: sp.GetRequiredService<ReportingRunCertificationService>()));
        builder.Services.AddSingleton<ReportingStarterKitService>();
        builder.Services.AddSingleton<ReportPackWorkflowService>();
        builder.Services.AddSingleton<ReportPackDeliveryService>();
        if (workspaceService is not null)
        {
            builder.Services.AddSingleton(workspaceService);
        }

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            var actor = context.Request.Headers.TryGetValue("X-Meridian-Test-User", out var testActor) &&
                !string.IsNullOrWhiteSpace(testActor.ToString())
                    ? testActor.ToString().Trim()
                    : username;
            context.Items[LoginSessionMiddleware.CurrentUserKey] = actor;
            context.Items[LoginSessionMiddleware.CurrentUserRoleKey] = role;
            if (!string.IsNullOrWhiteSpace(roleProfileName))
            {
                context.Items[LoginSessionMiddleware.CurrentUserRoleProfileNameKey] = roleProfileName.Trim();
            }

            context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = resolvedCompanyId;
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = resolvedCompanyId;

            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = tenantId.Trim();
            }

            await next();
        });
        app.MapFundStructureEndpoints(ServerJsonOptions);

        await app.StartAsync();
        return app;
    }

    private static FundOperationsWorkspaceReadService CreateStructuredExportWorkspaceService()
    {
        var securityMaster = new NullSecurityMasterQueryService();
        return new FundOperationsWorkspaceReadService(
            new InMemoryFundAccountService(),
            new StrategyRunStore(),
            new PortfolioReadService(),
            new NavAttributionService(securityMaster),
            new ReportGenerationService(securityMaster));
    }

    private sealed class InMemoryReportingScheduleStore(IReadOnlyList<ReportingScheduleRecordDto> schedules)
        : IReportingScheduleStore
    {
        public IReadOnlyList<ReportingScheduleRecordDto> Load() => schedules;

        public void Save(IReadOnlyList<ReportingScheduleRecordDto> schedules)
        {
        }
    }

    private sealed class InMemoryReportPackDeliveryRecordStore(IReadOnlyList<ReportPackDeliveryAttemptDto> attempts)
        : IReportPackDeliveryRecordStore
    {
        public IReadOnlyList<ReportPackDeliveryAttemptDto> Load() => attempts;

        public void Save(IReadOnlyList<ReportPackDeliveryAttemptDto> attempts)
        {
        }
    }

    private sealed class FundStructureAuthoritativeSource : IReportingAuthoritativeSource
    {
        private static readonly Guid FallbackLedgerBookId = Guid.Parse("77777777-7777-7777-7777-777777777777");

        public ValueTask<ReportingAuthoritativeSourceCapture> CaptureAsync(
            ReportingRunParametersDto parameters,
            ReportAccessQueryContext accessContext,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var checkpointHash = new string('9', 64);
            var checkpointId = "ledger-checkpoint-99999999999999999999999999999999";
            var rows = ImmutableArray.Create<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["account"] = "Cash",
                    ["debit"] = "100",
                    ["credit"] = "0",
                    ["netAmount"] = "100"
                });
            var basis = parameters.AccountingBasis switch
            {
                ReportingAccountingBasisDto.Gaap => "Gaap",
                ReportingAccountingBasisDto.Tax => "Tax",
                ReportingAccountingBasisDto.Cash => "Cash",
                ReportingAccountingBasisDto.Statutory => "Statutory",
                _ => "Primary"
            };
            var ledgerBookId = parameters.LedgerBook.LedgerBookId ?? FallbackLedgerBookId;
            var checkpoint = new ReportingAuthoritativeSourceCheckpoint(
                "durable-ledger-journal",
                $"ledger:{ledgerBookId:D}:{parameters.PeriodId}",
                accessContext.TenantId!,
                "organization-a",
                accessContext.CompanyId,
                parameters.Scope.FundProfileId.Trim(),
                ledgerBookId.ToString("D"),
                parameters.PeriodId,
                basis,
                parameters.AsOfDate,
                new DateTimeOffset(
                    parameters.AsOfDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc),
                    TimeSpan.Zero),
                9,
                1,
                rows.Length,
                checkpointId,
                checkpointHash,
                new DateTimeOffset(2026, 5, 5, 0, 0, 0, TimeSpan.Zero),
                [$"reporting-source-checkpoint:{checkpointId}:{checkpointHash}"]);
            return ValueTask.FromResult(new ReportingAuthoritativeSourceCapture(checkpoint, rows));
        }
    }

    private sealed class FundStructureReconciliationEvidenceSource : IReportingReconciliationEvidenceSource
    {
        public ValueTask<ReportingReconciliationEvidenceReceipt> ResolveAsync(
            ReportingRunParametersDto parameters,
            ReportingAuthoritativeSourceCheckpoint source,
            ReportAccessQueryContext accessContext,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ReportingReconciliationEvidenceValidation.CreateReceipt(
                source,
                new ReportingReconciliationCompletionEvidence(
                    $"hard-close-{source.AccountingPeriodId}-v1",
                    new string('c', 64),
                    source.CapturedAtUtc,
                    HasOpenBreaks: false,
                    [$"ledger-period:{source.AccountingPeriodId}:hard-closed"])));
        }
    }

    private sealed class FundStructureGovernanceCoordinator(IReportingOrchestrationService orchestration)
        : IReportingGovernanceEndpointCoordinator
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, GovernedReportingRun> _runs = new(StringComparer.Ordinal);

        public Task<GovernedReportingRun> CreateFromCompletedCertifiedManifestAsync(
            string manifestRunId,
            ReportingGovernanceCallerContext caller,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = orchestration.GetManifest(manifestRunId)
                ?? throw new ReportingGovernanceNotFoundException($"Reporting manifest '{manifestRunId}' was not retained.");
            var scope = manifest.OperationalScope
                ?? throw new ReportingGovernanceException("The manifest is missing its certified operational scope.");
            var access = manifest.ImmutableAccessScope
                ?? throw new ReportingGovernanceException("The manifest is missing its immutable access scope.");
            var snapshot = manifest.CertifiedSnapshot
                ?? throw new ReportingGovernanceException("The manifest is missing its certified source snapshot.");
            var authority = new ReportingAuthorityScope(
                caller.ActorId,
                caller.TenantId,
                scope.OrganizationId,
                caller.CompanyId,
                ImmutableArray.Create(
                    ReportingGovernancePermission.CreateRun,
                    ReportingGovernancePermission.ExecuteRun),
                caller.Origin,
                caller.CorrelationId,
                caller.PrincipalIds);
            var run = new GovernedReportingRun(
                manifest.RunId,
                manifest.RunSeriesId ?? manifest.RunId,
                1,
                manifest.ResolvedTemplate?.Name ?? manifest.TemplateId,
                manifest.ResolvedTemplate?.Version.ToString(CultureInfo.InvariantCulture) ?? "1",
                scope,
                access,
                snapshot,
                authority,
                new DateTimeOffset(2026, 5, 5, 9, 1, 0, TimeSpan.Zero),
                RestatementOfRunId: null,
                ExecutionState: GovernedReportingExecutionState.Succeeded,
                GovernanceState: GovernedReportingState.Draft,
                Version: 3,
                Readiness: null,
                Approval: null,
                Release: null,
                AuditTrail: ImmutableArray<ReportingGovernanceAuditEntry>.Empty);
            lock (_gate)
            {
                _runs[run.RunId] = run;
            }

            return Task.FromResult(run);
        }

        public Task<GovernedReportingRun> GetAsync(
            string runId,
            ReportingGovernanceCallerContext caller,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                return _runs.TryGetValue(runId, out var run)
                    ? Task.FromResult(run)
                    : Task.FromException<GovernedReportingRun>(
                        new ReportingGovernanceNotFoundException($"Reporting run '{runId}' was not found."));
            }
        }

        public Task<IReadOnlyList<GovernedReportingRun>> ListAsync(
            string seriesId,
            ReportingGovernanceCallerContext caller,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                return Task.FromResult<IReadOnlyList<GovernedReportingRun>>(_runs.Values
                    .Where(run => string.Equals(run.SeriesId, seriesId, StringComparison.Ordinal))
                    .ToArray());
            }
        }

        public Task<GovernedReportingRun> ValidateAsync(
            string runId,
            long expectedVersion,
            ReportingGovernanceCallerContext caller,
            CancellationToken cancellationToken = default) =>
            Task.FromException<GovernedReportingRun>(
                new NotSupportedException("The fund-structure test coordinator does not govern lifecycle transitions."));

        public Task<GovernedReportingRun> SubmitAsync(
            string runId,
            long expectedVersion,
            ReportingGovernanceCallerContext caller,
            CancellationToken cancellationToken = default) =>
            Task.FromException<GovernedReportingRun>(
                new NotSupportedException("The fund-structure test coordinator does not govern lifecycle transitions."));

        public Task<GovernedReportingRun> ApproveAsync(
            string runId,
            long expectedVersion,
            string decisionNote,
            ReportingGovernanceCallerContext caller,
            CancellationToken cancellationToken = default) =>
            Task.FromException<GovernedReportingRun>(
                new NotSupportedException("The fund-structure test coordinator does not govern lifecycle transitions."));

        public Task<GovernedReportingRun> ReleaseAsync(
            string runId,
            long expectedVersion,
            ReportingGovernanceCallerContext caller,
            CancellationToken cancellationToken = default) =>
            Task.FromException<GovernedReportingRun>(
                new NotSupportedException("The fund-structure test coordinator does not govern lifecycle transitions."));

        public Task<ReportingRestatementRequest> RequestRestatementAsync(
            string predecessorRunId,
            long expectedPredecessorVersion,
            string reason,
            ReportingGovernanceCallerContext caller,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ReportingRestatementRequest>(
                new NotSupportedException("The fund-structure test coordinator does not govern restatements."));

        public Task<ReportingRestatementApprovalResult> ApproveRestatementAsync(
            string requestId,
            long expectedRequestVersion,
            ReportingGovernanceCallerContext caller,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ReportingRestatementApprovalResult>(
                new NotSupportedException("The fund-structure test coordinator does not govern restatements."));
    }
}
