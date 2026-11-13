using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Ledger;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Moq;

namespace Meridian.Tests.Application;

public sealed class IntercompanyAgreementBatchAuthorityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OppositeBooksSharingAgreement_UseOneNativeLeaseAndValidateEveryRequest(bool wrongSecondCurrency)
    {
        var agreementId = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var date = new DateOnly(2026, 5, 31);
        var start = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var revision = new IntercompanyAgreementRevisionDto(agreementId, 1, null, null, IntercompanyAgreementActionDto.Activate,
            new("IC-1", "Bilateral loan", first, "Lender", second, "Borrower", IntercompanyAgreementTypeDto.Loan,
                "USD", new(2026, 1, 1)), Guid.NewGuid(), new string('a', 64), ["preparer"], "preparer", "reviewer", start, "approved");
        var state = new IntercompanyAgreementStoreState(agreementId, 1, [], [revision], [], []);
        var lease = new Mock<IIntercompanyAgreementPostingLease>(MockBehavior.Strict);
        lease.SetupGet(item => item.Agreements).Returns([state]);
        lease.Setup(item => item.GetLegalEntityAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new LegalEntitySummaryDto(id, LegalEntityTypeDto.Fund,
                id.ToString("N"), "Entity", "US", "USD", true, start, null));
        lease.Setup(item => item.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var store = new Mock<IIntercompanyAgreementStore>(MockBehavior.Strict);
        store.Setup(item => item.AcquirePostingLeaseAsync("tenant", It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == agreementId),
            It.IsAny<CancellationToken>())).ReturnsAsync(lease.Object);
        var authority = new PostgresIntercompanyAgreementPostingAuthority(store.Object);
        IntercompanyAgreementPostingRequest[] requests =
        [
            new("tenant", first, date, "USD", [new(agreementId, 1, first, second, "USD")], false),
            new("tenant", second, date, "USD", [new(agreementId, 1, second, first, wrongSecondCurrency ? "EUR" : "USD")], false)
        ];
        if (wrongSecondCurrency)
            await Assert.ThrowsAsync<LedgerValidationException>(() => authority.AcquireValidatedBatchLeaseAsync(requests));
        else
        {
            var retained = await authority.AcquireValidatedBatchLeaseAsync(requests);
            lease.Verify(item => item.DisposeAsync(), Times.Never);
            await retained.DisposeAsync();
        }
        store.Verify(item => item.AcquirePostingLeaseAsync("tenant", It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        lease.Verify(item => item.DisposeAsync(), Times.Once);
    }
}
