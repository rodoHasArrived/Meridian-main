using System.Net;
using System.Net.Http.Json;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    public async Task Endpoint_CreateSubmitApprovePublish_CompletesW4LifecycleWithoutUnreachableIntermediateState()
    {
        await using var app = await CreateFundStructureAppAsync(UserRole.Admin);
        var client = app.GetTestClient();
        var request = new ReportPackCreateRequestDto(
            "fund-a",
            "acct-1",
            "2026-03",
            new VersionedReportTemplateIdDto("board-pack", 1));

        var createResponse = await client.PostAsJsonAsync("/api/fund-structure/reporting/packs", request, ServerJsonOptions);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<ReportPackWorkflowRecordDto>(ServerJsonOptions);

        created.Should().NotBeNull();
        created!.State.Should().Be(ReportPackWorkflowStateDto.Draft);

        var submitResponse = await client.PostAsync($"/api/fund-structure/reporting/packs/{created.ReportId:D}/submit", null);
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<ReportPackWorkflowRecordDto>(ServerJsonOptions);

        submitted.Should().NotBeNull();
        submitted!.State.Should().Be(ReportPackWorkflowStateDto.InReview);
        submitted.AuditTrail.Should().ContainSingle(entry =>
            entry.FromState == ReportPackWorkflowStateDto.Draft &&
            entry.ToState == ReportPackWorkflowStateDto.InReview);

        var approveResponse = await client.PostAsync($"/api/fund-structure/reporting/packs/{created.ReportId:D}/approve", null);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var approved = await approveResponse.Content.ReadFromJsonAsync<ReportPackWorkflowRecordDto>(ServerJsonOptions);

        approved.Should().NotBeNull();
        approved!.State.Should().Be(ReportPackWorkflowStateDto.Approved);

        var publishRequest = new ReportPackPublishRequestDto(
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            [new ReportPackEvidenceLinkDto("report-pack-1", "Report pack manifest", "/evidence/report-pack-1", "reporting")],
            BrandingTheme: new ReportBrandingThemeDto(
                "investor-board",
                "Investor Board",
                "Northstar Capital",
                "#123456",
                "#55AA99",
                "#111111",
                "#FFFFFF"));
        var publishResponse = await client.PostAsJsonAsync($"/api/fund-structure/reporting/packs/{created.ReportId:D}/publish", publishRequest, ServerJsonOptions);
        publishResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var published = await publishResponse.Content.ReadFromJsonAsync<ReportPackWorkflowRecordDto>(ServerJsonOptions);

        published.Should().NotBeNull();
        published!.State.Should().Be(ReportPackWorkflowStateDto.Published);
        published.Publication.Should().NotBeNull();
        published.Publication!.BrandingTheme.Should().NotBeNull();
        published.Publication.BrandingTheme!.ThemeId.Should().Be("investor-board");
        published.Publication.BrandingTheme.FirmName.Should().Be("Northstar Capital");
        published.AuditTrail.Select(entry => entry.ToState).Should().ContainInOrder(
            ReportPackWorkflowStateDto.Draft,
            ReportPackWorkflowStateDto.InReview,
            ReportPackWorkflowStateDto.Approved,
            ReportPackWorkflowStateDto.Published);
        published.AuditTrail.Should().NotContain(entry =>
            entry.ToState == ReportPackWorkflowStateDto.Validated ||
            entry.ToState == ReportPackWorkflowStateDto.PendingApproval);
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
            "strategy-contribution");
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
            CompleteLineProvenanceEvidenceLinks("ledger-evidence-1"));
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
        genericRun.NextActions.Should().Contain(action =>
            action.Kind == "approval-submit" &&
            action.Method == "POST" &&
            action.IsEnabled);
        var workflowRun = payload.RecentRuns.Single(run => run.RunId == $"report-pack:{published.ReportId:D}");
        workflowRun.Family.Should().Be("GovernedReportPack");
        workflowRun.AsOfDate.Should().Be(published.Period);
        workflowRun.Status.Should().Be(ReportPackWorkflowStateDto.Restated.ToString());
        workflowRun.Artifacts.Should().Contain(item => item.Contains("/evidence-bundle", StringComparison.OrdinalIgnoreCase));
        workflowRun.Artifacts.Should().Contain("publication-manifest:manifest-1");
        workflowRun.Artifacts.Should().Contain("restatement:NAV_CORRECTION");
        workflowRun.Artifacts.Should().Contain("/evidence/cash-restatement-1");
        workflowRun.DrilldownLinks.Should().Contain(link =>
            link.Kind == "evidence" &&
            link.Label == "Evidence bundle" &&
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
            action.Kind == "archive" &&
            action.Method == "POST" &&
            action.Href.EndsWith($"/reporting/packs/{published.ReportId:D}/archive", StringComparison.Ordinal));
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
    public void ReportPackDeliveryService_PersistsAttemptsAndUpdatesDistributionState()
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
        attempt.Package.AccessLinks.Count(link => link.Kind.StartsWith("artifact-", StringComparison.Ordinal)).Should().Be(3);
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
        packet.EntitlementScope.Should().Be("CompanyWide");
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
            deliveryService: reloaded).BuildPayload();
        payload.DeliveryAttempts.Should().ContainSingle(item =>
            item.AttemptId == attempt.AttemptId &&
            item.Package != null &&
            item.Package.Artifacts.Count == 3);
        payload.ReportPackDistributions.Should().Contain(distribution =>
            distribution.DistributionId == "board-reporting-committee" &&
            distribution.State == "Delivered" &&
            distribution.PendingItems == 0 &&
            distribution.LastSentAtUtc != null);
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
    public async Task Endpoint_DeliveryPackageLink_ReturnsPackageManifestWhenTokenMatches()
    {
        await using var app = await CreateFundStructureAppAsync(UserRole.Admin);
        var workflow = app.Services.GetRequiredService<ReportPackWorkflowService>();
        var delivery = app.Services.GetRequiredService<ReportPackDeliveryService>();
        var created = workflow.Create(
            "fund-a",
            "acct-1",
            "2026-03",
            new VersionedReportTemplateIdDto("board-pack", 1),
            "author");
        workflow.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        workflow.Transition(created.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
        var brandingTheme = new ReportBrandingThemeDto(
            "allocator-quarterly",
            "Allocator Quarterly",
            "Northstar Capital",
            "#123456",
            "#55AA99",
            "#111111",
            "#FFFFFF",
            "/branding/northstar.svg",
            "Northstar Capital confidential",
            "For approved recipients only.",
            IsBuiltIn: false);
        var published = workflow.Publish(
            created.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            [new ReportPackEvidenceLinkDto("report-pack-1", "Report pack manifest", "/evidence/report-pack-1", "reporting")],
            brandingTheme: brandingTheme);
        var attempt = delivery.Deliver(
            published.ReportId,
            new ReportPackDeliveryRequestDto(
                "board-reporting-committee",
                Actor: "fund-controller",
                DeliveryMode: ReportPackDeliveryModeDto.SecurePortal),
            fallbackActor: "fund-controller");
        var client = app.GetTestClient();
        var token = attempt.Package!.SecureLink.Split("token=", 2, StringSplitOptions.None)[1];

        var portalResponse = await client.GetAsync(attempt.Package.SecureLink);
        var emailLinkResponse = await client.GetAsync($"/api/fund-structure/reporting/packs/{published.ReportId:D}/deliveries/{attempt.AttemptId:D}/package?token={token}");
        var badTokenResponse = await client.GetAsync($"/api/fund-structure/reporting/packs/{published.ReportId:D}/deliveries/{attempt.AttemptId:D}/package?token=bad-token");

        portalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var portalPackage = await portalResponse.Content.ReadFromJsonAsync<ReportPackDeliveryPackageDto>(ServerJsonOptions);
        portalPackage.Should().NotBeNull();
        portalPackage!.PackageId.Should().Be(attempt.Package.PackageId);
        portalPackage.DeliveryMode.Should().Be(ReportPackDeliveryModeDto.SecurePortal);
        portalPackage.Artifacts.Should().HaveCount(3);
        portalPackage.PublicationEvidenceHash.Should().Be("sha256:abc123");
        portalPackage.IntegritySummary.Should().Be("3 artifact(s) retained with SHA-256 checksums against publication evidence hash sha256:abc123.");
        portalPackage.BrandingTheme.Should().NotBeNull();
        portalPackage.BrandingTheme!.ThemeId.Should().Be("allocator-quarterly");
        portalPackage.BrandingTheme.FirmName.Should().Be("Northstar Capital");
        portalPackage.AccessLinks.Should().NotBeNull();
        portalPackage.AccessLinks!.Should().Contain(link =>
            link.Kind == "secure-portal" &&
            link.Href == attempt.Package.SecureLink &&
            link.RequiresToken);
        portalPackage.AccessLinks.Should().Contain(link =>
            link.Kind == "operator-route" &&
            link.Href == attempt.Package.PortalRoute &&
            !link.RequiresToken);
        portalPackage.AccessLinks.Count(link => link.Kind.StartsWith("artifact-", StringComparison.Ordinal)).Should().Be(3);
        portalPackage.DeliveryEvidencePacket.Should().NotBeNull();
        portalPackage.DeliveryEvidencePacket!.RecipientList.Should().ContainSingle(recipient =>
            recipient.DistributionId == "board-reporting-committee" &&
            recipient.Recipient == "Board reporting committee");
        portalPackage.DeliveryEvidencePacket.PackageContents.Should().Contain("branding-theme:allocator-quarterly");
        portalPackage.DeliveryEvidencePacket.DeliveryEvidence.Should().OnlyContain(link =>
            link.Source == "report-pack-delivery");
        var deliveryArtifactVersionPrefix = $"delivery-artifact:{published.ReportId:N}:board-reporting-committee:";
        portalPackage.Artifacts.Should().OnlyContain(artifact =>
            artifact.ChecksumSha256.Length == 64 &&
            artifact.VersionStamp.StartsWith(deliveryArtifactVersionPrefix, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(artifact.DownloadRoute));
        var csvArtifact = portalPackage.Artifacts.Should()
            .ContainSingle(artifact => artifact.Format == GovernanceReportArtifactFormatDto.Csv)
            .Subject;
        var pdfArtifact = portalPackage.Artifacts.Should()
            .ContainSingle(artifact => artifact.Format == GovernanceReportArtifactFormatDto.Pdf)
            .Subject;
        var xlsxArtifact = portalPackage.Artifacts.Should()
            .ContainSingle(artifact => artifact.Format == GovernanceReportArtifactFormatDto.Xlsx)
            .Subject;
        csvArtifact.DownloadRoute.Should().Contain($"/api/fund-structure/reporting/packs/{published.ReportId:D}/deliveries/{attempt.AttemptId:D}/artifacts/");

        emailLinkResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var emailPackage = await emailLinkResponse.Content.ReadFromJsonAsync<ReportPackDeliveryPackageDto>(ServerJsonOptions);
        emailPackage.Should().NotBeNull();
        emailPackage!.PackageId.Should().Be(attempt.Package.PackageId);
        var artifactResponse = await client.GetAsync(csvArtifact.DownloadRoute);
        artifactResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        artifactResponse.Content.Headers.ContentType?.MediaType.Should().Be("text/csv");
        artifactResponse.Content.Headers.ContentDisposition?.FileName.Should().Be(csvArtifact.ArtifactName);
        artifactResponse.Content.Headers.ContentLength.Should().Be(csvArtifact.ByteSize);
        var artifactCsv = await artifactResponse.Content.ReadAsStringAsync();
        artifactCsv.Should().Contain($"packageId,{portalPackage.PackageId}");
        artifactCsv.Should().Contain($"publicationEvidenceHash,{portalPackage.PublicationEvidenceHash}");
        artifactCsv.Should().Contain("brandingThemeId,allocator-quarterly");
        artifactCsv.Should().Contain("brandingFirmName,Northstar Capital");
        artifactCsv.Should().Contain("brandingDisclaimer,For approved recipients only.");
        var pdfResponse = await client.GetAsync(pdfArtifact.DownloadRoute);
        pdfResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        pdfResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
        pdfResponse.Content.Headers.ContentLength.Should().Be(pdfArtifact.ByteSize);
        var pdfBytes = await pdfResponse.Content.ReadAsByteArrayAsync();
        System.Text.Encoding.ASCII.GetString(pdfBytes.Take(8).ToArray()).Should().StartWith("%PDF-1.");
        System.Text.Encoding.ASCII.GetString(pdfBytes).Should().Contain("allocator-quarterly");

        var xlsxResponse = await client.GetAsync(xlsxArtifact.DownloadRoute);
        xlsxResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        xlsxResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        xlsxResponse.Content.Headers.ContentLength.Should().Be(xlsxArtifact.ByteSize);
        var xlsxBytes = await xlsxResponse.Content.ReadAsByteArrayAsync();
        xlsxBytes.Should().StartWith([0x50, 0x4B]);
        using (var workbook = new ZipArchive(new MemoryStream(xlsxBytes), ZipArchiveMode.Read))
        {
            workbook.GetEntry("[Content_Types].xml").Should().NotBeNull();
            var workbookXmlEntry = workbook.GetEntry("xl/workbook.xml");
            workbookXmlEntry.Should().NotBeNull();
            workbook.GetEntry("xl/worksheets/sheet1.xml").Should().NotBeNull();
            workbook.GetEntry("xl/worksheets/sheet2.xml").Should().NotBeNull();
            using var workbookXmlReader = new StreamReader(workbookXmlEntry!.Open());
            var workbookXml = workbookXmlReader.ReadToEnd();
            workbookXml.Should().Contain("Branding");
            var sharedStringsEntry = workbook.GetEntry("xl/sharedStrings.xml");
            sharedStringsEntry.Should().NotBeNull();
            using var sharedStringsReader = new StreamReader(sharedStringsEntry!.Open());
            var sharedStringsXml = sharedStringsReader.ReadToEnd();
            sharedStringsXml.Should().Contain("allocator-quarterly");
            sharedStringsXml.Should().Contain("Allocator Quarterly");
            sharedStringsXml.Should().Contain("Northstar Capital");
            sharedStringsXml.Should().Contain("/branding/northstar.svg");
            sharedStringsXml.Should().Contain("For approved recipients only.");
        }
        var badArtifactResponse = await client.GetAsync(csvArtifact.DownloadRoute!.Replace(token, "bad-token", StringComparison.Ordinal));
        badTokenResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        badArtifactResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Endpoint_StructuredReportingExport_ReturnsJsonCsvAndXlsxWhenFormatRequested()
    {
        await using var app = await CreateFundStructureAppAsync(
            UserRole.Admin,
            workspaceService: CreateStructuredExportWorkspaceService());
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/fund-structure/reporting/structured-exports/regulatory-trial-balance?fundProfileId=fund-alpha&asOf=2026-04-11T16%3A00%3A00Z&currency=USD&format=csv");
        var xlsxResponse = await client.GetAsync("/api/fund-structure/reporting/structured-exports/investment-portfolio-cuts?fundProfileId=fund-alpha&asOf=2026-04-11T16%3A00%3A00Z&currency=USD&format=xlsx");
        var warehouseJsonResponse = await client.GetAsync("/api/fund-structure/reporting/structured-exports/warehouse-ledger-facts?fundProfileId=fund-alpha&asOf=2026-04-11T16%3A00%3A00Z&currency=USD");
        var warehouseJsonDownloadResponse = await client.GetAsync("/api/fund-structure/reporting/structured-exports/warehouse-ledger-facts?fundProfileId=fund-alpha&asOf=2026-04-11T16%3A00%3A00Z&currency=USD&format=json");
        var warehouseCsvResponse = await client.GetAsync("/api/fund-structure/reporting/structured-exports/warehouse-ledger-facts?fundProfileId=fund-alpha&asOf=2026-04-11T16%3A00%3A00Z&currency=USD&format=csv");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/csv");
        response.Content.Headers.ContentDisposition?.FileName.Should().Be("regulatory-trial-balance-20260411160000.csv");
        var csv = await response.Content.ReadAsStringAsync();
        csv.Should().StartWith("accountName,accountType,symbol,financialAccountId,currency,balance,entryCount,securityId,securityDisplayName,sourceAsOfUtc");
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
            using var workbookXmlReader = new StreamReader(structuredWorkbook.GetEntry("xl/workbook.xml")!.Open());
            var workbookXml = workbookXmlReader.ReadToEnd();
            workbookXml.Should().Contain("portfolio-reporting-cuts");
            workbookXml.Should().Contain("Metadata");
            workbookXml.Should().Contain("DataDictionary");
            workbookXml.Should().Contain("Validation");
        }
        warehouseJsonResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        warehouseJsonResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        var warehousePayload = await warehouseJsonResponse.Content.ReadFromJsonAsync<StructuredReportingExportPayloadDto>(ServerJsonOptions);
        warehousePayload.Should().NotBeNull();
        warehousePayload!.Export.ExportId.Should().Be("warehouse-ledger-facts");
        warehousePayload.Export.Purpose.Should().Be(StructuredReportingExportPurposeDto.DataWarehouse);
        warehousePayload.DataDictionary.Should().NotBeNull();
        warehousePayload.DataDictionary!.Should().Contain(field =>
            field.Name == "ledgerEntryCount" &&
            field.DataType == "integer" &&
            field.Ordinal == 8);
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
            "balance",
            "journalEntryCount",
            "ledgerEntryCount",
            "sourceAsOfUtc");
        warehouseJsonDownloadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        warehouseJsonDownloadResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        warehouseJsonDownloadResponse.Content.Headers.ContentDisposition?.FileName.Should().Be("warehouse-ledger-facts-20260411160000.json");
        var warehouseJsonDownload = await warehouseJsonDownloadResponse.Content.ReadAsStringAsync();
        warehouseJsonDownload.Should().Contain("\"exportId\":\"warehouse-ledger-facts\"");
        warehouseJsonDownload.Should().Contain("\"rows\"");
        warehouseCsvResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        warehouseCsvResponse.Content.Headers.ContentType?.MediaType.Should().Be("text/csv");
        warehouseCsvResponse.Content.Headers.ContentDisposition?.FileName.Should().Be("warehouse-ledger-facts-20260411160000.csv");
        var warehouseCsv = await warehouseCsvResponse.Content.ReadAsStringAsync();
        warehouseCsv.Should().StartWith("scope,accountName,accountType,symbol,financialAccountId,balance,journalEntryCount,ledgerEntryCount,sourceAsOfUtc");
    }

    [Fact]
    public async Task ReportingScheduleService_PersistsSchedulesAndRunsDueSchedules()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var runStore = new FileReportingRunStore(
            new ReportingRunStoreOptions(Path.Combine(root, "reporting-runs")),
            NullLogger<FileReportingRunStore>.Instance);
        var orchestration = new ReportingOrchestrationService(
            new DefaultReportingTemplateCatalog(),
            new DeterministicReportingSectionRenderer(),
            () => new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero),
            runStore);
        var scheduleStore = new FileReportingScheduleStore(
            new ReportingScheduleStoreOptions(Path.Combine(root, "reporting-schedules.json")),
            NullLogger<FileReportingScheduleStore>.Instance);
        var schedules = new ReportingScheduleService(orchestration, scheduleStore);

        var created = schedules.Upsert(new ReportingScheduleUpsertRequestDto(
            "sched-investor",
            "investor-monthly-statement",
            "0 8 1 * *",
            new DateOnly(2026, 5, 1),
            new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero),
            2,
            "fund-controller",
            "Monthly investor statement close packet."));

        created.State.Should().Be(ReportingScheduleStateDto.Active);
        var due = await schedules.RunDueAsync(new DateTimeOffset(2026, 5, 1, 8, 5, 0, TimeSpan.Zero));

        due.Runs.Should().ContainSingle();
        var result = due.Runs.Single();
        result.Run.RunId.Should().Be("sched-investor-20260501");
        result.Run.TemplateId.Should().Be("investor-monthly-statement");
        result.Schedule.RunCount.Should().Be(1);
        result.Schedule.LastRunId.Should().Be(result.Run.RunId);
        result.Schedule.DueAtUtc.Should().Be(new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero));
        result.Schedule.NextAsOfDate.Should().Be(new DateOnly(2026, 6, 1));

        var reloaded = new ReportingScheduleService(orchestration, scheduleStore);
        reloaded.ListSchedules().Should().ContainSingle(schedule =>
            schedule.ScheduleId == "sched-investor" &&
            schedule.LastRunId == result.Run.RunId &&
            schedule.RunCount == 1);
    }

    [Fact]
    public async Task ReportingScheduleService_DeliversConfiguredReportPackTargetsAfterScheduledRun()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var workflow = new ReportPackWorkflowService();
        var approved = CreateApprovedPack(
            workflow,
            [CompleteLineProvenance("trial-balance.cash", "ledger-evidence-1")],
            "holdings-board-report");
        var published = workflow.Publish(
            approved.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:scheduled-pack",
            "manifest-scheduled",
            "vault/report-packs/manifest-scheduled.json",
            CompleteLineProvenanceEvidenceLinks("ledger-evidence-1"));
        var deliveryStore = new FileReportPackDeliveryRecordStore(
            new ReportPackDeliveryStoreOptions(Path.Combine(root, "report-pack-deliveries.json")),
            NullLogger<FileReportPackDeliveryRecordStore>.Instance);
        var delivery = new ReportPackDeliveryService(workflow, deliveryStore);
        var runStore = new FileReportingRunStore(
            new ReportingRunStoreOptions(Path.Combine(root, "reporting-runs")),
            NullLogger<FileReportingRunStore>.Instance);
        var orchestration = new ReportingOrchestrationService(
            new DefaultReportingTemplateCatalog(),
            new DeterministicReportingSectionRenderer(),
            () => new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero),
            runStore);
        var scheduleStore = new FileReportingScheduleStore(
            new ReportingScheduleStoreOptions(Path.Combine(root, "reporting-schedules.json")),
            NullLogger<FileReportingScheduleStore>.Instance);
        var schedules = new ReportingScheduleService(orchestration, scheduleStore, delivery);

        var created = schedules.Upsert(new ReportingScheduleUpsertRequestDto(
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
                    [GovernanceReportArtifactFormatDto.Pdf, GovernanceReportArtifactFormatDto.Xlsx, GovernanceReportArtifactFormatDto.Csv],
                    ReportPackDeliveryModeDto.SecurePortal,
                    "Board portal delivery."),
                new ReportingScheduleDeliveryTargetDto(
                    "investor-relations",
                    [GovernanceReportArtifactFormatDto.Pdf, GovernanceReportArtifactFormatDto.Csv],
                    ReportPackDeliveryModeDto.EmailLink,
                    "Investor email-link delivery.")
            ]));

        created.DeliveryTargets.Should().HaveCount(2);
        var due = await schedules.RunDueAsync(new DateTimeOffset(2026, 5, 1, 8, 5, 0, TimeSpan.Zero));

        due.Runs.Should().ContainSingle();
        var result = due.Runs.Single();
        result.Run.RunId.Should().Be("sched-board-distribution-20260501");
        result.DeliveryWarnings.Should().NotBeNull().And.BeEmpty();
        result.DeliveryAttempts.Should().NotBeNull();
        var attempts = result.DeliveryAttempts!;
        attempts.Should().HaveCount(2);
        attempts.Should().Contain(attempt =>
            attempt.ReportId == published.ReportId &&
            attempt.DistributionId == "board-reporting-committee" &&
            attempt.Package != null &&
            attempt.Package.DeliveryMode == ReportPackDeliveryModeDto.SecurePortal &&
            attempt.Package.Artifacts.Count == 3);
        attempts.Should().Contain(attempt =>
            attempt.ReportId == published.ReportId &&
            attempt.DistributionId == "investor-relations" &&
            attempt.Package != null &&
            attempt.Package.DeliveryMode == ReportPackDeliveryModeDto.EmailLink &&
            attempt.Package.Formats.SequenceEqual(new[] { GovernanceReportArtifactFormatDto.Pdf, GovernanceReportArtifactFormatDto.Csv }));
        attempts.Should().OnlyContain(attempt =>
            attempt.DeliveryReference.StartsWith("schedule:holdings-board-report:", StringComparison.Ordinal));

        var reloadedDelivery = new ReportPackDeliveryService(workflow, deliveryStore);
        reloadedDelivery.GetHistory(published.ReportId).Should().HaveCount(2);
        var reloadedSchedules = new ReportingScheduleService(orchestration, scheduleStore, reloadedDelivery);
        reloadedSchedules.ListSchedules().Should().ContainSingle(schedule =>
            schedule.ScheduleId == "sched-board-distribution" &&
            schedule.DeliveryTargets != null &&
            schedule.DeliveryTargets.Count == 2);
        var payload = new ReportPackRunReadService(
            new DefaultReportingTemplateCatalog(),
            workflowService: workflow,
            deliveryService: reloadedDelivery,
            scheduleService: reloadedSchedules).BuildPayload();
        payload.ScheduleDeliveryPlans.Should().NotBeNull().And.HaveCount(2);
        var boardPlan = payload.ScheduleDeliveryPlans!.Single(plan => plan.DistributionId == "board-reporting-committee");
        boardPlan.ScheduleId.Should().Be("sched-board-distribution");
        boardPlan.TemplateId.Should().Be("holdings-board-report");
        boardPlan.Recipient.Should().Be("Board reporting committee");
        boardPlan.DeliveryMode.Should().Be(ReportPackDeliveryModeDto.SecurePortal);
        boardPlan.Formats.Should().Equal(
            GovernanceReportArtifactFormatDto.Pdf,
            GovernanceReportArtifactFormatDto.Xlsx,
            GovernanceReportArtifactFormatDto.Csv);
        boardPlan.IsReady.Should().BeTrue();
        boardPlan.ReadinessSummary.Should().Contain("Ready to deliver Pdf/Xlsx/Csv by SecurePortal to Board reporting committee");
        boardPlan.ReadinessBlockers.Should().NotBeNull().And.BeEmpty();
        boardPlan.LastDeliveryState.Should().Be(ReportPackDeliveryStateDto.Delivered);
        boardPlan.LastDeliveryAttemptId.Should().NotBeNull();
        boardPlan.LastDeliverySecureLink.Should().NotBeNullOrWhiteSpace();
        boardPlan.LastDeliveryAccessLinks.Should().NotBeNull();
        boardPlan.LastDeliveryAccessLinks!.Should().Contain(link =>
            link.Kind == "secure-portal" &&
            link.Href == boardPlan.LastDeliverySecureLink &&
            link.RequiresToken);
        boardPlan.LastDeliveryArtifactCount.Should().Be(3);
        boardPlan.LastDeliveryIntegritySummary.Should().Be("3 artifact(s) retained with SHA-256 checksums against publication evidence hash sha256:scheduled-pack.");
        boardPlan.VersionStamp.Should().StartWith("schedule-delivery-plan:sched-board-distribution:board-reporting-committee:");
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
    public async Task ReportingScheduleService_DeliversGeneratedReportWriterRunWhenNoPublishedPackExists()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var registry = new ReportTemplateRegistryService();
        var draft = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "scheduled-report-writer-pack",
                "Scheduled Report Writer Pack",
                Sections: [],
                Parameters: [],
                Family: ReportingTemplateFamily.CustomReport.ToString(),
                Rationale: "No-code custom report pack for scheduled investor delivery.",
                Grids:
                [
                    new ReportWriterGridDefinitionDto(
                        "sector-pivot",
                        "Sector Pivot",
                        ReportWriterGridKindDto.Pivot,
                        RowFields: ["sector"],
                        Metrics:
                        [
                            new ReportWriterMetricDefinitionDto("marketValue", "marketValue", ReportWriterAggregateFunctionDto.Sum, "Market value")
                        ],
                        Formulas:
                        [
                            new ReportWriterFormulaDefinitionDto("weightPct", "{marketValue} / total(marketValue) * 100", "Weight %")
                        ]),
                    new ReportWriterGridDefinitionDto(
                        "top-laggards",
                        "Top Laggards",
                        ReportWriterGridKindDto.TopN,
                        RowFields: ["security"],
                        Metrics:
                        [
                            new ReportWriterMetricDefinitionDto("totalPnl", "totalPnl", ReportWriterAggregateFunctionDto.Sum, "Total P&L")
                        ],
                        TopN: 5,
                        SortBy: "totalPnl",
                        SortDescending: false)
                ]),
            "report-owner");
        registry.Submit(draft.Definition.TemplateId, "report-owner", "Ready for scheduled delivery.");
        registry.Approve(
            draft.Definition.TemplateId,
            new ReportTemplateDecisionRequestDto("Approved for scheduled investor distribution.", "approval:schedule-report-writer"),
            "ops-lead");

        var catalog = new GovernedReportingTemplateCatalog(new DefaultReportingTemplateCatalog(), registry);
        var runStore = new FileReportingRunStore(
            new ReportingRunStoreOptions(Path.Combine(root, "reporting-runs")),
            NullLogger<FileReportingRunStore>.Instance);
        var orchestration = new ReportingOrchestrationService(
            catalog,
            new DeterministicReportingSectionRenderer(),
            () => new DateTimeOffset(2026, 5, 2, 8, 0, 0, TimeSpan.Zero),
            runStore);
        var workflow = new ReportPackWorkflowService();
        var deliveryStore = new FileReportPackDeliveryRecordStore(
            new ReportPackDeliveryStoreOptions(Path.Combine(root, "report-pack-deliveries.json")),
            NullLogger<FileReportPackDeliveryRecordStore>.Instance);
        var delivery = new ReportPackDeliveryService(workflow, deliveryStore);
        var scheduleStore = new FileReportingScheduleStore(
            new ReportingScheduleStoreOptions(Path.Combine(root, "reporting-schedules.json")),
            NullLogger<FileReportingScheduleStore>.Instance);
        var schedules = new ReportingScheduleService(orchestration, scheduleStore, delivery, catalog);

        schedules.Upsert(new ReportingScheduleUpsertRequestDto(
            "sched-custom-writer",
            "scheduled-report-writer-pack",
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
                    [GovernanceReportArtifactFormatDto.Pdf, GovernanceReportArtifactFormatDto.Xlsx, GovernanceReportArtifactFormatDto.Csv],
                    ReportPackDeliveryModeDto.EmailLink,
                    "Investor email-link package from generated report run.")
            ],
            DatasetRows:
            [
                new Dictionary<string, string>
                {
                    ["sector"] = "Credit",
                    ["security"] = "ABC Loan",
                    ["marketValue"] = "150",
                    ["totalPnl"] = "-5"
                },
                new Dictionary<string, string>
                {
                    ["sector"] = "Equity",
                    ["security"] = "XYZ Equity",
                    ["marketValue"] = "50",
                    ["totalPnl"] = "12"
                },
                new Dictionary<string, string>
                {
                    ["sector"] = "Credit",
                    ["security"] = "DEF Loan",
                    ["marketValue"] = "50",
                    ["totalPnl"] = "-9"
                }
            ]));

        var due = await schedules.RunDueAsync(new DateTimeOffset(2026, 5, 2, 8, 1, 0, TimeSpan.Zero));

        due.Runs.Should().ContainSingle();
        var result = due.Runs.Single();
        result.Run.RunId.Should().Be("sched-custom-writer-20260502");
        result.Run.Artifacts.Should().Contain("report-writer://sched-custom-writer-20260502/grids/sector-pivot");
        result.Run.Artifacts.Should().Contain("report-writer://sched-custom-writer-20260502/grids/top-laggards");
        result.Run.GeneratedReportWriterGrids.Should().NotBeNull();
        result.Run.GeneratedReportWriterGrids!.Should().Contain(grid =>
            grid.GridId == "sector-pivot" &&
            grid.Title == "Sector Pivot" &&
            grid.Kind == ReportWriterGridKindDto.Pivot.ToString() &&
            grid.Artifact == "report-writer://sched-custom-writer-20260502/grids/sector-pivot" &&
            grid.DimensionCount == 1 &&
            grid.MetricCount == 1 &&
            grid.FormulaCount == 1);
        var manifest = runStore.GetManifest(result.Run.RunId);
        manifest.Should().NotBeNull();
        var renderedSectorPivot = manifest!.RenderedReportWriterGrids.FirstOrDefault(static grid =>
            grid.GridId == "sector-pivot");
        renderedSectorPivot.Should().NotBeNull();
        renderedSectorPivot!.Lineage.Should().NotBeNull();
        renderedSectorPivot.Lineage!.InputRowCount.Should().Be(3);
        renderedSectorPivot.Rows.Any(static row =>
            row.Values.TryGetValue("sector", out var sector) &&
            sector == "Credit" &&
            row.Values.TryGetValue("marketValue", out var marketValue) &&
            marketValue == "200").Should().BeTrue();

        var renderedTopLaggards = manifest.RenderedReportWriterGrids.FirstOrDefault(static grid =>
            grid.GridId == "top-laggards");
        renderedTopLaggards.Should().NotBeNull();
        renderedTopLaggards!.Rows.Any(static row =>
            row.Values.TryGetValue("security", out var security) &&
            security == "DEF Loan" &&
            row.Values.TryGetValue("totalPnl", out var totalPnl) &&
            totalPnl == "-9").Should().BeTrue();
        result.DeliveryWarnings.Should().NotBeNull().And.BeEmpty();
        result.DeliveryAttempts.Should().NotBeNull().And.ContainSingle();
        var attempt = result.DeliveryAttempts!.Single();
        attempt.DistributionId.Should().Be("investor-relations");
        attempt.ReportId.Should().NotBeEmpty();
        attempt.DeliveryReference.Should().Be("schedule:scheduled-report-writer-pack:sched-custom-writer-20260502:investor-relations");
        attempt.Package.Should().NotBeNull();
        attempt.Package!.DeliveryMode.Should().Be(ReportPackDeliveryModeDto.EmailLink);
        attempt.Package.GeneratedReportWriterGrids.Should().NotBeNull();
        attempt.Package.GeneratedReportWriterGrids!.Should().Contain(grid =>
            grid.GridId == "sector-pivot" &&
            grid.Artifact == "report-writer://sched-custom-writer-20260502/grids/sector-pivot" &&
            grid.FormulaCount == 1);
        attempt.Package.RenderedReportWriterGrids.Should().NotBeNull();
        var packageSectorPivot = attempt.Package.RenderedReportWriterGrids!.Should().Contain(grid =>
            grid.GridId == "sector-pivot" &&
            grid.Rows.Count == 2 &&
            grid.Warnings.Count == 0).Subject;
        packageSectorPivot.DataDictionary.Should().NotBeNull();
        packageSectorPivot.DataDictionary!.Should().Contain(field =>
            field.Key == "marketValue" &&
            field.SourceField == "marketValue" &&
            field.DataType == "decimal" &&
            !field.IsGenerated);
        packageSectorPivot.DataDictionary.Should().Contain(field =>
            field.Key == "weightPct" &&
            field.IsGenerated);
        packageSectorPivot.ValidationChecks.Should().NotBeNull();
        packageSectorPivot.ValidationChecks!.Should().Contain(check =>
            check.CheckId == "source-field-lineage" &&
            check.Status == "Passed");
        attempt.Package.Formats.Should().Equal(
            GovernanceReportArtifactFormatDto.Pdf,
            GovernanceReportArtifactFormatDto.Xlsx,
            GovernanceReportArtifactFormatDto.Csv);
        attempt.Package.Artifacts.Should().HaveCount(3);
        attempt.Package.PortalRoute.Should().Be("/reporting/runs/sched-custom-writer-20260502/packages/" + attempt.Package.PackageId);
        attempt.Package.SecureLink.Should().Contain("/package?token=");
        attempt.Package.IntegritySummary.Should().Be("3 artifact(s) retained with SHA-256 checksums without a publication evidence hash.");
        attempt.Package.DeliveryEvidencePacket.Should().NotBeNull();
        var packet = attempt.Package.DeliveryEvidencePacket!;
        packet.PacketKind.Should().Be("ReportingRunDelivery");
        packet.DatasetVersion.Should().Be("sched-custom-writer-20260502");
        packet.TemplateVersion.Should().Be("scheduled-report-writer-pack");
        packet.DeliveryChannel.Should().Be("EmailLink via Investor portal");
        packet.RecipientList.Should().ContainSingle(recipient =>
            recipient.DistributionId == "investor-relations" &&
            recipient.Recipient == "Investor relations");
        packet.PackageContents.Should().Contain("source-artifact:report-writer://sched-custom-writer-20260502/grids/sector-pivot");
        packet.SupportEvidenceIds.Should().Contain("reporting-run-source:report-writer://sched-custom-writer-20260502/grids/sector-pivot");
        packet.DeliveryEvidence.Should().HaveCount(3);
        packet.RequestHistory.Should().Contain("delivery-request:investor-relations");

        var token = attempt.Package.SecureLink.Split("token=", 2, StringSplitOptions.None)[1];
        delivery.GetPackage(attempt.ReportId, attempt.AttemptId, token).PackageId.Should().Be(attempt.Package.PackageId);
        var pdfArtifact = attempt.Package.Artifacts.Single(artifact => artifact.Format == GovernanceReportArtifactFormatDto.Pdf);
        var pdfContent = delivery.GetArtifact(attempt.ReportId, attempt.AttemptId, pdfArtifact.ArtifactName, token);
        pdfContent.ContentType.Should().Be("application/pdf");
        pdfContent.Content.LongLength.Should().Be(pdfArtifact.ByteSize);
        System.Text.Encoding.ASCII.GetString(pdfContent.Content.Take(8).ToArray()).Should().StartWith("%PDF-1.");
        System.Text.Encoding.ASCII.GetString(pdfContent.Content).Should().Contain("sched-custom-writer-20260502");
        var xlsxArtifact = attempt.Package.Artifacts.Single(artifact => artifact.Format == GovernanceReportArtifactFormatDto.Xlsx);
        var xlsxContent = delivery.GetArtifact(attempt.ReportId, attempt.AttemptId, xlsxArtifact.ArtifactName, token);
        xlsxContent.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        xlsxContent.Content.LongLength.Should().Be(xlsxArtifact.ByteSize);
        xlsxContent.Content.Should().StartWith([0x50, 0x4B]);
        using (var workbook = new ZipArchive(new MemoryStream(xlsxContent.Content), ZipArchiveMode.Read))
        {
            workbook.GetEntry("[Content_Types].xml").Should().NotBeNull();
            workbook.GetEntry("xl/workbook.xml").Should().NotBeNull();
            workbook.GetEntry("xl/worksheets/sheet1.xml").Should().NotBeNull();
            workbook.GetEntry("xl/worksheets/sheet2.xml").Should().NotBeNull();
            workbook.GetEntry("xl/worksheets/sheet3.xml").Should().NotBeNull();
            workbook.GetEntry("xl/worksheets/sheet4.xml").Should().NotBeNull();
            workbook.GetEntry("xl/worksheets/sheet5.xml").Should().NotBeNull();
            var workbookXmlEntry = workbook.GetEntry("xl/workbook.xml");
            using var workbookXmlReader = new StreamReader(workbookXmlEntry!.Open());
            var workbookXml = workbookXmlReader.ReadToEnd();
            workbookXml.Should().Contain("ReportWriterGrids");
            workbookXml.Should().Contain("ReportWriterGridRows");
            workbookXml.Should().Contain("ReportWriterDictionary");
            workbookXml.Should().Contain("ReportWriterValidation");
        }
        var csvArtifact = attempt.Package.Artifacts.Single(artifact => artifact.Format == GovernanceReportArtifactFormatDto.Csv);
        var csv = System.Text.Encoding.UTF8.GetString(delivery.GetArtifact(attempt.ReportId, attempt.AttemptId, csvArtifact.ArtifactName, token).Content);
        csv.Should().Contain("reportingRunId,sched-custom-writer-20260502");
        csv.Should().Contain("sourceArtifacts");
        csv.Should().Contain("report-writer://sched-custom-writer-20260502/grids/sector-pivot");
        csv.Should().Contain("generatedReportWriterGridCount,2");
        csv.Should().Contain("generatedReportWriterGrids[0].gridId,sector-pivot");
        csv.Should().Contain("generatedReportWriterGrids[0].formulaCount,1");
        csv.Should().Contain("renderedReportWriterGridCount,2");
        csv.Should().Contain("renderedReportWriterGrids[0].lineage.inputRowCount,3");
        csv.Should().Contain("renderedReportWriterGrids[0].rowCount,2");
        csv.Should().Contain("renderedReportWriterGrids[0].rows[0].values.marketValue,200");
        csv.Should().Contain("renderedReportWriterGrids[1].rows[0].values.security,DEF Loan");
        csv.Should().Contain("renderedReportWriterGrids[0].dataDictionary.fieldCount,3");
        csv.Should().Contain("renderedReportWriterGrids[0].dataDictionary[1].sourceField,marketValue");
        csv.Should().Contain("renderedReportWriterGrids[0].dataDictionary[2].isGenerated,true");
        csv.Should().Contain("renderedReportWriterGrids[0].validationChecks.checkCount,4");
        csv.Should().Contain("renderedReportWriterGrids[0].validationChecks[2].checkId,source-field-lineage");

        var reloadedDelivery = new ReportPackDeliveryService(workflow, deliveryStore);
        var reloadedSchedules = new ReportingScheduleService(orchestration, scheduleStore, reloadedDelivery, catalog);
        var payload = new ReportPackRunReadService(
            catalog,
            runStore,
            workflowService: workflow,
            templateRegistry: registry,
            deliveryService: reloadedDelivery,
            scheduleService: reloadedSchedules).BuildPayload(new ReportAccessQueryContext("report-owner"));
        payload.RecentRuns.Should().Contain(run =>
            run.RunId == "sched-custom-writer-20260502" &&
            run.GeneratedReportWriterGrids != null &&
            run.GeneratedReportWriterGrids.Any(grid =>
                grid.GridId == "top-laggards" &&
                grid.Title == "Top Laggards" &&
                grid.Kind == ReportWriterGridKindDto.TopN.ToString() &&
                grid.Artifact == "report-writer://sched-custom-writer-20260502/grids/top-laggards" &&
                grid.DimensionCount == 1 &&
                grid.MetricCount == 1 &&
                grid.FormulaCount == 0));
        var plan = payload.ScheduleDeliveryPlans.Should().ContainSingle().Subject;
        plan.LastDeliveryState.Should().Be(ReportPackDeliveryStateDto.Delivered);
        plan.LastDeliverySecureLink.Should().Be(attempt.Package.SecureLink);
        plan.LastDeliveryAccessLinks.Should().NotBeNull();
        plan.LastDeliveryAccessLinks!.Should().Contain(link =>
            link.Kind == "email-link" &&
            link.Href == attempt.Package.SecureLink &&
            link.RequiresToken);
        plan.LastDeliveryArtifactCount.Should().Be(3);
        plan.LastDeliveryIntegritySummary.Should().Be("3 artifact(s) retained with SHA-256 checksums without a publication evidence hash.");
        var reloadedAttempt = reloadedDelivery.GetHistory(attempt.ReportId).Should().ContainSingle().Subject;
        reloadedAttempt.Package!.DeliveryEvidencePacket.Should().NotBeNull();
        reloadedAttempt.Package.DeliveryEvidencePacket!.PacketKind.Should().Be("ReportingRunDelivery");
        reloadedAttempt.Package.RenderedReportWriterGrids.Should().NotBeNull();
        reloadedAttempt.Package.RenderedReportWriterGrids!.Any(static grid =>
            grid.GridId == "top-laggards" &&
            grid.Rows.Any(row =>
                row.Values.TryGetValue("security", out var security) &&
                security == "DEF Loan")).Should().BeTrue();
        reloadedAttempt.Package.RenderedReportWriterGrids!.Should().Contain(grid =>
            grid.GridId == "sector-pivot" &&
            grid.DataDictionary != null &&
            grid.DataDictionary.Any(field => field.Key == "weightPct" && field.IsGenerated) &&
            grid.ValidationChecks != null &&
            grid.ValidationChecks.Any(check => check.CheckId == "column-dictionary" && check.Status == "Passed"));
        reloadedAttempt.Package.ReportingRunAsOfDate.Should().Be("2026-05-02");
        reloadedAttempt.Package.ReportingRunStatus.Should().Be(ReportingRunStatus.Draft.ToString());
        reloadedAttempt.Package.ReportingRunTrigger.Should().Be(ReportingRunTrigger.Scheduled.ToString());
        reloadedAttempt.Package.ReportingRunAttemptCount.Should().Be(1);
        reloadedAttempt.Package.ReportingRunSectionCount.Should().Be(result.Run.SectionCount);
        reloadedAttempt.Package.ReportingRunLineageLinkedSections.Should().Be(result.Run.LineageLinkedSections);
        var reloadedPdf = reloadedDelivery.GetArtifact(attempt.ReportId, attempt.AttemptId, pdfArtifact.ArtifactName, token);
        reloadedPdf.Content.LongLength.Should().Be(reloadedAttempt.Package.Artifacts.Single(artifact => artifact.Format == GovernanceReportArtifactFormatDto.Pdf).ByteSize);
        System.Text.Encoding.ASCII.GetString(reloadedPdf.Content).Should().Contain("sched-custom-writer-20260502");
        var reloadedXlsx = reloadedDelivery.GetArtifact(attempt.ReportId, attempt.AttemptId, xlsxArtifact.ArtifactName, token);
        reloadedXlsx.Content.LongLength.Should().Be(reloadedAttempt.Package.Artifacts.Single(artifact => artifact.Format == GovernanceReportArtifactFormatDto.Xlsx).ByteSize);
        reloadedXlsx.Content.Should().StartWith([0x50, 0x4B]);
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
        var service = new ReportingRunCommandService(orchestration, catalog);

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
            action.Kind == "approval-submit" &&
            action.Method == "POST" &&
            action.IsEnabled);
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
        var commandService = new ReportingRunCommandService(orchestration, catalog, catalog, datasetSource);

        var result = await commandService.RunAsync(
            new ReportingRunRequestDto(
                "source-backed-portfolio-grid",
                new DateOnly(2026, 5, 6),
                JobId: "adhoc-source-backed"),
            "report.author",
            CancellationToken.None);

        result.Run.RunId.Should().Be("adhoc-source-backed-20260506");
        var manifest = runStore.GetManifest(result.Run.RunId);
        manifest.Should().NotBeNull();
        var renderedGrid = manifest!.RenderedReportWriterGrids.Should().ContainSingle().Subject;
        renderedGrid.GridId.Should().Be("portfolio-source-grid");
        renderedGrid.Lineage.Should().NotBeNull();
        renderedGrid.Lineage!.FilteredInputRowCount.Should().Be(1);
        renderedGrid.Lineage.InputRowCount.Should().BeGreaterThan(1);
        renderedGrid.Warnings.Should().NotContain(warning => warning.Contains("no dataset rows", StringComparison.OrdinalIgnoreCase));
        var renderedRow = renderedGrid.Rows.Should().ContainSingle().Subject;
        renderedRow.Values["kind"].Should().Be(PortfolioReportingCutKindDto.Fund.ToString());
        renderedRow.Values["grossExposure"].Should().Be("2500000");
        renderedRow.Values["totalPnl"].Should().Be("60000");
        renderedRow.Values["shadowNav"].Should().Be("2935000");
        renderedRow.Values["pnlOnGrossPct"].Should().Be("2.4");
    }

    [Fact]
    public async Task ReportingScheduleService_ResolvesSourceBackedDatasetRowsForReportWriterRuns()
    {
        var workflow = new ReportPackWorkflowService();
        var published = workflow.Create(
            "fund-alpha",
            "acct-main",
            "2026-05",
            new VersionedReportTemplateIdDto("shadow-nav-pack", 1),
            "report.author",
            [
                SourcePortfolioReportingLine("portfolio.gross-exposure", "evidence-gross", "1250000", "/evidence/gross"),
                SourcePortfolioReportingLine("portfolio.realized-pnl", "evidence-realized", "15000"),
                SourcePortfolioReportingLine("portfolio.unrealized-pnl", "evidence-unrealized", "10000"),
                SourcePortfolioReportingLine("portfolio.shadow-nav", "evidence-shadow-nav", "1260000"),
                SourcePortfolioReportingLine("portfolio.reported-nav", "evidence-reported-nav", "1250000")
            ]);
        workflow.Transition(published.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        workflow.Transition(published.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
        workflow.Publish(
            published.ReportId,
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
                Rationale: "Render scheduled retained portfolio rows without a stored dataset payload.",
                Grids:
                [
                    new ReportWriterGridDefinitionDto(
                        "scheduled-portfolio-grid",
                        "Scheduled Portfolio Grid",
                        ReportWriterGridKindDto.TopN,
                        RowFields: ["kind"],
                        Metrics:
                        [
                            new ReportWriterMetricDefinitionDto("grossExposure", "grossExposure", ReportWriterAggregateFunctionDto.Sum, "Gross exposure"),
                            new ReportWriterMetricDefinitionDto("totalPnl", "totalPnl", ReportWriterAggregateFunctionDto.Sum, "Total P&L"),
                            new ReportWriterMetricDefinitionDto("shadowNav", "shadowNav", ReportWriterAggregateFunctionDto.Sum, "Shadow NAV")
                        ],
                        TopN: 3,
                        SortBy: "grossExposure",
                        Filters:
                        [
                            new ReportWriterFilterDefinitionDto("dataset", ReportWriterFilterOperatorDto.Equals, "portfolio-cut", "Portfolio cuts only")
                        ])
                ]),
            "report.author");
        registry.Submit(draft.Definition.TemplateId, "report.author", "Ready for scheduled source-backed run.");
        registry.Approve(
            draft.Definition.TemplateId,
            new ReportTemplateDecisionRequestDto("Approved for retained scheduled portfolio rows.", "approval:scheduled-source-grid"),
            "ops-lead");

        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var catalog = new GovernedReportingTemplateCatalog(new DefaultReportingTemplateCatalog(), registry);
        var runStore = new FileReportingRunStore(
            new ReportingRunStoreOptions(Path.Combine(root, "reporting-runs")),
            NullLogger<FileReportingRunStore>.Instance);
        var orchestration = new ReportingOrchestrationService(
            catalog,
            new DeterministicReportingSectionRenderer(),
            () => new DateTimeOffset(2026, 5, 7, 8, 0, 0, TimeSpan.Zero),
            runStore);
        var scheduleStore = new FileReportingScheduleStore(
            new ReportingScheduleStoreOptions(Path.Combine(root, "reporting-schedules.json")),
            NullLogger<FileReportingScheduleStore>.Instance);
        var schedules = new ReportingScheduleService(
            orchestration,
            scheduleStore,
            deliveryService: null,
            governedTemplateCatalog: catalog,
            datasetSourceService: new ReportWriterDatasetSourceService(workflow));

        schedules.Upsert(new ReportingScheduleUpsertRequestDto(
            "sched-source-backed",
            "scheduled-source-backed-grid",
            "0 8 1 * *",
            new DateOnly(2026, 5, 7),
            new DateTimeOffset(2026, 5, 7, 8, 0, 0, TimeSpan.Zero),
            0,
            "report.author",
            "Scheduled source-backed report-writer run."));

        var due = await schedules.RunDueAsync(new DateTimeOffset(2026, 5, 7, 8, 1, 0, TimeSpan.Zero));

        due.Runs.Should().ContainSingle();
        var run = due.Runs.Single().Run;
        run.RunId.Should().Be("sched-source-backed-20260507");
        var manifest = runStore.GetManifest(run.RunId);
        manifest.Should().NotBeNull();
        var renderedGrid = manifest!.RenderedReportWriterGrids.Should().ContainSingle().Subject;
        renderedGrid.GridId.Should().Be("scheduled-portfolio-grid");
        renderedGrid.Lineage.Should().NotBeNull();
        renderedGrid.Lineage!.FilteredInputRowCount.Should().Be(1);
        renderedGrid.Warnings.Should().NotContain(warning => warning.Contains("no dataset rows", StringComparison.OrdinalIgnoreCase));
        var renderedRow = renderedGrid.Rows.Should().ContainSingle().Subject;
        renderedRow.Values["kind"].Should().Be(PortfolioReportingCutKindDto.Fund.ToString());
        renderedRow.Values["grossExposure"].Should().Be("1250000");
        renderedRow.Values["totalPnl"].Should().Be("25000");
        renderedRow.Values["shadowNav"].Should().Be("1260000");
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
            "owner.user");
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
            "owner.user");
        var workflow = new ReportPackWorkflowService();
        var privatePack = workflow.Create(
            "fund-a",
            "acct-1",
            "2026-03",
            privateTemplate.Definition.TemplateId,
            "owner.user",
            accessPolicy: new ReportAccessPolicyDto(ReportAccessModeDto.Private, OwnerPrincipalId: "owner.user"));
        var groupPack = workflow.Create(
            "fund-a",
            "acct-1",
            "2026-03",
            groupTemplate.Definition.TemplateId,
            "owner.user",
            accessPolicy: new ReportAccessPolicyDto(
                ReportAccessModeDto.Restricted,
                Principals: [new ReportAccessPrincipalDto(ReportAccessPrincipalKindDto.Group, "ops-control")]));
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var scheduleStore = new FileReportingScheduleStore(
            new ReportingScheduleStoreOptions(Path.Combine(root, "reporting-schedules.json")),
            NullLogger<FileReportingScheduleStore>.Instance);
        scheduleStore.Save(
        [
            new ReportingScheduleRecordDto(
                "sched-owner-only",
                privateTemplate.Definition.TemplateId.Name,
                "0 8 1 * *",
                new DateOnly(2026, 4, 1),
                new DateTimeOffset(2026, 4, 1, 8, 0, 0, TimeSpan.Zero),
                0,
                "owner.user",
                ReportingScheduleStateDto.Active,
                new DateTimeOffset(2026, 3, 20, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 3, 20, 9, 0, 0, TimeSpan.Zero),
                DeliveryTargets:
                [
                    new ReportingScheduleDeliveryTargetDto(
                        "investor-relations",
                        [GovernanceReportArtifactFormatDto.Pdf],
                        ReportPackDeliveryModeDto.EmailLink)
                ]),
            new ReportingScheduleRecordDto(
                "sched-ops-control",
                groupTemplate.Definition.TemplateId.Name,
                "0 8 1 * *",
                new DateOnly(2026, 4, 1),
                new DateTimeOffset(2026, 4, 1, 8, 0, 0, TimeSpan.Zero),
                0,
                "owner.user",
                ReportingScheduleStateDto.Active,
                new DateTimeOffset(2026, 3, 20, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 3, 20, 9, 0, 0, TimeSpan.Zero),
                DeliveryTargets:
                [
                    new ReportingScheduleDeliveryTargetDto(
                        "board-reporting-committee",
                        [GovernanceReportArtifactFormatDto.Pdf, GovernanceReportArtifactFormatDto.Csv],
                        ReportPackDeliveryModeDto.SecurePortal)
                ])
        ]);
        var schedules = new ReportingScheduleService(
            new ReportingOrchestrationService(new DefaultReportingTemplateCatalog()),
            scheduleStore);
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
        var groupPayload = readService.BuildPayload(new ReportAccessQueryContext("viewer.user", ["ops-control"]));
        var strangerPayload = readService.BuildPayload(new ReportAccessQueryContext("viewer.user"));

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
        groupPayload.DeliveryAttempts.Should().NotBeNull();
        groupPayload.DeliveryAttempts!.Select(static attempt => attempt.AttemptId).Should().Contain(groupDeliveryAttemptId);
        groupPayload.DeliveryAttempts.Select(static attempt => attempt.AttemptId).Should().Contain(genericRunDeliveryAttemptId);
        groupPayload.DeliveryAttempts.Select(static attempt => attempt.AttemptId).Should().NotContain(privateDeliveryAttemptId);
        groupPayload.ScheduleDeliveryPlans.Single(plan => plan.ScheduleId == "sched-ops-control").LastDeliveryAttemptId.Should().Be(genericRunDeliveryAttemptId);
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
        groupPayload.AccessAudit.VisibleDeliveryAttemptCount.Should().Be(2);
        groupPayload.AccessAudit.HiddenDeliveryAttemptCount.Should().Be(1);
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
        strangerPayload.AccessAudit.HiddenDeliveryAttemptCount.Should().Be(3);
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
        groupWorkspace.Reporting.DeliveryAttempts.Should().NotBeNull();
        groupWorkspace.Reporting.DeliveryAttempts!.Select(static attempt => attempt.AttemptId).Should().Contain(genericRunDeliveryAttemptId);
        groupWorkspace.Reporting.DeliveryAttempts.Select(static attempt => attempt.AttemptId).Should().NotContain(privateDeliveryAttemptId);
        groupWorkspace.Reporting.ScheduleDeliveryPlans.Should().NotBeNull();
        groupWorkspace.Reporting.ScheduleDeliveryPlans!.Select(static plan => plan.ScheduleId).Should().Contain("sched-ops-control");
        groupWorkspace.Reporting.AccessAudit.Should().NotBeNull();
        groupWorkspace.Reporting.AccessAudit!.VisibleTemplateCount.Should().Be(groupPayload.AccessAudit.VisibleTemplateCount);
        groupWorkspace.Reporting.AccessAudit.HiddenTemplateCount.Should().Be(1);
        groupWorkspace.Reporting.AccessAudit.HiddenDeliveryAttemptCount.Should().Be(1);
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
            reportGroupPrincipalIds: ["reporting-ops"]);
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
            reportGroupPrincipalIds: ["reporting-ops"]);

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
            "owner.user");
        registry.Submit(draft.Definition.TemplateId, "owner.user", "ready");
        registry.Approve(draft.Definition.TemplateId, new ReportTemplateDecisionRequestDto("approved", "APP-PRIVATE-1"), "controller.admin");
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
            "owner.user");
        registry.Submit(draft.Definition.TemplateId, "owner.user", "ready");
        registry.Approve(draft.Definition.TemplateId, new ReportTemplateDecisionRequestDto("approved", "APP-GROUP-1"), "controller.admin");
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
            "owner.user");
        registry.Submit(draft.Definition.TemplateId, "owner.user", "ready");
        registry.Approve(draft.Definition.TemplateId, new ReportTemplateDecisionRequestDto("approved", "APP-COMPANY-1"), "controller.admin");
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
            UserRole.TradeDesk,
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
            "owner.user");
        registry.Submit(draft.Definition.TemplateId, "owner.user", "ready");
        registry.Approve(draft.Definition.TemplateId, new ReportTemplateDecisionRequestDto("approved", "APP-RUN-1"), "controller.admin");
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
                Rationale: "Expose retained run audit trail details"),
            "report.author");
        registry.Submit(draft.Definition.TemplateId, "report.author", "ready");
        registry.Approve(draft.Definition.TemplateId, new ReportTemplateDecisionRequestDto("approved", "APP-RUN-AUDIT-1"), "controller.admin");
        var client = app.GetTestClient();

        var runResponse = await client.PostAsJsonAsync(
            "/api/fund-structure/reporting/runs",
            new ReportingRunRequestDto(
                draft.Definition.TemplateId.Name,
                new DateOnly(2026, 5, 5),
                JobId: "audit-visible-run"),
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
        audit.Entries.Should().ContainSingle(entry =>
            entry.RunId == audit.RunId &&
            entry.Action == "RunGenerated" &&
            entry.Actor == "controller.admin" &&
            entry.TimestampUtc == new DateTimeOffset(2026, 5, 5, 9, 0, 0, TimeSpan.Zero) &&
            entry.Notes.Contains("trigger=AdHoc", StringComparison.Ordinal) &&
            entry.Notes.Contains("lineageSections=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Endpoint_ReportingRunAuditTrail_WhenCallerCannotAccessTemplate_ReturnsForbidden()
    {
        await using var app = await CreateFundStructureAppAsync(UserRole.TradeDesk, "viewer.user");
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
    public async Task Endpoint_ReportWriterGridArtifact_ReturnsJsonCsvAndXlsxForRetainedRunGrid()
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
            "report.author");
        registry.Submit(draft.Definition.TemplateId, "report.author", "ready");
        registry.Approve(draft.Definition.TemplateId, new ReportTemplateDecisionRequestDto("approved", "APP-GRID-ARTIFACT-1"), "controller.admin");
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
        var xlsxResponse = await client.GetAsync($"/api/fund-structure/reporting/runs/{run.Run.RunId}/report-writer-grids/sector-pnl?format=xlsx");

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
    }

    [Fact]
    public async Task Endpoint_ReportWriterGridArtifact_WhenCallerCannotAccessTemplate_ReturnsForbidden()
    {
        await using var app = await CreateFundStructureAppAsync(UserRole.TradeDesk, "viewer.user");
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
        await using var app = await CreateFundStructureAppAsync(UserRole.TradeDesk, "viewer.user");
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
            "owner.user");
        registry.Submit(draft.Definition.TemplateId, "owner.user", "ready");
        registry.Approve(draft.Definition.TemplateId, new ReportTemplateDecisionRequestDto("approved", "APP-RUN-PRIVATE-1"), "controller.admin");
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
    public async Task Endpoint_ScheduleRestrictedCustomTemplate_WhenRoleProfileMatchesGroup_CreatesScheduleAndRun()
    {
        await using var app = await CreateFundStructureAppAsync(
            UserRole.TradeDesk,
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
            "owner.user");
        registry.Submit(draft.Definition.TemplateId, "owner.user", "ready");
        registry.Approve(draft.Definition.TemplateId, new ReportTemplateDecisionRequestDto("approved", "APP-SCHED-GROUP-1"), "controller.admin");
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
                "Ops control custom report schedule."),
            ServerJsonOptions);
        var runResponse = await client.PostAsync("/api/fund-structure/reporting/schedules/sched-ops-control-custom/run", null);

        scheduleResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var schedule = await scheduleResponse.Content.ReadFromJsonAsync<ReportingScheduleRecordDto>(ServerJsonOptions);
        schedule.Should().NotBeNull();
        schedule!.TemplateId.Should().Be(draft.Definition.TemplateId.Name);
        schedule.RequestedBy.Should().Be("viewer.user");
        runResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await runResponse.Content.ReadFromJsonAsync<ReportingScheduleRunResultDto>(ServerJsonOptions);
        result.Should().NotBeNull();
        result!.Run.RunId.Should().Be("sched-ops-control-custom-20260505");
        result.Run.TemplateId.Should().Be(draft.Definition.TemplateId.Name);
        result.Run.Family.Should().Be(ReportingTemplateFamily.CustomReport.ToString());
        result.Run.Trigger.Should().Be(ReportingRunTrigger.Scheduled.ToString());
        result.Schedule.RunCount.Should().Be(1);
    }

    [Fact]
    public async Task Endpoint_SchedulePrivateTemplate_WhenCallerIsNotOwner_ReturnsForbiddenForCreateAndRun()
    {
        await using var app = await CreateFundStructureAppAsync(UserRole.TradeDesk, "viewer.user");
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
            "owner.user");
        registry.Submit(draft.Definition.TemplateId, "owner.user", "ready");
        var approved = registry.Approve(draft.Definition.TemplateId, new ReportTemplateDecisionRequestDto("approved", "APP-SCHED-PRIVATE-1"), "controller.admin");
        ReportAccessPolicyEvaluator.Evaluate(approved.Definition.AccessPolicy, new ReportAccessQueryContext("owner.user")).IsAccessible.Should().BeTrue();
        ReportAccessPolicyEvaluator.Evaluate(approved.Definition.AccessPolicy, new ReportAccessQueryContext("viewer.user")).IsAccessible.Should().BeFalse();
        var scheduleService = app.Services.GetRequiredService<ReportingScheduleService>();
        scheduleService.Upsert(new ReportingScheduleUpsertRequestDto(
            "sched-private-custom",
            draft.Definition.TemplateId.Name,
            "0 8 1 * *",
            new DateOnly(2026, 5, 5),
            new DateTimeOffset(2026, 5, 5, 8, 0, 0, TimeSpan.Zero),
            0,
            "owner.user",
            "Owner-created private report schedule."));
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
                "Unauthorized private report schedule."),
            ServerJsonOptions);
        var runResponse = await client.PostAsync("/api/fund-structure/reporting/schedules/sched-private-custom/run", null);

        scheduleResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        runResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Endpoint_CreateDraft_WithSessionCompanyAndRoleProfile_DefaultsTenantAwareAccessPolicy()
    {
        await using var app = await CreateFundStructureAppAsync(
            UserRole.TradeDesk,
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
    public async Task Endpoint_DeliveryCommands_PrivateReportPack_WhenCallerIsOwner_AllowsDelivery()
    {
        await using var app = await CreateFundStructureAppAsync(UserRole.TradeDesk, "owner.user");
        var workflow = app.Services.GetRequiredService<ReportPackWorkflowService>();
        var approved = CreateApprovedPack(
            workflow,
            [CompleteLineProvenance("trial-balance.cash", "ledger-evidence-1")],
            accessPolicy: new ReportAccessPolicyDto(ReportAccessModeDto.Private, OwnerPrincipalId: "owner.user"));
        var published = workflow.Publish(
            approved.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            CompleteLineProvenanceEvidenceLinks("ledger-evidence-1"));
        var client = app.GetTestClient();

        var deliveryResponse = await client.PostAsJsonAsync(
            $"/api/fund-structure/reporting/packs/{published.ReportId:D}/deliveries",
            new ReportPackDeliveryRequestDto(
                "board-reporting-committee",
                DeliveryReference: "owner-delivery",
                DeliveryMode: ReportPackDeliveryModeDto.SecurePortal),
            ServerJsonOptions);
        var historyResponse = await client.GetAsync($"/api/fund-structure/reporting/packs/{published.ReportId:D}/deliveries");

        deliveryResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        historyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = await historyResponse.Content.ReadFromJsonAsync<ReportPackDeliveryHistoryDto>(ServerJsonOptions);
        history.Should().NotBeNull();
        history!.Attempts.Should().ContainSingle(attempt => attempt.DeliveryReference == "owner-delivery");
    }

    [Fact]
    public async Task Endpoint_DeliveryCommands_RestrictedReportPack_WhenRoleMatchesGroup_AllowsDelivery()
    {
        await using var app = await CreateFundStructureAppAsync(UserRole.TradeDesk, "viewer.user");
        var workflow = app.Services.GetRequiredService<ReportPackWorkflowService>();
        var approved = CreateApprovedPack(
            workflow,
            [CompleteLineProvenance("trial-balance.cash", "ledger-evidence-1")],
            accessPolicy: new ReportAccessPolicyDto(
                ReportAccessModeDto.Restricted,
                Principals: [new ReportAccessPrincipalDto(ReportAccessPrincipalKindDto.Group, UserRole.TradeDesk.ToString())]));
        var published = workflow.Publish(
            approved.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            CompleteLineProvenanceEvidenceLinks("ledger-evidence-1"));
        var client = app.GetTestClient();

        var deliveryResponse = await client.PostAsJsonAsync(
            $"/api/fund-structure/reporting/packs/{published.ReportId:D}/deliveries",
            new ReportPackDeliveryRequestDto(
                "board-reporting-committee",
                DeliveryReference: "group-delivery",
                DeliveryMode: ReportPackDeliveryModeDto.SecurePortal),
            ServerJsonOptions);
        var historyResponse = await client.GetAsync($"/api/fund-structure/reporting/packs/{published.ReportId:D}/deliveries");

        deliveryResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        historyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var history = await historyResponse.Content.ReadFromJsonAsync<ReportPackDeliveryHistoryDto>(ServerJsonOptions);
        history.Should().NotBeNull();
        history!.Attempts.Should().ContainSingle(attempt => attempt.DeliveryReference == "group-delivery");
    }

    [Fact]
    public async Task Endpoint_DeliveryCommands_PrivateReportPack_WhenCallerIsNotOwner_ReturnsForbidden()
    {
        await using var app = await CreateFundStructureAppAsync(UserRole.TradeDesk, "viewer.user");
        var workflow = app.Services.GetRequiredService<ReportPackWorkflowService>();
        var approved = CreateApprovedPack(
            workflow,
            [CompleteLineProvenance("trial-balance.cash", "ledger-evidence-1")],
            accessPolicy: new ReportAccessPolicyDto(ReportAccessModeDto.Private, OwnerPrincipalId: "owner.user"));
        var published = workflow.Publish(
            approved.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            CompleteLineProvenanceEvidenceLinks("ledger-evidence-1"));
        var client = app.GetTestClient();

        var historyResponse = await client.GetAsync($"/api/fund-structure/reporting/packs/{published.ReportId:D}/deliveries");
        var deliveryResponse = await client.PostAsJsonAsync(
            $"/api/fund-structure/reporting/packs/{published.ReportId:D}/deliveries",
            new ReportPackDeliveryRequestDto("board-reporting-committee"),
            ServerJsonOptions);
        var failureResponse = await client.PostAsJsonAsync(
            $"/api/fund-structure/reporting/packs/{published.ReportId:D}/deliveries/failures",
            new ReportPackDeliveryFailureRequestDto("board-reporting-committee", "Blocked by recipient portal."),
            ServerJsonOptions);

        historyResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        deliveryResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        failureResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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
        ReportAccessPolicyDto? accessPolicy = null)
    {
        var created = svc.Create(
            "fund-a",
            "acct-1",
            "2026-03",
            new VersionedReportTemplateIdDto(templateName, 1),
            "author",
            lineProvenance,
            accessPolicy);
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        return svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
    }

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

    private static async Task<WebApplication> CreateFundStructureAppAsync(
        UserRole role,
        string username = "controller.admin",
        FundOperationsWorkspaceReadService? workspaceService = null,
        string? roleProfileName = null,
        string? companyId = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ReportTemplateRegistryService>();
        builder.Services.AddSingleton<DefaultReportingTemplateCatalog>();
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
        builder.Services.AddSingleton<ReportingRunCommandService>();
        builder.Services.AddSingleton(sp =>
            new ReportingScheduleService(
                sp.GetRequiredService<IReportingOrchestrationService>(),
                datasetSourceService: sp.GetRequiredService<ReportWriterDatasetSourceService>(),
                governedTemplateCatalog: sp.GetRequiredService<GovernedReportingTemplateCatalog>()));
        builder.Services.AddSingleton<ReportPackWorkflowService>();
        builder.Services.AddSingleton<ReportPackDeliveryService>();
        if (workspaceService is not null)
        {
            builder.Services.AddSingleton(workspaceService);
        }

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = username;
            context.Items[LoginSessionMiddleware.CurrentUserRoleKey] = role;
            if (!string.IsNullOrWhiteSpace(roleProfileName))
            {
                context.Items[LoginSessionMiddleware.CurrentUserRoleProfileNameKey] = roleProfileName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(companyId))
            {
                context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = companyId.Trim();
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
}
