using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class ReconciliationBreakQueueProjectionTests
{
    [Fact]
    public void ProjectStatement_UsesImportAndAccountingAuthorityInCaseIdentity()
    {
        var source = Break("statement-run-a");
        var firstScope = new StatementAccountingScope(
            "fund-alpha",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            new DateOnly(2026, 6, 30));
        var secondScope = firstScope with
        {
            LedgerBookId = Guid.Parse("33333333-3333-3333-3333-333333333333")
        };

        var first = ReconciliationBreakQueueProjection.ProjectStatement(
            source,
            firstScope,
            "fund-account-alpha");
        var secondBook = ReconciliationBreakQueueProjection.ProjectStatement(
            source,
            secondScope,
            "fund-account-alpha");
        var secondImport = ReconciliationBreakQueueProjection.ProjectStatement(
            Break("statement-run-b"),
            firstScope,
            "fund-account-alpha");

        first.BreakId.Should().NotBe(secondBook.BreakId);
        first.BreakId.Should().NotBe(secondImport.BreakId);
        first.SourceFingerprint.Should().Be(secondBook.SourceFingerprint);
        first.SourceImportId.Should().Be("statement-run-a");
        secondImport.SourceImportId.Should().Be("statement-run-b");
    }

    [Fact]
    public void ProjectStatement_UnscopedIdentityCanBeDeterministicallyPromotedToExactScope()
    {
        var source = Break("statement-run-a");
        var scope = new StatementAccountingScope(
            "fund-alpha",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            new DateOnly(2026, 6, 30));

        var unscoped = ReconciliationBreakQueueProjection.ProjectStatement(source);
        var scoped = ReconciliationBreakQueueProjection.ProjectStatement(source, scope);

        unscoped.BreakId.Should().NotBe(scoped.BreakId);
        unscoped.SourceFingerprint.Should().Be(scoped.SourceFingerprint);
        ReconciliationBreakQueueProjection.ProjectStatement(source).BreakId
            .Should().Be(unscoped.BreakId);
        ReconciliationBreakQueueProjection.ProjectStatement(source, scope).BreakId
            .Should().Be(scoped.BreakId);
    }

    private static StatementBreakDto Break(string importId)
        => new(
            BreakId: "cash-usd",
            BreakType: StatementBreakType.CashBalanceMismatch,
            Severity: StatementValidationSeverity.Error,
            MatchTier: StatementMatchTier.Unmatched,
            Description: "USD cash differs from the retained book.",
            Currency: "USD",
            StatementAmount: 125_000m,
            BookAmount: 124_900m,
            Delta: 100m,
            Tolerance: 1m,
            Status: "Open",
            Owner: null,
            RecommendedAction: "Review",
            EvidenceLink: $"/evidence/{importId}/cash-usd",
            StatementReference: $"{importId}:cash-usd",
            InternalReference: importId,
            CreatedAtUtc: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            LastObservedAtUtc: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));
}
