using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Xunit;

namespace Meridian.FundStructure.Tests;

/// <summary>
/// The owner of an approved accounting profile for an intercompany loan revises it. Guards against a
/// profile revision or its owner approval acting as a legal novation: substituting a party, pointing
/// at legal terms that were never independently approved, carrying a different legal agreement
/// identity, or being approved against legal terms that changed after the profile was submitted.
/// </summary>
public sealed partial class IntercompanyAccountingArrangementTests
{
    [Fact]
    public async Task ProfileRevisionCannotSubstituteALegalParty_AndTheApprovedProfileKeepsTheNativeParties()
    {
        var fixture = new ArrangementFixture();
        var approved = await ApprovedAsync(fixture.Service, fixture.Request());
        var outsider = Guid.NewGuid();
        var revision = Revision(approved, Cutoff, "novated-profile");

        var replacedBorrower = await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.ReviseAsync("tenant-a", "owner",
            approved.ArrangementId, revision with { PartyMappings = [approved.PartyMappings[0], approved.PartyMappings[1] with { EntityId = outsider }] }));
        Assert.Contains("pinned legal parties", replacedBorrower.Message);
        var replacedLender = await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.ReviseAsync("tenant-a", "owner",
            approved.ArrangementId, revision with
            {
                RequestId = Guid.NewGuid(),
                PartyMappings = [approved.PartyMappings[0] with { EntityId = outsider }, approved.PartyMappings[1]]
            }));
        Assert.Contains("pinned legal parties", replacedLender.Message);
        // Mapping one legal party twice would silently drop the other party from the profile.
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.ReviseAsync("tenant-a", "owner",
            approved.ArrangementId, revision with
            {
                RequestId = Guid.NewGuid(),
                PartyMappings = [approved.PartyMappings[0], approved.PartyMappings[1] with { EntityId = approved.PartyMappings[0].EntityId }]
            }));

        var history = (await fixture.Service.GetHistoryAsync("tenant-a", approved.ArrangementId))!;
        Assert.DoesNotContain(history.Events, item => item.Action == IntercompanyAccountingArrangementActionDto.Revise);
        Assert.Equal(approved.Version, history.Current.Version);
        Assert.Equal(approved.RevisionId, history.Current.RevisionId);
        var resolved = await fixture.Service.ResolveApprovedRevisionAsync("tenant-a", approved.ArrangementId, approved.RevisionId, Cutoff);
        Assert.Equal(fixture.Legal.Terms.PartyOneId, resolved.PartyOneEntityId);
        Assert.Equal(fixture.Legal.Terms.PartyTwoId, resolved.PartyTwoEntityId);
        Assert.DoesNotContain(resolved.PartyMappings, item => item.EntityId == outsider);
        Assert.Equal(0, fixture.Native.Commands);
        Assert.Equal(0, fixture.Native.ActiveLeases);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task ProfileRevisionCannotRepointToLegalTermsThatWereNotIndependentlyApproved(int version, bool differentPayload)
    {
        var fixture = new ArrangementFixture();
        var approved = await ApprovedAsync(fixture.Service, fixture.Request());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ReviseAsync("tenant-a", "owner",
            approved.ArrangementId, Revision(approved, Cutoff, "repointed-profile") with
            {
                CanonicalAgreementVersion = version,
                CanonicalPayloadHash = differentPayload ? new string('b', 64) : LegalHash
            }));

        Assert.Contains("exact approved canonical legal agreement version and payload hash", error.Message);
        var history = (await fixture.Service.GetHistoryAsync("tenant-a", approved.ArrangementId))!;
        Assert.DoesNotContain(history.Events, item => item.Action == IntercompanyAccountingArrangementActionDto.Revise);
        Assert.Equal(fixture.Legal.AgreementVersion, history.Current.CanonicalAgreementVersion);
        Assert.Equal(LegalHash, history.Current.CanonicalPayloadHash);
        Assert.Equal(0, fixture.Native.Commands);
        Assert.Equal(0, fixture.Native.ActiveLeases);
    }

    [Fact]
    public async Task ProfileRevisionFollowsAnAmendmentOnlyByItsExactNativePayload_AndCannotNameAnotherLegalAgreement()
    {
        var fixture = new ArrangementFixture();
        var approved = await ApprovedAsync(fixture.Service, fixture.Request());
        var amendment = fixture.NextLegal(termination: false);
        await fixture.Native.ReplaceAsync(fixture.Legal, amendment);
        var revision = Revision(approved, Cutoff, "amended-profile") with { CanonicalAgreementVersion = amendment.AgreementVersion };

        // The amended version exists, but the profile still names the superseded payload.
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ReviseAsync("tenant-a", "owner",
            approved.ArrangementId, revision with { CanonicalPayloadHash = LegalHash }));
        var followed = await fixture.Service.ReviseAsync("tenant-a", "owner", approved.ArrangementId,
            revision with { RequestId = Guid.NewGuid(), CanonicalPayloadHash = amendment.PayloadHash });

        // The revision is only a draft of the mapping; it changed neither the legal agreement nor its parties.
        Assert.Equal(IntercompanyAccountingArrangementStatusDto.Draft, followed.Status);
        Assert.Null(followed.ApprovedBy);
        Assert.Equal(approved.CanonicalAgreementId, followed.CanonicalAgreementId);
        Assert.Equal(amendment.AgreementVersion, followed.CanonicalAgreementVersion);
        Assert.Equal(amendment.PayloadHash, followed.CanonicalPayloadHash);
        Assert.Equal(approved.PartyMappings.Select(item => item.EntityId), followed.PartyMappings.Select(item => item.EntityId));
        Assert.Equal(0, fixture.Native.Commands);

        // A revision request has nowhere to carry a different legal agreement identity or legal facts.
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(revision,
            IntercompanyAccountingArrangementsJsonContext.Default.ReviseIntercompanyAccountingArrangementRequest));
        Assert.True(json.RootElement.TryGetProperty("canonicalAgreementVersion", out _));
        Assert.True(json.RootElement.TryGetProperty("canonicalPayloadHash", out _));
        foreach (var property in new[] { "canonicalAgreementId", "arrangementId", "organizationId", "currency", "partyOneEntityId",
                     "partyTwoEntityId", "partyOneRole", "partyTwoRole", "signedDocument", "settlementCadence", "settlementDueDays", "terms", "evidence" })
            Assert.False(json.RootElement.TryGetProperty(property, out _), property);
    }

    [Theory]
    [InlineData(IntercompanyAccountingArrangementActionDto.Submit)]
    [InlineData(IntercompanyAccountingArrangementActionDto.Approve)]
    public async Task ProfileOwnerApprovalCannotStandInForLegalApprovalOfRepaperedParties(IntercompanyAccountingArrangementActionDto refusedAction)
    {
        var fixture = new ArrangementFixture();
        var draft = await fixture.Service.CreateAsync("tenant-a", "owner", fixture.Request());
        var current = refusedAction == IntercompanyAccountingArrangementActionDto.Approve
            ? await fixture.Service.ApplyActionAsync("tenant-a", "owner", draft.ArrangementId,
                IntercompanyAccountingArrangementActionDto.Submit, new(Guid.NewGuid(), draft.Version))
            : draft;
        // The legal registry now retains different parties under that version; the profile's pin no longer matches any approved payload.
        var repapered = fixture.Legal with
        {
            Terms = fixture.Legal.Terms with { PartyTwoId = Guid.NewGuid() },
            ProposalId = Guid.NewGuid(),
            PayloadHash = new string('c', 64)
        };
        await fixture.Native.ReplaceAsync(repapered);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ApplyActionAsync("tenant-a", "owner",
            draft.ArrangementId, refusedAction, new(Guid.NewGuid(), current.Version)));

        Assert.Contains("exact approved canonical legal agreement version and payload hash", error.Message);
        var history = (await fixture.Service.GetHistoryAsync("tenant-a", draft.ArrangementId))!;
        Assert.Equal(current.Version, history.Current.Version);
        Assert.Equal(current.Status, history.Current.Status);
        Assert.Null(history.Current.ApprovedBy);
        Assert.DoesNotContain(history.Events, item => item.Action == IntercompanyAccountingArrangementActionDto.Approve);
        Assert.Equal(LegalHash, history.Current.CanonicalPayloadHash);
        Assert.Equal(0, fixture.Native.Commands);
        Assert.Equal(0, fixture.Native.ActiveLeases);
    }
}
