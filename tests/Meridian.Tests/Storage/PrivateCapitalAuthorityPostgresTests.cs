using System.Data;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Banking;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Banking;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Meridian.Tests.Storage;

[Trait("Category", "Integration")]
public sealed class PrivateCapitalAuthorityPostgresTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-15T12:00:00Z");
    private static readonly DateOnly NoticeDate = new(2026, 5, 15);

    [Fact]
    public void BankEvidenceHash_IsStableAcrossPostgresNumericScaleAndUtcRoundTrip()
    {
        var source = new BankTransactionDto(Guid.NewGuid(), Guid.NewGuid(), "CapitalCallFunding", NoticeDate,
            NoticeDate, NoticeDate, 25_000m, "USD", "retained-bank-line", Now, false, "operator");
        var roundTrip = source with { Amount = 25_000.0000m, RecordedAt = Now.ToOffset(TimeSpan.FromHours(2)) };
        PrivateCapitalPostingAuthority.BankTransactionHash(roundTrip)
            .Should().Be(PrivateCapitalPostingAuthority.BankTransactionHash(source));
        PrivateCapitalPostingAuthority.BankTransactionHash(source with { Amount = 25_001m })
            .Should().NotBe(PrivateCapitalPostingAuthority.BankTransactionHash(source));
    }

    [LedgerDatabaseFact]
    public async Task CommitmentRevisions_ApproveAndReplayWithoutRewritingRetainedTerms()
    {
        await using var fixture = await Fixture.CreateAsync();
        var draft = fixture.Commitment("a", 600_000m);
        await fixture.Capital.SaveCommitmentDraftAsync(draft, 0);
        var approved = await fixture.Capital.ApproveCommitmentAsync(fixture.Scope, "a", 1, "reviewer", Now);
        approved.ApprovalStatus.Should().Be(PrivateCapitalCommitmentApprovalStatus.Approved);
        (await fixture.Capital.SaveCommitmentDraftAsync(draft, 0)).ApprovedBy.Should().Be("reviewer");
        (await fixture.Capital.ApproveCommitmentAsync(fixture.Scope, "a", 1, "retry-reviewer", Now.AddDays(1)))
            .Should().BeEquivalentTo(approved);

        var conflictingReplay = () => fixture.Capital.SaveCommitmentDraftAsync(draft with { TotalCommitment = 1m }, 0);
        await conflictingReplay.Should().ThrowAsync<InvalidOperationException>();
        var successor = draft with { Revision = 2, TotalCommitment = 700_000m, EffectiveFrom = NoticeDate };
        await fixture.Capital.SaveCommitmentDraftAsync(successor, 1);
        await fixture.Capital.ApproveCommitmentAsync(fixture.Scope, "a", 2, "reviewer", Now.AddMinutes(1));
        (await fixture.Capital.GetCommitmentAsync(fixture.Scope, "a", 1))!.TotalCommitment.Should().Be(600_000m);
        (await fixture.Capital.GetCommitmentAsync(fixture.Scope, "a"))!.TotalCommitment.Should().Be(700_000m);
    }

    [LedgerDatabaseFact]
    public async Task CompetingCommitmentRevisions_OnlyOneExpectedVersionCanWin()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ApproveAsync("a", 100m);
        var successor = fixture.Commitment("a", 120m) with { Revision = 2 };
        var outcomes = await Task.WhenAll(
            AttemptAsync(() => fixture.Capital.SaveCommitmentDraftAsync(successor, 1)),
            AttemptAsync(() => new PostgresPrivateCapitalStore(fixture.Database.Options)
                .SaveCommitmentDraftAsync(successor with { TotalCommitment = 130m }, 1)));
        outcomes.Count(x => x is null).Should().Be(1);
        var retained = (await fixture.Capital.ListCommitmentsAsync(fixture.Scope)).Where(x => x.Revision == 2).ToArray();
        retained.Should().ContainSingle();
        retained[0].TotalCommitment.Should().BeOneOf(120m, 130m);
    }

    [LedgerDatabaseFact]
    public async Task AuthorityReadsAndWrites_RejectForeignScopeAndMismatchedEntityOrCurrency()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ApproveAsync("a", 100m);
        PrivateCapitalScopeDto[] invalidScopes =
        [
            fixture.Scope with { TenantId = "foreign-tenant" },
            fixture.Scope with { CompanyId = "foreign-company" },
            fixture.Scope with { EntityId = Guid.NewGuid().ToString("D") },
            fixture.Scope with { Currency = "EUR" },
            fixture.Scope with { FundProfileId = "foreign-fund" }
        ];
        foreach (var scope in invalidScopes)
        {
            var read = () => fixture.Capital.ListCommitmentsAsync(scope);
            await read.Should().ThrowAsync<InvalidOperationException>();
            var write = () => fixture.Capital.SaveCommitmentDraftAsync(fixture.Commitment("b", 100m) with { Scope = scope }, 0);
            await write.Should().ThrowAsync<InvalidOperationException>();
        }
        (await fixture.Capital.ListCommitmentsAsync(fixture.Scope)).Should().ContainSingle();
    }

    [LedgerDatabaseFact]
    public async Task CapitalAccount_CannotBeApprovedForDifferentInvestorsAcrossCommitments()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ApproveAsync("a", 100m);
        var sameInvestor = fixture.Commitment("b", 100m) with { InvestorId = "investor-a", CapitalAccountId = "capital-a" };
        await fixture.Capital.SaveCommitmentDraftAsync(sameInvestor, 0);
        await fixture.Capital.ApproveCommitmentAsync(fixture.Scope, "b", 1, "reviewer", Now);
        var differentInvestor = fixture.Commitment("c", 100m) with { CapitalAccountId = "capital-a" };
        await fixture.Capital.SaveCommitmentDraftAsync(differentInvestor, 0);
        var approveForeignInvestor = () => fixture.Capital.ApproveCommitmentAsync(fixture.Scope, "c", 1, "reviewer", Now);
        await approveForeignInvestor.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.Capital.GetCommitmentAsync(fixture.Scope, "c"))!.ApprovalStatus
            .Should().Be(PrivateCapitalCommitmentApprovalStatus.Draft);

        var first = fixture.Commitment("d", 100m) with { CapitalAccountId = "capital-shared" };
        var second = fixture.Commitment("e", 100m) with { CapitalAccountId = "capital-shared" };
        await fixture.Capital.SaveCommitmentDraftAsync(first, 0);
        await fixture.Capital.SaveCommitmentDraftAsync(second, 0);
        var outcomes = await Task.WhenAll(
            AttemptAsync(() => fixture.Capital.ApproveCommitmentAsync(fixture.Scope, "d", 1, "reviewer", Now)),
            AttemptAsync(() => new PostgresPrivateCapitalStore(fixture.Database.Options)
                .ApproveCommitmentAsync(fixture.Scope, "e", 1, "reviewer", Now)));
        outcomes.Count(x => x is null).Should().Be(1);
        (await fixture.Capital.ListCommitmentsAsync(fixture.Scope)).Where(x => x.CapitalAccountId == "capital-shared"
            && x.ApprovalStatus == PrivateCapitalCommitmentApprovalStatus.Approved).Should().ContainSingle();
    }

    [LedgerDatabaseFact]
    public async Task FrozenCallPlan_ExactReplayRetainsOriginalAndChangedPlanConflicts()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ApproveAsync("a", 100m);
        var call = fixture.Call("call-a", ("a", 60m, 100m));
        await fixture.Capital.SaveCallAsync(call);
        (await fixture.Capital.SaveCallAsync(call with { CreatedAtUtc = Now.AddDays(1) })).CreatedAtUtc.Should().Be(Now);
        var changed = call with { Purpose = "Changed purpose" };
        changed = changed with { PlanHash = PrivateCapitalCallPlanHash.Compute(changed) };
        var replace = () => fixture.Capital.SaveCallAsync(changed);
        await replace.Should().ThrowAsync<InvalidOperationException>();
        var corruptHash = () => fixture.Capital.SaveCallAsync(call with { CallId = "call-b" });
        await corruptHash.Should().ThrowAsync<ArgumentException>();
        (await fixture.Capital.GetCallAsync(fixture.Scope, "call-a"))!.PlanHash.Should().Be(call.PlanHash);
    }

    [LedgerDatabaseFact]
    public async Task TwoInvestorsAndSuccessivePartialReceipts_ReconcileAndReplayExactlyOnce()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ApproveAsync("a", 600_000m);
        await fixture.ApproveAsync("b", 400_000m);
        var call = fixture.Call("call-a", ("a", 60_000m, 600_000m), ("b", 40_000m, 400_000m));
        await fixture.Capital.SaveCallAsync(call);
        await fixture.PostAsync(fixture.Issuance(call, "a"));
        await fixture.PostAsync(fixture.Issuance(call, "b"));
        var first = await fixture.FundingAsync(call, "a", "receipt-1", 25_000m);
        await fixture.PostAsync(first);
        (await fixture.Capital.GetCallAsync(fixture.Scope, "call-a"))!.Lines.Sum(x => x.OutstandingAmount).Should().Be(75_000m);
        await fixture.PostAsync(await fixture.FundingAsync(call, "a", "receipt-2", 35_000m));
        await fixture.PostAsync(await fixture.FundingAsync(call, "b", "receipt-3", 40_000m));

        using var restartedTarget = new DurableLedgerPostingTarget(new PostgresLedgerJournalStore(fixture.Database.Options));
        (await restartedTarget.PostAsync(first)).WasAppended.Should().BeFalse();
        var retained = await fixture.Capital.GetCallAsync(fixture.Scope, "call-a");
        retained!.Lines.Sum(x => x.IssuedAmount).Should().Be(100_000m);
        retained.Lines.Sum(x => x.FundedAmount).Should().Be(100_000m);
        retained.Lines.Sum(x => x.OutstandingAmount).Should().Be(0m);
        var commitments = await fixture.Capital.ListCommitmentsAsync(fixture.Scope);
        commitments.Sum(x => x.UncalledAmount).Should().Be(900_000m);
        (await fixture.Capital.ListPostingReceiptsAsync(fixture.Scope)).Should().HaveCount(5);
        var journals = await fixture.Database.JournalStore.GetByPeriodAsync(fixture.Period.PeriodId);
        journals.Should().HaveCount(5).And.OnlyContain(x => x.Entry.IsBalanced);
        journals.SelectMany(x => x.Entry.Lines).Where(x => x.Account.Name == "Cash").Sum(x => x.Debit - x.Credit)
            .Should().Be(100_000m);
        journals.SelectMany(x => x.Entry.Lines).Where(x => x.Account.Name == "Capital Call Receivable").Sum(x => x.Debit - x.Credit)
            .Should().Be(0m);

        // Rehydrate the existing financial reporting engine from durable posted journals,
        // rather than using the new authority receipts as a second accounting truth.
        var ledger = new Meridian.Ledger.Ledger();
        foreach (var journal in journals)
            ledger.Post(journal.Entry);
        var periodStart = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var periodEnd = new DateTimeOffset(2026, 5, 31, 23, 59, 59, TimeSpan.Zero);
        var pack = LedgerReportPackBuilder.Build(ledger, new LedgerReportPackRequest("capital-proof", fixture.Scope.FundProfileId,
            fixture.Period.PeriodId.ToString("D"), periodStart, periodEnd, periodEnd, fixture.Scope.Currency, "reviewer", Now));
        var capitalStatement = pack.Statements.PartnersCapital!;
        capitalStatement.IsReconciled.Should().BeTrue();
        capitalStatement.Contributions.Should().Be(100_000m);
        capitalStatement.EndingCapital.Should().Be(100_000m);
        capitalStatement.EndingCapital.Should().Be(pack.Statements.TotalAssets - pack.Statements.TotalLiabilities);
        capitalStatement.Accounts.Single(x => x.InvestorId == "investor-a").EndingCapital.Should().Be(60_000m);
        capitalStatement.Accounts.Single(x => x.InvestorId == "investor-b").EndingCapital.Should().Be(40_000m);
        pack.Artifacts.Should().Contain(x => x.Name == "partners-capital-statement.csv");
        pack.LineProvenance.SelectMany(x => x.LedgerJournalEntryIds).Distinct()
            .Should().BeEquivalentTo(journals.Select(x => x.Entry.JournalEntryId));
    }

    [LedgerDatabaseFact]
    public async Task Funding_RejectsReusedBankLineAndOverfundingWithoutExtraJournalOrReceipt()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ApproveAsync("a", 100m);
        var firstCall = fixture.Call("call-a", ("a", 60m, 100m));
        await fixture.Capital.SaveCallAsync(firstCall);
        await fixture.PostAsync(fixture.Issuance(firstCall, "a"));
        var secondCall = fixture.Call("call-b", ("a", 40m, 40m));
        await fixture.Capital.SaveCallAsync(secondCall);
        await fixture.PostAsync(fixture.Issuance(secondCall, "a"));
        var source = await fixture.BankAsync(25m);
        await fixture.PostAsync(fixture.Funding(firstCall, "a", "receipt-1", source));
        var reused = () => fixture.PostAsync(fixture.Funding(secondCall, "a", "receipt-2", source));
        await reused.Should().ThrowAsync<PostgresException>().Where(x => x.SqlState == PostgresErrorCodes.UniqueViolation);
        var aliased = fixture.Funding(secondCall, "a", "receipt-alias", source);
        var aliasedTags = new Dictionary<string, string>(aliased.Entry.Metadata.Tags!);
        var authority = PrivateCapitalPostingAuthority.Deserialize(aliasedTags[PrivateCapitalPostingAuthority.MetadataTag]);
        aliasedTags[PrivateCapitalPostingAuthority.MetadataTag] = PrivateCapitalPostingAuthority.Serialize(authority with
        { BankLineId = source.BankTransactionId.ToString("B").ToUpperInvariant() });
        aliased = aliased with
        {
            Entry = new JournalEntry(aliased.Entry.JournalEntryId, aliased.Entry.Timestamp,
            aliased.Entry.Description, aliased.Entry.Lines, aliased.Entry.Metadata with { Tags = aliasedTags })
        };
        var reusedAlias = () => fixture.PostAsync(aliased);
        await reusedAlias.Should().ThrowAsync<LedgerValidationException>();
        var tooMuch = await fixture.FundingAsync(firstCall, "a", "receipt-3", 36m);
        var overfund = () => fixture.PostAsync(tooMuch);
        await overfund.Should().ThrowAsync<LedgerValidationException>();
        (await fixture.Capital.ListPostingReceiptsAsync(fixture.Scope)).Should().HaveCount(3);
        (await fixture.Database.JournalStore.GetByPeriodAsync(fixture.Period.PeriodId)).Should().HaveCount(3);
    }

    [LedgerDatabaseFact]
    public async Task ReviewedVaultBankObservation_ImportsPostsAndReplaysWithoutDuplicatingCash()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ApproveAsync("a", 100m);
        var call = fixture.Call("call-a", ("a", 60m, 100m));
        await fixture.Capital.SaveCallAsync(call);
        await fixture.PostAsync(fixture.Issuance(call, "a"));
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "capital-bank-observation", Guid.NewGuid().ToString("N"));
        try
        {
            var vault = new FileEvidenceArtifactStore(root, NullLogger<FileEvidenceArtifactStore>.Instance);
            var importer = new PrivateCapitalBankObservationService(vault, fixture.Banking);
            var source = new PrivateCapitalBankObservationSourceDto(Guid.NewGuid(), Guid.Parse(fixture.Scope.EntityId),
                25m, "USD", NoticeDate, NoticeDate, NoticeDate, "Credit", "statement-line-a", Now.AddTicks(7));
            var retained = await RetainAsync(source);
            var first = await importer.ImportAsync(fixture.Scope, retained.Request, "operator");
            first.IsReplay.Should().BeFalse();
            first.BankTransaction.TransactionType.Should().Be("CapitalCallFunding");
            first.BankTransaction.RecordedAt.Should().Be(Now, "PostgreSQL retains microseconds while source bytes retain their exact hash");
            first.BankTransaction.ExternalRef.Should().Contain(first.SourceHashSha256);
            var funding = fixture.Funding(call, "a", "receipt-from-vault", first.BankTransaction);
            await fixture.PostAsync(funding);
            await fixture.PostAsync(funding);

            var replay = await importer.ImportAsync(fixture.Scope, retained.Request, "operator");
            replay.IsReplay.Should().BeTrue();
            replay.BankTransaction.Should().BeEquivalentTo(first.BankTransaction);
            (await fixture.Banking.GetBankTransactionsAsync()).Should().ContainSingle();
            (await fixture.Capital.GetCallAsync(fixture.Scope, "call-a"))!.Lines.Single().OutstandingAmount.Should().Be(35m);

            var changedSource = await RetainAsync(source with { Amount = 26m });
            var conflictingReplay = () => importer.ImportAsync(fixture.Scope, changedSource.Request, "operator");
            await conflictingReplay.Should().ThrowAsync<InvalidOperationException>();
            var sourcePath = Path.Combine(root, retained.Intake.VaultIdentity.Artifacts.Single().RelativePath);
            await File.WriteAllTextAsync(sourcePath, "{}");
            var changedBytes = () => importer.ImportAsync(fixture.Scope, retained.Request, "operator");
            await changedBytes.Should().ThrowAsync<InvalidOperationException>();
            (await fixture.Banking.GetBankTransactionsAsync()).Should().ContainSingle();
            (await fixture.Capital.ListPostingReceiptsAsync(fixture.Scope)).Should().HaveCount(2);
            (await fixture.Database.JournalStore.GetByPeriodAsync(fixture.Period.PeriodId)).Should().HaveCount(2);

            async Task<(ImportPrivateCapitalBankObservationRequest Request, EvidenceVaultIntakeResponseDto Intake)> RetainAsync(
                PrivateCapitalBankObservationSourceDto observation)
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(observation, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                var intake = await vault.WriteIntakeArtifactAsync(new("fund", fixture.Scope.FundProfileId, "ManualUpload",
                    "bank-observation.json", Convert.ToBase64String(bytes), "application/json", "bank-statement",
                    observation.StatementReference, "source-maker")
                {
                    Actor = "source-maker",
                    TenantId = fixture.Scope.TenantId,
                    Scope = fixture.Scope.CompanyId,
                    Classification = EvidenceDocumentClassificationDto.BankEvidence
                });
                await vault.ReviewDocumentAsync(intake.VaultIdentity.VaultId, intake.Document!.DocumentId,
                    fixture.Scope.TenantId, fixture.Scope.CompanyId,
                    new(EvidenceDocumentReviewStatusDto.Accepted, "source-reviewer")
                    {
                        ConfirmedFields = [new("sourceContentHashSha256", Sha256Digest.Compute(bytes), "source-reviewer", DateTimeOffset.UtcNow)]
                    });
                return (new(EvidenceVaultReference.CanonicalPrefix + intake.VaultIdentity.VaultId, intake.Document.DocumentId), intake);
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [LedgerDatabaseFact]
    public async Task CompetingCallPostings_RecheckCapacityInDatabaseAcrossIndependentTargets()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ApproveAsync("a", 100m);
        var first = fixture.Call("call-a", ("a", 80m, 100m));
        var second = fixture.Call("call-b", ("a", 80m, 100m));
        await fixture.Capital.SaveCallAsync(first);
        await fixture.Capital.SaveCallAsync(second);
        var outcomes = await Task.WhenAll(
            AttemptAsync(() => fixture.PostAsync(fixture.Issuance(first, "a"))),
            AttemptAsync(() => fixture.PostAsync(fixture.Issuance(second, "a"))));
        outcomes.Count(x => x is null).Should().Be(1);
        (await fixture.Capital.ListPostingReceiptsAsync(fixture.Scope)).Should().ContainSingle().Which.Amount.Should().Be(80m);
        (await fixture.Database.JournalStore.GetByPeriodAsync(fixture.Period.PeriodId)).Should().ContainSingle();
    }

    [LedgerDatabaseFact]
    public async Task Funding_RejectsVoidedOrOutgoingRetainedBankEvidence()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ApproveAsync("a", 100m);
        var call = fixture.Call("call-a", ("a", 60m, 100m));
        await fixture.Capital.SaveCallAsync(call);
        await fixture.PostAsync(fixture.Issuance(call, "a"));
        var source = await fixture.BankAsync(25m);
        var reviewed = fixture.Funding(call, "a", "receipt-a", source);
        await fixture.ExecuteAsync($"update \"{fixture.Database.Options.PrivateCapitalBankingSchema}\".bank_transactions "
            + $"set is_voided=true where bank_transaction_id='{source.BankTransactionId:D}'");
        var post = () => fixture.PostAsync(reviewed);
        await post.Should().ThrowAsync<LedgerValidationException>().WithMessage("*changed or was voided*");
        var outgoing = source with { BankTransactionId = Guid.NewGuid(), TransactionType = "Drawdown" };
        await fixture.Banking.InsertBankTransactionAsync(outgoing);
        var fundWithOutgoing = () => fixture.PostAsync(fixture.Funding(call, "a", "receipt-outgoing", outgoing));
        await fundWithOutgoing.Should().ThrowAsync<LedgerValidationException>();
        (await fixture.Capital.ListPostingReceiptsAsync(fixture.Scope)).Should().ContainSingle().Which.Kind.Should().Be("Issuance");
        (await fixture.Database.JournalStore.GetByPeriodAsync(fixture.Period.PeriodId)).Should().ContainSingle();
    }

    [LedgerDatabaseFact]
    public async Task CompetingFundingPostings_RecheckOpenReceivableAcrossIndependentTargets()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ApproveAsync("a", 100m);
        var call = fixture.Call("call-a", ("a", 60m, 100m));
        await fixture.Capital.SaveCallAsync(call);
        await fixture.PostAsync(fixture.Issuance(call, "a"));
        var first = await fixture.FundingAsync(call, "a", "receipt-a", 40m);
        var second = await fixture.FundingAsync(call, "a", "receipt-b", 40m);
        var outcomes = await Task.WhenAll(
            AttemptAsync(() => fixture.PostAsync(first)), AttemptAsync(() => fixture.PostAsync(second)));
        outcomes.Count(x => x is null).Should().Be(1);
        var retained = await fixture.Capital.GetCallAsync(fixture.Scope, "call-a");
        retained!.Lines.Single().OutstandingAmount.Should().Be(20m);
        (await fixture.Capital.ListPostingReceiptsAsync(fixture.Scope)).Should().HaveCount(2);
        (await fixture.Database.JournalStore.GetByPeriodAsync(fixture.Period.PeriodId)).Should().HaveCount(2);
    }

    [LedgerDatabaseFact]
    public async Task RevisedApprovedCommitment_InvalidatesPendingCallAndCannotErasePostedCapital()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ApproveAsync("a", 100m);
        var stale = fixture.Call("call-stale", ("a", 60m, 100m));
        await fixture.Capital.SaveCallAsync(stale);
        await fixture.Capital.SaveCommitmentDraftAsync(fixture.Commitment("a", 110m) with { Revision = 2 }, 1);
        await fixture.Capital.ApproveCommitmentAsync(fixture.Scope, "a", 2, "reviewer", Now.AddMinutes(1));
        var stalePost = () => fixture.PostAsync(fixture.Issuance(stale, "a"));
        await stalePost.Should().ThrowAsync<LedgerValidationException>();
        (await fixture.Capital.ListPostingReceiptsAsync(fixture.Scope)).Should().BeEmpty();
        var current = fixture.Call("call-current", ("a", 60m, 110m));
        current = current with { Lines = current.Lines.Select(x => x with { CommitmentRevision = 2 }).ToArray() };
        current = current with { PlanHash = PrivateCapitalCallPlanHash.Compute(current) };
        await fixture.Capital.SaveCallAsync(current);
        await fixture.PostAsync(fixture.Issuance(current, "a"));
        await fixture.Capital.SaveCommitmentDraftAsync(fixture.Commitment("a", 50m) with { Revision = 3 }, 2);
        var reduce = () => fixture.Capital.ApproveCommitmentAsync(fixture.Scope, "a", 3, "reviewer", Now.AddMinutes(2));
        await reduce.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.Capital.ListPostingReceiptsAsync(fixture.Scope)).Should().ContainSingle();
    }

    [LedgerDatabaseFact]
    public async Task FutureApprovedReduction_BlocksBackdatedPreviewAndRetention()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ApproveAsync("a", 100m);
        await fixture.ApproveRevisionAsync("a", 2, 50m, new(2026, 5, 20));
        var service = new Meridian.FinancialOperations.PrivateCapital.PrivateCapitalLifecycleService(fixture.Capital);
        var preview = await service.PreviewCallAsync(fixture.Scope,
            new("preview-call", 80m, NoticeDate, NoticeDate.AddDays(10), fixture.Period.PeriodId.ToString("D"), ["a"]));
        preview.IsReady.Should().BeFalse();
        preview.Blockers.Should().ContainSingle(x => x.Contains("revision 2 effective 2026-05-20", StringComparison.Ordinal));
        var retain = () => fixture.Capital.SaveCallAsync(fixture.Call("call-backdated", ("a", 80m, 100m)));
        await retain.Should().ThrowAsync<InvalidOperationException>().WithMessage("*revision 2 effective 2026-05-20*");
        (await fixture.Capital.ListCallsAsync(fixture.Scope)).Should().BeEmpty();
        (await fixture.Capital.ListPostingReceiptsAsync(fixture.Scope)).Should().BeEmpty();
    }

    [LedgerDatabaseFact]
    public async Task FutureApprovedReduction_BlocksAlreadyRetainedBackdatedCallAtFinalAppend()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ApproveAsync("a", 100m);
        var call = fixture.Call("call-backdated", ("a", 80m, 100m));
        await fixture.Capital.SaveCallAsync(call);
        await fixture.ApproveRevisionAsync("a", 2, 50m, new(2026, 5, 20));
        var post = () => fixture.PostAsync(fixture.Issuance(call, "a"));
        await post.Should().ThrowAsync<LedgerValidationException>().WithMessage("*revision 2 effective 2026-05-20*");
        (await fixture.Capital.ListPostingReceiptsAsync(fixture.Scope)).Should().BeEmpty();
        (await fixture.Database.JournalStore.GetByPeriodAsync(fixture.Period.PeriodId)).Should().BeEmpty();
        (await fixture.Capital.GetCommitmentAsync(fixture.Scope, "a"))!.UncalledAmount.Should().Be(50m);
    }

    [LedgerDatabaseFact]
    public async Task FutureIncreaseAndLaterPostedCall_PreserveValidBackdatedAllocation()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ApproveAsync("a", 100m);
        var early = fixture.At(fixture.Call("early", ("a", 80m, 100m)), new(2026, 5, 5));
        await fixture.Capital.SaveCallAsync(early);
        await fixture.PostAsync(fixture.Issuance(early, "a"));
        await fixture.ApproveRevisionAsync("a", 2, 150m, new(2026, 5, 20));
        var later = fixture.At(fixture.Call("later", ("a", 50m, 70m)), new(2026, 5, 25), 2);
        await fixture.Capital.SaveCallAsync(later);
        await fixture.PostAsync(fixture.Issuance(later, "a"));

        var service = new Meridian.FinancialOperations.PrivateCapital.PrivateCapitalLifecycleService(fixture.Capital);
        var request = new PreviewPrivateCapitalCallRequest("backdated", 20m, NoticeDate, NoticeDate.AddDays(10),
            fixture.Period.PeriodId.ToString("D"), ["a"]);
        var preview = await service.PreviewCallAsync(fixture.Scope, request);
        preview.IsReady.Should().BeTrue();
        preview.Call!.Lines.Single().UncalledBefore.Should().Be(20m);
        preview.Call.Lines.Single().CommitmentRevision.Should().Be(1);
        var retained = await service.CreateCallAsync(fixture.Scope, new(request, preview.Call.PlanHash), "preparer");
        await fixture.PostAsync(fixture.Issuance(retained, "a"));

        var receipts = await fixture.Capital.ListPostingReceiptsAsync(fixture.Scope);
        receipts.Where(x => x.EffectiveDate <= NoticeDate).Sum(x => x.Amount).Should().Be(100m);
        receipts.Sum(x => x.Amount).Should().Be(150m);
        (await fixture.Capital.GetCommitmentAsync(fixture.Scope, "a"))!.UncalledAmount.Should().Be(0m);
        (await fixture.Database.JournalStore.GetByPeriodAsync(fixture.Period.PeriodId)).Should().HaveCount(3);
    }

    [LedgerDatabaseFact]
    public async Task SupersededReductionAtSameEffectiveDate_DoesNotConstrainBackdatedCall()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ApproveAsync("a", 100m);
        await fixture.ApproveRevisionAsync("a", 2, 50m, new(2026, 5, 20));
        await fixture.ApproveRevisionAsync("a", 3, 150m, new(2026, 5, 20));
        var call = fixture.Call("call-backdated", ("a", 80m, 100m));
        await fixture.Capital.SaveCallAsync(call);
        await fixture.PostAsync(fixture.Issuance(call, "a"));
        (await fixture.Capital.ListPostingReceiptsAsync(fixture.Scope)).Should().ContainSingle().Which.Amount.Should().Be(80m);
        (await fixture.Capital.GetCommitmentAsync(fixture.Scope, "a"))!.UncalledAmount.Should().Be(70m);
    }

    [LedgerDatabaseFact]
    public async Task FutureReductionApprovalAndBackdatedPosting_SerializeWithoutOvercallingEitherTimeline()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ApproveAsync("a", 100m);
        var call = fixture.Call("call-backdated", ("a", 80m, 100m));
        await fixture.Capital.SaveCallAsync(call);
        await fixture.Capital.SaveCommitmentDraftAsync(fixture.Commitment("a", 50m) with
        { Revision = 2, EffectiveFrom = new(2026, 5, 20) }, 1);

        var outcomes = await Task.WhenAll(
            AttemptAsync(() => fixture.PostAsync(fixture.Issuance(call, "a"))),
            AttemptAsync(() => new PostgresPrivateCapitalStore(fixture.Database.Options)
                .ApproveCommitmentAsync(fixture.Scope, "a", 2, "reviewer", Now)));

        outcomes.Count(x => x is null).Should().Be(1);
        var approved = (await fixture.Capital.ListCommitmentsAsync(fixture.Scope))
            .Where(x => x.ApprovalStatus == PrivateCapitalCommitmentApprovalStatus.Approved).ToArray();
        var receipts = await fixture.Capital.ListPostingReceiptsAsync(fixture.Scope);
        PrivateCapitalAuthorityRules.FindIssuanceTimelineViolation("a", approved,
            receipts.Select(x => (x.EffectiveDate, x.Amount))).Should().BeNull();
        if (approved.Any(x => x.Revision == 2))
            receipts.Should().BeEmpty();
        else
            receipts.Should().ContainSingle().Which.Amount.Should().Be(80m);
    }

    [LedgerDatabaseFact]
    public async Task RepeatableReadSnapshotCannotHideNewlyApprovedFutureReductionFromExternalAppend()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ApproveAsync("a", 100m);
        var call = fixture.Call("call-backdated", ("a", 80m, 100m));
        await fixture.Capital.SaveCallAsync(call);
        await using var connection = new NpgsqlConnection(fixture.Database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await using (var snapshot = new NpgsqlCommand(
            $"select count(*) from \"{fixture.Database.Options.SchemaName}\".private_capital_commitments", connection, transaction))
            (await snapshot.ExecuteScalarAsync()).Should().Be(1L);
        // The old snapshot cannot see this inserted and approved successor row.
        await fixture.ApproveRevisionAsync("a", 2, 50m, new(2026, 5, 20));
        var append = () => fixture.Database.JournalStore.AppendAsync(connection, transaction, fixture.Issuance(call, "a"));
        await append.Should().ThrowAsync<LedgerValidationException>().WithMessage("*transaction isolation*");
        await transaction.RollbackAsync();
        (await fixture.Capital.ListPostingReceiptsAsync(fixture.Scope)).Should().BeEmpty();
        (await fixture.Database.JournalStore.GetByPeriodAsync(fixture.Period.PeriodId)).Should().BeEmpty();
    }

    [LedgerDatabaseFact]
    public async Task ExternalTransactionRollback_RollsBackJournalAndReceiptAndAllowsExactRecovery()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ApproveAsync("a", 100m);
        var call = fixture.Call("call-a", ("a", 60m, 100m));
        await fixture.Capital.SaveCallAsync(call);
        var write = fixture.Issuance(call, "a");
        await using (var connection = new NpgsqlConnection(fixture.Database.Options.ConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            await fixture.Database.JournalStore.AppendAsync(connection, transaction, write);
            await transaction.RollbackAsync();
        }
        (await fixture.Capital.ListPostingReceiptsAsync(fixture.Scope)).Should().BeEmpty();
        (await fixture.Database.JournalStore.GetByPeriodAsync(fixture.Period.PeriodId)).Should().BeEmpty();
        await fixture.PostAsync(write);
        await fixture.PostAsync(write);
        (await fixture.Capital.ListPostingReceiptsAsync(fixture.Scope)).Should().ContainSingle();
        (await fixture.Database.JournalStore.GetByPeriodAsync(fixture.Period.PeriodId)).Should().ContainSingle();
    }

    [LedgerDatabaseFact]
    public async Task DatabaseTriggers_ProtectApprovedCommitmentCallAndPostingFromUpdateOrDelete()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ApproveAsync("a", 100m);
        var call = fixture.Call("call-a", ("a", 60m, 100m));
        await fixture.Capital.SaveCallAsync(call);
        await fixture.PostAsync(fixture.Issuance(call, "a"));
        foreach (var table in new[] { "private_capital_commitments", "private_capital_calls", "private_capital_postings" })
        {
            var assignment = table == "private_capital_postings" ? "amount=amount+1" : "document=document || '{\"tampered\":true}'::jsonb";
            foreach (var sql in new[] { $"update \"{fixture.Database.Options.SchemaName}\".{table} set {assignment}",
                         $"delete from \"{fixture.Database.Options.SchemaName}\".{table}" })
            {
                var mutate = () => fixture.ExecuteAsync(sql);
                await mutate.Should().ThrowAsync<PostgresException>().Where(x => x.SqlState == PostgresErrorCodes.RaiseException);
            }
        }
        (await fixture.Capital.GetCommitmentAsync(fixture.Scope, "a"))!.TotalCommitment.Should().Be(100m);
        (await fixture.Capital.GetCallAsync(fixture.Scope, "call-a"))!.PlanHash.Should().Be(call.PlanHash);
        (await fixture.Capital.ListPostingReceiptsAsync(fixture.Scope)).Should().ContainSingle().Which.Amount.Should().Be(60m);
    }

    private static async Task<Exception?> AttemptAsync(Func<Task> operation)
    {
        try
        { await operation(); return null; }
        catch (Exception exception) { return exception; }
    }

    private sealed record Fixture(
        LedgerPostgresTestDatabase Database,
        PrivateCapitalScopeDto Scope,
        LedgerAccountingPeriod Period,
        PostgresPrivateCapitalStore Capital,
        PostgresBankingStore Banking) : IAsyncDisposable
    {
        public static async Task<Fixture> CreateAsync()
        {
            var database = await LedgerPostgresTestDatabase.CreateAsync();
            try
            {
                var entity = Guid.NewGuid();
                var book = new LedgerBookRecord(Guid.NewGuid(), "fund-alpha", entity, FundStructureNodeKindDto.Entity,
                    "Synthetic capital book", "USD", Now, Now);
                await new PostgresFundProfileTenancyRegistry(database.Options).BindAsync("fund-alpha", "tenant-alpha", "company-alpha");
                await database.JournalStore.SaveLedgerBookAsync(book);
                var period = await database.JournalStore.SavePeriodAsync(new(Guid.NewGuid(), book.LedgerBookId, 2026, 5,
                    "2026-05", new(2026, 5, 1), new(2026, 5, 31), "Open", Now, null, 0), 0);
                var bankingOptions = new BankingStoreOptions
                {
                    ConnectionString = database.Options.ConnectionString,
                    Schema = database.CreateSchemaName("capital_banking")
                };
                await new BankingMigrationRunner(bankingOptions).EnsureMigratedAsync();
                database.Options.PrivateCapitalBankingSchema = bankingOptions.Schema;
                database.Options.RequireGovernedPostingCommand = true;
                database.Options.RequireExpectedVersion = true;
                return new(database, new("tenant-alpha", "company-alpha", entity.ToString("D"), "fund-alpha", book.LedgerBookId, "USD"),
                    period, new(database.Options), new(bankingOptions));
            }
            catch { await database.DisposeAsync(); throw; }
        }

        public PrivateCapitalCommitmentDto Commitment(string id, decimal amount) => new(id, Scope, 1, $"investor-{id}",
            $"capital-{id}", amount, new(2026, 5, 1), new(2026, 5, 1), null, true,
            PrivateCapitalCommitmentApprovalStatus.Draft, [$"evidence://commitment/{id}"], "preparer", Now);

        public async Task ApproveAsync(string id, decimal amount)
        {
            await Capital.SaveCommitmentDraftAsync(Commitment(id, amount), 0);
            await Capital.ApproveCommitmentAsync(Scope, id, 1, "reviewer", Now);
        }

        public async Task ApproveRevisionAsync(string id, int revision, decimal amount, DateOnly effectiveFrom)
        {
            await Capital.SaveCommitmentDraftAsync(Commitment(id, amount) with { Revision = revision, EffectiveFrom = effectiveFrom }, revision - 1);
            await Capital.ApproveCommitmentAsync(Scope, id, revision, "reviewer", Now);
        }

        public PrivateCapitalCallDto At(PrivateCapitalCallDto call, DateOnly noticeDate, int revision = 1)
        {
            call = call with
            {
                NoticeDate = noticeDate,
                DueDate = noticeDate.AddDays(5),
                Lines = call.Lines.Select(x => x with { CommitmentRevision = revision }).ToArray()
            };
            return call with { PlanHash = PrivateCapitalCallPlanHash.Compute(call) };
        }

        public PrivateCapitalCallDto Call(string id, params (string Commitment, decimal Amount, decimal Uncalled)[] lines)
        {
            var call = new PrivateCapitalCallDto(id, Scope, Period.PeriodId.ToString("D"), NoticeDate, NoticeDate.AddDays(10),
                lines.Sum(x => x.Amount), "Investment funding", "", lines.Select(x => new PrivateCapitalCallLineDto(
                    $"{id}:{x.Commitment}", x.Commitment, 1, $"investor-{x.Commitment}", $"capital-{x.Commitment}",
                    x.Amount, x.Uncalled, x.Uncalled - x.Amount)).ToArray(), "preparer", Now);
            return call with { PlanHash = PrivateCapitalCallPlanHash.Compute(call) };
        }

        public LedgerJournalEntryWrite Issuance(PrivateCapitalCallDto call, string commitment)
            => Write(call, commitment, "Issuance", call.Lines.Single(x => x.CommitmentId == commitment).Amount);

        public async Task<BankTransactionDto> BankAsync(decimal amount)
        {
            var source = new BankTransactionDto(Guid.NewGuid(), Guid.Parse(Scope.EntityId), "CapitalCallFunding", NoticeDate,
                NoticeDate, NoticeDate, amount, Scope.Currency, $"bank-{Guid.NewGuid():N}", Now, false, "operator");
            await Banking.InsertBankTransactionAsync(source);
            return source;
        }

        public async Task<LedgerJournalEntryWrite> FundingAsync(PrivateCapitalCallDto call, string commitment, string receipt, decimal amount)
            => Funding(call, commitment, receipt, await BankAsync(amount));

        public LedgerJournalEntryWrite Funding(PrivateCapitalCallDto call, string commitment, string receipt, BankTransactionDto source)
            => Write(call, commitment, "Funding", source.Amount, receipt, source);

        private LedgerJournalEntryWrite Write(PrivateCapitalCallDto call, string commitmentId, string kind,
            decimal amount, string? receipt = null, BankTransactionDto? bank = null)
        {
            var line = call.Lines.Single(x => x.CommitmentId == commitmentId);
            var effectiveDate = kind == "Issuance" ? call.NoticeDate : bank?.EffectiveDate ?? call.NoticeDate;
            var authority = new PrivateCapitalPostingAuthorityDto(Scope, call.CallId, commitmentId, line.CommitmentRevision,
                call.PlanHash, kind, amount, effectiveDate, receipt, bank?.BankTransactionId.ToString("D"),
                bank is null ? null : PrivateCapitalPostingAuthority.BankTransactionHash(bank));
            var journalId = Guid.NewGuid();
            var key = kind == "Issuance" ? $"capital-call:{commitmentId}:{line.InstallmentId}"
                : $"capital-call-funding:{commitmentId}:{line.InstallmentId}:receipt:{Uri.EscapeDataString(receipt!)}";
            var dimensions = new LedgerLineDimensionSet
            {
                EntityId = Scope.EntityId,
                FundId = Scope.FundProfileId,
                InvestorId = line.InvestorId,
                CapitalAccountId = line.CapitalAccountId,
                ExternalGlDimensions = new Dictionary<string, string>
                {
                    ["capitalCallId"] = call.CallId,
                    ["commitmentId"] = commitmentId,
                    ["commitmentRevision"] = line.CommitmentRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["capitalActivityKind"] = kind == "Issuance" ? "CapitalCallIssuance" : "CapitalCallFunding"
                }
            };
            var metadata = new JournalEntryMetadata(ProjectId: Scope.FundProfileId, EffectiveDate: effectiveDate,
                IdempotencyKey: key, FundEventId: $"fund-event:{Scope.FundProfileId}:capital-call:{line.InstallmentId}",
                FundEventType: "CapitalCall", CapitalAccountId: line.CapitalAccountId, InvestorId: line.InvestorId,
                Tags: new Dictionary<string, string>
                {
                    ["tenantId"] = Scope.TenantId,
                    ["companyId"] = Scope.CompanyId,
                    [PrivateCapitalPostingAuthority.MetadataTag] = PrivateCapitalPostingAuthority.Serialize(authority)
                });
            var debit = kind == "Issuance" ? LedgerAccounts.CapitalCallReceivableFor(line.InvestorId)
                : LedgerAccounts.CashAccount(Scope.FundProfileId);
            var credit = kind == "Issuance" ? LedgerAccounts.InvestorCapitalFor(line.InvestorId)
                : LedgerAccounts.CapitalCallReceivableFor(line.InvestorId);
            var entry = new JournalEntry(journalId, Now, "Synthetic capital activity",
            [
                new(Guid.NewGuid(), journalId, Now, debit, amount, 0m, "Synthetic capital activity", dimensions),
                new(Guid.NewGuid(), journalId, Now, credit, 0m, amount, "Synthetic capital activity", dimensions)
            ], metadata);
            var aggregate = Guid.NewGuid();
            var command = new AccountingPostingCommandDto(Guid.NewGuid(), aggregate, Period.PeriodId, effectiveDate, Now, key,
                ExpectedVersion: Period.Version, ApprovalState: AccountingPostingApprovalStateDto.Approved,
                ApprovalId: "reviewed-capital-activity", OperatorRationale: "Reviewed synthetic fixture", LedgerBookId: Scope.LedgerBookId)
            { Actor = "operator", Provenance = DataProvenance.Simulated };
            return new(entry, aggregate, Period.PeriodId, PostingCommand: command, LedgerBookId: Scope.LedgerBookId);
        }

        public async Task PostAsync(LedgerJournalEntryWrite write)
        {
            using var target = new DurableLedgerPostingTarget(new PostgresLedgerJournalStore(Database.Options));
            await target.PostAsync(write);
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(Database.Options.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }
}
