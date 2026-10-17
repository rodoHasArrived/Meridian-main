using FluentAssertions;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Infrastructure.Reconciliation;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Reconciliation;

public sealed class StatementRunComparisonEvidenceTests
{
    private static readonly CanonicalStatementRow Cash = new("run", 1, "external", "", 0m, 0m, 20m,
        "cash", new DateOnly(2026, 9, 1), "hash");
    private static readonly InternalReconciliationPopulations Populations = new([], [new InternalCashBalance(
        "cash-1", "external", "USD", 20m, "ledger:cash-1", new DateOnly(2026, 9, 1))], []);
    private static readonly StatementRunMatchArtifact Artifact = new("run", "run", [], [], 1);

    [Fact]
    public void Missing_mapping_proof_is_unknown_and_semantic_change_separates_comparison()
    {
        var legacy = StatementRunComparisonEvidence.Retain(Artifact, [Cash], Populations, StatementToleranceProfile.Default);
        StatementReconciliationIntakeAuthority.CanCompareSourceRun(legacy).Should().BeFalse();
        var first = StatementRunComparisonEvidence.Retain(Artifact, [Cash], Populations, StatementToleranceProfile.Default, new string('a', 64));
        var changed = StatementRunComparisonEvidence.Retain(Artifact, [Cash], Populations, StatementToleranceProfile.Default, new string('b', 64));
        first.SourceComparisonPolicyFingerprint.Should().NotBe(changed.SourceComparisonPolicyFingerprint);
        StatementReconciliationIntakeAuthority.CanCompareSourceRun(first).Should().BeTrue();
    }

    [Fact]
    public void Missing_or_failed_internal_population_cannot_establish_clearing_even_when_no_breaks_are_returned()
    {
        var unavailable = StatementRunComparisonEvidence.Retain(Artifact, [Cash],
            InternalReconciliationPopulations.Empty, StatementToleranceProfile.Default);
        unavailable.SourceComparisonComplete.Should().BeFalse();
        StatementReconciliationIntakeAuthority.CanCompareSourceRun(unavailable).Should().BeFalse();
        StatementReconciliationIntakeAuthority.CanCompareSourceRun(Artifact).Should().BeFalse("legacy artifacts lack completeness proof");
        var available = StatementRunComparisonEvidence.Retain(Artifact, [Cash], Populations, StatementToleranceProfile.Default, new string('a', 64));
        StatementReconciliationIntakeAuthority.CanCompareSourceRun(available).Should().BeTrue();
        var unavailableBreak = new ReconciliationBreakRecord("break", "run", "run", "run:1", "TX_UNMATCHED", "transaction",
            1m, 0m, true, DateTimeOffset.UtcNow, "Open")
        { Classification = ReconciliationBreakClassifications.InternalTransactionPopulationUnavailable };
        StatementReconciliationIntakeAuthority.CanCompareSourceRun(available with { Breaks = [unavailableBreak] }).Should().BeFalse();
    }

    [Fact]
    public void Tolerance_version_or_rule_change_creates_different_comparison_scope_from_retained_executed_policy()
    {
        var first = StatementRunComparisonEvidence.Retain(Artifact, [Cash], Populations, StatementToleranceProfile.Default, new string('a', 64));
        var wider = StatementToleranceProfile.Default with
        {
            Version = 2,
            CashRules = [new CashToleranceRule("wider-rule", 1000m, null, TimeSpan.Zero)]
        };
        var second = StatementRunComparisonEvidence.Retain(Artifact, [Cash], Populations, wider);
        first.SourceComparisonPolicyFingerprint.Should().NotBe(second.SourceComparisonPolicyFingerprint);
        var import = new CanonicalStatementImport("run", "custodian", new DateOnly(2026, 9, 1),
            DateTimeOffset.UtcNow, "source", "hash", 1, 1)
        { ToleranceProfileId = StatementToleranceProfile.DefaultProfileId };
        StatementReconciliationIntakeAuthority.ComparisonSourceIdentity(import, first, "custodian").Should().NotBe(
            StatementReconciliationIntakeAuthority.ComparisonSourceIdentity(import, second, "custodian"));
        StatementReconciliationIntakeAuthority.CanCompareSourceRun(first with { SourceComparisonPolicyFingerprint = null }).Should().BeFalse();
    }

    [Fact]
    public void Narrower_or_empty_source_population_cannot_be_compared_to_a_broader_population()
    {
        var cashOnly = StatementRunComparisonEvidence.Retain(Artifact, [Cash], Populations, StatementToleranceProfile.Default, new string('a', 64));
        var includesTransactions = StatementRunComparisonEvidence.Retain(Artifact,
            [Cash, Cash with { ActivityType = "trade", ExternalTransactionId = "trade-1" }], Populations, StatementToleranceProfile.Default, new string('a', 64));
        includesTransactions.SourceComparisonComplete.Should().BeFalse();
        includesTransactions.SourceComparisonPopulationKinds.Should().NotBeEquivalentTo(cashOnly.SourceComparisonPopulationKinds);
        StatementRunComparisonEvidence.Retain(Artifact, [], Populations,
            StatementToleranceProfile.Default).SourceComparisonComplete.Should().BeFalse();
    }
}
