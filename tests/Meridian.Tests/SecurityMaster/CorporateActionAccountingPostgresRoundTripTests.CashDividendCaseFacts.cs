using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Tests.Storage;

namespace Meridian.Tests.SecurityMaster;

public sealed partial class CorporateActionAccountingPostgresRoundTripTests
{
    [LedgerDatabaseFact]
    public async Task CashDividendCaseFacts_AreCompleteScopedVersionedAndStableAcrossAttestationStage()
    {
        await using var fixture = await NativeDividendFixture.CreateAsync();
        var declaration = (await fixture.Store.ChangeCashDividendDeclarationAsync(fixture.DeclarationRequest(0, true))).Declaration;
        var candidate = await fixture.CandidateAsync(Guid.NewGuid(), 120m, 120m, declaration);
        var before = await fixture.Store.GetCashDividendCaseFactsAsync(candidate.Request.CaseId, Tenant, Company,
            candidate.Request.ExpectedVersion);

        before.Options.Should().BeEmpty();
        before.Evidence.Should().ContainSingle().Which.EvidenceKind.Should().Be(CorporateActionEvidenceKinds.CustodianNotice);
        before.SnapshotHash.Should().Be(Sha256Digest.ComputeUtf8(before.SnapshotJson));
        await Assert.ThrowsAsync<CorporateActionNotFoundException>(() => fixture.Store.GetCashDividendCaseFactsAsync(
            candidate.Request.CaseId, Tenant, "foreign-company", candidate.Request.ExpectedVersion));
        await Assert.ThrowsAsync<CorporateActionVersionConflictException>(() => fixture.Store.GetCashDividendCaseFactsAsync(
            candidate.Request.CaseId, Tenant, Company, candidate.Request.ExpectedVersion + 1));

        var attested = await fixture.Store.AttestCashDividendAsync(candidate.Request, candidate.Receipt,
            Guid.NewGuid(), candidate.Fingerprint);
        var after = await fixture.Store.GetCashDividendCaseFactsAsync(candidate.Request.CaseId, Tenant, Company,
            attested.Case.Version);

        after.CaseVersion.Should().BeGreaterThan(before.CaseVersion);
        after.SnapshotHash.Should().Be(before.SnapshotHash,
            "a preparer-attestation workflow stage does not rewrite the retained processing options or source evidence");
    }
}
