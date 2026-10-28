using FluentAssertions;
using Meridian.Domain.Reconciliation;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class StatementReconciliationLineageTests
{
    [Fact]
    public void Source_identity_survives_amount_date_row_order_and_import_changes()
    {
        var row = new CanonicalStatementRow("run-a", 1, "account", "CUSIP1", 100m, 2m, 200m,
            "position", new DateOnly(2026, 9, 1), "hash")
        { Currency = "USD" };
        StatementReconciliationIntakeAuthority.SourceSubject(row).Should().Be(
            StatementReconciliationIntakeAuthority.SourceSubject(row with
            { ImportId = "run-b", SourceRowNumber = 8, Quantity = 20m, Price = 3m, TradeDate = new DateOnly(2026, 9, 2) }));
        StatementReconciliationIntakeAuthority.SourceSubject(row).Should().NotBe(
            StatementReconciliationIntakeAuthority.SourceSubject(row with { Currency = "EUR" }));
    }

    [Fact]
    public void Transaction_without_retained_external_identity_is_not_given_synthetic_lineage()
    {
        var row = new CanonicalStatementRow("run", 1, "account", "CUSIP1", 100m, 2m, 200m,
            "trade", new DateOnly(2026, 9, 1), "hash");
        StatementReconciliationIntakeAuthority.SourceSubject(row).Should().BeNull();
        StatementReconciliationIntakeAuthority.SourceSubject(row with { ExternalTransactionId = "trade-1" }).Should().NotBeNull();
    }

    [Fact]
    public void Transaction_identity_survives_mapped_activity_symbol_and_currency_changes()
    {
        var row = new CanonicalStatementRow("run-a", 1, "account", "CUSIP1", 100m, 2m, 200m,
            "trade", new DateOnly(2026, 9, 1), "hash")
        { ExternalTransactionId = "source-transaction-1", Currency = "USD" };

        StatementReconciliationIntakeAuthority.SourceSubject(row).Should().Be(
            StatementReconciliationIntakeAuthority.SourceSubject(row with
            { ActivityType = "fee", Symbol = "corrected-symbol", Currency = "EUR" }));
        StatementReconciliationIntakeAuthority.SourceSubject(row).Should().NotBe(
            StatementReconciliationIntakeAuthority.SourceSubject(row with { ExternalTransactionId = "source-transaction-2" }));
    }

    [Fact]
    public void Broker_request_conversion_retains_executed_mapping_proof()
    {
        var request = new BrokerStatementImportRequest("fixture", "source.csv", new DateOnly(2026, 9, 1))
        {
            CanonicalSourcePath = "canonical.csv",
            CanonicalArtifactHash = new string('a', 64),
            ExecutedMappingFingerprint = new string('b', 64),
            AccountingScope = new StatementAccountingScope("fund", Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 1))
        };

        var run = request.ToStatementRunRequest();

        run.ExecutedMappingFingerprint.Should().Be(request.ExecutedMappingFingerprint);
        run.CanonicalSourcePath.Should().Be(request.CanonicalSourcePath);
        run.CanonicalArtifactHash.Should().Be(request.CanonicalArtifactHash);
        run.AccountingScope.Should().Be(request.AccountingScope);
    }
}
