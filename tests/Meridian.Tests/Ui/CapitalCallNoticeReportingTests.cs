using System.Collections.Immutable;
using System.Globalization;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Strategies.Services;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class CapitalCallNoticeReportingTests
{
    private static readonly PrivateCapitalScopeDto Scope = new("tenant-a", "company-a", "entity-a", "fund-a",
        Guid.Parse("10000000-0000-0000-0000-000000000001"), "USD");
    private static readonly Guid JournalId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly DateOnly NoticeDate = new(2026, 11, 10);

    [Fact]
    public void NoticeOperationIdentity_BindsEveryAuthorityAndTargetField()
    {
        var operationId = Guid.NewGuid();
        var original = CapitalCallNoticeReportingService.BuildOperation(Scope, "call-a", "investor-a", operationId, "operator");
        CapitalCallNoticeReportingService.BuildOperation(Scope, " CALL-A ", " INVESTOR-A ", operationId, "operator")
            .Should().Be(original);
        var alteredScopes = new[]
        {
            Scope with { TenantId = "tenant-b" }, Scope with { CompanyId = "company-b" },
            Scope with { EntityId = "entity-b" }, Scope with { FundProfileId = "fund-b" },
            Scope with { LedgerBookId = Guid.NewGuid() }, Scope with { Currency = "EUR" }
        };
        foreach (var scope in alteredScopes)
            CapitalCallNoticeReportingService.BuildOperation(scope, "call-a", "investor-a", operationId, "operator")
                .RequestHash.Should().NotBe(original.RequestHash);
        CapitalCallNoticeReportingService.BuildOperation(Scope, "call-b", "investor-a", operationId, "operator")
            .RequestHash.Should().NotBe(original.RequestHash);
        CapitalCallNoticeReportingService.BuildOperation(Scope, "call-a", "investor-b", operationId, "operator")
            .RequestHash.Should().NotBe(original.RequestHash);
        CapitalCallNoticeReportingService.BuildOperation(Scope, "call-a", "investor-a", operationId, "another-operator")
            .RequestHash.Should().NotBe(original.RequestHash);
        CapitalCallNoticeReportingService.BuildOperation(Scope, "call-a", "investor-a", Guid.NewGuid(), "operator")
            .RunId.Should().NotBe(original.RunId);
        var empty = () => CapitalCallNoticeReportingService.BuildOperation(Scope, "call-a", "investor-a", Guid.Empty, "operator");
        empty.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task ResolveAsync_UsesExactApprovedRevisionAndExplicitRecipientMapping()
    {
        var (validator, store) = Setup();
        var result = await validator.ResolveAsync(Scope, "call-a", "investor-a");

        result.Recipient.Should().Be("reporting-user-17");
        result.Line.IssuanceJournalEntryId.Should().Be(JournalId);
        await store.Received(1).GetCommitmentAsync(Scope, "commitment-a", 2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void NoticeOperationIdentity_CommitmentSelectionIsBoundWithoutChangingLegacyOmittedHash()
    {
        var intent = Guid.NewGuid();
        var legacy = CapitalCallNoticeReportingService.BuildOperation(Scope, "call-a", "investor-a", intent, "operator");
        // Frozen hash of the pre-selector request schema for this fixed scope and target.
        legacy.RequestHash.Should().Be("c08b3bb9c2fca7cc31ce6b10e657583a5dbfc63e3e88b147b54d8107d4b09760");
        CapitalCallNoticeReportingService.BuildOperation(Scope, "call-a", "investor-a", intent, "operator", null)
            .Should().Be(legacy);
        var first = CapitalCallNoticeReportingService.BuildOperation(Scope, "call-a", "investor-a", intent, "operator", "commitment-a");
        var second = CapitalCallNoticeReportingService.BuildOperation(Scope, "call-a", "investor-a", intent, "operator", "commitment-b");
        first.RunId.Should().Be(second.RunId).And.Be(legacy.RunId);
        first.RequestHash.Should().NotBe(second.RequestHash).And.NotBe(legacy.RequestHash);
        CapitalCallNoticeReportingService.BuildOperation(Scope, "call-a", "investor-a", intent, "operator", " COMMITMENT-A ")
            .Should().Be(first);
    }

    [Fact]
    public void CanonicalParameters_PreviouslyCertifiedTwoKeyFilter_PreservesFrozenBytesAndDigest()
    {
        var parameters = Parameters();
        ReportingCanonicalParameterSerializer.Serialize(parameters).Should().Be(LegacyParametersJson);
        parameters = parameters with
        {
            Scope = parameters.Scope with
            {
                Dimensions = parameters.Scope.Dimensions! with
                {
                    ExternalGlDimensions = new SortedDictionary<string, string>(
                        parameters.Scope.Dimensions!.ExternalGlDimensions.ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.Ordinal)
                }
            }
        };

        ReportingCanonicalParameterSerializer.Serialize(parameters).Should().Be(LegacyParametersJson);
        ReportingCanonicalParameterSerializer.ComputeHash(parameters).Should().Be(LegacyParametersHash);
    }

    [Fact]
    public void RetainedParameters_ExactLegacyBytesRemainReadable_AlteredFieldsAndFlagsAreRefused()
    {
        var parameters = Parameters();
        ReportingCanonicalParameterSerializer.MatchesRetained(LegacyRawParametersJson, parameters).Should().BeTrue();
        ReportingCanonicalParameterSerializer.MatchesRetained(LegacyParametersJson, parameters).Should().BeTrue();
        Meridian.Contracts.Integrity.Sha256Digest.ComputeUtf8(LegacyRawParametersJson).Should().Be(LegacyRawParametersHash);
        // Creation continues to emit the canonical representation, leaving retained raw bytes alone.
        ReportingCanonicalParameterSerializer.Serialize(parameters).Should().Be(LegacyParametersJson).And.NotBe(LegacyRawParametersJson);
        const string rawMap = "\"capitalCallId\":\"call-a\",\"capitalActivityKind\":\"CapitalCallIssuance\"";
        var altered = new[]
        {
            LegacyRawParametersJson.Replace(rawMap, rawMap.Replace("capitalCallId", "unknownCallId"), StringComparison.Ordinal),
            LegacyRawParametersJson.Replace(rawMap, rawMap.Replace("call-a", "call-other"), StringComparison.Ordinal),
            LegacyRawParametersJson[..^1] + ",\"extra\":true}",
            LegacyRawParametersJson.Replace("\"includeEvidenceAppendix\":true", "\"includeEvidenceAppendix\":false", StringComparison.Ordinal),
            LegacyRawParametersJson + " "
        };
        foreach (var frozen in altered)
        {
            frozen.Should().NotBe(LegacyRawParametersJson);
            ReportingCanonicalParameterSerializer.MatchesRetained(frozen, parameters).Should().BeFalse();
        }
        ReportingCanonicalParameterSerializer.MatchesRetained(LegacyRawParametersJson, parameters,
            requiresCertifiedLedgerPresentation: true).Should().BeFalse();
        ReportingCanonicalParameterSerializer.MatchesRetained(null, parameters).Should().BeFalse();
        ReportingCanonicalParameterSerializer.MatchesRetained(LegacyRawParametersJson,
            parameters with { IncludeEvidenceAppendix = false }).Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void CanonicalParameters_SelectedCommitmentFilter_IgnoresMapInsertionOrderWithoutMutatingInput(int permutation)
    {
        var orders = new[]
        {
            new[] { "capitalActivityKind", "capitalCallId", "commitmentId" },
            new[] { "capitalActivityKind", "commitmentId", "capitalCallId" },
            new[] { "capitalCallId", "capitalActivityKind", "commitmentId" },
            new[] { "capitalCallId", "commitmentId", "capitalActivityKind" },
            new[] { "commitmentId", "capitalActivityKind", "capitalCallId" },
            new[] { "commitmentId", "capitalCallId", "capitalActivityKind" }
        };
        var values = new Dictionary<string, string>
        {
            ["capitalActivityKind"] = "CapitalCallIssuance",
            ["capitalCallId"] = "call-a",
            ["commitmentId"] = "commitment-a"
        };
        var map = orders[permutation].ToDictionary(key => key, key => values[key], StringComparer.Ordinal);
        var parameters = Parameters();
        parameters = parameters with
        { Scope = parameters.Scope with { Dimensions = parameters.Scope.Dimensions! with { ExternalGlDimensions = map } } };
        var expected = LegacyParametersJson.Replace("\"capitalCallId\":\"call-a\"}",
            "\"capitalCallId\":\"call-a\",\"commitmentId\":\"commitment-a\"}", StringComparison.Ordinal);

        ReportingCanonicalParameterSerializer.Serialize(parameters).Should().Be(expected);
        ReportingCanonicalParameterSerializer.ComputeHash(parameters).Should().Be(SelectedParametersHash);
        map.Keys.Should().Equal(orders[permutation]);
        parameters.Scope.Dimensions.ExternalGlDimensions.Should().BeSameAs(map);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CanonicalParameters_NullAndEmptyDimensions_PreserveFrozenDigests(bool empty)
    {
        var parameters = Parameters();
        parameters = parameters with { Scope = parameters.Scope with { Dimensions = empty ? new LedgerDimensionSetDto() : null } };

        ReportingCanonicalParameterSerializer.ComputeHash(parameters).Should().Be(empty ? EmptyDimensionsHash : NullDimensionsHash);
        ReportingCanonicalParameterSerializer.Serialize(parameters).Should().Contain(empty ? "\"ExternalGlDimensions\":{}" : "\"dimensions\":null");
    }

    [Fact]
    public void CanonicalParameters_EmptyExternalMap_PreservesFrozenDigestAndDimensionFields()
    {
        var parameters = Parameters();
        parameters = parameters with
        {
            Scope = parameters.Scope with
            { Dimensions = parameters.Scope.Dimensions! with { ExternalGlDimensions = new Dictionary<string, string>() } }
        };

        ReportingCanonicalParameterSerializer.ComputeHash(parameters).Should().Be(EmptyExternalMapHash);
        ReportingCanonicalParameterSerializer.Serialize(parameters).Should().Contain("\"EntityId\":\"entity-a\"");
    }

    [Fact]
    public async Task ResolveAsync_SameInvestorWithMultipleCommitments_RequiresAndHonorsExactSelection()
    {
        var fixture = MultipleCommitments();
        var ambiguous = () => fixture.Validator.ResolveAsync(fixture.Scope, fixture.Call.CallId, "investor-a");
        await ambiguous.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*select a commitment*");

        foreach (var line in fixture.Call.Lines)
        {
            var resolved = await fixture.Validator.ResolveAsync(fixture.Scope, fixture.Call.CallId,
                "investor-a", line.CommitmentId);
            resolved.Line.Should().Be(line);
            resolved.Commitment.CommitmentId.Should().Be(line.CommitmentId);
            resolved.Commitment.Revision.Should().Be(line.CommitmentRevision);
        }
        var unknown = () => fixture.Validator.ResolveAsync(fixture.Scope, fixture.Call.CallId, "investor-a", "commitment-other");
        await unknown.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>();
        var foreignInvestor = () => fixture.Validator.ResolveAsync(fixture.Scope, fixture.Call.CallId, "investor-b", "commitment-a");
        await foreignInvestor.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveAsync_SelectedUnpostedOrPartialSiblingCannotUseThePostedCommitment(bool partial)
    {
        var fixture = MultipleCommitments();
        var second = fixture.Call.Lines[1] with
        {
            IssuedAmount = partial ? 10m : 0m,
            IssuanceJournalEntryId = partial ? fixture.Call.Lines[1].IssuanceJournalEntryId : null
        };
        fixture.Store.GetCallAsync(fixture.Scope, fixture.Call.CallId, Arg.Any<CancellationToken>())
            .Returns(fixture.Call with { Lines = [fixture.Call.Lines[0], second] });

        var blocked = () => fixture.Validator.ResolveAsync(fixture.Scope, fixture.Call.CallId, "investor-a", second.CommitmentId);
        await blocked.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*no complete posted issuance*");
        (await fixture.Validator.ResolveAsync(fixture.Scope, fixture.Call.CallId, "investor-a", "commitment-a"))
            .Line.Should().Be(fixture.Call.Lines[0]);
    }

    [Fact]
    public async Task CaptureAsync_SameInvestorAndCapitalAccount_IsolatesEachSelectedJournalAndAmount()
    {
        var fixture = MultipleCommitments();
        var captures = new List<ReportingAuthoritativeSourceCapture>();
        foreach (var line in fixture.Call.Lines)
        {
            var parameters = SelectedParameters(fixture, line);
            await fixture.Validator.ValidateAsync(parameters, Access());
            var capture = await fixture.Source.CaptureAsync(parameters, Access(),
                new ReportingAuthoritativeSourceCaptureIntent(CapitalCallNoticeReportingService.TemplateId));
            capture.DatasetRows.Should().HaveCount(2);
            capture.DatasetRows.Should().OnlyContain(row => row["journalEntryId"] == line.IssuanceJournalEntryId!.Value.ToString("D")
                && row["externalGl.commitmentId"] == line.CommitmentId
                && row["externalGl.commitmentRevision"] == line.CommitmentRevision.ToString(CultureInfo.InvariantCulture)
                && row["investorId"] == "investor-a" && row["capitalAccountId"] == "capital-a");
            capture.DatasetRows.Sum(row => decimal.Parse(row["debit"], CultureInfo.InvariantCulture)).Should().Be(line.Amount);
            capture.DatasetRows.Sum(row => decimal.Parse(row["credit"], CultureInfo.InvariantCulture)).Should().Be(line.Amount);
            capture.Checkpoint.JournalEntryCount.Should().Be(1);
            parameters.Scope.Dimensions!.ExternalGlDimensions.Should().ContainKey("commitmentId").WhoseValue.Should().Be(line.CommitmentId);
            captures.Add(capture);
        }
        var first = SelectedParameters(fixture, fixture.Call.Lines[0]);
        var siblingRows = () => CapitalCallNoticeSourceValidator.ValidateRows(first, captures[1].DatasetRows);
        siblingRows.Should().Throw<ReportingAuthoritativeSourceUnavailableException>();
        var combinedRows = () => CapitalCallNoticeSourceValidator.ValidateRows(first,
            captures.SelectMany(capture => capture.DatasetRows).ToImmutableArray());
        combinedRows.Should().Throw<ReportingAuthoritativeSourceUnavailableException>();
        var wrongDimensions = first.Scope.Dimensions!.ExternalGlDimensions.ToDictionary(pair => pair.Key, pair => pair.Value);
        wrongDimensions["commitmentId"] = fixture.Call.Lines[1].CommitmentId;
        var forged = () => fixture.Validator.ValidateAsync(first with
        {
            Scope = first.Scope with { Dimensions = first.Scope.Dimensions with { ExternalGlDimensions = wrongDimensions } }
        }, Access());
        await forged.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>();
        var omittedFilter = () => fixture.Validator.ValidateAsync(SelectedParameters(fixture, fixture.Call.Lines[0], legacyFilter: true), Access());
        await omittedFilter.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>();
        var operation = CapitalCallNoticeReportingService.BuildOperation(fixture.Scope, fixture.Call.CallId,
            "investor-a", Guid.NewGuid(), "operator", fixture.Call.Lines[0].CommitmentId);
        var certified = await CertifiedSelectionAsync(fixture, fixture.Call.Lines[0], operation);
        CapitalCallNoticeReportingService.MatchesOperationalReceipt(first, certified.Receipt).Should().BeTrue();
        CapitalCallNoticeReportingService.MatchesOperationalReceipt(
            SelectedParameters(fixture, fixture.Call.Lines[1]), certified.Receipt).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveAsync_UnpostedOrPartiallyIssuedInstallmentIsBlocked(bool partial)
    {
        var line = Line() with { IssuanceJournalEntryId = partial ? JournalId : null, IssuedAmount = partial ? 10m : 0m };
        var (validator, _) = Setup(line);

        var action = () => validator.ResolveAsync(Scope, "call-a", "investor-a");

        await action.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*no complete posted issuance*");
    }

    [Fact]
    public async Task ResolveAsync_AnotherInvestorCannotReadTheInstallment()
    {
        var (validator, _) = Setup();
        var action = () => validator.ResolveAsync(Scope, "call-a", "investor-b");
        await action.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*one exact retained commitment installment*");
    }

    [Fact]
    public async Task ResolveAsync_MissingRecipientMappingDoesNotInferUserIdentity()
    {
        var (_, store) = Setup();
        var validator = new CapitalCallNoticeSourceValidator(store);
        var action = () => validator.ResolveAsync(Scope, "call-a", "investor-a");
        await action.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*explicit scoped notice recipient binding*");
    }

    [Fact]
    public async Task ValidateAsync_RejectsForgedCallAmountAndCrossTenantContext()
    {
        var (validator, _) = Setup();
        var parameters = Parameters();
        await validator.ValidateAsync(parameters, Access());
        var changed = parameters.TemplateParameters.ToDictionary(pair => pair.Key, pair => pair.Value);
        changed["callAmount"] = "1";
        var forged = () => validator.ValidateAsync(parameters with { TemplateParameters = changed }, Access());
        await forged.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>();
        var foreign = () => validator.ValidateAsync(parameters, Access() with { TenantId = "tenant-b" });
        await foreign.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>();
    }

    [Theory]
    [InlineData("investorId", "investor-b")]
    [InlineData("journalEntryId", "30000000-0000-0000-0000-000000000001")]
    [InlineData("externalGl.capitalActivityKind", "CapitalCallFunding")]
    [InlineData("externalGl.commitmentRevision", "3")]
    public void ValidateRows_RejectsForeignInvestorJournalAndFundingEvidence(string key, string value)
    {
        var rows = Rows();
        var altered = rows[0].ToDictionary(pair => pair.Key, pair => pair.Value);
        altered[key] = value;
        var action = () => CapitalCallNoticeSourceValidator.ValidateRows(Parameters(), [altered, rows[1]]);
        action.Should().Throw<ReportingAuthoritativeSourceUnavailableException>();
    }

    [Fact]
    public void ValidateRows_ExactRowsConserveCallAmountAndEmptySourceIsBlocked()
    {
        CapitalCallNoticeSourceValidator.ValidateRows(Parameters(), Rows());
        var missing = () => CapitalCallNoticeSourceValidator.ValidateRows(Parameters(), []);
        missing.Should().Throw<ReportingAuthoritativeSourceUnavailableException>();
        var changed = Rows()[0].ToDictionary(pair => pair.Key, pair => pair.Value);
        changed["debit"] = "99";
        var incorrectAmount = () => CapitalCallNoticeSourceValidator.ValidateRows(Parameters(), [changed, Rows()[1]]);
        incorrectAmount.Should().Throw<ReportingAuthoritativeSourceUnavailableException>()
            .WithMessage("*does not reproduce*");
    }

    [Fact]
    public void PrimaryDocument_ContainsOneInvestorAndRejectsBroadenedAudience()
    {
        var manifest = Manifest();
        var model = DocumentsReportingPrimaryDocumentRenderer.BuildModel(manifest);
        model.Title.Should().Be("Capital Call Notice");
        model.HeaderFields.Should().Contain(field => field.Label == "Investor" && field.Value == "investor-a");
        var broadened = manifest with
        {
            ImmutableAccessScope = manifest.ImmutableAccessScope! with { Mode = ReportingGovernanceAccessMode.CompanyWide }
        };
        var action = () => DocumentsReportingPrimaryDocumentRenderer.BuildModel(broadened);
        action.Should().Throw<ReportingGovernanceException>().WithMessage("*restricted recipient audience*");
    }

    [Fact]
    public async Task PostedOperationalNoticeCanReleaseBeforeClose_OrdinaryDraftReportsRemainBlocked()
    {
        var parameters = Parameters();
        var capturedAt = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var hash = new string('b', 64);
        var checkpoint = new ReportingAuthoritativeSourceCheckpoint("durable-ledger-journal", "ledger:notice",
            Scope.TenantId, "50000000-0000-0000-0000-000000000001", Scope.CompanyId, Scope.FundProfileId,
            Scope.LedgerBookId.ToString("D"), parameters.PeriodId, "Primary", NoticeDate,
            new DateTimeOffset(NoticeDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc), TimeSpan.Zero),
            1, 1, 2, "notice-checkpoint", hash, capturedAt,
            [$"reporting-source-checkpoint:notice-checkpoint:{hash}"]);
        var capture = new ReportingAuthoritativeSourceCapture(checkpoint, Rows());
        var source = Substitute.For<IReportingAuthoritativeSource>();
        source.CaptureAsync(Arg.Any<ReportingRunParametersDto>(), Arg.Any<ReportAccessQueryContext>(),
            Arg.Any<ReportingAuthoritativeSourceCaptureIntent>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(capture));
        var receipt = ReportingReconciliationEvidenceValidation.CreateReceipt(checkpoint,
            new ReportingReconciliationCompletionEvidence("posted-issuance-reconciled", new string('c', 64),
                capturedAt, false, ["posted-capital-call:" + JournalId]));
        receipt.CloseWorkflowCompletion.Should().BeNull("an issuance notice is not accounting-close evidence");
        var reconciliation = Substitute.For<IReportingReconciliationEvidenceSource>();
        reconciliation.ResolveAsync(Arg.Any<ReportingRunParametersDto>(), Arg.Any<ReportingAuthoritativeSourceCheckpoint>(),
            Arg.Any<ReportAccessQueryContext>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(receipt));
        var certification = new ReportingRunCertificationService(source, reconciliation);
        var template = new DefaultReportingTemplateCatalog().Get(CapitalCallNoticeReportingService.TemplateId);
        var readiness = new ReportingRunReadinessDto("evaluation", capturedAt,
            new VersionedReportTemplateIdDto(template.TemplateId, 1), parameters,
            ReportingRunReadinessStatusDto.Ready, true, false,
            [new ReportingRunReadinessCheckDto("posted-issuance", "Posted issuance", ReportingRunReadinessStatusDto.Ready,
                "The exact retained installment was posted.", 0, false, false, EvidenceReferences: ["posted:" + JournalId])],
            [], new string('d', 64));
        var certified = await certification.CertifyAsync(template, readiness, Access());
        var run = new GovernedReportingRun("notice-run", "notice-series", 1, template.TemplateId, "1.0.0",
            certified.OperationalScope, certified.AccessScope, certified.Snapshot, null!, capturedAt, null,
            GovernedReportingExecutionState.Succeeded, GovernedReportingState.Approved, 1, null, null, null, []);

        certified.AccessScope.Mode.Should().Be(ReportingGovernanceAccessMode.Restricted);
        certified.AccessScope.Principals.Should().ContainSingle(principal => principal.PrincipalId == "reporting-user-17");
        certified.AccessScope.Principals.Should().ContainSingle(principal => principal.Kind == ReportingAccessPrincipalKind.Group
            && principal.PrincipalId == "capital-reviewers");
        ReportingGovernanceCanonicalValidation.ValidateReleaseSnapshot(run);
        await certification.RevalidateForReleaseAsync(certified.Readiness.ResolvedParameters, template.TemplateId,
            checkpoint, certified.Snapshot, Access());

        var ordinaryRun = run with { TemplateId = "investor-monthly-statement" };
        var ordinaryDraft = () => ReportingGovernanceCanonicalValidation.ValidateReleaseSnapshot(ordinaryRun);
        ordinaryDraft.Should().Throw<ReportingGovernanceException>().WithMessage("*Draft-certified bytes cannot be released*");
        var ordinaryRevalidation = async () => await certification.RevalidateForReleaseAsync(
            certified.Readiness.ResolvedParameters, ordinaryRun.TemplateId, checkpoint, certified.Snapshot, Access());
        await ordinaryRevalidation.Should().ThrowAsync<ReportingGovernanceException>().WithMessage("*committed accounting-close*");
    }

    [Fact]
    public async Task NoticeReconciliationRetainsStablePostedIssuanceProofWithoutClaimingFinancialClose()
    {
        var (validator, _) = Setup();
        var parameters = Parameters();
        var capturedAt = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        var hash = new string('b', 64);
        var checkpoint = new ReportingAuthoritativeSourceCheckpoint("durable-ledger-journal", "ledger:notice",
            Scope.TenantId, "50000000-0000-0000-0000-000000000001", Scope.CompanyId, Scope.FundProfileId,
            Scope.LedgerBookId.ToString("D"), parameters.PeriodId, "Primary", NoticeDate,
            new DateTimeOffset(NoticeDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc), TimeSpan.Zero),
            1, 1, 2, "notice-checkpoint", hash, capturedAt,
            [$"reporting-source-checkpoint:notice-checkpoint:{hash}"]);
        var source = Substitute.For<IReportingAuthoritativeSource>();
        source.CaptureAsync(Arg.Any<ReportingRunParametersDto>(), Arg.Any<ReportAccessQueryContext>(),
            Arg.Any<ReportingAuthoritativeSourceCaptureIntent>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(new ReportingAuthoritativeSourceCapture(checkpoint, Rows())));
        var queue = Substitute.For<IReconciliationBreakQueueRepository>();
        queue.GetAllAsync(Arg.Any<ReconciliationBreakQueueScope>(), Arg.Any<ReconciliationBreakQueueStatus?>(),
            Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<ReconciliationBreakQueueItem>>([]));
        var retained = new List<ReportingReconciliationEvidenceReceipt>();
        var durable = new InMemoryReportingReconciliationEvidenceStore();
        var store = Substitute.For<IReportingReconciliationEvidenceRetentionStore>();
        store.RetainAsync(Arg.Any<ReportingReconciliationEvidenceReceipt>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                retained.Add(call.ArgAt<ReportingReconciliationEvidenceReceipt>(0));
                return durable.RetainAsync(call.ArgAt<ReportingReconciliationEvidenceReceipt>(0));
            });
        var service = new CapitalCallNoticeReportingService(validator, null!, null!,
            authoritativeSource: source, reconciliationRetention: store);

        await service.ReconcilePostedIssuanceAsync(parameters, Access());
        await service.ReconcilePostedIssuanceAsync(parameters, Access());

        retained.Should().HaveCount(2);
        retained[0].CloseWorkflowCompletion.Should().BeNull();
        retained[0].EvidenceIds.Should().Contain("posted-capital-call:" + JournalId);
        retained[1].ReconciliationCheckpointHash.Should().Be(retained[0].ReconciliationCheckpointHash,
            "retrying one immutable issuance must not fabricate a newer reconciliation outcome");
        var closedClaim = () => ReportingReconciliationEvidenceValidation.RequireCommittedCloseWorkflow(retained[0]);
        closedClaim.Should().Throw<ArgumentException>();

        var reconciliation = new ReportingReconciliationEvidenceSource(durable, queue);
        var first = await reconciliation.ResolveAsync(parameters, checkpoint, Access());
        queue.GetAllAsync(Arg.Any<ReconciliationBreakQueueScope>(), Arg.Any<ReconciliationBreakQueueStatus?>(),
            Arg.Any<CancellationToken>()).Returns(Task.FromException<IReadOnlyList<ReconciliationBreakQueueItem>>(
                new InvalidOperationException("Unrelated close queue changed.")));
        await service.ReconcilePostedIssuanceAsync(parameters, Access());
        var template = new DefaultReportingTemplateCatalog().Get(CapitalCallNoticeReportingService.TemplateId);
        var readiness = new ReportingRunReadinessDto("operational-evaluation", capturedAt,
            new VersionedReportTemplateIdDto(template.TemplateId, 1), parameters,
            ReportingRunReadinessStatusDto.Ready, true, false,
            [new ReportingRunReadinessCheckDto("posted-issuance", "Posted issuance", ReportingRunReadinessStatusDto.Ready,
                "Retained installment agrees to its journal.", 0, false, false, EvidenceReferences: ["posted:" + JournalId])],
            [], new string('d', 64));
        var certification = new ReportingRunCertificationService(source, reconciliation);
        var certified = await certification.CertifyAsync(template, readiness, Access());
        await certification.RevalidateForReleaseAsync(parameters, template.TemplateId,
            checkpoint, certified.Snapshot, Access());
        retained[2].ReconciliationCheckpointHash.Should().Be(first.ReconciliationCheckpointHash);
        await queue.DidNotReceive().GetAllAsync(Arg.Any<ReconciliationBreakQueueScope>(),
            Arg.Any<ReconciliationBreakQueueStatus?>(), Arg.Any<CancellationToken>());
        var foreign = async () => await reconciliation.ResolveAsync(parameters with
        { Scope = parameters.Scope with { InvestorId = "investor-b" } }, checkpoint, Access());
        await foreign.Should().ThrowAsync<ReportingReconciliationEvidenceInvalidException>();
    }

    private static (CapitalCallNoticeSourceValidator, IPrivateCapitalStore) Setup(PrivateCapitalCallLineDto? line = null)
    {
        var store = Substitute.For<IPrivateCapitalStore>();
        store.GetCallAsync(Scope, "call-a", Arg.Any<CancellationToken>()).Returns(Call(line));
        store.GetCommitmentAsync(Scope, "commitment-a", 2, Arg.Any<CancellationToken>()).Returns(Commitment());
        return (new CapitalCallNoticeSourceValidator(store,
            [new CapitalCallNoticeRecipientBinding("tenant-a", "company-a", "fund-a", "investor-a", "reporting-user-17", "capital-reviewers")]), store);
    }

    internal sealed record MultipleCommitmentFixture(PrivateCapitalScopeDto Scope, PrivateCapitalCallDto Call,
        IReadOnlyList<PrivateCapitalCommitmentDto> Commitments, IPrivateCapitalStore Store,
        CapitalCallNoticeSourceValidator Validator, LedgerReportingAuthoritativeSource Source);

    internal static MultipleCommitmentFixture MultipleCommitments(bool singleLegacyCommitment = false)
    {
        var scope = Scope with { EntityId = "60000000-0000-0000-0000-000000000001" };
        var now = new DateTimeOffset(2026, 11, 10, 20, 0, 0, TimeSpan.Zero);
        var first = Line();
        var second = first with
        {
            InstallmentId = "installment-b",
            CommitmentId = "commitment-b",
            CommitmentRevision = 3,
            Amount = 250m,
            IssuedAmount = 250m,
            UncalledBefore = 750m,
            UncalledAfter = 500m,
            IssuanceJournalEntryId = Guid.Parse("20000000-0000-0000-0000-000000000002")
        };
        var call = Call() with
        {
            Scope = scope,
            CallId = singleLegacyCommitment ? "legacy-call" : "call-a",
            Lines = singleLegacyCommitment ? [first] : [first, second],
            AmountToCall = singleLegacyCommitment ? 100m : 350m
        };
        var commitments = new[] { Commitment() with { Scope = scope }, Commitment() with
        { Scope = scope, CommitmentId = "commitment-b", Revision = 3, TotalCommitment = 750m } };
        var store = Substitute.For<IPrivateCapitalStore>();
        store.GetCallAsync(scope, call.CallId, Arg.Any<CancellationToken>()).Returns(call);
        foreach (var commitment in commitments)
            store.GetCommitmentAsync(scope, commitment.CommitmentId, commitment.Revision, Arg.Any<CancellationToken>())
                .Returns(commitment);
        var validator = new CapitalCallNoticeSourceValidator(store,
            [new("tenant-a", "company-a", "fund-a", "investor-a", "reporting-user-17", "capital-reviewers")]);
        var book = new LedgerBookRecord(scope.LedgerBookId, scope.FundProfileId, Guid.Parse(scope.EntityId),
            FundStructureNodeKindDto.Entity, "Capital book", "USD", now, now);
        var period = new LedgerAccountingPeriod(Guid.Parse(call.PeriodId), book.LedgerBookId, 2026, 11, "2026-11",
            new(2026, 11, 1), new(2026, 11, 30), "Open", now.AddDays(-10), null, 1);
        var organizationId = Guid.Parse("50000000-0000-0000-0000-000000000001");
        var journals = Substitute.For<ILedgerJournalStore, ILedgerReportingSnapshotSource>();
        journals.GetLedgerBookAsync(book.LedgerBookId, Arg.Any<CancellationToken>()).Returns(book);
        journals.GetPeriodAsync(period.PeriodId, Arg.Any<CancellationToken>()).Returns(period);
        var records = call.Lines.Select((line, index) =>
        {
            var journalId = line.IssuanceJournalEntryId!.Value;
            var dimensions = new LedgerLineDimensionSet(scope.FundProfileId, EntityId: scope.EntityId,
                InvestorId: line.InvestorId, CapitalAccountId: line.CapitalAccountId,
                OrganizationId: organizationId.ToString("D"), BookId: book.LedgerBookId.ToString("D"),
                ExternalGlDimensions: new Dictionary<string, string>
                {
                    ["capitalCallId"] = call.CallId,
                    ["capitalActivityKind"] = "CapitalCallIssuance",
                    ["commitmentId"] = line.CommitmentId,
                    ["commitmentRevision"] = line.CommitmentRevision.ToString(CultureInfo.InvariantCulture)
                });
            var timestamp = now.AddHours(-8);
            var entry = new JournalEntry(journalId, timestamp, "Posted capital call installment",
                [new LedgerEntry(Guid.NewGuid(), journalId, timestamp, new LedgerAccount("Assets:Capital receivable", LedgerAccountType.Asset),
                    line.Amount, 0m, "Posted capital call installment", dimensions),
                 new LedgerEntry(Guid.NewGuid(), journalId, timestamp, new LedgerAccount("Equity:Capital", LedgerAccountType.Equity),
                    0m, line.Amount, "Posted capital call installment", dimensions)]);
            return new LedgerJournalEntryRecord(entry, Guid.NewGuid(), period.PeriodId, null, null,
                index + 1, timestamp, AccountingBasisKindDto.Primary);
        }).ToArray();
        ((ILedgerReportingSnapshotSource)journals).CaptureReportingSnapshotAsync(
            Arg.Any<LedgerJournalEntryQuery>(), period.PeriodId, Arg.Any<CancellationToken>())
            .Returns(new LedgerReportingSnapshot(records, [], period, []));
        var tenancy = Substitute.For<IFundProfileTenancyRegistry>();
        tenancy.ResolveAsync(scope.FundProfileId, Arg.Any<CancellationToken>())
            .Returns(new FundProfileOwnership(scope.FundProfileId, scope.TenantId, scope.CompanyId));
        var structure = Substitute.For<IFundStructureService>();
        structure.GetOrganizationStructureAsync(Arg.Any<OrganizationStructureQuery>(), Arg.Any<CancellationToken>())
            .Returns(new OrganizationStructureGraphDto(
                [new OrganizationSummaryDto(organizationId, "ORG", "Notice organization", "USD", true, now.AddYears(-1), null, [])],
                [], [], [], [], [], [], [], [],
                [new FundStructureNodeDto(organizationId, FundStructureNodeKindDto.Organization, "ORG", "Notice organization", null, true, now.AddYears(-1), null),
                 new FundStructureNodeDto(book.FundStructureNodeId, FundStructureNodeKindDto.Entity, "ENTITY", "Issuing entity", null, true, now.AddYears(-1), null)],
                [new OwnershipLinkDto(Guid.NewGuid(), organizationId, book.FundStructureNodeId,
                    OwnershipRelationshipTypeDto.Owns, 100m, true, now.AddYears(-1), null, null)], []));
        var source = new LedgerReportingAuthoritativeSource(journals, tenancy, structure,
            new NoticeTimeProvider(now), capitalCallNotices: validator);
        return new(scope, call, commitments, store, validator, source);
    }

    internal static ReportingRunParametersDto SelectedParameters(MultipleCommitmentFixture fixture,
        PrivateCapitalCallLineDto line, bool legacyFilter = false)
    {
        var external = new Dictionary<string, string>
        { ["capitalCallId"] = fixture.Call.CallId, ["capitalActivityKind"] = "CapitalCallIssuance" };
        if (!legacyFilter)
            external["commitmentId"] = line.CommitmentId;
        return new(new ReportingRunScopeDto(fixture.Scope.FundProfileId, ReportingEntityScopeKindDto.Investor,
                InvestorId: line.InvestorId, Dimensions: new LedgerDimensionSetDto(EntityId: fixture.Scope.EntityId,
                    InvestorId: line.InvestorId, CapitalAccountId: line.CapitalAccountId, ExternalGlDimensions: external)),
            fixture.Call.PeriodId, fixture.Call.NoticeDate, new ReportingLedgerBookSelectionDto(fixture.Scope.LedgerBookId),
            ReportingAccountingBasisDto.Management, "USD", ReportingConsolidationLevelDto.Investor,
            ReportingOutputFormatDto.ClientPackage, ReportingFinalityDto.Draft, true, true,
            CapitalCallNoticeReportingService.BuildTemplateParameters(fixture.Call, line,
                fixture.Commitments.Single(commitment => commitment.CommitmentId == line.CommitmentId),
                "reporting-user-17", "capital-reviewers"));
    }

    internal static async Task<(ReportingJobContract Contract, ReportingReconciliationEvidenceReceipt Receipt)> CertifiedSelectionAsync(
        MultipleCommitmentFixture fixture, PrivateCapitalCallLineDto line, ReportingRunOperation operation, bool legacyFilter = false)
    {
        var parameters = SelectedParameters(fixture, line, legacyFilter);
        var bindings = parameters.TemplateParameters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        bindings[ReportingOperationIdentity.OperationIdParameter] = operation.RunId;
        bindings[ReportingOperationIdentity.RequestHashParameter] = operation.RequestHash;
        parameters = parameters with { TemplateParameters = bindings };
        ReportingReconciliationEvidenceReceipt? retained = null;
        var retention = Substitute.For<IReportingReconciliationEvidenceRetentionStore>();
        retention.RetainAsync(Arg.Any<ReportingReconciliationEvidenceReceipt>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            retained = call.ArgAt<ReportingReconciliationEvidenceReceipt>(0);
            return ValueTask.FromResult(true);
        });
        var notices = new CapitalCallNoticeReportingService(fixture.Validator, null!, null!,
            authoritativeSource: fixture.Source, reconciliationRetention: retention);
        await notices.ReconcilePostedIssuanceAsync(parameters, Access());
        retained.Should().NotBeNull();
        var reconciliation = Substitute.For<IReportingReconciliationEvidenceSource>();
        reconciliation.ResolveAsync(Arg.Any<ReportingRunParametersDto>(), Arg.Any<ReportingAuthoritativeSourceCheckpoint>(),
            Arg.Any<ReportAccessQueryContext>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(retained!));
        var template = new DefaultReportingTemplateCatalog().Get(CapitalCallNoticeReportingService.TemplateId);
        var requestedAt = DateTimeOffset.UtcNow;
        var readiness = new ReportingRunReadinessDto("selected-issuance-evaluation", retained!.ReconciledAtUtc,
            new VersionedReportTemplateIdDto(template.TemplateId, 1), parameters, ReportingRunReadinessStatusDto.Ready,
            true, false, [new ReportingRunReadinessCheckDto("posted-issuance", "Posted issuance",
                ReportingRunReadinessStatusDto.Ready, "The selected journal exactly matches its installment.", 0, false, false,
                EvidenceReferences: ["posted:" + line.IssuanceJournalEntryId])], [], new string('d', 64));
        var certified = await new ReportingRunCertificationService(fixture.Source, reconciliation)
            .CertifyAsync(template, readiness, Access());
        return (new ReportingJobContract(operation.RunId, template.TemplateId, fixture.Call.NoticeDate,
            ReportingRunTrigger.AdHoc, 0, "operator", requestedAt, DatasetRows: certified.DatasetRows,
            ResolvedTemplate: certified.Readiness.ResolvedTemplate, ResolvedParameters: certified.Readiness.ResolvedParameters,
            Readiness: certified.Readiness, OperationalScope: certified.OperationalScope, ImmutableAccessScope: certified.AccessScope,
            CertifiedSnapshot: certified.Snapshot, AuthoritativeSource: certified.AuthoritativeSource)
        { DurableOperationId = operation.RunId, OperationRequestHash = operation.RequestHash }, retained!);
    }

    private sealed class NoticeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private const string LegacyParametersJson =
        """{"scope":{"fundProfileId":"fund-a","entityScopeKind":"Investor","entityId":null,"portfolioId":null,"investorId":"investor-a","dimensions":{"FundId":null,"EntityId":"entity-a","SleeveId":null,"StrategyId":null,"InvestorId":"investor-a","CapitalAccountId":"capital-a","InstrumentId":null,"TaxLotId":null,"CostCenterId":null,"CounterpartyId":null,"OrganizationId":null,"PortfolioId":null,"BookId":null,"AccountId":null,"CustomerId":null,"VendorId":null,"ProjectId":null,"ExternalGlDimensions":{"capitalActivityKind":"CapitalCallIssuance","capitalCallId":"call-a"},"PositionId":null}},"periodId":"40000000-0000-0000-0000-000000000001","asOfDate":"2026-11-10","ledgerBookId":"10000000-0000-0000-0000-000000000001","ledgerBookCode":null,"accountingBasis":"Management","presentationCurrency":"USD","consolidationLevel":"Investor","outputFormat":"ClientPackage","finality":"Draft","includeSupportingSchedules":true,"includeEvidenceAppendix":true,"templateParameters":{"callAmount":"100","capitalAccountId":"capital-a","capitalCallId":"call-a","commitmentId":"commitment-a","commitmentRevision":"2","dueDate":"2026-11-25","entityId":"entity-a","installmentId":"installment-a","issuanceJournalEntryId":"20000000-0000-0000-0000-000000000001","noticeDate":"2026-11-10","operationalNoticeTemplateId":"capital-call-notice","planHash":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","purpose":"Investment funding","recipientPrincipalId":"reporting-user-17","reviewGroupPrincipalId":"capital-reviewers","totalCommitment":"500","uncalledAfter":"400","uncalledBefore":"500"}}""";
    private const string LegacyParametersHash = "7086f769eff37334114e06b2ec2095174c0c82b232285e89a41c79f59bb2cb60";
    private const string LegacyRawParametersJson =
        """{"scope":{"fundProfileId":"fund-a","entityScopeKind":"Investor","entityId":null,"portfolioId":null,"investorId":"investor-a","dimensions":{"FundId":null,"EntityId":"entity-a","SleeveId":null,"StrategyId":null,"InvestorId":"investor-a","CapitalAccountId":"capital-a","InstrumentId":null,"TaxLotId":null,"CostCenterId":null,"CounterpartyId":null,"OrganizationId":null,"PortfolioId":null,"BookId":null,"AccountId":null,"CustomerId":null,"VendorId":null,"ProjectId":null,"ExternalGlDimensions":{"capitalCallId":"call-a","capitalActivityKind":"CapitalCallIssuance"},"PositionId":null}},"periodId":"40000000-0000-0000-0000-000000000001","asOfDate":"2026-11-10","ledgerBookId":"10000000-0000-0000-0000-000000000001","ledgerBookCode":null,"accountingBasis":"Management","presentationCurrency":"USD","consolidationLevel":"Investor","outputFormat":"ClientPackage","finality":"Draft","includeSupportingSchedules":true,"includeEvidenceAppendix":true,"templateParameters":{"callAmount":"100","capitalAccountId":"capital-a","capitalCallId":"call-a","commitmentId":"commitment-a","commitmentRevision":"2","dueDate":"2026-11-25","entityId":"entity-a","installmentId":"installment-a","issuanceJournalEntryId":"20000000-0000-0000-0000-000000000001","noticeDate":"2026-11-10","operationalNoticeTemplateId":"capital-call-notice","planHash":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","purpose":"Investment funding","recipientPrincipalId":"reporting-user-17","reviewGroupPrincipalId":"capital-reviewers","totalCommitment":"500","uncalledAfter":"400","uncalledBefore":"500"}}""";
    private const string LegacyRawParametersHash = "1476bc8a99515e52ef0ddd1a7fd4fe09b83106f6e21ee3794145c57cd3c70dbe";
    private const string SelectedParametersHash = "ec5293a1530b52328e3b8a3dabf9e404b572020011563fec581bc38ca2cd60b3";
    private const string EmptyDimensionsHash = "0dc6380ffb361768a941b8fa9e67da5884f5f7c795ea04473570beda9b0b8cd3";
    private const string EmptyExternalMapHash = "f849d55ec5fa78d81e8a1e8dd2db16a55ab42643077bb00926e1b2ff82723eb0";
    private const string NullDimensionsHash = "973de113e40fc348ebc0f4a87fc7f5aa919c3d9527f90d61568c74fcb4ca1611";

    private static PrivateCapitalCallLineDto Line() => new("installment-a", "commitment-a", 2,
        "investor-a", "capital-a", 100m, 500m, 400m, 100m, IssuanceJournalEntryId: JournalId);

    private static PrivateCapitalCallDto Call(PrivateCapitalCallLineDto? line = null) => new("call-a", Scope,
        "40000000-0000-0000-0000-000000000001", NoticeDate, NoticeDate.AddDays(15), 100m,
        "Investment funding", new string('a', 64), [line ?? Line()], "operator", DateTimeOffset.UtcNow);

    private static PrivateCapitalCommitmentDto Commitment() => new("commitment-a", Scope, 2,
        "investor-a", "capital-a", 500m, NoticeDate.AddMonths(-1), NoticeDate.AddMonths(-1), null, true,
        PrivateCapitalCommitmentApprovalStatus.Approved, ["vault:subscription"], "operator", DateTimeOffset.UtcNow);

    private static ReportingRunParametersDto Parameters() => new(
        new ReportingRunScopeDto("fund-a", ReportingEntityScopeKindDto.Investor, InvestorId: "investor-a",
            Dimensions: new LedgerDimensionSetDto(EntityId: "entity-a", InvestorId: "investor-a", CapitalAccountId: "capital-a",
                ExternalGlDimensions: new Dictionary<string, string>
                {
                    ["capitalCallId"] = "call-a",
                    ["capitalActivityKind"] = "CapitalCallIssuance"
                })), Call().PeriodId, NoticeDate, new ReportingLedgerBookSelectionDto(Scope.LedgerBookId),
        ReportingAccountingBasisDto.Management, "USD", ReportingConsolidationLevelDto.Investor,
        ReportingOutputFormatDto.ClientPackage, ReportingFinalityDto.Draft, true, true,
        CapitalCallNoticeReportingService.BuildTemplateParameters(Call(), Line(), Commitment(), "reporting-user-17", "capital-reviewers"));

    private static ReportAccessQueryContext Access() => new("operator", [], "company-a", TenantId: "tenant-a", RequireBoundScope: true);

    private static ImmutableArray<IReadOnlyDictionary<string, string>> Rows()
    {
        IReadOnlyDictionary<string, string> Row(string debit, string credit) => new Dictionary<string, string>
        {
            ["investorId"] = "investor-a",
            ["capitalAccountId"] = "capital-a",
            ["journalEntryId"] = JournalId.ToString("D"),
            ["externalGl.capitalCallId"] = "call-a",
            ["externalGl.capitalActivityKind"] = "CapitalCallIssuance",
            ["externalGl.commitmentId"] = "commitment-a",
            ["externalGl.commitmentRevision"] = "2",
            ["debit"] = debit,
            ["credit"] = credit,
            ["recordedAtUtc"] = "2026-11-10T12:00:00.0000000+00:00"
        };
        return [Row("100", "0"), Row("0", "100")];
    }

    private static ReportingOutputManifest Manifest() => new("run-a", CapitalCallNoticeReportingService.TemplateId,
        NoticeDate, ReportingRunStatus.Draft, [], [], 1, ReportingRunTrigger.AdHoc,
        ResolvedParameters: Parameters(), CertifiedDatasetRows: Rows(),
        ImmutableAccessScope: new ReportingAccessScope("policy-a", "1", ReportingGovernanceAccessMode.Restricted,
            "operator", true, [new ReportingAccessPrincipalScope(ReportingAccessPrincipalKind.User, "reporting-user-17"),
                new ReportingAccessPrincipalScope(ReportingAccessPrincipalKind.Group, "capital-reviewers")], new string('a', 64)));
}
