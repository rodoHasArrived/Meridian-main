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
}
