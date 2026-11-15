using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class AutomatedPostingGrantTests
{
    private const string TenantId = "synthetic-tenant";
    private const string AccountId = "synthetic-broker-account";
    private static readonly DateTimeOffset Timestamp = new(2026, 5, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SystemPosting_TypedScopeRetainsAuthorityAndSimulatedProvenance()
    {
        var write = BuildWrite();

        var normalized = AccountingPostingCommandValidator.NormalizeAndValidate(write, true, true);

        normalized.PostingCommand!.ApprovalState.Should().Be(AccountingPostingApprovalStateDto.NotRequired);
        normalized.PostingCommand.AutomationGrant.Should().Be(write.PostingCommand!.AutomationGrant);
        normalized.Entry.Metadata.Tags!["dataProvenance"].Should().Be("SIMULATED");
        normalized.Entry.Metadata.Tags["postingActor"].Should().Be("synthetic-fill-accounting");
    }

    [Fact]
    public void SystemPosting_AccountIdentityUsesTheLedgerCaseInsensitiveScope()
    {
        var write = WithCommand(BuildWrite(), command => command with
        {
            AutomationGrant = command.AutomationGrant! with { FinancialAccountId = AccountId.ToUpperInvariant() }
        });

        var normalized = AccountingPostingCommandValidator.NormalizeAndValidate(write, true, true);

        normalized.Entry.Lines.Should().OnlyContain(line => string.Equals(line.Account.FinancialAccountId,
            normalized.PostingCommand!.AutomationGrant!.FinancialAccountId, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(OperationsActionOriginDto.AutomationSuggestion)]
    [InlineData(OperationsActionOriginDto.AssistantDraft)]
    [InlineData(OperationsActionOriginDto.AutomationAssistant)]
    public void AssistantOrigins_CannotUseAnAutomationGrant(OperationsActionOriginDto origin)
    {
        var write = WithCommand(BuildWrite(), command => command with { ActionOrigin = origin });

        Action post = () => AccountingPostingCommandValidator.NormalizeAndValidate(write, true, true);

        post.Should().Throw<LedgerValidationException>().WithMessage("*human-operator action origin*");
    }

    [Fact]
    public void HumanPosting_CannotBorrowSystemGrantAuthority()
    {
        var write = WithCommand(BuildWrite(), command => command with { ActionOrigin = OperationsActionOriginDto.HumanOperator });

        Action post = () => AccountingPostingCommandValidator.NormalizeAndValidate(write, true, true);

        post.Should().Throw<LedgerValidationException>().WithMessage("*cannot claim automated grant authority*");
    }

    [Fact]
    public void SystemPosting_MissingGrantFailsBeforePersistence()
    {
        var write = WithCommand(BuildWrite(), command => command with { AutomationGrant = null });

        Action post = () => AccountingPostingCommandValidator.NormalizeAndValidate(write, true, true);

        post.Should().Throw<LedgerValidationException>().WithMessage("*typed automated posting grant*");
    }

    [Fact]
    public void SystemPosting_RefusesBroaderIntentSourceOrReviewClaims()
    {
        var original = BuildWrite();
        var mutations = new (string Name, Func<AccountingPostingCommandDto, AccountingPostingCommandDto> Change)[]
        {
            ("real data", command => command with { Provenance = DataProvenance.Real }),
            ("live grant", command => command with { AutomationGrant = command.AutomationGrant! with { SourceMode = "Live" } }),
            ("reversal", command => command with { Intent = AccountingPostingIntentDto.Reversal }),
            ("adjustment", command => command with { Intent = AccountingPostingIntentDto.Adjustment }),
            ("unrelated source", command => command with { SourceEventType = "loan.drawdown-booked" }),
            ("human approval", command => command with { ApprovalState = AccountingPostingApprovalStateDto.Approved }),
            ("approval identity", command => command with { ApprovalId = "borrowed-human-approval" }),
            ("borrowed controlled-close authority", command => command with { ControlledCloseAuthorizationId = Guid.NewGuid() }),
            ("unattributed actor", command => command with { Actor = null }),
            ("other book", command => command with { AutomationGrant = command.AutomationGrant! with { LedgerBookId = Guid.NewGuid() } }),
            ("other period", command => command with { AutomationGrant = command.AutomationGrant! with { PeriodId = Guid.NewGuid() } }),
            ("other account", command => command with { AutomationGrant = command.AutomationGrant! with { FinancialAccountId = "other-account" } }),
            ("other policy", command => command with { AutomationGrant = command.AutomationGrant! with { PolicyId = "other-policy" } }),
            ("missing mapping", command => command with { AutomationGrant = command.AutomationGrant! with { MappingRevision = "" } })
        };

        foreach (var mutation in mutations)
        {
            var write = WithCommand(original, mutation.Change);
            Action post = () => AccountingPostingCommandValidator.NormalizeAndValidate(write, true, true);
            post.Should().Throw<LedgerValidationException>(mutation.Name).WithMessage("*grant scope*");
        }
    }

    [Fact]
    public void SystemPosting_RefusesCurrencyBlindForeignAndOtherAccountLines()
    {
        var original = BuildWrite();
        var mutations = new (string Name, LedgerAccount Account, LedgerEntryCurrency? Currency)[]
        {
            ("currency blind", original.Entry.Lines[0].Account, null),
            ("foreign currency", original.Entry.Lines[0].Account, new LedgerEntryCurrency("EUR", "USD", 100m, 0m, 1m)),
            ("other account", LedgerAccounts.Securities("AAPL", "other-account"), new LedgerEntryCurrency("USD", "USD", 100m, 0m, 1m)),
            ("short liability", new LedgerAccount("Short Proceeds", LedgerAccountType.Liability, "AAPL", AccountId), new LedgerEntryCurrency("USD", "USD", 100m, 0m, 1m))
        };

        foreach (var mutation in mutations)
        {
            var first = original.Entry.Lines[0];
            var lines = original.Entry.Lines.ToArray();
            lines[0] = new LedgerEntry(first.EntryId, first.JournalEntryId, first.Timestamp,
                mutation.Account, first.Debit, first.Credit, first.Description, first.Dimensions, mutation.Currency);
            var write = original with
            {
                Entry = new JournalEntry(original.Entry.JournalEntryId,
                original.Entry.Timestamp, original.Entry.Description, lines, original.Entry.Metadata)
            };
            Action post = () => AccountingPostingCommandValidator.NormalizeAndValidate(write, true, true);
            post.Should().Throw<LedgerValidationException>(mutation.Name).WithMessage("*USD long cash-equity*");
        }
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task Activate_RetainsExactTenantScopeAcrossRestartAndRefusesRebinding()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var scope = await CreateScopeAsync(database);
        var request = Activation(scope.Reference);
        var activated = await ActivateAsync(database.JournalStore, request, "operator", TenantId);

        var duplicate = await ActivateAsync(database.JournalStore, request, "operator", TenantId);
        var restarted = new PostgresLedgerJournalStore(database.Options);
        var retained = await restarted.GetAsync(activated.Reference.GrantId, TenantId);

        duplicate.Should().BeEquivalentTo(activated);
        retained.Should().BeEquivalentTo(activated);
        (await restarted.GetAsync(activated.Reference.GrantId, "other-tenant")).Should().BeNull();
        Func<Task> rebind = () => ActivateAsync(database.JournalStore,
            request with { FinancialAccountId = "other-account" }, "operator", TenantId);
        await rebind.Should().ThrowAsync<LedgerValidationException>().WithMessage("*cannot be reactivated or rebound*");
        Func<Task> otherTenant = () => ActivateAsync(database.JournalStore,
            request with { GrantId = Guid.NewGuid() }, "operator", "other-tenant");
        await otherTenant.Should().ThrowAsync<LedgerValidationException>().WithMessage("*tenant-owned open USD book*");
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task Revoke_BlocksNewAppendButRetainedExactReplayStillSucceeds()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var scope = await CreateScopeAsync(database);
        var grant = await ActivateAsync(database.JournalStore, Activation(scope.Reference), "operator", TenantId);
        var write = CommissionWrite(grant.Reference, scope.Period.Version);
        using var target = new DurableLedgerPostingTarget(database.JournalStore);
        (await target.PostAsync(write)).WasAppended.Should().BeTrue();

        var revoked = await database.JournalStore.RevokeAsync(grant.Reference.GrantId,
            grant.Reference.Version, "revoking-operator", TenantId);
        revoked.IsActive.Should().BeFalse();
        revoked.Reference.Version.Should().Be(grant.Reference.Version + 1);
        revoked.RevokedBy.Should().Be("revoking-operator");
        var replay = await target.PostAsync(write);
        replay.WasAppended.Should().BeFalse();
        replay.JournalEntryId.Should().Be(write.Entry.JournalEntryId);

        Func<Task> newPost = () => target.PostAsync(CommissionWrite(grant.Reference, scope.Period.Version));
        await newPost.Should().ThrowAsync<LedgerValidationException>().WithMessage("*missing, revoked, stale*");
        (await database.JournalStore.GetByPeriodAsync(scope.Period.PeriodId)).Should().ContainSingle();
        Func<Task> staleRevoke = () => database.JournalStore.RevokeAsync(grant.Reference.GrantId,
            grant.Reference.Version, "operator", TenantId);
        await staleRevoke.Should().ThrowAsync<LedgerValidationException>().WithMessage("*version has changed*");
        Func<Task> reactivate = () => ActivateAsync(database.JournalStore, Activation(grant.Reference), "operator", TenantId);
        await reactivate.Should().ThrowAsync<LedgerValidationException>().WithMessage("*cannot be reactivated or rebound*");
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task Append_RequiresCurrentRetainedMappingAndExactGrantVersion()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var scope = await CreateScopeAsync(database);
        var grant = await ActivateAsync(database.JournalStore, Activation(scope.Reference), "operator", TenantId);
        Func<Task> forgedVersion = () => database.JournalStore.AppendAsync(CommissionWrite(
            grant.Reference with { Version = grant.Reference.Version + 1 }, scope.Period.Version));
        await forgedVersion.Should().ThrowAsync<LedgerValidationException>().WithMessage("*missing, revoked, stale*");

        await database.AccountingConfigurationStore.SaveAsync(scope.Workspace with { ConfigurationVersion = "mapping-v2" });
        Func<Task> staleMapping = () => database.JournalStore.AppendAsync(CommissionWrite(grant.Reference, scope.Period.Version));
        await staleMapping.Should().ThrowAsync<LedgerValidationException>().WithMessage("*current retained accounting mapping revision*");
        (await database.JournalStore.GetByPeriodAsync(scope.Period.PeriodId)).Should().BeEmpty();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task Append_RefusesChangedChartContentEvenWhenDisplayRevisionIsUnchanged()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var scope = await CreateScopeAsync(database);
        var grant = await ActivateAsync(database.JournalStore, Activation(scope.Reference), "operator", TenantId);
        await database.AccountingConfigurationStore.SaveAsync(scope.Workspace with
        {
            ChartOfAccounts = [new ChartOfAccountsNodeDto("changed-node", "Expenses:Commission", "Changed commission mapping", "Expense", FinancialAccountId: AccountId)]
        });

        Func<Task> post = () => database.JournalStore.AppendAsync(CommissionWrite(grant.Reference, scope.Period.Version));

        await post.Should().ThrowAsync<LedgerValidationException>().WithMessage("*mapping*changed*");
        (await database.JournalStore.GetByPeriodAsync(scope.Period.PeriodId)).Should().BeEmpty();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task Activate_RefusesCompanySpoofEvenWhenSpoofedConfigurationExists()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var scope = await CreateScopeAsync(database, "owning-company");
        await database.AccountingConfigurationStore.SaveAsync(scope.Workspace with { CompanyId = "spoofed-company" });
        var request = Activation(scope.Reference) with { CompanyId = "spoofed-company" };

        Func<Task> activate = () => ActivateAsync(database.JournalStore, request, "operator", TenantId);

        await activate.Should().ThrowAsync<LedgerValidationException>().WithMessage("*tenant-owned open USD book*");
        (await database.JournalStore.GetAsync(request.GrantId, TenantId)).Should().BeNull();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task GrantLifecycle_RetainsActivationAndRevocationActorInAuditChain()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var scope = await CreateScopeAsync(database);
        var grant = await ActivateAsync(database.JournalStore, Activation(scope.Reference), "activating-operator", TenantId);
        await database.JournalStore.RevokeAsync(grant.Reference.GrantId, grant.Reference.Version, "revoking-operator", TenantId);
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"select action, actor from \"{database.Options.SchemaName}\".ledger_event_audit_events where subject_kind = 'automation-grant' and subject_id = @id order by chain_sequence";
        command.Parameters.AddWithValue("id", grant.Reference.GrantId);
        var actions = new List<(string Action, string Actor)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            actions.Add((reader.GetString(0), reader.GetString(1)));

        actions.Should().Equal(("AutomationGrantActivated", "activating-operator"), ("AutomationGrantRevoked", "revoking-operator"));
    }

    [Fact]
    public async Task Activation_RejectsAssistantBeforeDatabaseAccess()
    {
        var store = new PostgresLedgerJournalStore(new LedgerJournalStoreOptions());
        var request = Activation(BuildWrite().PostingCommand!.AutomationGrant!) with
        {
            ActionOrigin = OperationsActionOriginDto.AutomationAssistant
        };

        Func<Task> activate = () => store.ActivateAsync(request, "assistant", TenantId);

        await activate.Should().ThrowAsync<HumanOperatorRequiredException>();
    }

    private static Task<AutomatedPostingGrantDto> ActivateAsync(PostgresLedgerJournalStore store,
        ActivateAutomatedPostingGrantRequest request, string actor, string tenantId)
        => store.ActivateWithPolicyAsync(request,
            new AccountingPolicyDto(request.PolicyId, AccountingBasisKindDto.Primary, request.PolicyVersion,
                "Synthetic automatic fill policy", new DateOnly(2026, 1, 1), null, false, "{}", Timestamp,
                FundProfileId: "synthetic-fill-fund",
                RulePack: new AccountingPolicyRulePackDto("synthetic-fill-rules", "1",
                    new[] { "execution.trade-fill", "execution.trade-commission", "execution.trade-fee" }
                        .Select(source => new AccountingPolicyRuleDto(source, AccountingTreatmentKindDto.General,
                            "1", source, RequiresEvidence: true, RequiresApproval: false, AllowsAutoPosting: true)).ToArray())),
            actor, tenantId);

    private static ActivateAutomatedPostingGrantRequest Activation(AutomatedPostingGrantReferenceDto reference)
        => new(reference.GrantId, reference.FinancialAccountId, reference.LedgerBookId, reference.PeriodId,
            reference.PolicyId, reference.PolicyVersion, reference.MappingRevision, reference.SourceMode, reference.CompanyId);

    private static async Task<(AutomatedPostingGrantReferenceDto Reference, LedgerAccountingPeriod Period,
        AccountingConfigurationWorkspaceDto Workspace)> CreateScopeAsync(LedgerPostgresTestDatabase database, string? companyId = null)
    {
        const string fund = "synthetic-fill-fund";
        await new PostgresFundProfileTenancyRegistry(database.Options).BindAsync(fund, TenantId, companyId);
        var book = new LedgerBookRecord(Guid.NewGuid(), fund, Guid.NewGuid(), FundStructureNodeKindDto.Fund,
            "Synthetic fill accounting", "USD", Timestamp, Timestamp,
            AccountingPolicyId: "synthetic-fill-policy", AccountingPolicyVersion: "v1");
        await database.JournalStore.SaveLedgerBookAsync(book);
        var period = await database.JournalStore.SavePeriodAsync(new LedgerAccountingPeriod(Guid.NewGuid(),
            book.LedgerBookId, 2026, 5, "Synthetic May", new(2026, 5, 1), new(2026, 5, 31),
            "Open", Timestamp, null, 0), 0);
        var workspace = new AccountingConfigurationWorkspaceDto(fund, book.LedgerBookId,
            AccountingConfigurationStatusDto.Active, "mapping-v1", Timestamp, [], [], [], [], [], [], TenantId: TenantId, CompanyId: companyId);
        await database.AccountingConfigurationStore.SaveAsync(workspace);
        return (new AutomatedPostingGrantReferenceDto(Guid.NewGuid(), 1, TenantId, AccountId,
            book.LedgerBookId, period.PeriodId, book.AccountingPolicyId, book.AccountingPolicyVersion,
            workspace.ConfigurationVersion, "Synthetic", companyId), period, workspace);
    }

    private static LedgerJournalEntryWrite CommissionWrite(AutomatedPostingGrantReferenceDto reference, long periodVersion)
    {
        var write = BuildWrite(reference);
        var first = write.Entry.Lines[0];
        var lines = write.Entry.Lines.ToArray();
        lines[0] = new LedgerEntry(first.EntryId, first.JournalEntryId, first.Timestamp,
            LedgerAccounts.CommissionExpenseFor(AccountId), first.Debit, first.Credit, first.Description, currency: first.Currency);
        return write with
        {
            Entry = new JournalEntry(write.Entry.JournalEntryId, write.Entry.Timestamp, write.Entry.Description,
                lines, write.Entry.Metadata with { ActivityType = "trade-commission", Symbol = null }),
            RuleId = "execution.trade-commission",
            PostingCommand = write.PostingCommand! with
            {
                SourceEventType = "execution.trade-commission",
                ExpectedVersion = periodVersion
            }
        };
    }

    private static LedgerJournalEntryWrite WithCommand(LedgerJournalEntryWrite write,
        Func<AccountingPostingCommandDto, AccountingPostingCommandDto> change)
        => write with { PostingCommand = change(write.PostingCommand!) };

    private static LedgerJournalEntryWrite BuildWrite(AutomatedPostingGrantReferenceDto? reference = null)
    {
        reference ??= new AutomatedPostingGrantReferenceDto(Guid.NewGuid(), 1, TenantId, AccountId,
            Guid.NewGuid(), Guid.NewGuid(), "synthetic-fill-policy", "v1", "mapping-v1", "Synthetic");
        var journalId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var key = $"synthetic-fill:{eventId:D}";
        const string description = "Synthetic buy 1 AAPL";
        var journal = new JournalEntry(journalId, Timestamp, description,
        [
            new LedgerEntry(Guid.NewGuid(), journalId, Timestamp, LedgerAccounts.Securities("AAPL", AccountId),
                100m, 0m, description, currency: new LedgerEntryCurrency("USD", "USD", 100m, 0m, 1m)),
            new LedgerEntry(Guid.NewGuid(), journalId, Timestamp, LedgerAccounts.CashAccount(AccountId),
                0m, 100m, description, currency: new LedgerEntryCurrency("USD", "USD", 0m, 100m, 1m))
        ], new JournalEntryMetadata(ActivityType: "trade-fill", Symbol: "AAPL", FinancialAccountId: AccountId,
            LedgerBook: reference.LedgerBookId.ToString("D"), IdempotencyKey: key));
        var command = new AccountingPostingCommandDto(commandId, reference.LedgerBookId, reference.PeriodId,
            DateOnly.FromDateTime(Timestamp.UtcDateTime), Timestamp, key,
            SourceEventId: eventId, ExpectedVersion: 1, SourceEventType: "execution.trade-fill",
            ApprovalState: AccountingPostingApprovalStateDto.NotRequired,
            Evidence: [new AccountingPostingEvidenceReferenceDto("synthetic-fill", "evidence://synthetic/fill",
                AccountingPostingEvidenceKindDto.Source, "Synthetic", Timestamp, "synthetic-fill-accounting")],
            ActionOrigin: OperationsActionOriginDto.AuthorizedSystem, LedgerBookId: reference.LedgerBookId)
        {
            Actor = "synthetic-fill-accounting",
            AutomationGrant = reference,
            Provenance = DataProvenance.Simulated
        };
        return new LedgerJournalEntryWrite(journal, reference.LedgerBookId, reference.PeriodId,
            CommandId: commandId, AccountingPolicyId: reference.PolicyId, AccountingPolicyVersion: reference.PolicyVersion,
            RuleId: "execution.trade-fill", RuleVersion: "1", SourceEventId: eventId,
            PostingCommand: command, LedgerBookId: reference.LedgerBookId);
    }
}
