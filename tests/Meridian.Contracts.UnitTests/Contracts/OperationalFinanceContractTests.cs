using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Ledger;

namespace Meridian.Tests.Contracts;

public sealed class OperationalFinanceContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void LedgerDimensionSetDto_CarriesNeutralOperationalFinanceDimensions()
    {
        var dimensions = new LedgerDimensionSetDto(
            EntityId: "entity-alpha",
            CounterpartyId: "counterparty-bank",
            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Department"] = "Treasury"
            },
            OrganizationId: "org-alpha",
            PortfolioId: "portfolio-income",
            BookId: "book-gaap",
            AccountId: "account-operating-cash",
            CustomerId: "customer-advisory",
            VendorId: "vendor-custodian",
            ProjectId: "project-close");

        dimensions.FundId.Should().BeNull();
        dimensions.OrganizationId.Should().Be("org-alpha");
        dimensions.PortfolioId.Should().Be("portfolio-income");
        dimensions.BookId.Should().Be("book-gaap");
        dimensions.AccountId.Should().Be("account-operating-cash");
        dimensions.CustomerId.Should().Be("customer-advisory");
        dimensions.VendorId.Should().Be("vendor-custodian");
        dimensions.ProjectId.Should().Be("project-close");
        dimensions.ExternalGlDimensions["Department"].Should().Be("Treasury");

        var json = JsonSerializer.Serialize(dimensions, JsonOptions);
        json.Should().Contain("\"organizationId\":\"org-alpha\"");
        json.Should().Contain("\"portfolioId\":\"portfolio-income\"");
        json.Should().Contain("\"bookId\":\"book-gaap\"");
        json.Should().Contain("\"accountId\":\"account-operating-cash\"");
    }

    [Fact]
    public void OperationalFinanceRecordTrace_CanRepresentEntityAccountingFlowWithoutFundScope()
    {
        var dimensions = new LedgerDimensionSetDto(
            OrganizationId: "org-family-office",
            EntityId: "entity-management-company",
            BookId: "book-gaap",
            AccountId: "account-operating-cash",
            CostCenterId: "operations");
        var scope = new OperationalFinanceScopeDto(
            ScopeId: "entity-management-company",
            ScopeKind: OperationalFinanceScopeKindDto.Entity,
            DisplayName: "Management Company",
            Dimensions: dimensions,
            OrganizationId: "org-family-office",
            EntityId: "entity-management-company",
            BookId: "book-gaap",
            AccountId: "account-operating-cash");
        var operationalEvent = new OperationalEventCommandContextDto(
            OperationalEventId: "invoice-accrual-2026-06",
            OperationalEventType: "InvoiceAccrual",
            EffectiveDate: new DateOnly(2026, 6, 30),
            Actor: "controller@meridian.local",
            Scope: scope,
            SourceSystem: "ap-workflow",
            CorrelationId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            CausationId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            EvidenceLinks: ["evidence://invoice/2026-06/vendor-custodian"]);
        var trace = new OperationalFinanceRecordTraceDto(
            TraceId: "trace-invoice-accrual-2026-06",
            OperationalEvent: operationalEvent,
            Nodes:
            [
                Node("event", OperationalFinanceTraceStageDto.OperationalEvent),
                Node("evidence", OperationalFinanceTraceStageDto.Evidence),
                Node("candidate", OperationalFinanceTraceStageDto.PostingCandidate),
                Node("journal", OperationalFinanceTraceStageDto.JournalLifecycle),
                Node("ledger", OperationalFinanceTraceStageDto.LedgerImpact),
                Node("report", OperationalFinanceTraceStageDto.ReportLine),
                Node("audit", OperationalFinanceTraceStageDto.AuditEvent)
            ],
            JournalStatus: ManualJournalEntryStatusDto.Submitted,
            RequiresFundScope: false,
            BlockedOutputs: ["close-package"],
            EvidenceLinks: ["evidence://invoice/2026-06/vendor-custodian"]);

        trace.RequiresFundScope.Should().BeFalse();
        trace.OperationalEvent.Scope.ScopeKind.Should().Be(OperationalFinanceScopeKindDto.Entity);
        trace.OperationalEvent.Scope.Dimensions!.FundId.Should().BeNull();
        trace.Nodes.Select(node => node.Stage).Should().ContainInOrder(
            OperationalFinanceTraceStageDto.OperationalEvent,
            OperationalFinanceTraceStageDto.Evidence,
            OperationalFinanceTraceStageDto.PostingCandidate,
            OperationalFinanceTraceStageDto.JournalLifecycle,
            OperationalFinanceTraceStageDto.LedgerImpact,
            OperationalFinanceTraceStageDto.ReportLine,
            OperationalFinanceTraceStageDto.AuditEvent);

        var json = JsonSerializer.Serialize(trace, JsonOptions);
        json.Should().Contain("\"operationalEvent\"");
        json.Should().Contain("\"scopeKind\":\"Entity\"");
        json.Should().Contain("\"stage\":\"PostingCandidate\"");
        json.Should().Contain("\"stage\":\"JournalLifecycle\"");
        json.Should().Contain("\"stage\":\"ReportLine\"");
        json.Should().Contain("\"requiresFundScope\":false");
        json.Should().NotContain("fund-alpha");
    }

    private static OperationalFinanceTraceNodeDto Node(
        string nodeId,
        OperationalFinanceTraceStageDto stage)
        => new(
            NodeId: nodeId,
            Stage: stage,
            DisplayName: stage.ToString(),
            Status: "Ready",
            RecordId: $"{nodeId}-record",
            Route: $"/workstation/accounting/{nodeId}",
            EvidenceLinks: ["evidence://invoice/2026-06/vendor-custodian"]);
}
