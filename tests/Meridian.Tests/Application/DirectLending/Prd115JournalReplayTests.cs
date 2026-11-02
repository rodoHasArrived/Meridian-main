using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.SecurityMaster;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.DirectLending;
using Meridian.Storage.Ledger;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Application.DirectLending;

public sealed class Prd115JournalReplayTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly DateOnly Date = new(2026, 3, 24);
    private static readonly DateTimeOffset RecordedAt = new(2026, 3, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid SecurityId = Guid.Parse("e76f7cd5-a5c2-4552-a03e-2d2a332c50c3");

    [Fact]
    public async Task VersionedReceipt_ProjectsBalancedReceivablesAndLiabilitiesWithoutIncome()
    {
        var loanId = Guid.NewGuid();
        var contract = Contract(loanId);
        using var payload = PaymentPayload(contract);
        var projector = Projector();

        var writes = await projector.ProjectAsync(loanId, contract, "loan.mixed-payment-applied", Date,
            payload, Guid.NewGuid(), new DirectLendingEventWriteMetadata(null, null, Guid.NewGuid(), "test", false),
            CancellationToken.None);

        var entry = writes.Should().ContainSingle().Subject.Entry;
        entry.IsBalanced.Should().BeTrue();
        entry.Lines.Sum(line => line.Debit).Should().Be(100m);
        entry.Lines.Sum(line => line.Credit).Should().Be(100m);
        entry.Lines.Should().NotContain(line => line.Account.AccountType == LedgerAccountType.Revenue);
        entry.Lines.Should().ContainSingle(line => line.Account.Name == "AccruedInterestReceivable" && line.Credit == 10m);
        entry.Lines.Should().ContainSingle(line => line.Account.Name == "CommitmentFeeReceivable" && line.Credit == 5m);
        entry.Lines.Should().ContainSingle(line => line.Account.Name == "FeeReceivable" && line.Credit == 3m);
        entry.Lines.Should().ContainSingle(line => line.Account.Name == "PenaltyReceivable" && line.Credit == 2m);
        entry.Lines.Should().ContainSingle(line => line.Account.Name == "LoanPrincipal" && line.Credit == 50m);
        entry.Lines.Should().ContainSingle(line => line.Account.Name == "UnappliedCash" && line.Account.AccountType == LedgerAccountType.Liability && line.Credit == 20m);
        entry.Lines.Should().ContainSingle(line => line.Account.Name == "PrepaidInterest" && line.Account.AccountType == LedgerAccountType.Liability && line.Credit == 10m);
    }

    [Fact]
    public async Task VersionedReceipt_StillRefusesClosedAccountingPeriods()
    {
        var contract = Contract(Guid.NewGuid());
        using var payload = PaymentPayload(contract);
        var act = () => Projector("Closed").ProjectAsync(contract.LoanId, contract, "loan.mixed-payment-applied",
            Date, payload, Guid.NewGuid(), new DirectLendingEventWriteMetadata(null, null, null, "test", false),
            CancellationToken.None);

        await act.Should().ThrowAsync<DirectLendingCommandException>().WithMessage("*No ledger accounting period accepts*");
    }

    [Fact]
    public async Task AssessmentsThenReceipt_ClearReceivablesWithoutRecognizingIncomeTwice()
    {
        var contract = Contract(Guid.NewGuid());
        var projector = Projector();
        using var fee = JsonSerializer.SerializeToDocument(new { amount = 3m, cashAccountingVersion = DirectLendingCashJournal.CashAccountingVersion }, Json);
        using var penalty = JsonSerializer.SerializeToDocument(new { penaltyAmount = 2m, cashAccountingVersion = DirectLendingCashJournal.CashAccountingVersion }, Json);
        using var receipt = PaymentPayload(contract,
            new MixedPaymentResolutionDto(new PaymentBreakdownDto(0m, 0m, 3m, 2m, 0m),
                "Manual", DirectLendingCashJournal.CashAccountingVersion, 0m, 0m, DirectLendingCashJournal.CashAccountingVersion), 5m);
        var writes = new List<LedgerJournalEntryWrite>();
        foreach (var (type, payload) in new[]
                 {
                     ("loan.fee-assessed", fee), ("loan.prepayment-penalty-charged", penalty),
                     ("loan.mixed-payment-applied", receipt)
                 })
            writes.AddRange(await projector.ProjectAsync(contract.LoanId, contract, type, Date, payload,
                Guid.NewGuid(), new DirectLendingEventWriteMetadata(null, null, null, "test", false), CancellationToken.None));

        writes.Should().HaveCount(3);
        writes.Should().OnlyContain(write => write.Entry.IsBalanced);
        var lines = writes.SelectMany(write => write.Entry.Lines).ToArray();
        lines.Where(line => line.Account.Name == "FeeReceivable").Sum(line => line.Debit - line.Credit).Should().Be(0m);
        lines.Where(line => line.Account.Name == "PenaltyReceivable").Sum(line => line.Debit - line.Credit).Should().Be(0m);
        lines.Where(line => line.Account.AccountType == LedgerAccountType.Revenue).Sum(line => line.Credit - line.Debit).Should().Be(5m);
        writes[^1].Entry.Lines.Should().NotContain(line => line.Account.AccountType == LedgerAccountType.Revenue);
    }

    [Theory]
    [InlineData("loan.fee-assessed")]
    [InlineData("loan.prepayment-penalty-charged")]
    public async Task LegacyAssessmentProjection_DoesNotBackfillIncome(string eventType)
    {
        var contract = Contract(Guid.NewGuid());
        using var payload = JsonSerializer.SerializeToDocument(new { amount = 3m, penaltyAmount = 3m }, Json);

        var writes = await Projector().ProjectAsync(contract.LoanId, contract, eventType, Date, payload,
            Guid.NewGuid(), new DirectLendingEventWriteMetadata(null, null, null, "test", false), CancellationToken.None);

        writes.Should().BeEmpty();
    }

    [Fact]
    public void LegacyReceipt_KeepsOriginalClearingJournal()
    {
        using var payload = JsonDocument.Parse("""{"amount":100,"resolution":{"breakdown":{"toInterest":10,"toCommitmentFee":0,"toFees":0,"toPenalty":0,"toPrincipal":0},"resolutionBasis":"Manual","ruleVersion":"v1","unappliedAmount":90}}""");

        var lines = DirectLendingCashJournal.BuildLines(DirectLendingCashJournal.Read(payload.RootElement));

        lines.Should().BeEquivalentTo(new[]
        {
            new DirectLendingCashJournal.Line("Cash", LedgerAccountType.Asset, 100m, 0m),
            new DirectLendingCashJournal.Line("LoanAndAccrualsClearing", LedgerAccountType.Asset, 0m, 100m)
        });
    }

    [Theory]
    [InlineData("unknown-version")]
    [InlineData("negative-allocation")]
    [InlineData("unbalanced")]
    [InlineData("missing-bucket")]
    [InlineData("missing-hash")]
    [InlineData("missing-cash-id")]
    [InlineData("unsupported-currency")]
    [InlineData("missing-version")]
    [InlineData("missing-date")]
    [InlineData("conflicting-rule")]
    [InlineData("excess-precision")]
    public void VersionedReceipt_RejectsMalformedRetainedEvidence(string defect)
    {
        using var original = PaymentPayload(Contract(Guid.NewGuid()));
        var node = JsonNode.Parse(original.RootElement.GetRawText())!;
        switch (defect)
        {
            case "unknown-version": node["resolution"]!["cashAccountingVersion"] = "direct-lending-cash-v99"; break;
            case "negative-allocation": node["resolution"]!["unappliedAmount"] = -1m; break;
            case "unbalanced": node["resolution"]!["unappliedAmount"] = 21m; break;
            case "missing-bucket": node["resolution"]!["breakdown"]!.AsObject().Remove("toFees"); break;
            case "missing-hash": node.AsObject().Remove("termsHash"); break;
            case "missing-cash-id": node.AsObject().Remove("cashTransactionId"); break;
            case "unsupported-currency": node["currency"] = 99; break;
            case "missing-version": node["resolution"]!.AsObject().Remove("cashAccountingVersion"); break;
            case "missing-date": node.AsObject().Remove("effectiveDate"); break;
            case "conflicting-rule": node["resolution"]!["ruleVersion"] = "unreviewed-rule"; break;
            case "excess-precision":
                node["amount"] = 100.00000000001m;
                node["resolution"]!["unappliedAmount"] = 20.00000000001m;
                break;
        }
        using var malformed = JsonDocument.Parse(node.ToJsonString());

        Action act = () => DirectLendingCashJournal.Read(malformed.RootElement);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void VersionedReceipt_RejectsAmbiguousPropertyCasing()
    {
        using var original = PaymentPayload(Contract(Guid.NewGuid()));
        var json = original.RootElement.GetRawText().Replace("\"toInterest\":10", "\"ToInterest\":100,\"toInterest\":10", StringComparison.Ordinal);
        using var ambiguous = JsonDocument.Parse(json);

        Action act = () => DirectLendingCashJournal.Read(ambiguous.RootElement);

        act.Should().Throw<InvalidOperationException>().WithMessage("*duplicate property names*");
    }

    [Fact]
    public void Replay_RetainsCashBalancesThroughTermsAmendmentActivationAndSubsequentAccrual()
    {
        var loanId = Guid.NewGuid();
        var contract = Contract(loanId);
        var history = SeedHistory(contract);
        using var receipt = PaymentPayload(contract);
        history.Add(Event(6, "loan.mixed-payment-applied", receipt.RootElement));
        var amended = contract.CurrentTerms with { FixedAnnualRate = 0.09m };
        history.Add(Event(7, "loan.terms-amended", new { terms = amended, versionNumber = 2, amendmentReason = "Synthetic amendment after payment" }));
        history.Add(Event(8, "loan.activated", new { activationDate = Date }));
        history.Add(Event(9, "loan.daily-accrual-posted", Accrual(4m, 0m, 0m)));

        var rebuilder = new DirectLendingEventRebuilder();
        var first = rebuilder.Rebuild(loanId, history)!;
        var again = rebuilder.Rebuild(loanId, history)!;

        first.Should().BeEquivalentTo(again);
        first.Servicing.Balances.PrincipalOutstanding.Should().Be(50m);
        first.Servicing.DrawdownLots.Should().ContainSingle().Which.RemainingPrincipal.Should().Be(50m);
        first.Servicing.Balances.InterestAccruedUnpaid.Should().Be(4m);
        first.Servicing.Balances.FeesAccruedUnpaid.Should().Be(0m);
        first.Servicing.Balances.UnappliedCash.Should().Be(20m);
        first.Servicing.Balances.PrepaidInterest.Should().Be(10m, "future application needs separately supported contractual instructions");
        first.Contract.TermsVersions.Single(version => version.VersionNumber == 1).TermsHash.Should().Be(contract.TermsVersions[0].TermsHash);
    }

    [Fact]
    public void Replay_LegacyUnappliedCashDoesNotCreateUnsupportedLiabilityHistory()
    {
        var contract = Contract(Guid.NewGuid());
        var history = SeedHistory(contract);
        history.Add(Event(6, "loan.mixed-payment-applied", new
        {
            amount = 120m,
            resolution = new MixedPaymentResolutionDto(new PaymentBreakdownDto(100m, 0m, 0m, 0m, 0m), "Manual", "v1", 20m)
        }));

        var state = new DirectLendingEventRebuilder().Rebuild(contract.LoanId, history)!;

        state.Servicing.Balances.InterestAccruedUnpaid.Should().Be(0m, "legacy replay preserves the recorded, formerly clamped decision");
        state.Servicing.Balances.UnappliedCash.Should().Be(0m);
        state.Servicing.Balances.PrepaidInterest.Should().Be(0m);
        state.Servicing.Balances.PrincipalOutstanding.Should().Be(100m);
    }

    [Fact]
    public void Replay_RefusesCurrencyAmendmentThatWouldRelabelHeldCash()
    {
        var contract = Contract(Guid.NewGuid());
        var history = SeedHistory(contract);
        using var receipt = PaymentPayload(contract);
        history.Add(Event(6, "loan.mixed-payment-applied", receipt.RootElement));
        history.Add(Event(7, "loan.terms-amended", new
        {
            terms = contract.CurrentTerms with { BaseCurrency = CurrencyCode.EUR },
            versionNumber = 2, amendmentReason = "Unsupported denomination change"
        }));

        Action act = () => new DirectLendingEventRebuilder().Rebuild(contract.LoanId, history);

        act.Should().Throw<InvalidOperationException>().WithMessage("*currency*");
    }

    [Fact]
    public void Replay_RejectsVersionedAllocationBeyondRetainedBalance()
    {
        var contract = Contract(Guid.NewGuid());
        var history = SeedHistory(contract);
        using var receipt = PaymentPayload(contract, new MixedPaymentResolutionDto(
            new PaymentBreakdownDto(11m, 5m, 3m, 2m, 50m), "Manual", DirectLendingCashJournal.CashAccountingVersion, 19m, 10m,
            DirectLendingCashJournal.CashAccountingVersion));
        history.Add(Event(6, "loan.mixed-payment-applied", receipt.RootElement));

        Action act = () => new DirectLendingEventRebuilder().Rebuild(contract.LoanId, history);

        act.Should().Throw<InvalidOperationException>().WithMessage("*exceed the balances*");
    }

    [Fact]
    public void Replay_RejectsReceiptBoundToDifferentTermsHash()
    {
        var contract = Contract(Guid.NewGuid());
        var history = SeedHistory(contract);
        using var receipt = PaymentPayload(contract);
        var node = JsonNode.Parse(receipt.RootElement.GetRawText())!;
        node["termsHash"] = "different-hash";
        history.Add(Event(6, "loan.mixed-payment-applied", node));

        Action act = () => new DirectLendingEventRebuilder().Rebuild(contract.LoanId, history);

        act.Should().Throw<InvalidOperationException>().WithMessage("*does not match the retained contract*");
    }

    [Theory]
    [InlineData("missing-policy")]
    [InlineData("hold-policy")]
    [InlineData("at-maturity")]
    [InlineData("prohibited-principal")]
    [InlineData("penalty-bearing-principal")]
    public void VersionedReceipt_RejectsClassificationUnsupportedByRetainedTerms(string defect)
    {
        var original = Contract(Guid.NewGuid());
        var terms = defect switch
        {
            "missing-policy" => original.CurrentTerms with { ExcessPaymentPolicy = null },
            "hold-policy" => original.CurrentTerms with
            {
                ExcessPaymentPolicy = new DirectLendingExcessPaymentPolicyDto(1, ExcessPaymentTreatment.HoldUnappliedCash, "synthetic-hold-instructions")
            },
            "at-maturity" => original.CurrentTerms with { MaturityDate = Date },
            "prohibited-principal" => original.CurrentTerms with { PrepaymentAllowed = false },
            "penalty-bearing-principal" => original.CurrentTerms with { PrepaymentPenaltyRate = 0.02m },
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        var contract = original with
        {
            CurrentTerms = terms,
            TermsVersions = [new LoanTermsVersionDto(1, DirectLendingServiceSupport.ComputeTermsHash(terms), terms, "LoanCreated", null, RecordedAt)]
        };
        using var payload = PaymentPayload(contract);
        var receipt = DirectLendingCashJournal.Read(payload.RootElement);

        Action act = () => DirectLendingCashJournal.ValidateTerms(receipt, contract);

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssessmentAdmission_BlocksLegacyReceivableButAllowsMarkedAssessmentAndHeldCash(bool penalty)
    {
        var type = penalty ? "loan.prepayment-penalty-charged" : "loan.fee-assessed";
        var legacy = Event(1, type, penalty ? new { penaltyAmount = 3m } : (object)new { amount = 3m });
        var marked = Event(1, type, penalty
            ? new { penaltyAmount = 3m, cashAccountingVersion = DirectLendingCashJournal.CashAccountingVersion }
            : (object)new { amount = 3m, cashAccountingVersion = DirectLendingCashJournal.CashAccountingVersion });
        var collection = new MixedPaymentResolutionDto(
            new PaymentBreakdownDto(0m, 0m, penalty ? 0m : 3m, penalty ? 3m : 0m, 0m),
            "Manual", DirectLendingCashJournal.CashAccountingVersion, 0m, 0m, DirectLendingCashJournal.CashAccountingVersion);
        var hold = collection with { Breakdown = new PaymentBreakdownDto(0m, 0m, 0m, 0m, 0m), UnappliedAmount = 3m };

        DirectLendingCashJournal.ValidateAssessmentEvidence([legacy], collection).Should().Contain("reviewed receivable reconciliation");
        DirectLendingCashJournal.ValidateAssessmentEvidence([marked], collection).Should().BeNull();
        DirectLendingCashJournal.ValidateAssessmentEvidence([legacy], hold).Should().BeNull();
    }

    [Fact]
    public async Task OutboxDraft_UsesSameAllocationsAndRetainedCurrencyAfterTermsChange()
    {
        var contract = Contract(Guid.NewGuid());
        using var payload = PaymentPayload(contract);
        var source = Event(6, "loan.mixed-payment-applied", payload.RootElement);
        var updatedTerms = contract.CurrentTerms with { BaseCurrency = CurrencyCode.EUR };
        var updated = contract with
        {
            CurrentTermsVersion = 2,
            CurrentTerms = updatedTerms,
            TermsVersions = [.. contract.TermsVersions, new LoanTermsVersionDto(2, "new-hash", updatedTerms, "TermsAmended", "Test amendment", RecordedAt)]
        };
        var saved = await DraftJournalAsync(updated, source);

        saved.Lines.Should().OnlyContain(line => line.Currency == CurrencyCode.USD);
        saved.Lines.Sum(line => line.DebitAmount).Should().Be(saved.Lines.Sum(line => line.CreditAmount));
        saved.Lines.Should().ContainSingle(line => line.AccountCode == "PrepaidInterest" && line.CreditAmount == 10m);
        saved.Lines.Should().NotContain(line => line.AccountCode.EndsWith("Income", StringComparison.Ordinal));
        saved.SourceEventId.Should().Be(source.EventId);
    }

    [Fact]
    public async Task ReleaseOutboxDraft_RetainsOriginalCurrencyAndContainsNoNewCash()
    {
        var contract = Contract(Guid.NewGuid(), PrepaidInterestApplication.FutureCashInterest);
        using var payload = PrepaidAccrualPayload(Date.AddDays(1));
        var source = Event(8, "loan.daily-accrual-posted", payload.RootElement, Date.AddDays(1));
        // A delayed outbox delivery must retain the accrual denomination even if a later
        // amendment, after the held balances were cleared, changed the current currency.
        var updated = contract with { CurrentTerms = contract.CurrentTerms with { BaseCurrency = CurrencyCode.EUR } };

        var draft = await DraftJournalAsync(updated, source);

        draft.Lines.Should().OnlyContain(line => line.Currency == CurrencyCode.USD);
        draft.Lines.Should().NotContain(line => line.AccountCode == "Cash");
        draft.Lines.Sum(line => line.DebitAmount).Should().Be(draft.Lines.Sum(line => line.CreditAmount));
        draft.Lines.Should().ContainSingle(line => line.AccountCode == "PrepaidInterest" && line.DebitAmount == 4m);
        draft.Lines.Where(line => line.AccountCode == "AccruedInterestReceivable").Sum(line => line.DebitAmount - line.CreditAmount).Should().Be(0m);
    }

    [Fact]
    public async Task OptedInPrepaidAccrual_ReducesLiabilityAndReceivableWithoutNewCashOrDoubleIncome()
    {
        var contract = Contract(Guid.NewGuid(), PrepaidInterestApplication.FutureCashInterest);
        var accrualDate = Date.AddDays(1);
        var entry = new DailyAccrualEntryDto(Guid.NewGuid(), accrualDate, 4m, 2m, 0m, 0.08m, RecordedAt,
            PrepaidInterestApplied: 4m,
            PrepaidInterestReleases: [new PrepaidInterestReleaseDto(ReceiptId, 4m)]);
        var service = new AccrualLedgerService(Projector());

        var writes = await service.AccrueAsync(contract.LoanId, contract, new PostDailyAccrualRequest(accrualDate),
            entry, Guid.NewGuid(), new DirectLendingEventWriteMetadata(null, null, null, "test", false));

        var journal = writes.Should().ContainSingle().Subject.Entry;
        journal.IsBalanced.Should().BeTrue();
        journal.Lines.Should().NotContain(line => line.Account.Name == "Cash");
        journal.Lines.Where(line => line.Account.Name == "InterestIncome").Sum(line => line.Credit - line.Debit).Should().Be(4m);
        journal.Lines.Where(line => line.Account.Name == "AccruedInterestReceivable").Sum(line => line.Debit - line.Credit).Should().Be(0m);
        journal.Lines.Should().ContainSingle(line => line.Account.Name == "PrepaidInterest" && line.Debit == 4m);
        journal.Lines.Should().ContainSingle(line => line.Account.Name == "CommitmentFeeReceivable" && line.Debit == 2m,
            "prepaid interest must not settle fees");
    }

    [Theory]
    [InlineData("missing-version")]
    [InlineData("unknown-version")]
    [InlineData("exceeds-earned")]
    [InlineData("missing-releases")]
    [InlineData("duplicate-receipt")]
    [InlineData("empty-receipt")]
    [InlineData("negative-release")]
    [InlineData("missing-currency")]
    [InlineData("precision")]
    [InlineData("pik-only")]
    public void PrepaidAccrual_RejectsUnsupportedReleaseEvidence(string defect)
    {
        using var original = PrepaidAccrualPayload(Date.AddDays(1));
        var node = JsonNode.Parse(original.RootElement.GetRawText())!;
        switch (defect)
        {
            case "missing-version": node.AsObject().Remove("cashAccountingVersion"); break;
            case "unknown-version": node["cashAccountingVersion"] = "cash-v99"; break;
            case "exceeds-earned": node["interestAmount"] = 3m; break;
            case "missing-releases": node.AsObject().Remove("prepaidInterestReleases"); break;
            case "duplicate-receipt":
                node["prepaidInterestReleases"]!.AsArray().Add(node["prepaidInterestReleases"]![0]!.DeepClone());
                node["prepaidInterestApplied"] = 8m;
                node["interestAmount"] = 8m;
                break;
            case "empty-receipt": node["prepaidInterestReleases"]![0]!["cashTransactionId"] = Guid.Empty; break;
            case "negative-release": node["prepaidInterestReleases"]![0]!["amount"] = -4m; break;
            case "missing-currency": node.AsObject().Remove("currency"); break;
            case "precision": node["prepaidInterestReleases"]![0]!["amount"] = 4.00000000001m; break;
            case "pik-only": node["interestAmount"] = 0m; node["pikInterestAmount"] = 4m; break;
        }
        using var payload = JsonDocument.Parse(node.ToJsonString());

        Action act = () => DirectLendingCashJournal.ReadAccrualRelease(payload.RootElement,
            payload.RootElement.GetProperty("interestAmount").GetDecimal());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Replay_UsesImmutableReceiptReleaseInstructionAcrossSubsequentTermsAmendment()
    {
        var contract = Contract(Guid.NewGuid(), PrepaidInterestApplication.FutureCashInterest);
        var history = SeedHistory(contract);
        using var receipt = PaymentPayload(contract);
        history.Add(Event(6, "loan.mixed-payment-applied", receipt.RootElement));
        var amended = contract.CurrentTerms with
        {
            ExcessPaymentPolicy = contract.CurrentTerms.ExcessPaymentPolicy! with { Application = PrepaidInterestApplication.HoldForReview }
        };
        history.Add(Event(7, "loan.terms-amended", new { terms = amended, versionNumber = 2, amendmentReason = "Future receipts are held" }));
        using var accrual = PrepaidAccrualPayload(Date.AddDays(1));
        history.Add(Event(8, "loan.daily-accrual-posted", accrual.RootElement, Date.AddDays(1)));

        var state = new DirectLendingEventRebuilder().Rebuild(contract.LoanId, history)!;

        state.Contract.CurrentTerms.ExcessPaymentPolicy!.Application.Should().Be(PrepaidInterestApplication.HoldForReview);
        state.Servicing.Balances.PrepaidInterest.Should().Be(6m);
        state.Servicing.Balances.PrepaidInterestLots.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new PrepaidInterestLotDto(ReceiptId, Date, 10m, 6m, PrepaidInterestApplication.FutureCashInterest, "synthetic-prepaid-interest-election"));
        state.Servicing.Balances.InterestAccruedUnpaid.Should().Be(0m);
        state.Servicing.Balances.CommitmentFeeAccruedUnpaid.Should().Be(2m);
        state.Servicing.AccrualEntries.Should().ContainSingle(item => item.PrepaidInterestApplied == 4m &&
            item.PrepaidInterestReleases!.Count == 1 && item.PrepaidInterestReleases[0].CashTransactionId == ReceiptId);
        state.Should().BeEquivalentTo(new DirectLendingEventRebuilder().Rebuild(contract.LoanId, history));
    }

    [Fact]
    public void Replay_RefusesReleaseFromReceiptWithoutOptIn()
    {
        var contract = Contract(Guid.NewGuid());
        var history = SeedHistory(contract);
        using var receipt = PaymentPayload(contract);
        history.Add(Event(6, "loan.mixed-payment-applied", receipt.RootElement));
        using var accrual = PrepaidAccrualPayload(Date.AddDays(1));
        history.Add(Event(7, "loan.daily-accrual-posted", accrual.RootElement, Date.AddDays(1)));

        Action act = () => new DirectLendingEventRebuilder().Rebuild(contract.LoanId, history);

        act.Should().Throw<InvalidOperationException>().WithMessage("*retained receipt instruction*");
    }

    [Fact]
    public async Task ReleaseBearingAccrualReversal_RequiresGovernedReceiptRestoration()
    {
        var contract = Contract(Guid.NewGuid(), PrepaidInterestApplication.FutureCashInterest);
        var accrual = new DailyAccrualEntryDto(Guid.NewGuid(), Date.AddDays(1), 4m, 0m, 0m, 0.08m, RecordedAt,
            PrepaidInterestApplied: 4m, PrepaidInterestReleases: [new PrepaidInterestReleaseDto(ReceiptId, 4m)]);
        var service = new AccrualLedgerService(Projector());
        var metadata = new DirectLendingEventWriteMetadata(null, null, null, "test", false);

        Func<Task> serviceCall = () => service.ReverseAccrualAsync(contract.LoanId, contract, accrual, Guid.NewGuid(), metadata);
        await serviceCall.Should().ThrowAsync<DirectLendingCommandException>().WithMessage("*governed restoration*");

        using var payload = PrepaidAccrualPayload(Date.AddDays(1));
        Func<Task> projectorCall = () => Projector().ProjectAsync(contract.LoanId, contract, "loan.daily-accrual-reversed",
            Date.AddDays(1), payload, Guid.NewGuid(), metadata, CancellationToken.None);
        await projectorCall.Should().ThrowAsync<DirectLendingCommandException>().WithMessage("*governed restoration*");
    }

    private static readonly Guid ReceiptId = Guid.Parse("c5399570-97f4-4da1-85d0-3342769506b1");

    private static async Task<JournalEntryDto> DraftJournalAsync(LoanContractDetailDto contract, LoanEventLineageDto source)
    {
        var store = Substitute.For<IDirectLendingOperationsStore>();
        var query = Substitute.For<IDirectLendingQueryService>();
        query.GetHistoryAsync(contract.LoanId, Arg.Any<CancellationToken>()).Returns([source]);
        query.GetJournalsAsync(contract.LoanId, Arg.Any<CancellationToken>()).Returns([]);
        query.GetLoanAsync(contract.LoanId, Arg.Any<CancellationToken>()).Returns(contract);
        JournalEntryDto? saved = null;
        store.SaveJournalEntryAsync(Arg.Do<JournalEntryDto>(entry => saved = entry), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<JournalEntryDto>());
        var dispatcher = new DirectLendingOutboxDispatcher(store, Substitute.For<IDirectLendingCommandService>(), query,
            new DirectLendingOptions(), NullLogger<DirectLendingOutboxDispatcher>.Instance);
        var envelope = JsonSerializer.Serialize(new
        {
            loanId = contract.LoanId, sourceEventId = source.EventId, eventType = source.EventType,
            effectiveDate = source.EffectiveDate, servicingRevision = 3, sourceSystem = "test"
        }, Json);
        var message = new DirectLendingOutboxMessage(Guid.NewGuid(), "direct-lending.journal.requested", contract.LoanId.ToString("N"),
            envelope, null, RecordedAt, RecordedAt, null, 0, null);
        var method = typeof(DirectLendingOutboxDispatcher).GetMethod("ProcessAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)method.Invoke(dispatcher, [message, CancellationToken.None])!;
        saved.Should().NotBeNull();
        return saved!;
    }

    private static JsonDocument PrepaidAccrualPayload(DateOnly accrualDate) => JsonSerializer.SerializeToDocument(new
    {
        accrualEntryId = Guid.NewGuid(), accrualDate, interestAmount = 4m, commitmentFeeAmount = 2m,
        penaltyAmount = 0m, annualRateApplied = 0.08m, pikInterestAmount = 0m,
        prepaidInterestApplied = 4m, prepaidInterestReleases = new[] { new PrepaidInterestReleaseDto(ReceiptId, 4m) },
        cashAccountingVersion = DirectLendingCashJournal.CashAccountingVersion, currency = CurrencyCode.USD
    }, Json);

    private static JsonDocument PaymentPayload(LoanContractDetailDto contract, MixedPaymentResolutionDto? resolution = null, decimal amount = 100m)
        => JsonSerializer.SerializeToDocument(new
        {
            loanId = contract.LoanId,
            amount,
            effectiveDate = Date,
            externalRef = "synthetic-cash-1",
            requestedBreakdown = (PaymentBreakdownDto?)null,
            resolution = resolution ?? new MixedPaymentResolutionDto(new PaymentBreakdownDto(10m, 5m, 3m, 2m, 50m),
                "Manual", DirectLendingCashJournal.CashAccountingVersion, 20m, 10m, DirectLendingCashJournal.CashAccountingVersion),
            termsVersion = contract.CurrentTermsVersion,
            termsHash = contract.TermsVersions.Single(version => version.VersionNumber == contract.CurrentTermsVersion).TermsHash,
            currency = contract.CurrentTerms.BaseCurrency,
            cashTransactionId = ReceiptId
        }, Json);

    private static List<LoanEventLineageDto> SeedHistory(LoanContractDetailDto contract) =>
    [
        Event(1, "loan.created", new
        {
            loanId = contract.LoanId, facilityName = contract.FacilityName, borrower = contract.Borrower,
            effectiveDate = Date, terms = contract.CurrentTerms
        }),
        Event(2, "loan.activated", new { activationDate = Date }),
        Event(3, "loan.drawdown-booked", new
        {
            lotId = Guid.Parse("5c99efc7-846b-4f69-bff7-9963c7101348"), amount = 100m,
            tradeDate = Date, settleDate = Date, externalRef = "synthetic-draw"
        }),
        Event(4, "loan.daily-accrual-posted", Accrual(10m, 5m, 2m)),
        Event(5, "loan.fee-assessed", new { amount = 3m, effectiveDate = Date, feeType = "Synthetic", note = "Test" })
    ];

    private static object Accrual(decimal interest, decimal fee, decimal penalty) => new
    {
        accrualEntryId = Guid.NewGuid(), accrualDate = Date, interestAmount = interest,
        commitmentFeeAmount = fee, penaltyAmount = penalty, annualRateApplied = 0.08m, pikInterestAmount = 0m
    };

    private static LoanEventLineageDto Event(long version, string type, object payload, DateOnly? effectiveDate = null) =>
        new(Guid.NewGuid(), version, type, 1, effectiveDate ?? Date, RecordedAt, JsonSerializer.Serialize(payload, Json),
            null, null, null, "test", false);

    private static LoanContractDetailDto Contract(Guid loanId, PrepaidInterestApplication application = PrepaidInterestApplication.HoldForReview)
    {
        var terms = new DirectLendingTermsDto(Date, new DateOnly(2029, 3, 24), 1000m, CurrencyCode.USD,
            RateTypeKind.Fixed, 0.08m, null, null, null, null, DayCountBasis.Act360, PaymentFrequency.Monthly,
            AmortizationType.Bullet, null, null, true, null,
            SecurityMasterReference: new DirectLendingSecurityMasterReferenceDto(SecurityId, "PRD115", "synthetic", "approval", "mapping"),
            ExcessPaymentPolicy: new DirectLendingExcessPaymentPolicyDto(1, ExcessPaymentTreatment.PrepaidInterest, "synthetic-prepaid-interest-election", application));
        return new LoanContractDetailDto(loanId, "PRD-115 Synthetic Loan", new BorrowerInfoDto(Guid.NewGuid(), "Synthetic", null),
            LoanStatus.Active, Date, Date, null, 1, terms,
            [new LoanTermsVersionDto(1, DirectLendingServiceSupport.ComputeTermsHash(terms), terms, "LoanCreated", null, RecordedAt)]);
    }

    private static LoanAccountingProjector Projector(string periodStatus = "Open")
    {
        var store = Substitute.For<ILedgerJournalStore>();
        var period = new LedgerAccountingPeriod(Guid.NewGuid(), null, 2026, 3, "Synthetic March",
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), periodStatus, RecordedAt, null, 1);
        store.ListPeriodsAsync(ct: Arg.Any<CancellationToken>()).Returns([period]);
        var security = Substitute.For<Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService>();
        security.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(new SecurityDetailDto(
            SecurityId, "CustomAsset", SecurityStatusDto.Active, "PRD115", "USD",
            JsonSerializer.SerializeToElement(new { currency = "USD" }), JsonSerializer.SerializeToElement(new { }),
            [], [], 1, RecordedAt, null));
        return new LoanAccountingProjector(store, new AccountingPolicyService(), security);
    }
}
