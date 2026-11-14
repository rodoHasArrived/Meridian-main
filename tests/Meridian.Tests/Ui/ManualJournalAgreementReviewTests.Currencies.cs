using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Ui;

public sealed partial class ManualJournalAgreementReviewTests
{
    [Theory]
    [InlineData("legacy")]
    [InlineData("native")]
    [InlineData("foreign")]
    public async Task AgreementReversal_FinalPostingPreservesExactRetainedCurrencyAndIndependentApproval(string currencyEvidence)
    {
        var fixture = CorrectionHarness(profiled: false, "reversal", currencyEvidence);
        var source = Assert.Single(fixture.Sources);
        var sourceBytes = JsonSerializer.Serialize(source);

        var final = await PostCurrencyCorrectionAsync(fixture);

        Assert.Equal(AccountingPostingApprovalStateDto.Approved, final.PostingCommand!.ApprovalState);
        Assert.Equal("reviewer", final.PostingCommand.Actor);
        Assert.Equal(source.Entry.JournalEntryId, final.SourceJournalEntryId);
        Assert.True(IntercompanyAgreementPostingGuard.IsExactSourceReversal(final, source));
        foreach (var line in final.Entry.Lines)
        {
            var original = Assert.Single(source.Entry.Lines.Where(candidate => candidate.Account == line.Account));
            Assert.Equal(original.Credit, line.Debit);
            Assert.Equal(original.Debit, line.Credit);
            Assert.Equal(JsonSerializer.Serialize(PostgresLedgerJournalStore.CanonicalizeLineDimensions(original.Dimensions)),
                JsonSerializer.Serialize(PostgresLedgerJournalStore.CanonicalizeLineDimensions(line.Dimensions)));
            if (original.Currency is not { } currency)
                Assert.Null(line.Currency);
            else
                Assert.Equal(new LedgerEntryCurrency(currency.TransactionCurrency, currency.FunctionalCurrency,
                    currency.TransactionCredit, currency.TransactionDebit, currency.FxRateToFunctional), line.Currency);
        }
        Assert.Equal(sourceBytes, JsonSerializer.Serialize(Assert.Single(fixture.Sources)));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("currency")]
    [InlineData("amount")]
    [InlineData("rate")]
    public async Task AgreementReversal_ChangedOrMissingTransactionEvidenceCannotReachPosting(string change)
    {
        var fixture = CorrectionHarness(profiled: false, "reversal", "foreign");
        var sourceBytes = JsonSerializer.Serialize(Assert.Single(fixture.Sources));
        var draft = fixture.Draft with
        {
            Lines = fixture.Draft.Lines.Select(line => line with
            {
                TransactionCurrency = change switch
                {
                    "missing" => null,
                    "currency" => line.TransactionCurrency! with { Currency = "GBP" },
                    "amount" => line.TransactionCurrency! with { Amount = 95m },
                    "rate" => line.TransactionCurrency! with { Amount = 80m, FxRateToFunctional = 1.5m },
                    _ => throw new ArgumentOutOfRangeException(nameof(change))
                }
            }).ToArray()
        };

        await AssertCurrencyCorrectionBlockedAsync(fixture, draft);

        Assert.Equal(sourceBytes, JsonSerializer.Serialize(Assert.Single(fixture.Sources)));
    }

    [Theory]
    [InlineData("account")]
    [InlineData("dimensions")]
    [InlineData("functional-currency")]
    public async Task AgreementReversal_RequiresOriginalAccountsDimensionsAndFunctionalBook(string change)
    {
        var fixture = CorrectionHarness(profiled: false, "reversal", "foreign");
        var source = Assert.Single(fixture.Sources);
        var original = source.Entry.Lines[0];
        var changed = new LedgerEntry(original.EntryId, original.JournalEntryId, original.Timestamp,
            change == "account" ? new LedgerAccount("Assets:Redirected", LedgerAccountType.Asset) : original.Account,
            original.Debit, original.Credit, original.Description,
            change == "dimensions" ? original.Dimensions! with { CostCenterId = "changed" } : original.Dimensions,
            change == "functional-currency" ? new LedgerEntryCurrency("EUR", "GBP", 0m, 96m, 1.25m) : original.Currency);
        fixture.Sources[0] = source with
        {
            Entry = new(source.Entry.JournalEntryId, source.Entry.Timestamp, source.Entry.Description,
                [changed, source.Entry.Lines[1]], source.Entry.Metadata)
        };

        await AssertCurrencyCorrectionBlockedAsync(fixture, fixture.Draft);
    }

    private static async Task<LedgerJournalEntryWrite> PostCurrencyCorrectionAsync(CorrectionFixture fixture)
    {
        var draft = await fixture.Service.SaveDraftAsync(new(fixture.Draft, "preparer",
            TenantId: fixture.Draft.TenantId, CompanyId: fixture.Draft.CompanyId));
        Assert.DoesNotContain(draft.ValidationIssues,
            issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        var submitted = await fixture.Service.SubmitApprovalAsync(new(draft.JournalEntryId, draft.FundProfileId,
            "preparer", draft.Version, Notes: "Review the exact retained source inverse and original currency evidence.",
            LedgerBookId: draft.LedgerBookId, TenantId: draft.TenantId, CompanyId: draft.CompanyId));
        var approved = (await fixture.Service.ApplyLifecycleActionAsync(Action(submitted, JournalEntryLifecycleActionDto.Approve))).JournalEntry;
        Assert.Empty(fixture.Posted);
        var posted = (await fixture.Service.ApplyLifecycleActionAsync(Action(approved, JournalEntryLifecycleActionDto.Post))).JournalEntry;
        Assert.Equal(ManualJournalEntryStatusDto.Posted, posted.Status);
        return Assert.Single(fixture.Posted);
    }

    private static async Task AssertCurrencyCorrectionBlockedAsync(CorrectionFixture fixture, ManualJournalEntryDraftDto draft)
    {
        var saved = await fixture.Service.SaveDraftAsync(new(draft, "preparer",
            TenantId: draft.TenantId, CompanyId: draft.CompanyId));
        Assert.Contains(saved.ValidationIssues, issue => issue.Code == "manual-je.agreement-authority-invalid"
            && issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SubmitApprovalAsync(new(saved.JournalEntryId,
            saved.FundProfileId, "preparer", saved.Version, Notes: "This changed inverse must remain unpostable.",
            LedgerBookId: saved.LedgerBookId, TenantId: saved.TenantId, CompanyId: saved.CompanyId)));
        Assert.Empty(fixture.Posted);
    }
}
