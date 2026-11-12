using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster.CorporateActions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

public sealed partial class CorporateActionCaseAccountingServiceTests
{
    [Fact]
    public async Task CashDividendEligibility_ComposesAllBookAccountsAndAcceptedCanonicalSource()
    {
        var fixture = await EntitlementFixture.CreateAsync();

        var result = await fixture.ResolveAsync();

        result.GrossImpactUsd.Should().Be(120.50m);
        result.WholeEventGrossUsd.Should().Be(170.50m);
        result.BudgetMonth.Should().Be(new DateOnly(2026, 8, 1));
        result.AccountRoleBinding!.DividendReceivableAccount.Should().Be("assets:receivable-selected-by-native-rule");
        result.AccountRoleBinding.DividendIncomeAccount.Should().Be("revenue:native-dividend-income");
        result.EntitlementSnapshotHash.Should().Be(Sha256Digest.ComputeUtf8(result.EntitlementSnapshotJson));
        result.RelatedEventKey.Should().Be(CorporateActionCashDividendAttestationValidation.RelatedEventKey(SecurityId, fixture.Action.ExDate));
        fixture.Action.CorpActId.Should().NotBe(fixture.Case.CorporateActionId,
            "accepted canonical corporate-action IDs differ from the immutable provider proposal IDs");
        await fixture.Reconciliation.Received(1).GetSupportAsync(fixture.Case.Scope, SecurityId,
            fixture.Action.ExDate.AddDays(-1), Arg.Is<IReadOnlyList<CashDividendReconciliationAccountDto>>(accounts =>
                accounts.Count == 2 && accounts.Any(account => account.FinancialAccountId == "account-a" && account.ExpectedLongQuantity == 241m)
                && accounts.Any(account => account.FinancialAccountId == "account-b" && account.ExpectedLongQuantity == 100m)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CashDividendEligibility_CaseBelowCapCannotHideOtherAccounts()
    {
        var fixture = await EntitlementFixture.CreateAsync(otherUnits: 20_000m);
        await ExcludedAsync(fixture);
    }

    [Fact]
    public async Task CashDividendEligibility_WholeEventLimitIsInclusiveAndFractionalCentsCannotRoundDown()
    {
        var atLimit = await EntitlementFixture.CreateAsync(otherUnits: 19_759m);
        (await atLimit.ResolveAsync()).WholeEventGrossUsd.Should().Be(10_000m);
        var aboveLimit = await EntitlementFixture.CreateAsync(otherUnits: 19_759.0002m);
        await ExcludedAsync(aboveLimit);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("missing-acquisition")]
    [InlineData("missing-account")]
    [InlineData("foreign-currency")]
    [InlineData("unknown-position")]
    [InlineData("wrong-acquisition-subject")]
    public async Task CashDividendEligibility_UnknownOrExcludedOtherAccountCannotBeOmitted(string problem)
    {
        var fixture = await EntitlementFixture.CreateAsync();
        var lot = fixture.Lots[1];
        fixture.Lots[1] = problem switch
        {
            "short" => lot with { Acquisition = lot.Acquisition! with { Direction = OpenLotDirection.Short } },
            "missing-acquisition" => lot with { Acquisition = null },
            "missing-account" => lot with { Account = lot.Account with { FinancialAccountId = null } },
            "foreign-currency" => lot with { Currency = "EUR" },
            "wrong-acquisition-subject" => lot with
            {
                Acquisition = lot.Acquisition! with
                { Evidence = [lot.Acquisition.Evidence[0] with { SubjectId = Guid.NewGuid().ToString("D") }] }
            },
            _ => lot with { BookPositionId = Guid.Empty }
        };
        await ExcludedAsync(fixture);
    }

    [Fact]
    public async Task CashDividendEligibility_RecordWindowQuantityChangeRequiresSpecialistEntitlement()
    {
        var fixture = await EntitlementFixture.CreateAsync();
        fixture.RecordLots = [fixture.Lots[0], fixture.Lots[1] with { OpenQuantity = 99m }];
        await ExcludedAsync(fixture);
    }

    [Theory]
    [InlineData("special")]
    [InlineData("tax-payload")]
    [InlineData("correction")]
    [InlineData("cancelled")]
    [InlineData("foreign-currency")]
    [InlineData("different-recognition-date")]
    [InlineData("unknown-source-version")]
    [InlineData("unknown-record-date")]
    [InlineData("current-record-date")]
    [InlineData("future-record-date")]
    public async Task CashDividendEligibility_ExcludesUncertainSourceAndTreatment(string problem)
    {
        var fixture = await EntitlementFixture.CreateAsync();
        fixture.Action = problem switch
        {
            "special" => fixture.Action with { EventType = CorporateActionEventTypes.SpecialDividend },
            "tax-payload" => fixture.Action with { Payload = JsonSerializer.SerializeToElement(new { withholding = 0 }) },
            "correction" => fixture.Action with { SupersedesCorpActId = Guid.NewGuid() },
            "cancelled" => fixture.Action with { LifecycleState = "Cancelled" },
            "foreign-currency" => fixture.Action with { Currency = "EUR" },
            "different-recognition-date" => fixture.Action with { ExDate = fixture.Action.ExDate.AddDays(-1) },
            "unknown-record-date" => fixture.Action with { RecordDate = null },
            "current-record-date" => fixture.Action with { RecordDate = new DateOnly(2026, 11, 12), PayDate = new DateOnly(2026, 11, 20) },
            "future-record-date" => fixture.Action with { RecordDate = new DateOnly(2026, 11, 13), PayDate = new DateOnly(2026, 11, 20) },
            _ => fixture.Action
        };
        if (problem == "unknown-source-version")
            fixture.Provider = fixture.Provider with { SourceEventVersion = "" };
        await ExcludedAsync(fixture);
    }

    [Fact]
    public async Task CashDividendEligibility_RefusesMissingExternalReconciliationAuthority()
    {
        var fixture = await EntitlementFixture.CreateAsync();
        fixture.WithReconciliation = false;
        (await Assert.ThrowsAsync<CorporateActionPersistenceUnavailableException>(() => fixture.ResolveAsync()))
            .Message.Should().Contain("external holdings reconciliation");
    }

    [Fact]
    public async Task CashDividendEligibility_RefusesTamperedReconciliationEvidence()
    {
        var fixture = await EntitlementFixture.CreateAsync();
        fixture.Reconciliation.GetSupportAsync(Arg.Any<CorporateActionCaseScopeDto>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(),
            Arg.Any<IReadOnlyList<CashDividendReconciliationAccountDto>>(), Arg.Any<CancellationToken>())
            .Returns(new CashDividendReconciliationSupportDto(new string('a', 64), "{}"));
        await ExcludedAsync(fixture);
    }

    [Fact]
    public async Task CashDividendEligibility_RefusesPagedOrUnverifiableInventory()
    {
        var fixture = await EntitlementFixture.CreateAsync();
        fixture.CompleteInventory = false;
        await ExcludedAsync(fixture);
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("changed-terms")]
    [InlineData("superseded")]
    [InlineData("ambiguous-unsuperseded-peer")]
    public async Task CashDividendEligibility_CurrentCanonicalSourceOverridesOldAcceptedProposal(string change)
    {
        var fixture = await EntitlementFixture.CreateAsync();
        var accepted = fixture.Action with { CorpActId = fixture.Case.CorporateActionId };
        fixture.CanonicalSource = change switch
        {
            "cancelled" => [accepted with { LifecycleState = "Cancelled" }],
            "changed-terms" => [accepted with { DividendPerShare = 0.6m }],
            "ambiguous-unsuperseded-peer" => [accepted, accepted with { CorpActId = Guid.NewGuid(), DividendPerShare = 0.6m }],
            _ => [accepted, accepted with { CorpActId = Guid.NewGuid(), SupersedesCorpActId = accepted.CorpActId }]
        };
        await ExcludedAsync(fixture);
    }

    [Theory]
    [InlineData("unpromoted")]
    [InlineData("self-promoted")]
    [InlineData("archived-account")]
    [InlineData("wrong-account-type")]
    [InlineData("stale-position")]
    public async Task CashDividendEligibility_PreservesNativeRuleChartAndPositionAuthority(string problem)
    {
        var fixture = await EntitlementFixture.CreateAsync();
        fixture.Workspace = problem switch
        {
            "unpromoted" => fixture.Workspace with { PostingRules = [fixture.Workspace.PostingRules[0] with { PromotionApproval = null }] },
            "self-promoted" => fixture.Workspace with
            {
                PostingRules = [fixture.Workspace.PostingRules[0] with
                { PromotionApproval = fixture.Workspace.PostingRules[0].PromotionApproval! with { ApprovedBy = "maker" } }]
            },
            "archived-account" => fixture.Workspace with { ChartOfAccounts = [fixture.Workspace.ChartOfAccounts[0] with { IsArchived = true }, fixture.Workspace.ChartOfAccounts[1]] },
            "wrong-account-type" => fixture.Workspace with { ChartOfAccounts = [fixture.Workspace.ChartOfAccounts[0], fixture.Workspace.ChartOfAccounts[1] with { AccountType = "Liability" }] },
            _ => fixture.Workspace
        };
        if (problem == "stale-position")
            fixture.PositionSnapshot = fixture.PositionSnapshot with { BookPositions = [fixture.PositionSnapshot.BookPositions[0] with { Version = 12 }, fixture.PositionSnapshot.BookPositions[1]] };
        await ExcludedAsync(fixture);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("ambiguous")]
    [InlineData("expired")]
    [InlineData("not-yet-valid")]
    [InlineData("current-drift")]
    [InlineData("retained-missing")]
    [InlineData("retained-drift")]
    [InlineData("other-held-lot-drift")]
    public async Task CashDividendEligibility_RequiresExactDatedCanonicalSymbolAcrossCandidateAndAllHeldLots(string problem)
    {
        var fixture = await EntitlementFixture.CreateAsync();
        var timestamp = fixture.Spine.DraftedCandidate!.AccountingTimestamp;
        fixture.CurrentIdentifiers = problem switch
        {
            "missing" => [],
            "ambiguous" => [new(SecurityIdentifierKind.Ticker, "FIX", true, DateTimeOffset.UnixEpoch),
                new(SecurityIdentifierKind.Ticker, "OTHER", false, DateTimeOffset.UnixEpoch)],
            "expired" => [new(SecurityIdentifierKind.Ticker, "FIX", true, DateTimeOffset.UnixEpoch, timestamp)],
            "not-yet-valid" => [new(SecurityIdentifierKind.Ticker, "FIX", true, timestamp.AddTicks(1))],
            "current-drift" => [new(SecurityIdentifierKind.Ticker, "OTHER", true, DateTimeOffset.UnixEpoch)],
            _ => fixture.CurrentIdentifiers
        };
        if (problem is "retained-missing" or "retained-drift")
        {
            var candidate = fixture.Spine.DraftedCandidate with { InstrumentSymbol = problem == "retained-missing" ? null : "OTHER" };
            var fingerprint = AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(candidate);
            fixture.Spine = fixture.Spine with { DraftedCandidate = candidate, DraftedCandidateFingerprint = fingerprint };
            fixture.Projection = fixture.Projection with { DraftedCandidateFingerprint = fingerprint };
        }
        if (problem == "other-held-lot-drift")
            fixture.Lots[1] = fixture.Lots[1] with { Account = fixture.Lots[1].Account with { Symbol = "OTHER" } };

        await ExcludedAsync(fixture);
    }

    [Theory]
    [InlineData("receivable-symbol")]
    [InlineData("income-symbol")]
    [InlineData("receivable-account")]
    [InlineData("income-account")]
    public async Task CashDividendEligibility_ExplicitChartMappingsCannotOverrideCanonicalInstrumentOrNativeAccount(string problem)
    {
        var fixture = await EntitlementFixture.CreateAsync();
        var nodes = fixture.Workspace.ChartOfAccounts.ToArray();
        var index = problem.StartsWith("receivable", StringComparison.Ordinal) ? 0 : 1;
        nodes[index] = problem.EndsWith("symbol", StringComparison.Ordinal)
            ? nodes[index] with { Symbol = "OTHER" }
            : nodes[index] with { FinancialAccountId = "different-account" };
        fixture.Workspace = fixture.Workspace with { ChartOfAccounts = nodes };

        await ExcludedAsync(fixture);
    }

    [Fact]
    public async Task CashDividendEligibility_AllowsMatchingExplicitChartMappingsAndRetainsCanonicalTicker()
    {
        var fixture = await EntitlementFixture.CreateAsync();
        var result = fixture.Spine.DraftedCandidateResult! with
        {
            GeneratedPostingLines = fixture.Spine.DraftedCandidateResult!.GeneratedPostingLines
            .Select(line => line with { Dimensions = new LedgerDimensionSetDto(AccountId: "account-a") }).ToArray()
        };
        fixture.Spine = fixture.Spine with
        {
            DraftedCandidateResult = result,
            DraftedCandidateResultFingerprint = AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(result)
        };
        fixture.Workspace = fixture.Workspace with
        {
            ChartOfAccounts = fixture.Workspace.ChartOfAccounts
            .Select(node => node with { Symbol = "fix", FinancialAccountId = "account-a" }).ToArray()
        };

        var eligibility = await fixture.ResolveAsync();

        using var proof = JsonDocument.Parse(eligibility.EntitlementSnapshotJson);
        proof.RootElement.GetProperty("canonicalInstrumentSymbol").GetString().Should().Be("FIX");
    }

    [Fact]
    public async Task CashDividendEligibility_ProofStableAcrossAttestationAppendButChangesWithCanonicalFacts()
    {
        var fixture = await EntitlementFixture.CreateAsync();
        var first = await fixture.ResolveAsync();
        fixture.Case = fixture.Case with { State = CorporateActionCaseStates.PreparerAttested, Version = fixture.Case.Version + 1 };
        fixture.Spine = fixture.Spine with
        {
            SpineVersion = fixture.Spine.SpineVersion + 1,
            Stages = [.. fixture.Spine.Stages,
            new(AssetAccountingLifecycleStageDto.PreparerAttested, DateTimeOffset.UtcNow, "fund-accountant")]
        };
        (await fixture.ResolveAsync()).EntitlementSnapshotHash.Should().Be(first.EntitlementSnapshotHash);
        fixture.Lots[1] = fixture.Lots[1] with { Version = fixture.Lots[1].Version + 1 };
        (await fixture.ResolveAsync()).EntitlementSnapshotHash.Should().NotBe(first.EntitlementSnapshotHash);
    }

    [Theory]
    [InlineData("candidate-result")]
    [InlineData("source-evidence")]
    [InlineData("candidate-source-evidence")]
    [InlineData("lot-authority")]
    public async Task CashDividendEligibility_NativeAuthorityCannotBeReplacedByMatchingNumbers(string problem)
    {
        var fixture = await EntitlementFixture.CreateAsync();
        if (problem == "candidate-result")
            fixture.Spine = fixture.Spine with { DraftedCandidateResultFingerprint = new string('0', 64) };
        else if (problem == "source-evidence")
            fixture.Spine = fixture.Spine with { RetainedEvidence = [fixture.Spine.RetainedEvidence[0] with { SourceReference = "different-event" }] };
        else if (problem == "candidate-source-evidence")
        {
            var request = fixture.Spine.DraftedCandidate! with
            {
                RetainedEvidence =
                [fixture.Spine.DraftedCandidate!.RetainedEvidence[0] with { SourceReference = "different-event" }]
            };
            var fingerprint = AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(request);
            fixture.Spine = fixture.Spine with { DraftedCandidate = request, DraftedCandidateFingerprint = fingerprint };
            fixture.Projection = fixture.Projection with { DraftedCandidateFingerprint = fingerprint };
        }
        else
            fixture.Projection = fixture.Projection with { HasAuthoritativeLotResolution = false };
        await ExcludedAsync(fixture);
    }

    [Theory]
    [InlineData("processing-option")]
    [InlineData("election-evidence")]
    [InlineData("unclassified-typed-metadata")]
    public async Task CashDividendEligibility_RequiresCompleteCaseFactsWithNoOptionsElectionsOrUnknownTypedTerms(string problem)
    {
        var fixture = await EntitlementFixture.CreateAsync();
        if (problem == "processing-option")
            fixture.Options = [new(Guid.NewGuid(), CaseId, "choice", "Choice", "Retained option", "Proposed", null,
                [], null, fixture.Case.Version, "accountant", DateTimeOffset.UnixEpoch)];
        else
            fixture.CaseEvidence = [new(Guid.NewGuid(), CaseId,
                problem == "election-evidence" ? CorporateActionEvidenceKinds.ElectionInstruction : CorporateActionEvidenceKinds.ProviderPayload,
                "provider://fixture-evidence/v1", new string('a', 64), null,
                problem == "unclassified-typed-metadata" ? JsonSerializer.SerializeToElement(new { withholdingRate = 0 }) : null,
                fixture.Case.Version, "accountant", DateTimeOffset.UnixEpoch)];
        await ExcludedAsync(fixture);
    }

    private static async Task ExcludedAsync(EntitlementFixture fixture) =>
        (await Assert.ThrowsAsync<CorporateActionOperationException>(() => fixture.ResolveAsync()))
        .Code.Should().Be(CorporateActionCashDividendAttestationValidation.ExcludedProblemCode);

    private sealed class EntitlementFixture
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        public required CorporateActionProcessingCaseDto Case { get; set; }
        public required CorporateActionCaseAccountingProjectionDto Projection { get; set; }
        public required AssetAccountingEventSpineDto Spine { get; set; }
        public required CorporateActionDto Action { get; set; }
        public required CorporateActionProviderEventIdentityDto Provider { get; set; }
        public required LedgerBookRecord Book { get; init; }
        public required LedgerTaxLotRecord[] Lots { get; init; }
        public LedgerTaxLotRecord[]? RecordLots { get; set; }
        public CorporateActionDto[]? CanonicalSource { get; set; }
        public IReadOnlyList<CorporateActionProcessingOptionDto> Options { get; set; } = [];
        public IReadOnlyList<CorporateActionEvidenceDto> CaseEvidence { get; set; } = [];
        public required InstrumentPositionProjectionSnapshot PositionSnapshot { get; set; }
        public required AccountingConfigurationWorkspaceDto Workspace { get; set; }
        public IReadOnlyList<SecurityIdentifierDto> CurrentIdentifiers { get; set; } =
            [new(SecurityIdentifierKind.Ticker, "FIX", true, DateTimeOffset.UnixEpoch)];
        public bool CompleteInventory { get; set; } = true;
        public bool WithReconciliation { get; set; } = true;
        public ICorporateActionCashDividendReconciliationSupport Reconciliation { get; } = Substitute.For<ICorporateActionCashDividendReconciliationSupport>();

        public async Task<CorporateActionCashDividendEligibilityDto> ResolveAsync()
        {
            var inventory = Substitute.For<ILedgerSecurityEntitlementInventory>();
            inventory.ReadSecurityEntitlementInventoryAsync("tenant-a", "company-a", LedgerBookId, SecurityId,
                Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(call => Inventory(call.Arg<DateOnly>()));
            var positions = Substitute.For<IInstrumentPositionProjectionStore>();
            positions.GetAsOfAsync(SecurityId, LedgerBookId, Action.ExDate.AddDays(-1), Arg.Any<CancellationToken>()).Returns(PositionSnapshot);
            var securities = Substitute.For<ISecurityMasterStore>();
            var empty = JsonSerializer.SerializeToElement(new { });
            securities.GetProjectionAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(new SecurityProjectionRecord(SecurityId,
                "Equity", SecurityStatusDto.Active, "Fixture equity", "USD", "Ticker", "FIX", empty, empty, empty,
                3, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), null, CurrentIdentifiers, []));
            var source = Substitute.For<ICorporateActionOperationsStore>();
            source.GetSourceProposalAsync(Case.ProposalId, Arg.Any<CancellationToken>()).Returns(new CorporateActionSourceProposalDto(
                Case.ProposalId, SecurityId, Provider, Action, 1, "fixture", CorporateActionSourceProposalStates.Accepted, 2,
                null, Case.CorporateActionId, Case.CaseId, "acceptor", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
            source.ListConflictsAsync(CaseId, "tenant-a", "company-a", null, 1, Arg.Any<CancellationToken>()).Returns(Array.Empty<CorporateActionConflictDto>());
            var configuration = Substitute.For<IAccountingConfigurationService>();
            configuration.GetWorkspaceAsync("fund-alpha", LedgerBookId, Arg.Any<CancellationToken>(), "tenant-a", "company-a").Returns(Workspace);
            var canonical = Substitute.For<ISecurityCorporateActionsQueryService>();
            canonical.GetCorporateActionsAsync(SecurityId, Arg.Any<CancellationToken>())
                .Returns(CanonicalSource ?? [Action with { CorpActId = Case.CorporateActionId }]);
            var factsStore = Substitute.For<ICorporateActionCashDividendAccountingStore>();
            var factsJson = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                method = "complete-scoped-cash-dividend-case-facts-v1",
                caseId = CaseId,
                scope = Case.Scope,
                complete = true,
                options = Options,
                evidence = CaseEvidence
            }, Json);
            factsStore.GetCashDividendCaseFactsAsync(CaseId, "tenant-a", "company-a", Case.Version, Arg.Any<CancellationToken>())
                .Returns(new CorporateActionCashDividendCaseFactsDto(CaseId, Case.Version, Options, CaseEvidence,
                    factsJson, Sha256Digest.ComputeUtf8(factsJson)));
            var resolver = new CorporateActionCashDividendEligibilityResolver(inventory, positions, securities, source, configuration,
                WithReconciliation ? Reconciliation : null, canonical, factsStore, new EntitlementClock());
            return await resolver.ResolveAsync(Case with { SourceSnapshot = new(Action, Provider, null) }, Projection, Spine);
        }

        private LedgerSecurityEntitlementInventorySnapshot Inventory(DateOnly date)
        {
            var held = date >= Action.ExDate ? RecordLots ?? Lots : Lots;
            var json = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                method = "complete-book-journal-effective-lot-inventory-v1",
                complete = CompleteInventory,
                tenantId = "tenant-a",
                companyId = "company-a",
                securityId = SecurityId,
                inspectedBookLotCount = held.Length,
                sourceBookLots = held,
                heldLots = held,
                book = Book,
                asOfDate = date
            }, Json);
            return new("tenant-a", "company-a", Book, SecurityId, date, held.Length, held, json, Sha256Digest.ComputeUtf8(json));
        }

        public static async Task<EntitlementFixture> CreateAsync(decimal otherUnits = 100m)
        {
            var existing = Fixture.Create(caseState: CorporateActionCaseStates.ReadyForApproval).WithCurrentProjection();
            var projection = (await existing.Store.GetAccountingProjectionAsync(CaseId, "tenant-a", "company-a"))!;
            var date = projection.EffectiveDate;
            var book = new LedgerBookRecord(LedgerBookId, "fund-alpha", Guid.NewGuid(), FundStructureNodeKindDto.Fund,
                "Fixture book", "USD", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, AccountingBasis: AccountingBasisKindDto.Gaap);
            var role = new InstrumentRoleDto(Guid.NewGuid(), SecurityId, book.FundStructureNodeId.ToString("D"), "Fund", InstrumentRoleKinds.Holder,
                "Asset", "Long", new DateOnly(2026, 1, 1));
            var otherPositionId = Guid.NewGuid();
            var context = new AccountingBookContextDto(LedgerBookId, "fund-alpha", book.FundStructureNodeId, book.FundStructureNodeKind,
                book.DisplayName, "USD", AccountingBasisKindDto.Gaap, book.AccountingPolicyId, book.AccountingPolicyVersion);
            BookPositionDto Position(Guid id, decimal units, string account, long version) => new(id, SecurityId, role.RoleId, context,
                BookPositionSides.Long, "Active", new DateOnly(2026, 1, 1), Version: version, PrimaryAccountId: account,
                CurrentEconomicState: new(Guid.NewGuid(), id, date.AddDays(-1), "USD", version, Quantity: units));
            LedgerTaxLotRecord Lot(Guid position, decimal units, string account)
            {
                var lotId = Guid.NewGuid();
                var acquiredDate = new DateOnly(2026, 1, 1);
                var acquiredAt = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
                var acquisitionEvidence = existing.Spine.RetainedEvidence[0] with
                {
                    EvidenceId = "acquisition:" + lotId.ToString("D"),
                    SubjectType = "OpenLotAcquisition",
                    SubjectId = lotId.ToString("D"),
                    EffectiveDate = acquiredDate,
                    ReviewedAtUtc = acquiredAt,
                    RetainedAtUtc = acquiredAt
                };
                return new(lotId, LedgerBookId, new LedgerAccount("Securities", LedgerAccountType.Asset, "FIX", account),
                    account + "-lot", acquiredDate, units, units, 10m, "USD", acquiredAt, acquiredAt, Guid.NewGuid(), Version: 1,
                    SecurityId: SecurityId, BookPositionId: position, Acquisition: new(LotQuantityBasis.Units, "USD", "USD", 1m,
                        units * 10m, units * 10m, acquiredDate, null, [acquisitionEvidence]));
            }
            var candidateResult = existing.Spine.DraftedCandidateResult! with
            {
                GeneratedPostingLines =
            [new("l1", "assets:receivable-selected-by-native-rule", AccountingTemplateLineSideDto.Debit, "amount", 120.50m),
             new("l2", "revenue:native-dividend-income", AccountingTemplateLineSideDto.Credit, "amount", 120.50m)]
            };
            var sourceEvidence = existing.Spine.RetainedEvidence[0] with
            {
                SourceSystem = "fixture-provider",
                SourceReference = "fixture-dividend",
                EvidenceUri = "provider://fixture-dividend/v1",
                ContentHashSha256 = new string('f', 64)
            };
            var request = existing.Spine.DraftedCandidate! with { RetainedEvidence = [sourceEvidence], InstrumentSymbol = "FIX" };
            var requestFingerprint = AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(request);
            projection = projection with { DraftedCandidateFingerprint = requestFingerprint };
            var spine = existing.Spine with
            {
                DraftedCandidateResult = candidateResult,
                DraftedCandidate = request,
                DraftedCandidateFingerprint = requestFingerprint,
                DraftedCandidateResultFingerprint = AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(candidateResult),
                ProjectedEffect = new(Guid.NewGuid(), "native-dividend", "1", date, 120.50m, 120.50m, "USD"),
                RetainedEvidence = [sourceEvidence]
            };
            var fixture = new EntitlementFixture
            {
                Case = existing.Case,
                Projection = projection,
                Spine = spine,
                Book = book,
                Lots = [Lot(spine.Scope.BookPositionId, 241m, "account-a"), Lot(otherPositionId, otherUnits, "account-b")],
                Action = new(Guid.NewGuid(), SecurityId, CorporateActionEventTypes.Dividend, date, date.AddDays(14), .5m, "USD",
                    null, null, null, null, null, null, null, RecordDate: date, LifecycleState: CorporateActionLifecycleStates.Confirmed),
                Provider = new("fixture-provider", "fixture-dividend", "1", DateTimeOffset.UnixEpoch, new string('f', 64),
                    "provider://fixture-dividend/v1", CorporateActionProviderReleaseStatusDto.AcceptanceEligible),
                PositionSnapshot = new(SecurityId, [role], [Position(spine.Scope.BookPositionId, 241m, "account-a", 11),
                    Position(otherPositionId, otherUnits, "account-b", 1)], [], [])
                { LedgerBookId = LedgerBookId, AsOfDate = date.AddDays(-1) },
                Workspace = new("fund-alpha", LedgerBookId, AccountingConfigurationStatusDto.Active, "v1", DateTimeOffset.UnixEpoch,
                    [], [new("receivable", "assets:receivable-selected-by-native-rule", "Existing asset", "Asset"),
                         new("income", "revenue:native-dividend-income", "Existing income", "Revenue")], [],
                    [new("cash-dividend", "Promoted native dividend", "AssetAccounting.CorporateAction", "native", "3",
                        PromotionApproval: new("approval-3", "maker", DateTimeOffset.UnixEpoch, ManualJournalEntryStatusDto.Approved,
                            "reviewer", DateTimeOffset.UnixEpoch, EvidenceLinks: ["review://cash-dividend/3/approval-3"]))], [], [],
                    TenantId: "tenant-a", CompanyId: "company-a")
            };
            const string proof = "{\"retainedExternalReconciliation\":true}";
            fixture.Reconciliation.GetSupportAsync(Arg.Any<CorporateActionCaseScopeDto>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(),
                Arg.Any<IReadOnlyList<CashDividendReconciliationAccountDto>>(), Arg.Any<CancellationToken>())
                .Returns(new CashDividendReconciliationSupportDto(Sha256Digest.ComputeUtf8(proof), proof));
            return fixture;
        }
    }

    private sealed class EntitlementClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
    }
}
