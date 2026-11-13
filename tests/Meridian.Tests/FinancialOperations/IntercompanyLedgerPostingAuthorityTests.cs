using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.Consolidation;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Ledger;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Meridian.Tests.TestSupport;
using Moq;
using Xunit;

namespace Meridian.Tests.FinancialOperations;

public sealed class IntercompanyLedgerPostingAuthorityTests
{
    [Fact]
    public async Task ExactReviewedFxAndCompanyScopedChart_PassesAndRetainsLeasesThroughCommit()
    {
        var fixture = await Fixture.CreateAsync();
        var context = fixture.Context(fixture.Write());
        var lease = await fixture.Authority().AcquireAsync([context]);
        Assert.Contains(context.Decisions, x => x.Code == "AgreementVerified" && x.Allowed);
        Assert.All(fixture.Leases, x => Assert.False(x.Disposed));
        fixture.Configuration.Verify(x => x.GetAsync(Fixture.Fund, fixture.BookId, It.IsAny<CancellationToken>(), Fixture.Tenant, Fixture.Company), Times.Once);
        await lease.DisposeAsync();
        Assert.All(fixture.Leases, x => Assert.True(x.Disposed));
    }

    [Theory]
    [InlineData("configuration")]
    [InlineData("rate-set")]
    public async Task SupersededFxWithSameNumericalRate_RequiresRenewedReview(string changed)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var fixture = await Fixture.CreateAsync();
        var write = fixture.Write();
        var old = write.PostingCommand!.IntercompanyFx!;
        fixture.AdvanceFx(changed == "rate-set");
        var current = ConsolidationFxTranslator.ConvertAmount(100m, "EUR", fixture.FxContext, Fixture.Date);
        Assert.Equal(old.Amount, current.Amount);
        Assert.Equal(old.Rate, current.Rate);
        Assert.NotEqual(old.ConfigurationHash, current.ConfigurationHash);
        var error = await Assert.ThrowsAsync<LedgerValidationException>(() => fixture.Authority().AcquireAsync([fixture.Context(write)], cancellation.Token));
        Assert.Contains("exact reviewed intercompany FX", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.All(fixture.Leases, x => Assert.True(x.Disposed));

        // A fresh review of the same amount under the successor authority succeeds.
        var reviewed = write with { PostingCommand = write.PostingCommand with { IntercompanyFx = current } };
        await using var lease = await fixture.Authority().AcquireAsync([fixture.Context(reviewed)], cancellation.Token);
    }

    [Fact]
    public async Task FunctionalCurrencyCannotDifferFromAuthoritativeBook_EvenWhenAmountsAndRateMatch()
    {
        var fixture = await Fixture.CreateAsync();
        var context = fixture.Context(fixture.Write(functionalCurrency: "CAD"));
        var error = await Assert.ThrowsAsync<LedgerValidationException>(() => fixture.Authority().AcquireAsync([context]));
        Assert.Contains("functional currency differs", error.Message, StringComparison.OrdinalIgnoreCase);
        fixture.Fx.Verify(x => x.ResolveAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.All(fixture.Leases, x => Assert.True(x.Disposed));
    }

    [Theory]
    [InlineData("archived")]
    [InlineData("retyped")]
    [InlineData("symbol")]
    [InlineData("financial-account")]
    public async Task ArchivedRetypedOrScopedAccount_CannotUseApprovedAgreement(string changed)
    {
        var fixture = await Fixture.CreateAsync();
        var node = fixture.Workspace.ChartOfAccounts[0];
        if (changed == "archived")
            fixture.Workspace = fixture.Workspace with { ChartOfAccounts = [node with { IsArchived = true }] };
        if (changed == "retyped")
            fixture.Workspace = fixture.Workspace with { ChartOfAccounts = [node with { AccountType = "Liability" }] };
        var write = fixture.Write(symbol: changed == "symbol" ? "UNAUTHORIZED" : null,
            financialAccount: changed == "financial-account" ? "another-account" : null);
        await Assert.ThrowsAsync<LedgerValidationException>(() => fixture.Authority().AcquireAsync([fixture.Context(write)]));
        Assert.All(fixture.Leases, x => Assert.True(x.Disposed));
    }

    [Fact]
    public async Task ChartRoleWithoutRetainedBinding_RefusesInsteadOfTreatingLineAsOrdinary()
    {
        var fixture = await Fixture.CreateAsync();
        fixture.CurrentBindings = [];
        var error = await Assert.ThrowsAsync<LedgerValidationException>(() => fixture.Authority().AcquireAsync([
            fixture.Context(fixture.Write(includeAgreement: false))]));
        Assert.Contains("binding is missing", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingLegacyAgreement_WarnsLegacyBookButBlocksEnforcedBook()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var fixture = await Fixture.CreateAsync();
        var write = fixture.Write(includeAgreement: false);
        var legacy = fixture.Context(write, LedgerBookControlModeDto.LegacyWarning);
        await using (var lease = await fixture.Authority().AcquireAsync([legacy], cancellation.Token))
            Assert.Contains(legacy.Decisions, x => !x.Allowed && x.Code == "IntercompanyAuthorityMissing");
        await Assert.ThrowsAsync<LedgerValidationException>(() => fixture.Authority().AcquireAsync([fixture.Context(write)], cancellation.Token));
    }

    [Theory]
    [InlineData(ConsolidationService.ReceivableAccount, LedgerAccountType.Asset)]
    [InlineData(ConsolidationService.PayableAccount, LedgerAccountType.Liability)]
    public async Task CanonicalLegacyAccountWithoutAnyBindingOrRoleMetadata_StillRequiresMigration(
        string accountName, LedgerAccountType accountType)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var fixture = await Fixture.CreateAsync();
        fixture.CurrentBindings = [];
        fixture.Workspace = fixture.Workspace with { ChartOfAccounts = [] };
        var original = fixture.Write(includeAgreement: false);
        var oldLine = original.Entry.Lines[0];
        var legacyLine = new LedgerEntry(oldLine.EntryId, oldLine.JournalEntryId, oldLine.Timestamp,
            new(accountName, accountType), oldLine.Debit, oldLine.Credit, oldLine.Description, currency: oldLine.Currency);
        var write = original with
        {
            Entry = new(original.Entry.JournalEntryId, original.Entry.Timestamp,
            original.Entry.Description, [legacyLine, original.Entry.Lines[1]], original.Entry.Metadata)
        };
        Assert.Null(legacyLine.Dimensions);
        var error = await Assert.ThrowsAsync<LedgerValidationException>(() =>
            fixture.Authority().AcquireAsync([fixture.Context(write)], cancellation.Token));
        Assert.Contains("binding is missing", error.Message, StringComparison.OrdinalIgnoreCase);
        var legacy = fixture.Context(write, LedgerBookControlModeDto.LegacyWarning);
        await using (var lease = await fixture.Authority().AcquireAsync([legacy], cancellation.Token))
            Assert.Contains(legacy.Decisions, decision => !decision.Allowed && decision.Code == "IntercompanyAuthorityMissing");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EndedOrReplacedCanonicalAgreement_ExactSameBookSourceInverseAfterCutoff_PreservesNativeAndMappingIdentity(bool novated)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var fixture = await Fixture.CreateAsync();
        var original = fixture.Write();
        fixture.RetainOriginal(original);
        await fixture.EndAgreementAsync(Fixture.Date.AddDays(1), novated);
        var reversal = fixture.Reversal(original, Fixture.Date.AddDays(2));
        var context = fixture.Context(reversal);
        await using (var lease = await fixture.Authority().AcquireAsync([context], cancellation.Token))
        {
            Assert.Contains(context.Decisions, decision => decision.Code == "HistoricalIntercompanyReversal" && decision.Allowed);
            Assert.Contains(context.Decisions, decision => decision.Code == "AgreementVerified" && decision.Allowed);
        }
        fixture.Fx.Verify(x => x.ResolveAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);

        // Retaining the source id alone never grants the historical-date exception.
        var ordinary = reversal with { PostingCommand = reversal.PostingCommand! with { Intent = AccountingPostingIntentDto.Originating } };
        var error = await Assert.ThrowsAsync<LedgerValidationException>(() => fixture.Authority().AcquireAsync([fixture.Context(ordinary)], cancellation.Token));
        Assert.Contains("approved accounting arrangement window", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("missing-source")]
    [InlineData("foreign-book")]
    [InlineData("agreement-revision")]
    [InlineData("transaction")]
    [InlineData("conflicting-source")]
    [InlineData("contra-scope")]
    [InlineData("contra-project")]
    [InlineData("binding-revision")]
    [InlineData("renamed-account")]
    [InlineData("canonical-id")]
    [InlineData("canonical-version")]
    public async Task HistoricalDateException_RefusesChangedEconomicsIdentityOrWrongSource(string changed)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var fixture = await Fixture.CreateAsync();
        var original = fixture.Write();
        fixture.RetainOriginal(original, foreignBook: changed == "foreign-book");
        await fixture.EndAgreementAsync(Fixture.Date.AddDays(1));
        var reversal = fixture.Reversal(original, Fixture.Date.AddDays(2),
            scale: changed == "amount" ? 0.5m : 1m,
            sourceOverride: changed == "missing-source" ? Guid.NewGuid() : null,
            revisionOverride: changed == "agreement-revision" ? Guid.NewGuid().ToString("D") : null,
            transactionOverride: changed == "transaction" ? "different-economic-occurrence" : null);
        if (changed == "conflicting-source")
            reversal = reversal with
            { PostingCommand = reversal.PostingCommand! with { SourceJournalEntryId = Guid.NewGuid() } };
        if (changed is "contra-scope" or "contra-project")
            reversal = reversal with
            {
                Entry = new(reversal.Entry.JournalEntryId,
            reversal.Entry.Timestamp, reversal.Entry.Description, reversal.Entry.Lines.Select(line => new LedgerEntry(line.EntryId,
                line.JournalEntryId, line.Timestamp,
                changed == "contra-scope" && line.Dimensions?.AccountingArrangementId is null
                    ? line.Account with { FinancialAccountId = "another-cash-account" } : line.Account,
                line.Debit, line.Credit, line.Description,
                changed == "contra-project" && line.Dimensions?.AccountingArrangementId is null
                    ? new LedgerLineDimensionSet(ProjectId: "another-project") : line.Dimensions, line.Currency)).ToArray(), reversal.Entry.Metadata)
            };
        if (changed is "binding-revision" or "renamed-account" or "canonical-id" or "canonical-version")
            reversal = reversal with
            {
                Entry = new(reversal.Entry.JournalEntryId, reversal.Entry.Timestamp,
                reversal.Entry.Description, reversal.Entry.Lines.Select(line => new LedgerEntry(line.EntryId,
                    line.JournalEntryId, line.Timestamp,
                    changed == "renamed-account" && line.Dimensions?.AccountingArrangementId is not null
                        ? line.Account with { Name = "Renamed IC receivable" } : line.Account,
                    line.Debit, line.Credit, line.Description,
                    line.Dimensions?.AccountingArrangementId is not null ? line.Dimensions with
                    {
                        AccountBindingRevisionId = changed == "binding-revision" ? Guid.NewGuid().ToString("D") : line.Dimensions.AccountBindingRevisionId,
                        IntercompanyAgreementId = changed == "canonical-id" ? Guid.NewGuid() : line.Dimensions.IntercompanyAgreementId,
                        IntercompanyAgreementVersion = changed == "canonical-version" ? 2 : line.Dimensions.IntercompanyAgreementVersion
                    } : line.Dimensions, line.Currency)).ToArray(), reversal.Entry.Metadata)
            };
        var context = fixture.Context(reversal);
        var error = await Assert.ThrowsAsync<LedgerValidationException>(() => fixture.Authority().AcquireAsync([context], cancellation.Token));
        Assert.Contains("exact economic inverse", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(context.Decisions, decision => !decision.Allowed && decision.Code == "InvalidIntercompanyReversal");
        Assert.DoesNotContain(context.Decisions, decision => decision.Code == "HistoricalIntercompanyReversal");
        Assert.All(fixture.Leases, lease => Assert.True(lease.Disposed));
    }

    [Fact]
    public async Task ValidConsolidationDelegatesToSpecializedGuard_WithoutAcquiringGenericLeases()
    {
        var fixture = await Fixture.CreateAsync();
        var write = fixture.ConsolidationWrite();
        Assert.NotNull(ConsolidationPostingEvidenceGuard.Validate(write));
        var store = new Mock<IIntercompanyAccountingArrangementStore>(MockBehavior.Strict);
        var context = fixture.Context(write);
        await using var lease = await fixture.Authority(store.Object).AcquireAsync([context]);
        Assert.Contains(context.Decisions, x => x.Code == "DelegatedConsolidationAuthority" && x.Allowed);
        fixture.Bindings.Verify(x => x.AcquireAuthorityLeaseAsync(It.IsAny<CancellationToken>()), Times.Never);
        fixture.Fx.Verify(x => x.AcquireAuthorityLeaseAsync(It.IsAny<CancellationToken>()), Times.Never);
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ForgedConsolidationMarkerDoesNotSkipAuthorityValidation()
    {
        var fixture = await Fixture.CreateAsync();
        var original = fixture.ConsolidationWrite();
        var invalid = original with
        {
            Entry = new(original.Entry.JournalEntryId, original.Entry.Timestamp,
            original.Entry.Description, original.Entry.Lines, original.Entry.Metadata with
            { Tags = new Dictionary<string, string> { [ConsolidationService.EvidenceTag] = "{}", [ConsolidationService.DigestTag] = Sha256Digest.ComputeUtf8("{}") } })
        };
        await Assert.ThrowsAsync<LedgerValidationException>(() => fixture.Authority().AcquireAsync([fixture.Context(invalid)]));
        fixture.Bindings.Verify(x => x.AcquireAuthorityLeaseAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class Fixture
    {
        public const string Tenant = "tenant-a", Company = "company-a", Fund = "fund-a";
        public static readonly DateOnly Date = new(2026, 6, 12);
        private static readonly DateTimeOffset Time = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);
        public Guid BookId { get; } = Guid.NewGuid();
        private Guid EntityId { get; } = Guid.NewGuid();
        private Guid OtherEntityId { get; } = Guid.NewGuid();
        private Guid OtherBookId { get; } = Guid.NewGuid();
        private readonly InMemoryIntercompanyAccountingArrangementStore _store = new();
        private IntercompanyAccountingArrangementService _service = null!;
        private ResolvedIntercompanyAccountingArrangementDto _agreement = null!;
        private Mock<IIntercompanyAccountingArrangementLegalAuthority> LegalAuthority { get; set; } = null!;
        public Mock<IIntercompanyAccountBindingStore> Bindings { get; } = new();
        public Mock<IAccountingConfigurationStore> Configuration { get; } = new();
        public Mock<IConsolidationFxAuthority> Fx { get; } = new();
        private Mock<IFundProfileTenancyRegistry> Owners { get; } = new();
        private Mock<ILedgerJournalStore> Journals { get; } = new();
        public List<TrackingLease> Leases { get; } = [];
        public IReadOnlyList<IntercompanyAccountBindingDto> CurrentBindings { get; set; } = [];
        public IReadOnlyList<IntercompanyAccountBindingDto> RetainedBindings { get; private set; } = [];
        public AccountingConfigurationWorkspaceDto Workspace { get; set; } = null!;
        public ConsolidationFxContextDto FxContext { get; private set; } = null!;

        public static async Task<Fixture> CreateAsync()
        {
            var value = new Fixture();
            value._agreement = IntercompanyArrangementTestData.Create(Guid.NewGuid(), Guid.NewGuid(),
                IntercompanyAccountingTemplateKindDto.Loan, value.EntityId, value.OtherEntityId, "EUR",
                [new(value.EntityId, value.BookId, "ar", "ap"), new(value.OtherEntityId, value.OtherBookId, "ar", "ap")],
                Guid.NewGuid(), Time.AddMonths(-1), reference: "EUR accounting arrangement");
            value.LegalAuthority = IntercompanyArrangementTestData.LegalAuthority(value._agreement);
            value._service = new(value._store, new Scope(), value.LegalAuthority.Object);
            var approved = value._agreement.Arrangement;
            var draft = approved with
            {
                Version = 1,
                Status = IntercompanyAccountingArrangementStatusDto.Draft,
                ApprovedBy = null,
                ApprovedAt = null
            };
            await value._store.AppendAsync(Tenant, 0, new(Guid.NewGuid(), new string('a', 64),
                IntercompanyAccountingArrangementActionDto.Create, "owner", Time, null, draft));
            await value._store.AppendAsync(Tenant, 1, new(Guid.NewGuid(), new string('b', 64),
                IntercompanyAccountingArrangementActionDto.Submit, "owner", Time, null,
                draft with { Version = 2, Status = IntercompanyAccountingArrangementStatusDto.Submitted }));
            await value._store.AppendAsync(Tenant, 2, new(Guid.NewGuid(), new string('c', 64),
                IntercompanyAccountingArrangementActionDto.Approve, "owner", Time, null, approved));
            value.CurrentBindings = [new(Guid.NewGuid(), Guid.NewGuid(), 1, value.BookId, "ar", IntercompanyAccountRoleDto.Receivable,
                "Assets:IC receivable", "IC receivable", "Asset", Date.AddMonths(-1), null, [], Tenant, Company, "owner", Time, "Reviewed node role")];
            value.RetainedBindings = value.CurrentBindings;
            value.Workspace = new(Fund, value.BookId, AccountingConfigurationStatusDto.Active, "config-v1", Time, [],
                [new("ar", "Assets:IC receivable", "IC receivable", "Asset", IntercompanyRole: IntercompanyAccountRoleDto.Receivable)],
                [], [], [], [], TenantId: Tenant, CompanyId: Company);
            value.Bindings.Setup(x => x.ListAsync(value.BookId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => value.CurrentBindings);
            value.Bindings.Setup(x => x.HistoryAsync(value.BookId, It.IsAny<CancellationToken>())).ReturnsAsync(() => value.CurrentBindings);
            value.Bindings.Setup(x => x.GetRevisionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => value.RetainedBindings.SingleOrDefault(binding => binding.RevisionId == id));
            value.Bindings.Setup(x => x.AcquireAuthorityLeaseAsync(It.IsAny<CancellationToken>())).Returns(() => value.NewLease());
            value.Configuration.Setup(x => x.GetAsync(Fund, value.BookId, It.IsAny<CancellationToken>(), Tenant, Company)).ReturnsAsync(() => value.Workspace);
            value.Owners.Setup(x => x.ResolveAsync(Fund, It.IsAny<CancellationToken>())).ReturnsAsync(new FundProfileOwnership(Fund, Tenant, Company));
            value.Journals.Setup(x => x.QueryAsync(It.IsAny<LedgerJournalEntryQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<LedgerJournalEntryRecord>());
            value.Fx.Setup(x => x.AcquireAuthorityLeaseAsync(It.IsAny<CancellationToken>())).Returns(() => value.NewLease());
            value.Fx.Setup(x => x.ResolveAsync(value.BookId, Date, It.IsAny<CancellationToken>())).ReturnsAsync(() => value.FxContext);
            var rates = new ConsolidationFxRateSetDto(Guid.NewGuid(), 1, Tenant, Company,
                [new(Guid.NewGuid(), "EUR", "USD", Date, 1.2m, ConsolidationFxRateKindDto.TransactionDate, "retained fixing")],
                "provider fixing", "preparer", "reviewer", Time);
            rates = rates with { ContentHash = ConsolidationFxValidation.Hash(rates) };
            var config = new ConsolidationFxBookConfigurationDto(value.BookId, 1, Tenant, Company, Date.AddMonths(-1), null,
                rates.RateSetId, rates.Revision, new("book-fx", "v1", "USD", 2), "reviewer", Time);
            config = config with { ContentHash = ConsolidationFxValidation.Hash(config) };
            value.FxContext = new(config, rates);
            return value;
        }

        private Task<IAsyncDisposable> NewLease()
        { var lease = new TrackingLease(); Leases.Add(lease); return Task.FromResult<IAsyncDisposable>(lease); }

        public void AdvanceFx(bool rateSet)
        {
            var rates = FxContext.RateSet;
            if (rateSet)
            {
                rates = rates with
                {
                    Revision = rates.Revision + 1,
                    PreviousHash = rates.ContentHash,
                    ContentHash = null,
                    SourceEvidence = "Corrected source provenance; same observed value"
                };
                rates = rates with { ContentHash = ConsolidationFxValidation.Hash(rates) };
            }
            var config = FxContext.Configuration with
            {
                Revision = FxContext.Configuration.Revision + 1,
                PreviousHash = FxContext.Configuration.ContentHash,
                ContentHash = null,
                RateSetRevision = rates.Revision
            };
            config = config with { ContentHash = ConsolidationFxValidation.Hash(config) };
            FxContext = new(config, rates);
        }

        public IntercompanyLedgerPostingAuthority Authority(IIntercompanyAccountingArrangementStore? store = null)
            => new(store ?? _store, _service, Bindings.Object, Configuration.Object, Journals.Object, Owners.Object, fxAuthority: Fx.Object);

        public void RetainOriginal(LedgerJournalEntryWrite original, bool foreignBook = false)
        {
            var sourceBook = foreignBook ? OtherBookId : BookId;
            Journals.Setup(x => x.QueryAsync(It.Is<LedgerJournalEntryQuery>(query => query.LedgerBookId == sourceBook &&
                    query.JournalEntryId == original.Entry.JournalEntryId), It.IsAny<CancellationToken>()))
                .ReturnsAsync([new LedgerJournalEntryRecord(original.Entry, sourceBook, original.PeriodId,
                    original.CommandId, null, 1, Time)]);
        }

        public async Task EndAgreementAsync(DateOnly cutoff, bool novated = false)
        {
            var effective = new DateTimeOffset(cutoff.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var retired = _agreement.Arrangement with
            {
                Version = _agreement.Version + 1,
                Status = IntercompanyAccountingArrangementStatusDto.Retired,
                RetiredAt = effective,
                UpdatedAt = effective
            };
            await _store.AppendAsync(Tenant, _agreement.Version, new(Guid.NewGuid(), new string('d', 64),
                IntercompanyAccountingArrangementActionDto.Retire, "owner", effective, "Retire completed mapping", retired));
            LegalAuthority.Setup(authority => authority.GetEffectiveRevisionAsync(Tenant, _agreement.Arrangement.CanonicalReference,
                    It.Is<DateOnly>(date => date >= cutoff), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("The pinned canonical legal revision is no longer effective."));
            if (novated)
            {
                // A replacement is a separately approved native agreement; the historical source
                // keeps the original canonical identity and cannot be relabeled to the successor.
                var successor = IntercompanyArrangementTestData.Create(Guid.NewGuid(), _agreement.OrganizationId,
                    _agreement.TemplateKind, EntityId, OtherEntityId, _agreement.Currency, _agreement.PartyMappings,
                    Guid.NewGuid(), effective, canonicalId: Guid.NewGuid());
                Assert.NotEqual(_agreement.CanonicalAgreementId, successor.CanonicalAgreementId);
                LegalAuthority.Setup(authority => authority.ListEffectiveRevisionsAsync(Tenant, EntityId,
                    It.Is<DateOnly>(date => date >= cutoff), It.IsAny<CancellationToken>())).ReturnsAsync([successor.LegalRevision]);
            }
        }

        public LedgerJournalEntryWrite Reversal(LedgerJournalEntryWrite original, DateOnly effectiveDate,
            decimal scale = 1m, Guid? sourceOverride = null, string? revisionOverride = null, string? transactionOverride = null)
        {
            var id = Guid.NewGuid();
            var time = new DateTimeOffset(effectiveDate.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
            const string description = "Reverse intercompany draw";
            var lines = original.Entry.Lines.Select(line => new LedgerEntry(Guid.NewGuid(), id, time, line.Account,
                line.Credit * scale, line.Debit * scale, description,
                line.Dimensions is { } dimensions ? dimensions with
                {
                    AccountingArrangementRevisionId = revisionOverride ?? dimensions.AccountingArrangementRevisionId,
                    IntercompanyTransactionId = transactionOverride ?? dimensions.IntercompanyTransactionId
                } : null,
                line.Currency is { } currency ? new(currency.TransactionCurrency, currency.FunctionalCurrency,
                    currency.TransactionCredit * scale, currency.TransactionDebit * scale, currency.FxRateToFunctional) : null)).ToArray();
            var sourceId = sourceOverride ?? original.Entry.JournalEntryId;
            var command = original.PostingCommand! with
            {
                CommandId = Guid.NewGuid(),
                EffectiveDate = effectiveDate,
                PostingDate = time,
                Intent = AccountingPostingIntentDto.Reversal,
                SourceJournalEntryId = sourceId,
                IntercompanyFx = null
            };
            return original with
            {
                Entry = new(id, time, description, lines, new(EffectiveDate: effectiveDate)),
                CommandId = command.CommandId,
                PostingCommand = command,
                SourceJournalEntryId = sourceId
            };
        }

        public LedgerPostingAuthorityContext Context(LedgerJournalEntryWrite write, LedgerBookControlModeDto mode = LedgerBookControlModeDto.Enforced)
            => new(write, new(BookId, Fund, EntityId, FundStructureNodeKindDto.Entity, "Entity USD book", "USD", Time, Time),
                new(BookId, mode, 1, "entity-intercompany-v1", Time), Tenant)
            { VerifiedCanonicalAgreementRevisions = [_agreement.LegalRevision] };

        public LedgerJournalEntryWrite Write(bool includeAgreement = true, string functionalCurrency = "USD", string? symbol = null, string? financialAccount = null)
        {
            var id = Guid.NewGuid();
            var period = Guid.NewGuid();
            var binding = CurrentBindings.FirstOrDefault();
            var dimensions = new LedgerLineDimensionSet(EntityId: EntityId.ToString("D"), CounterpartyId: OtherEntityId.ToString("D"))
            {
                AccountingArrangementId = includeAgreement ? _agreement.ArrangementId.ToString("D") : null,
                AccountingArrangementRevisionId = includeAgreement ? _agreement.RevisionId.ToString("D") : null,
                IntercompanyAgreementId = includeAgreement ? _agreement.CanonicalAgreementId : null,
                IntercompanyAgreementVersion = includeAgreement ? _agreement.CanonicalAgreementVersion : null,
                IntercompanyTransactionId = includeAgreement ? "economic-occurrence-one" : null,
                ChartAccountNodeId = "ar",
                AccountBindingRevisionId = binding?.RevisionId.ToString("D")
            };
            var entry = new JournalEntry(id, Time, "Intercompany draw", [
                new(Guid.NewGuid(), id, Time, new("IC receivable", LedgerAccountType.Asset, symbol, financialAccount), 120m, 0m,
                    "Intercompany draw", dimensions, new("EUR", functionalCurrency, 100m, 0m, 1.2m)),
                new(Guid.NewGuid(), id, Time, new("Cash", LedgerAccountType.Asset), 0m, 120m, "Intercompany draw", currency: new("EUR", "USD", 0m, 100m, 1.2m))],
                new(EffectiveDate: Date));
            var command = new AccountingPostingCommandDto(Guid.NewGuid(), BookId, period, Date, Time, "intercompany-draw",
                ApprovalState: AccountingPostingApprovalStateDto.Approved, ApprovalId: "owner-review", LedgerBookId: BookId)
            { Actor = "poster", IntercompanyFx = ConsolidationFxTranslator.ConvertAmount(100m, "EUR", FxContext, Date) };
            return new(entry, BookId, period, command.CommandId, PostingCommand: command, LedgerBookId: BookId);
        }

        public LedgerJournalEntryWrite ConsolidationWrite()
        {
            var id = Guid.NewGuid();
            var period = Guid.NewGuid();
            var lines = new LedgerEntry[] {
                new(Guid.NewGuid(), id, Time, new("Assets:Intercompany Receivable", LedgerAccountType.Asset), 100m, 0m, "Elimination"),
                new(Guid.NewGuid(), id, Time, new("Liabilities:Intercompany Payable", LedgerAccountType.Liability), 0m, 100m, "Elimination") };
            var evidence = new ConsolidationEvidenceDto(new(Guid.NewGuid(), Guid.NewGuid(), BookId, period, Date), "scope", "source",
                [new(Guid.NewGuid(), 0, 0), new(Guid.NewGuid(), 0, 0), new(BookId, 0, 0)], "perimeter", "w10-v1", [],
                [new("ar", AccountingTemplateLineSideDto.Debit, 100m, "USD", lines[0].Account.Name),
                 new("ap", AccountingTemplateLineSideDto.Credit, 100m, "USD", lines[1].Account.Name)]);
            var json = JsonSerializer.Serialize(evidence, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var entry = new JournalEntry(id, Time, "Elimination", lines, new(EffectiveDate: Date,
                Tags: new Dictionary<string, string> { [ConsolidationService.EvidenceTag] = json, [ConsolidationService.DigestTag] = Sha256Digest.ComputeUtf8(json) }));
            var command = new AccountingPostingCommandDto(Guid.NewGuid(), BookId, period, Date, Time, "consolidation",
                ApprovalState: AccountingPostingApprovalStateDto.Approved, ApprovalId: "consolidation-review", LedgerBookId: BookId)
            {
                Actor = "poster",
                BookContext = new(BookId, Fund, EntityId, FundStructureNodeKindDto.Entity, "Elimination book", "USD",
                AccountingBasisKindDto.Primary, "consolidation", "w10-v1")
            };
            return new(entry, BookId, period, command.CommandId, RuleId: ConsolidationService.RuleId, RuleVersion: "w10-v1",
                PostingCommand: command, LedgerBookId: BookId);
        }
    }

    private sealed class TrackingLease : IAsyncDisposable
    { public bool Disposed { get; private set; } public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; } }
    private sealed class Scope : IIntercompanyAccountingArrangementScopeAuthority
    {
        public Task<IIntercompanyAccountingArrangementScopeLease> AcquireLeaseAsync(string tenantId,
            ResolvedIntercompanyAccountingArrangementDto agreement, CancellationToken ct = default)
            => Task.FromResult<IIntercompanyAccountingArrangementScopeLease>(new ScopeLease());
        public Task ValidateAsync(string tenantId, ResolvedIntercompanyAccountingArrangementDto agreement, bool requireActive,
            CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<IntercompanyEntityLifecycleReferenceDto>> GetLifecycleEvidenceAsync(string tenantId,
            ResolvedIntercompanyAccountingArrangementDto agreement, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IntercompanyEntityLifecycleReferenceDto>>([new(agreement.PartyOneEntityId, 1, new string('a', 64)), new(agreement.PartyTwoEntityId, 1, new string('b', 64))]);
    }
    private sealed class ScopeLease : IIntercompanyAccountingArrangementScopeLease
    {
        public LegalEntityReadAuthorityProof? StructureAuthorityProof => null;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
