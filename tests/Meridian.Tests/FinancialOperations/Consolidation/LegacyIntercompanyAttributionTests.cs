using Meridian.Tests.TestSupport;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Meridian.Ledger;
using Meridian.Contracts.Tenancy;
using System.Text.Json;
using Moq;
using Fixture = Meridian.Tests.FinancialOperations.Consolidation.ConsolidationServiceTests.Fixture;

namespace Meridian.Tests.FinancialOperations.Consolidation;

public sealed class LegacyIntercompanyAttributionTests
{
    [Fact]
    public async Task ApprovedSidecarValidatesExactLegacyLineWithoutChangingPostedJournal()
    {
        var (fixture, validator, fact) = Setup();
        var original = fixture.Records[Fixture.FirstBookId][0].Entry;
        await validator.ValidateAsync(fact);
        Assert.Same(original, fixture.Records[Fixture.FirstBookId][0].Entry);
        Assert.All(original.Lines, x => Assert.Null(x.Dimensions?.AccountingArrangementId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CanonicalOnlySourceCannotBeReattributedToAnotherLegalIdentity(bool changedId)
    {
        var (_, validator, fact) = Setup(retainedCanonicalId: changedId ? Guid.NewGuid()
            : Guid.Parse("cc111111-1111-1111-1111-111111111111"), retainedCanonicalVersion: changedId ? 1 : 999);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync(fact));
        Assert.Contains("cannot replace canonical legal provenance", error.Message);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("version")]
    [InlineData("hash")]
    public async Task SidecarCannotReplaceTheAccountingArrangementsCanonicalLegalPin(string changed)
    {
        var (_, validator, fact) = Setup();
        var altered = changed switch
        {
            "id" => fact with { IntercompanyAgreementId = Guid.NewGuid() },
            "version" => fact with { IntercompanyAgreementVersion = 999 },
            _ => fact with { IntercompanyAgreementPayloadHash = new string('b', 64) }
        };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync(altered));
        Assert.Contains("canonical legal revision", error.Message);
    }

    [Fact]
    public async Task DifferentSourceDigestCannotBeApprovedAsHistoricalAttribution()
    {
        var (_, validator, fact) = Setup();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync(fact with { SourceLineDigest = new string('a', 64) }));
        Assert.Contains("digest", error.Message);
    }

    [Fact]
    public async Task SourceCounterpartyMustBelongToApprovedAgreement()
    {
        var (_, validator, fact) = Setup(Guid.NewGuid());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync(fact));
        Assert.Contains("parties", error.Message);
    }

    [Fact]
    public async Task ApprovedSuccessorCutsOffPredecessorForLegacySourceAttribution()
    {
        var (_, validator, fact) = Setup(superseded: true);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync(fact));
        Assert.Contains("arrangement window", error.Message);
    }

    [Fact]
    public void ReviewerCannotSelfApproveAndSuccessorsMustNameExactPredecessor()
    {
        var (_, _, fact) = Setup();
        Assert.Throws<InvalidOperationException>(() => LegacyIntercompanyAttributionRules.ValidateAppend(
            fact with { ApprovedBy = fact.PreparedBy }, null, []));
        LegacyIntercompanyAttributionRules.ValidateAppend(fact, null, []);
        var next = fact with { AttributionId = Guid.NewGuid(), Revision = 2, PreviousAttributionId = fact.AttributionId };
        LegacyIntercompanyAttributionRules.ValidateAppend(next, fact.AttributionId, [fact]);
        Assert.Throws<InvalidOperationException>(() => LegacyIntercompanyAttributionRules.ValidateAppend(next, null, [fact]));
        Assert.Same(next, Assert.Single(LegacyIntercompanyAttributionRules.Resolve([fact, next])));
    }

    [Fact]
    public void ExplicitOperatorReviewRetainsRationaleWithoutClaimingIndependentApproval()
    {
        var (_, _, fact) = Setup();
        var reviewed = fact with
        {
            ApprovedBy = fact.PreparedBy,
            ReviewMode = LegacyIntercompanyReviewModeDto.OperatorReviewed,
            ReviewRationale = "Reviewed the exact retained source and approved legal revision."
        };
        LegacyIntercompanyAttributionRules.ValidateAppend(reviewed, null, []);
        Assert.Throws<InvalidOperationException>(() => LegacyIntercompanyAttributionRules.ValidateAppend(reviewed with { ReviewRationale = null }, null, []));
        Assert.Throws<InvalidOperationException>(() => LegacyIntercompanyAttributionRules.ValidateAppend(reviewed with { ReviewMode = (LegacyIntercompanyReviewModeDto)99 }, null, []));
        Assert.Throws<InvalidOperationException>(() => LegacyIntercompanyAttributionRules.ValidateAppend(reviewed with { PreparedBy = "another operator" }, null, []));
    }

    [Fact]
    public void ReviewedRevocationRetractsOnlyLatestAssertionWithoutRestoringPredecessor()
    {
        var (_, _, first) = Setup();
        var corrected = first with
        {
            AttributionId = Guid.NewGuid(),
            Revision = 2,
            PreviousAttributionId = first.AttributionId,
            IntercompanyTransactionId = "corrected-transaction"
        };
        LegacyIntercompanyAttributionRules.ValidateAppend(corrected, first.AttributionId, [first]);
        var revoked = Revoke(corrected);
        LegacyIntercompanyAttributionRules.ValidateAppend(revoked, corrected.AttributionId, [first, corrected]);
        Assert.Empty(LegacyIntercompanyAttributionRules.Resolve([first, corrected, revoked]));
        var anotherLine = first with { AttributionId = Guid.NewGuid(), LineId = Guid.NewGuid() };
        Assert.Same(anotherLine, Assert.Single(LegacyIntercompanyAttributionRules.Resolve([first, corrected, revoked, anotherLine])));
        // All approval assertions, including the retraction, remain inspectable in the retained history.
        Assert.False(first.Revoked);
        Assert.False(corrected.Revoked);
        Assert.True(revoked.Revoked);
    }

    [Fact]
    public void RevocationRequiresLatestActivePredecessorAndExplicitReviewedRationale()
    {
        var (_, _, first) = Setup();
        var revoked = Revoke(first);
        Assert.Throws<InvalidOperationException>(() => LegacyIntercompanyAttributionRules.ValidateAppend(revoked, null, []));
        Assert.Throws<InvalidOperationException>(() => LegacyIntercompanyAttributionRules.ValidateAppend(revoked, null, [first]));
        Assert.Throws<InvalidOperationException>(() => LegacyIntercompanyAttributionRules.ValidateAppend(
            revoked with { ReviewRationale = null }, first.AttributionId, [first]));
        Assert.Throws<InvalidOperationException>(() => LegacyIntercompanyAttributionRules.ValidateAppend(
            Revoke(revoked), revoked.AttributionId, [first, revoked]));
    }

    [Fact]
    public void RevocationCannotAlterSourceLegalConfigurationOrTransactionPins()
    {
        var (_, _, first) = Setup();
        var revoked = Revoke(first);
        foreach (var altered in new[]
        {
            revoked with { SourceLineDigest = new string('b', 64) },
            revoked with { AccountingArrangementId = Guid.NewGuid() },
            revoked with { AccountingArrangementRevisionId = Guid.NewGuid() },
            revoked with { AccountBindingRevisionId = Guid.NewGuid() },
            revoked with { ChartAccountNodeId = "another-account" },
            revoked with { IntercompanyTransactionId = "another-transaction" },
            revoked with { IntercompanyAgreementId = Guid.NewGuid() },
            revoked with { IntercompanyAgreementVersion = first.IntercompanyAgreementVersion + 1 },
            revoked with { IntercompanyAgreementPayloadHash = new string('b', 64) },
            revoked with { TenantId = "another-tenant" },
            revoked with { CompanyId = "another-company" }
        })
            Assert.Throws<InvalidOperationException>(() => LegacyIntercompanyAttributionRules.ValidateAppend(altered, first.AttributionId, [first]));
    }

    [Fact]
    public async Task RevocationAfterProfileSupersessionStillRequiresExactUnchangedPostedSource()
    {
        var (fixture, validator, first) = Setup(superseded: true);
        var original = fixture.Records[Fixture.FirstBookId][0].Entry;
        await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync(first));
        var revoked = Revoke(first);
        // Releasing an invalid assertion does not depend on that old arrangement still being effective.
        await using (await validator.AcquireAuthorityLeaseAsync(revoked))
            await validator.ValidateAsync(revoked);
        Assert.Same(original, fixture.Records[Fixture.FirstBookId][0].Entry);
        await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync(revoked with { SourceLineDigest = new string('b', 64) }));
    }

    [Fact]
    public async Task RevocationHoldsBothConfigurationAuthorityFencesUntilTheDecisionCompletes()
    {
        var actions = new List<string>();
        var (_, validator, first) = Setup(superseded: true, configureAuthorities: (bindings, arrangements) =>
        {
            bindings.Setup(x => x.AcquireAuthorityLeaseAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() =>
            {
                actions.Add("acquire-binding");
                return new TrackedLease(() => actions.Add("release-binding"));
            });
            arrangements.Setup(x => x.AcquireAuthorityLeaseAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() =>
            {
                actions.Add("acquire-arrangement");
                return new TrackedLease(() => actions.Add("release-arrangement"));
            });
        });
        var revoked = Revoke(first);
        await using (await validator.AcquireAuthorityLeaseAsync(revoked))
        {
            await validator.ValidateAsync(revoked);
            Assert.Equal(["acquire-binding", "acquire-arrangement"], actions);
        }
        Assert.Equal(["acquire-binding", "acquire-arrangement", "release-arrangement", "release-binding"], actions);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RevocationCannotBypassUnavailableConfigurationAuthority(bool bindingUnavailable)
    {
        var releasedBinding = false;
        var (_, validator, first) = Setup(superseded: true, configureAuthorities: (bindings, arrangements) =>
        {
            if (bindingUnavailable)
                bindings.Setup(x => x.AcquireAuthorityLeaseAsync(It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new InvalidOperationException("Binding authority unavailable."));
            else
            {
                bindings.Setup(x => x.AcquireAuthorityLeaseAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new TrackedLease(() => releasedBinding = true));
                arrangements.Setup(x => x.AcquireAuthorityLeaseAsync(It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new InvalidOperationException("Arrangement authority unavailable."));
            }
        });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => validator.AcquireAuthorityLeaseAsync(Revoke(first)));
        Assert.Equal(bindingUnavailable ? "Binding authority unavailable." : "Arrangement authority unavailable.", error.Message);
        Assert.Equal(!bindingUnavailable, releasedBinding);
    }

    [Fact]
    public async Task FileStoreRetainsReviewedRevocationAcrossRestartAndExactRetryAfterProfileChange()
    {
        var (_, validator, first) = Setup(superseded: true);
        var directory = Path.Combine(Path.GetTempPath(), "meridian-revocation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "attributions.json");
            // Retained attribution was reviewed before the profile's later supersession.
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize<IReadOnlyList<LegacyIntercompanyAttributionDto>>([first],
                LegacyIntercompanyAttributionJsonContext.Default.IReadOnlyListLegacyIntercompanyAttributionDto));
            var tenants = Mock.Of<IFundScopeTenantAccessor>(tenant => tenant.ResolveCallerTenant() == first.TenantId);
            var store = new FileLegacyIntercompanyAttributionStore(path, tenants, validator);
            var revoked = Revoke(first);
            var saved = await store.AppendAsync(revoked, first.AttributionId);
            Assert.Equal(saved, await store.AppendAsync(revoked, first.AttributionId));
            var restarted = new FileLegacyIntercompanyAttributionStore(path, tenants, validator);
            Assert.Empty(await restarted.ListAsync(first.LedgerBookId));
            var history = await restarted.HistoryAsync(first.LedgerBookId);
            Assert.Equal(2, history.Count);
            Assert.Equal(first, history[0]);
            Assert.Equal(revoked, history[1]);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void RevocationFlagIsOmittedFromRetainedNonRevokedJson()
    {
        var (_, _, first) = Setup();
        var original = JsonSerializer.Serialize(first, LegacyIntercompanyAttributionJsonContext.Default.LegacyIntercompanyAttributionDto);
        Assert.DoesNotContain("\"revoked\"", original);
        var revoked = JsonSerializer.Serialize(Revoke(first), LegacyIntercompanyAttributionJsonContext.Default.LegacyIntercompanyAttributionDto);
        Assert.Contains("\"revoked\":true", revoked);
    }

    private static LegacyIntercompanyAttributionDto Revoke(LegacyIntercompanyAttributionDto previous) => previous with
    {
        AttributionId = Guid.NewGuid(),
        Revision = previous.Revision + 1,
        PreviousAttributionId = previous.AttributionId,
        Revoked = true,
        ReviewMode = LegacyIntercompanyReviewModeDto.OperatorReviewed,
        PreparedBy = "controller",
        ApprovedBy = "controller",
        ApprovedAt = previous.ApprovedAt.AddMinutes(1),
        EvidenceReference = "evidence:reviewed-revocation",
        ReviewRationale = "Retract the exact retained assertion after review."
    };

    private static (Fixture, LegacyIntercompanyAttributionValidator, LegacyIntercompanyAttributionDto) Setup(Guid? agreementCounterparty = null, bool superseded = false,
        Guid? retainedCanonicalId = null, int retainedCanonicalVersion = 1,
        Action<Mock<IIntercompanyAccountBindingStore>, Mock<IIntercompanyAccountingArrangementStore>>? configureAuthorities = null)
    {
        var fixture = new Fixture();
        var book = new LedgerBookRecord(Fixture.FirstBookId, Fixture.Profile, Fixture.FirstId,
            FundStructureNodeKindDto.Entity, "Source", "USD", Fixture.Time, Fixture.Time);
        fixture.Store.Setup(x => x.GetLedgerBookAsync(Fixture.FirstBookId, It.IsAny<CancellationToken>())).ReturnsAsync(book);
        var record = fixture.Records[Fixture.FirstBookId][0];
        if (retainedCanonicalId is { } canonicalId)
        {
            var source = record.Entry;
            var first = source.Lines[0];
            var dimensions = (first.Dimensions ?? new()) with
            { IntercompanyAgreementId = canonicalId, IntercompanyAgreementVersion = retainedCanonicalVersion };
            var attributed = new LedgerEntry(first.EntryId, first.JournalEntryId, first.Timestamp, first.Account,
                first.Debit, first.Credit, first.Description, dimensions, first.Currency);
            record = record with
            {
                Entry = new JournalEntry(source.JournalEntryId, source.Timestamp, source.Description,
                [attributed, .. source.Lines.Skip(1)], source.Metadata)
            };
            fixture.Records[Fixture.FirstBookId][0] = record;
        }
        var line = record.Entry.Lines[0];
        var binding = new IntercompanyAccountBindingDto(Guid.NewGuid(), Guid.NewGuid(), 1, book.LedgerBookId,
            "related-ar", IntercompanyAccountRoleDto.Receivable, line.Account.Name, line.Account.Name, "Asset",
            Fixture.Date.AddYears(-1), null, [], "tenant-test", "company-test", "preparer", Fixture.Time, "Reviewed legacy mapping");
        var agreements = new Mock<IIntercompanyAccountingArrangementStore>(MockBehavior.Strict);
        var bindings = new Mock<IIntercompanyAccountBindingStore>(MockBehavior.Strict);
        bindings.Setup(x => x.AcquireAuthorityLeaseAsync(It.IsAny<CancellationToken>())).ReturnsAsync(EmptyLedgerAuthorityLease.Instance);
        agreements.Setup(x => x.AcquireAuthorityLeaseAsync(It.IsAny<CancellationToken>())).ReturnsAsync(EmptyLedgerAuthorityLease.Instance);
        configureAuthorities?.Invoke(bindings, agreements);
        bindings.Setup(x => x.GetRevisionAsync(binding.RevisionId, It.IsAny<CancellationToken>())).ReturnsAsync(binding);
        var resolved = IntercompanyArrangementTestData.Create(Guid.NewGuid(), Fixture.OrganizationId,
            IntercompanyAccountingTemplateKindDto.Loan, Fixture.FirstId, agreementCounterparty ?? Fixture.SecondId, "USD",
            [new(Fixture.FirstId, Fixture.FirstBookId, binding.ChartAccountNodeId, "related-ap"),
             new(Fixture.SecondId, Fixture.SecondBookId, "related-ar", "related-ap")], Guid.NewGuid(), Fixture.Time.AddYears(-1));
        var agreement = resolved.Arrangement;
        var events = new List<IntercompanyAccountingArrangementEventDto> { new(Guid.NewGuid(), "request", IntercompanyAccountingArrangementActionDto.Approve,
            "reviewer", Fixture.Time, "Reviewed", agreement) };
        if (superseded)
            events.Add(new(Guid.NewGuid(), "successor", IntercompanyAccountingArrangementActionDto.Approve, "reviewer", Fixture.Time,
                "Approved successor", agreement with
                {
                    RevisionId = Guid.NewGuid(),
                    RevisionNumber = 2,
                    Version = 2,
                    EffectiveFrom = Fixture.Time.AddDays(-1),
                    SupersedesRevisionId = agreement.RevisionId
                }));
        agreements.Setup(x => x.GetAsync("tenant-test", agreement.ArrangementId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntercompanyAccountingArrangementHistoryDto(events));
        var fact = new LegacyIntercompanyAttributionDto(Guid.NewGuid(), 1, book.LedgerBookId, record.Entry.JournalEntryId,
            line.EntryId, LegacyIntercompanyAttributionRules.SourceDigest(book, record, line), agreement.ArrangementId,
            agreement.RevisionId, binding.RevisionId, binding.ChartAccountNodeId, "legacy-transaction-one",
            "preparer", "reviewer", Fixture.Time, "evidence:reviewed-opening-schedule", "tenant-test", "company-test")
        {
            IntercompanyAgreementId = resolved.CanonicalAgreementId,
            IntercompanyAgreementVersion = resolved.CanonicalAgreementVersion,
            IntercompanyAgreementPayloadHash = resolved.CanonicalPayloadHash
        };
        return (fixture, new LegacyIntercompanyAttributionValidator(fixture.Store.Object, bindings.Object, agreements.Object, IntercompanyArrangementTestData.LegalAuthority(resolved).Object), fact);
    }

    private sealed class TrackedLease(Action release) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { release(); return ValueTask.CompletedTask; }
    }
}
