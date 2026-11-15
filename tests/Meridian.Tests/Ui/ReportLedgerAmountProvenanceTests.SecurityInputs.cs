using System.Collections.Immutable;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed partial class ReportLedgerAmountProvenanceTests
{
    [Theory]
    [MemberData(nameof(ArtifactFormatCases))]
    public async Task CertifiedArtifacts_WithholdWholeBookSecurityInputsButRetainTheirDigest(ReportingOutputFormatDto format)
    {
        using var fixture = new Fixture();
        await fixture.CaptureAsync();
        var receipt = ReportingSecurityInputTestData.Receipt();
        var current = fixture.Current with { Entry = ReportingSecurityInputTestData.WithReceipt(fixture.Current.Entry, receipt) };
        var snapshot = fixture.Snapshot with { Journals = [fixture.Opening, current] };
        var inputs = ReportingSecurityInputTestData.Inputs((current.Entry, receipt));
        var manifest = fixture.BuildManifest(snapshot, format, inputs);

        var production = await new DeterministicReportingCertifiedArtifactProducer().ProduceAsync(manifest);

        production.Artifacts.Should().NotContain(artifact => CarriesString(artifact.Content, inputs.Bindings[0].ReceiptPayloadJson));
        var exported = DeterministicReportingCertifiedArtifactProducer.ParseRetainedManifest(
            production.Artifacts.Single(artifact => artifact.ArtifactId == production.ManifestArtifactId).Content.Span);
        exported.AuthoritativeSource.SecurityInputs!.ContentHashSha256.Should().Be(inputs.ContentHashSha256);
        exported.AuthoritativeSource.SecurityInputs.Bindings.Should().BeEmpty();
        manifest.AuthoritativeSource!.SecurityInputs!.Bindings.Should().ContainSingle("the private server manifest keeps exact inputs");
    }

    [Fact]
    public async Task ReleasedInputs_RemainExactAfterCanonicalTermsChangeAndLegacyRunsNeverBorrowCurrentInputs()
    {
        using var fixture = new Fixture();
        await fixture.CaptureAsync();
        var receipt = ReportingSecurityInputTestData.Receipt();
        var current = fixture.Current with { Entry = ReportingSecurityInputTestData.WithReceipt(fixture.Current.Entry, receipt) };
        var snapshot = fixture.Snapshot with { Journals = [fixture.Opening, current] };
        var inputs = ReportingSecurityInputTestData.Inputs((current.Entry, receipt));
        var manifest = fixture.BuildManifest(snapshot, securityInputs: inputs);
        var updated = ReportingSecurityInputTestData.Receipt(receipt.Security.SecurityId, 5.75m).Security with { Version = 3 };
        var service = SecurityInputReader([manifest], fixture.Access, updated);

        var compared = await service.GetComparisonsAsync(receipt.Security.SecurityId, manifest.RunId);

        compared.CurrentCanonical!.Version.Should().Be(3);
        compared.CurrentRates.Single(static rate => rate.Field == "couponRate").RawValue.Should().Be("5.75");
        var retained = compared.Reports.Should().ContainSingle().Which.Inputs.Should().ContainSingle().Which;
        retained.Security.Version.Should().Be(2);
        retained.Rates.Single(static rate => rate.Field == "couponRate").RawValue.Should().Be("5.25");
        retained.Rates.Single(static rate => rate.Field == "couponRate").NormalizedFraction.Should().Be("0.0525");
        retained.Attribution.Should().BeNull("this reader has no supplemental event/revision/workflow authorities");
        retained.ChangeProvenance.Should().Contain("supplemental audit lookup")
            .And.Contain("does not establish what identities or approvals were visible at report time");

        var legacy = fixture.BuildManifest(snapshot);
        var historical = await SecurityInputReader([legacy], fixture.Access, updated)
            .GetComparisonsAsync(receipt.Security.SecurityId, legacy.RunId);
        historical.Reports.Should().ContainSingle().Which.Inputs.Should().BeEmpty();
        historical.Reports[0].Gaps.Should().Contain(reason => reason.Contains("not retained"));
    }

    [Fact]
    public async Task ReleasedInputs_PageBeyondTwoHundredAndRejectUnboundOrForeignScope()
    {
        using var fixture = new Fixture();
        await fixture.CaptureAsync();
        var receipt = ReportingSecurityInputTestData.Receipt();
        var current = fixture.Current with { Entry = ReportingSecurityInputTestData.WithReceipt(fixture.Current.Entry, receipt) };
        var snapshot = fixture.Snapshot with { Journals = [fixture.Opening, current] };
        var inputs = ReportingSecurityInputTestData.Inputs((current.Entry, receipt));
        var baseline = fixture.BuildManifest(snapshot, securityInputs: inputs);
        var manifests = Enumerable.Range(0, 205).Select(index => Reidentify(baseline, $"run-{index:000}")).ToArray();
        var service = SecurityInputReader(manifests, fixture.Access, receipt.Security);

        var impact = await service.EvaluateAsync(receipt.Security.SecurityId, fixture.Scope.FundProfileId,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new(fixture.Scope.TenantId, fixture.Scope.CompanyId));

        impact.RunIds.Should().HaveCount(205);
        impact.RunIds.Should().Contain("run-204");
        impact.Completeness.Should().Be(SecurityImpactCompleteness.Partial, "concurrent report release is explicitly not fenced");
        var foreign = await service.EvaluateAsync(receipt.Security.SecurityId, null, DateTimeOffset.UtcNow,
            new("foreign-tenant", "foreign-company"));
        foreign.Completeness.Should().Be(SecurityImpactCompleteness.Unavailable);
        foreign.RunIds.Should().BeEmpty();
        var unbound = SecurityInputReader(manifests, null, receipt.Security);
        var read = () => unbound.GetComparisonsAsync(receipt.Security.SecurityId);
        await read.Should().ThrowAsync<UnauthorizedAccessException>();
        var denied = SecurityInputReader(manifests, fixture.Access with { CompanyId = "foreign-company" }, receipt.Security);
        var deniedRead = () => denied.GetRunLinksAsync("run-000");
        await deniedRead.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task NarrowReport_DoesNotExposeOtherInvestorsSecurityInputsFromPrivateBookHistory()
    {
        using var fixture = new Fixture();
        await fixture.CaptureAsync();
        var visibleReceipt = ReportingSecurityInputTestData.Receipt();
        var hiddenReceipt = ReportingSecurityInputTestData.Receipt();
        LedgerJournalEntryRecordForPrivacy(fixture.Current, visibleReceipt, "investor-visible", out var visible);
        LedgerJournalEntryRecordForPrivacy(fixture.Opening, hiddenReceipt, "investor-hidden", out var hidden);
        var request = fixture.Snapshot.Request;
        var scopedRequest = new LedgerReportPackRequest(request.ReportId, request.FundId, request.PeriodId,
            request.PeriodStart, request.PeriodEnd, request.AsOf, request.BaseCurrency, request.GeneratedBy,
            request.GeneratedAtUtc, lineDimensions: request.LineDimensions! with { InvestorId = "investor-visible" });
        var snapshot = fixture.Snapshot with { Request = scopedRequest, Journals = [hidden, visible] };
        snapshot = snapshot with { ReportAmounts = ReportAmountBindingBuilder.Build(snapshot) };
        var inputs = ReportingSecurityInputTestData.Inputs((visible.Entry, visibleReceipt), (hidden.Entry, hiddenReceipt));
        var manifest = fixture.BuildManifest(snapshot, securityInputs: inputs);
        var service = SecurityInputReader([manifest], fixture.Access, hiddenReceipt.Security);

        var links = await service.GetRunLinksAsync(manifest.RunId);
        links.Should().ContainSingle().Which.SecurityId.Should().Be(visibleReceipt.Security.SecurityId);
        var hiddenCompare = await service.GetComparisonsAsync(hiddenReceipt.Security.SecurityId, manifest.RunId);
        hiddenCompare.Reports.Should().ContainSingle().Which.Inputs.Should().BeEmpty();
        JsonSerializer.Serialize(hiddenCompare.Reports).Should().NotContain(hiddenReceipt.ContentSha256)
            .And.NotContain(hidden.Entry.JournalEntryId.ToString("D"));
    }

    private static void LedgerJournalEntryRecordForPrivacy(Meridian.Storage.Ledger.LedgerJournalEntryRecord record,
        SecurityCashFlowCalculationReceipt receipt, string investorId, out Meridian.Storage.Ledger.LedgerJournalEntryRecord result)
    {
        var entry = ReportingSecurityInputTestData.WithReceipt(record.Entry, receipt);
        result = record with
        {
            Entry = new JournalEntry(entry.JournalEntryId, entry.Timestamp, entry.Description,
            entry.Lines.Select(line => new LedgerEntry(line.EntryId, line.JournalEntryId, line.Timestamp, line.Account,
                line.Debit, line.Credit, line.Description, line.Dimensions! with { InvestorId = investorId }, line.Currency)).ToArray(), entry.Metadata)
        };
    }

    private static ReportingOutputManifest Reidentify(ReportingOutputManifest baseline, string id)
    {
        var manifest = baseline with { RunId = id };
        manifest = manifest with { Artifacts = ReportingArtifactDeclaration.Build(manifest).Select(static artifact => artifact.ArtifactId).ToImmutableArray() };
        return manifest with { CertifiedSnapshot = manifest.CertifiedSnapshot! with { SnapshotHash = ReportingCertifiedManifestValidation.ComputeSnapshotHash(manifest) } };
    }

    private static ReleasedReportSecurityInputReadService SecurityInputReader(IReadOnlyList<ReportingOutputManifest> manifests,
        ReportAccessQueryContext? access, SecurityDetailDto current)
    {
        var runs = Substitute.For<IReportingRunStore>();
        runs.SupportsCompleteScopedPaging.Returns(true);
        runs.ListRuns(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>()).Returns(call => manifests
            .Where(manifest => manifest.OperationalScope!.TenantId == call.ArgAt<string>(0) && manifest.OperationalScope.CompanyId == call.ArgAt<string?>(1))
            .Skip(call.ArgAt<int>(2)).Take(call.ArgAt<int>(3)).Select(static manifest => new ReportingRunSnapshot(manifest, [], DateTimeOffset.UtcNow)).ToArray());
        runs.GetManifest(Arg.Any<string>(), Arg.Any<string>()).Returns(call => manifests.SingleOrDefault(manifest =>
            manifest.OperationalScope!.TenantId == call.ArgAt<string>(0) && manifest.RunId == call.ArgAt<string>(1)));
        var transaction = Substitute.For<IReportingGovernanceTransaction>();
        transaction.GetRunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var manifest = manifests.SingleOrDefault(value => value.RunId == call.ArgAt<string>(1));
            if (manifest is null)
                return (GovernedReportingRun?)null;
            var scope = manifest.OperationalScope!;
            var actor = new ReportingAuthorityScope("controller", scope.TenantId, scope.OrganizationId, scope.CompanyId,
                [ReportingGovernancePermission.ReleaseRun], ReportingCommandOrigin.HumanOperator, "synthetic-release");
            return new GovernedReportingRun(manifest.RunId, "series", 1, manifest.TemplateId, "1", scope,
                manifest.ImmutableAccessScope!, manifest.CertifiedSnapshot!, actor, DateTimeOffset.UtcNow, null,
                GovernedReportingExecutionState.Succeeded, GovernedReportingState.Released, 7, null, null,
                new ReportingReleaseReceipt(actor, DateTimeOffset.UtcNow, "manifest", new string('a', 64), [], []), []);
        });
        var repository = Substitute.For<IReportingGovernanceRepository>();
        repository.ExecuteTransactionAsync(Arg.Any<Func<IReportingGovernanceTransaction, CancellationToken, ValueTask<GovernedReportingRun?>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<Func<IReportingGovernanceTransaction, CancellationToken, ValueTask<GovernedReportingRun?>>>(0)(transaction, call.ArgAt<CancellationToken>(1)));
        var securities = Substitute.For<ISecurityMasterQueryService>();
        securities.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(current with
        { AssetSpecificTerms = SecurityTermsEnvelope.StampDeclaredFormat(current.AssetSpecificTerms) });
        return new(runs, repository, securities, () => access);
    }
}
