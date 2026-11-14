using Meridian.Contracts.Ledger;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Ui.Tests.Services;

public sealed partial class IntercompanyAgreementWorkspaceTests
{
    [Fact]
    public async Task StructuredLoanRetainsExactDeclaredRatesAndBusinessCalendar()
    {
        var client = new Client();
        var view = await DraftAsync(client);
        view.UseStructuredTerms = true;
        view.EffectiveFrom = "2026-01-01";
        view.ScheduleStart = "2026-01-01";
        view.ScheduleEnd = "2027-01-01";
        view.CalendarId = "WEEKENDS";
        view.FixedAnnualRate = "0.0523456789";
        view.SettlementBusinessDays = 2;
        await view.SaveCommand.ExecuteAsync(null);
        var terms = Assert.Single(client.Commands).Draft!.Terms!;
        Assert.Null(terms.FixedAnnualRate);
        Assert.Equal(0.0523456789m, terms.StructuredTerms!.FixedAnnualRate);
        Assert.Equal("WEEKENDS", terms.StructuredTerms.Schedule!.CalendarId);
        Assert.Equal(2, terms.StructuredTerms.Schedule.SettlementBusinessDays);
    }

    [Fact]
    public async Task FloatingLoanClearsLegacyFixedRateWithoutInferringFixings()
    {
        var client = new Client();
        var view = await DraftAsync(client);
        view.UseStructuredTerms = true;
        view.ScheduleStart = "2026-01-01";
        view.ScheduleEnd = "2027-01-01";
        view.CalendarId = "WEEKENDS";
        view.RateBasis = IntercompanyRateBasisDto.Floating;
        view.FixedAnnualRate = "0.05";
        view.FloatingIndexReference = "Declared SOFR";
        view.SpreadBps = "123.456789";
        await view.SaveCommand.ExecuteAsync(null);
        var terms = Assert.Single(client.Commands).Draft!.Terms!;
        Assert.Null(terms.FixedAnnualRate);
        Assert.Null(terms.StructuredTerms!.FixedAnnualRate);
        Assert.Equal("Declared SOFR", terms.StructuredTerms.FloatingIndexReference);
        Assert.Equal(123.456789m, terms.StructuredTerms.SpreadBps);
    }

    [Fact]
    public async Task GuaranteeDisclosureDoesNotRequireScheduleOrBecomeLoanTerms()
    {
        var client = new Client();
        var view = await DraftAsync(client);
        view.AgreementType = IntercompanyAgreementTypeDto.Guarantee;
        view.UseStructuredTerms = true;
        view.BeneficiaryReference = "External lender / facility-4";
        view.GuaranteeLimit = "1234567890.123456789";
        await view.SaveCommand.ExecuteAsync(null);
        var structured = Assert.Single(client.Commands).Draft!.Terms!.StructuredTerms!;
        Assert.Null(structured.Schedule);
        Assert.Null(structured.RateBasis);
        Assert.Equal("External lender / facility-4", structured.Guarantee!.BeneficiaryReference);
        Assert.Equal(1234567890.123456789m, structured.Guarantee.LimitAmount);
    }

    [Fact]
    public async Task UnstructuredNotesNeverGainInferredTypedTerms()
    {
        var client = new Client();
        var view = await DraftAsync(client);
        view.Notes = "Five percent monthly, settle next business day";
        await view.SaveCommand.ExecuteAsync(null);
        Assert.Null(Assert.Single(client.Commands).Draft!.Terms!.StructuredTerms);
    }

    [Fact]
    public async Task ExternalAttestationUsesExplicitCapabilityAndExactSubmittedHash()
    {
        var retained = RejectedDetail();
        var submitted = retained.Proposals[0] with { State = IntercompanyAgreementProposalStateDto.Submitted, SubmittedHash = "exact-frozen-subject" };
        retained = retained with { Proposals = [submitted], Capabilities = retained.Capabilities with { CanAttestExternalReview = true } };
        var client = new Client { DetailResponse = ApiResponse<IntercompanyAgreementDetailDto>.Ok(retained) };
        var view = new IntercompanyAgreementWorkspace(client) { SelectedRow = retained };
        await view.OpenCommand.ExecuteAsync(null);
        Assert.False(view.ApproveCommand.CanExecute(null));
        view.UseExternalReview = true;
        view.ExternalReviewer = "External counsel";
        view.ReviewerReference = "Legal file-42";
        view.ReviewedOn = "2026-11-12";
        view.ReviewEvidenceReference = "evidence://evidence-vault/review";
        view.ReviewEvidenceArtifact = "retained-document";
        view.Rationale = "External review completed against frozen terms";
        Assert.True(view.ApproveCommand.CanExecute(null));
        await view.ApproveCommand.ExecuteAsync(null);
        var command = Assert.Single(client.Commands);
        Assert.Equal("exact-frozen-subject", command.ExternalReview!.SubmittedHash);
        Assert.Equal("External counsel", command.ExternalReview.ExternalReviewer);
        Assert.Equal("retained-document", command.ExternalReview.Evidence.ArtifactId);
        view.UseExternalReview = false;
        Assert.False(view.ApproveCommand.CanExecute(null));
    }

    [Fact]
    public async Task SessionChangeClearsExternalReviewAndTypedFinancialInputs()
    {
        var view = await DraftAsync(new Client());
        view.UseExternalReview = true;
        view.ExternalReviewer = "Prior-account counsel";
        view.ReviewEvidenceReference = "private-review";
        view.UseStructuredTerms = true;
        view.FixedFeeAmount = "12500.25";
        view.ResetSession();
        Assert.False(view.UseExternalReview);
        Assert.Empty(view.ExternalReviewer);
        Assert.Empty(view.ReviewEvidenceReference);
        Assert.False(view.UseStructuredTerms);
        Assert.Empty(view.FixedFeeAmount);
    }

    [Fact]
    public async Task MissingMonitorAuthorityIsUnavailableAndNeverInventsCoverage()
    {
        var view = new IntercompanyAgreementWorkspace(new Client()) { LedgerBook = Guid.NewGuid().ToString("D") };
        await view.EvaluateObligationsCommand.ExecuteAsync(null);
        Assert.Contains("Unavailable", view.ObligationStatus);
        Assert.Null(view.ObligationMonitor);
        Assert.Empty(view.ObligationFindings);
        Assert.Empty(view.ObligationBalances);
    }
    [Fact]
    public async Task OperatorCoverageReviewPreservesSourcePinsAndRetryIdentity()
    {
        var client = new Client();
        var book = Guid.NewGuid();
        var source = new IntercompanyLegacyCoverageCandidate(book, Guid.NewGuid(), Guid.NewGuid(), "source-pin", "Due from", "Asset", 100m, 0m, new(2026, 11, 12));
        var draft = RejectedDetail().Proposals[0].Draft;
        var legal = new IntercompanyAgreementRevisionDto(Guid.NewGuid(), 2, 1, 1, IntercompanyAgreementActionDto.Amend, draft, Guid.NewGuid(), "legal-pin", ["author"], "submitter", "approver", DateTimeOffset.UtcNow, "Reviewed");
        var profile = new IntercompanyAccountingArrangementDto(Guid.NewGuid(), Guid.NewGuid(), "Treasury profile", IntercompanyAccountingTemplateKindDto.Loan,
            legal.AgreementId, 2, legal.PayloadHash, DateTimeOffset.UtcNow.AddYears(-1), null, Guid.NewGuid(), 1, 1, IntercompanyAccountingArrangementStatusDto.Approved,
            [new(draft.PartyOneId, book, "ar", "ap")], "author", DateTimeOffset.UtcNow, "author", DateTimeOffset.UtcNow);
        var binding = new IntercompanyAccountBindingDto(Guid.NewGuid(), Guid.NewGuid(), 1, book, "ar", IntercompanyAccountRoleDto.Receivable, "Assets:Due from", "Due from", "Asset", new(2026, 1, 1), null, [], "tenant", "company", "operator", DateTimeOffset.UtcNow, "Reviewed mapping");
        var view = new IntercompanyAgreementWorkspace(client)
        {
            LedgerBook = book.ToString("D"),
            SelectedLegacyCandidate = source,
            SelectedLegacyProfile = new(profile, legal),
            SelectedLegacyBinding = binding,
            LegacyReviewMode = LegacyIntercompanyReviewModeDto.OperatorReviewed,
            LegacyReviewRationale = "Reviewed exact posted line",
            LegacyEvidenceReference = "evidence://coverage",
            LegacyTransactionId = "loan-draw-1"
        };
        await view.ReviewLegacyCoverageCommand.ExecuteAsync(null);
        await view.ReviewLegacyCoverageCommand.ExecuteAsync(null);
        Assert.Equal(2, client.LegacyCommands.Count);
        var first = client.LegacyCommands[0].Attribution;
        var retry = client.LegacyCommands[1].Attribution;
        Assert.Equal(first.AttributionId, retry.AttributionId);
        Assert.Equal("source-pin", first.SourceLineDigest);
        Assert.Equal("legal-pin", first.IntercompanyAgreementPayloadHash);
        Assert.Equal(source.EntryId, first.LineId);
        Assert.Equal(LegacyIntercompanyReviewModeDto.OperatorReviewed, first.ReviewMode);
        Assert.Equal("", first.TenantId);
        Assert.Equal("", first.ApprovedBy);
        Assert.Contains("Authority unavailable", view.StatusText);
    }

    [Fact]
    public async Task CoverageRevocationIsReviewedSuccessorWithOriginalPinsIntact()
    {
        var client = new Client();
        var book = Guid.NewGuid();
        var previous = new LegacyIntercompanyAttributionDto(Guid.NewGuid(), 3, book, Guid.NewGuid(), Guid.NewGuid(), "source-pin", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "ar", "transaction-1", "old-preparer", "old-reviewer", DateTimeOffset.UtcNow, "old-evidence", "tenant", "company")
        { IntercompanyAgreementId = Guid.NewGuid(), IntercompanyAgreementVersion = 2, IntercompanyAgreementPayloadHash = "legal-pin", ReviewMode = LegacyIntercompanyReviewModeDto.OperatorReviewed };
        var view = new IntercompanyAgreementWorkspace(client) { LedgerBook = book.ToString("D"), LegacyReviewRationale = "Incorrect coverage revoked", LegacyEvidenceReference = "evidence://correction" };
        view.LegacyCoverageHistory.Add(previous);
        view.SelectedLegacyAttribution = previous;
        await view.RevokeLegacyCoverageCommand.ExecuteAsync(null);
        var command = Assert.Single(client.LegacyCommands);
        Assert.True(command.Attribution.Revoked);
        Assert.Equal(4, command.Attribution.Revision);
        Assert.Equal(previous.AttributionId, command.ExpectedPreviousAttributionId);
        Assert.Equal(previous.AttributionId, command.Attribution.PreviousAttributionId);
        Assert.Equal(previous.SourceLineDigest, command.Attribution.SourceLineDigest);
        Assert.Equal(previous.IntercompanyAgreementPayloadHash, command.Attribution.IntercompanyAgreementPayloadHash);
        Assert.Single(view.LegacyCoverageHistory);
    }
    [Fact]
    public async Task CoverageMappingsResolveAtTheSourceLineDateRatherThanTheMonitorCutoff()
    {
        var client = new Client();
        var book = Guid.NewGuid();
        var sourceDate = new DateOnly(2026, 1, 12);
        var view = new IntercompanyAgreementWorkspace(client)
        {
            AsOf = "2026-11-13",
            LedgerBook = book.ToString("D"),
            SelectedLegacyCandidate = new(book, Guid.NewGuid(), Guid.NewGuid(), "source", "Due from", "Asset", 10m, 0m, sourceDate)
        };
        await view.LoadLegacySourceMappingsCommand.ExecuteAsync(null);
        Assert.Equal((book, sourceDate), Assert.Single(client.CoverageProfileReads));
    }
}
