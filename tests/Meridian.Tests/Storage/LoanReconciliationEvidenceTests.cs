using FluentAssertions;
using Meridian.Contracts.DirectLending;
using Meridian.Storage.DirectLending;

namespace Meridian.Tests.Storage;

public sealed class LoanReconciliationEvidenceTests
{
    [Fact]
    public void RunEvidence_RoundTripsAndKeepsLegacyFieldsUnknown()
    {
        var run = new ReconciliationRunDto(Guid.NewGuid(), Guid.NewGuid(), null, DateTimeOffset.UtcNow, null, "Completed")
        { RequestHash = "synthetic-request-hash", AsOfDate = new(2026, 11, 21), AmountTolerance = 0.01m, DateToleranceDays = 1 };
        var restored = LoanReconciliationEvidence.Restore(run with { RequestHash = null, AsOfDate = null, AmountTolerance = null, DateToleranceDays = null },
            LoanReconciliationEvidence.Serialize(run));

        restored.Should().Be(run);
        LoanReconciliationEvidence.Restore(run with { RequestHash = null }, null).RequestHash.Should().BeNull();
        LoanReconciliationEvidence.SameRequest(run, run with { RequestHash = "changed" }).Should().BeFalse();
    }

    [Fact]
    public void ResultEvidence_RoundTripsCanonicalSourcesAndAllocation()
    {
        var row = new ReconciliationResultDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null,
            "Matched", 1m, 1m, 0m, null, null, null, null, [], DateTimeOffset.UtcNow)
        {
            MeasurementKind = "ledger-balance:LoanPrincipal", Currency = "USD", AsOfDate = new(2026, 11, 21),
            LedgerBookId = Guid.NewGuid(), ServicingRevision = 9, PaymentAllocationId = Guid.NewGuid(), JournalEntryIds = [Guid.NewGuid()]
        };
        var restored = LoanReconciliationEvidence.Restore(row with { Currency = null, LedgerBookId = null, ServicingRevision = null, JournalEntryIds = [] },
            LoanReconciliationEvidence.Serialize(row));

        restored.Should().BeEquivalentTo(row);
    }
}
