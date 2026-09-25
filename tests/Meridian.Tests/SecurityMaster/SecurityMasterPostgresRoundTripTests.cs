using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Integration")]
public sealed class SecurityMasterPostgresRoundTripTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private readonly SecurityMasterDatabaseFixture _fixture;

    public SecurityMasterPostgresRoundTripTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SecurityMasterDatabaseFact]
    public async Task CreateAmendDeactivate_RoundTripsAgainstPostgres()
    {
        var eventStore = new PostgresSecurityMasterEventStore(_fixture.Options, NullLogger<PostgresSecurityMasterEventStore>.Instance);
        var snapshotStore = new PostgresSecurityMasterSnapshotStore(_fixture.Options);
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);
        var service = new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            rebuilder,
            _fixture.Options,
            NullLogger<SecurityMasterService>.Instance);
        var securityId = Guid.NewGuid();

        var created = await service.CreateAsync(new CreateSecurityRequest(
            securityId,
            "Equity",
            JsonSerializer.SerializeToElement(new
            {
                displayName = "Acme Common",
                currency = "USD",
                countryOfRisk = "US",
                issuerName = "Acme Corp",
                exchange = "XNYS",
                lotSize = 1,
                tickSize = 0.01m
            }),
            JsonSerializer.SerializeToElement(new
            {
                shareClass = "Common"
            }),
            new[]
            {
                new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "ACME", true, DateTimeOffset.UtcNow.AddDays(-1), null, null),
                new SecurityIdentifierDto(SecurityIdentifierKind.Isin, "US0000000001", false, DateTimeOffset.UtcNow.AddDays(-1), null, null)
            },
            DateTimeOffset.UtcNow,
            "test",
            "codex",
            null,
            "initial create"));

        created.Version.Should().Be(1);
        created.Status.Should().Be(SecurityStatusDto.Active);

        var amended = await service.AmendTermsAsync(new AmendSecurityTermsRequest(
            securityId,
            1,
            JsonSerializer.SerializeToElement(new
            {
                displayName = "Acme Common Updated",
                currency = "USD",
                countryOfRisk = "US",
                issuerName = "Acme Corp",
                exchange = "XNAS",
                lotSize = 1,
                tickSize = 0.01m
            }),
            null,
            Array.Empty<SecurityIdentifierDto>(),
            Array.Empty<SecurityIdentifierDto>(),
            DateTimeOffset.UtcNow.AddMinutes(1),
            "test",
            "codex",
            null,
            "rename"));

        amended.Version.Should().Be(2);
        amended.DisplayName.Should().Be("Acme Common Updated");

        await service.DeactivateAsync(new DeactivateSecurityRequest(
            securityId,
            2,
            DateTimeOffset.UtcNow.AddMinutes(2),
            "test",
            "codex",
            null,
            "deactivate"));

        var detail = await store.GetDetailAsync(securityId);
        detail.Should().NotBeNull();
        detail!.Status.Should().Be(SecurityStatusDto.Inactive);
        detail.Version.Should().Be(3);

        var history = await eventStore.LoadAsync(securityId);
        history.Select(evt => evt.EventType).Should().ContainInOrder("SecurityCreated", "TermsAmended", "SecurityDeactivated");

        var resolved = await store.GetByIdentifierAsync(
            SecurityIdentifierKind.Ticker,
            "ACME",
            null,
            DateTimeOffset.UtcNow,
            includeInactive: true);

        resolved.Should().NotBeNull();
        resolved!.SecurityId.Should().Be(securityId);
    }

    [SecurityMasterDatabaseFact]
    public async Task GetByIdentifierAsync_ResolvesNormalizedIdentifierAndProviderValues()
    {
        var eventStore = new PostgresSecurityMasterEventStore(_fixture.Options, NullLogger<PostgresSecurityMasterEventStore>.Instance);
        var snapshotStore = new PostgresSecurityMasterSnapshotStore(_fixture.Options);
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);
        var service = new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            rebuilder,
            _fixture.Options,
            NullLogger<SecurityMasterService>.Instance);
        var securityId = Guid.NewGuid();
        var effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1);

        await service.CreateAsync(new CreateSecurityRequest(
            securityId,
            "Equity",
            JsonSerializer.SerializeToElement(new
            {
                displayName = "Apple Inc.",
                currency = "USD",
                countryOfRisk = "US",
                issuerName = "Apple Inc.",
                exchange = "XNAS",
                lotSize = 1,
                tickSize = 0.01m
            }),
            JsonSerializer.SerializeToElement(new
            {
                shareClass = "Common"
            }),
            new[]
            {
                new SecurityIdentifierDto(SecurityIdentifierKind.Isin, "us-0378331005", true, effectiveFrom, null, "xnas")
            },
            effectiveFrom,
            "test",
            "codex",
            null,
            "create with raw vendor identifier"));

        await store.UpsertAliasAsync(new SecurityAliasDto(
            Guid.NewGuid(),
            securityId,
            SecurityIdentifierKind.Ric.ToString(),
            " aapl.o ",
            "refinitiv",
            SecurityAliasScope.Collector,
            "vendor alias",
            "codex",
            DateTimeOffset.UtcNow,
            effectiveFrom,
            null,
            true));

        var resolvedByIdentifier = await store.GetByIdentifierAsync(
            SecurityIdentifierKind.Isin,
            "US0378331005",
            "XNAS",
            DateTimeOffset.UtcNow,
            includeInactive: false);
        var resolvedByAlias = await store.GetByIdentifierAsync(
            SecurityIdentifierKind.Ric,
            "AAPL.O",
            "REFINITIV",
            DateTimeOffset.UtcNow,
            includeInactive: false);
        var resolvedWithoutProvider = await store.GetByIdentifierAsync(
            SecurityIdentifierKind.Isin,
            "US0378331005",
            null,
            DateTimeOffset.UtcNow,
            includeInactive: false);
        var resolvedWithWrongProvider = await store.GetByIdentifierAsync(
            SecurityIdentifierKind.Isin,
            "US0378331005",
            "BLOOMBERG",
            DateTimeOffset.UtcNow,
            includeInactive: false);

        resolvedByIdentifier.Should().NotBeNull();
        resolvedByIdentifier!.SecurityId.Should().Be(securityId);
        resolvedByIdentifier.Identifiers.Should().ContainSingle(identifier =>
            identifier.NormalizedValue == "US0378331005" &&
            identifier.NormalizedProvider == "XNAS");

        resolvedByAlias.Should().NotBeNull();
        resolvedByAlias!.SecurityId.Should().Be(securityId);

        resolvedWithoutProvider.Should().BeNull();
        resolvedWithWrongProvider.Should().BeNull();
    }

    [SecurityMasterDatabaseFact]
    public async Task EquityProjection_CustomClassification_ShouldPreserveRawOtherLabel()
    {
        var securityId = Guid.NewGuid();
        var effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1);
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        await store.UpsertProjectionAsync(new SecurityProjectionRecord(
            SecurityId: securityId,
            AssetClass: "Equity",
            Status: SecurityStatusDto.Active,
            DisplayName: "Acme Tracking Stock",
            Currency: "USD",
            PrimaryIdentifierKind: "Ticker",
            PrimaryIdentifierValue: "ACMTS",
            CommonTerms: JsonSerializer.SerializeToElement(new
            {
                currency = "USD",
                exchange = "XNAS",
                countryOfRisk = "US",
                issuerName = "Acme Corp"
            }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new
            {
                schemaVersion = 1,
                shareClass = "Class T",
                classification = "Other",
                otherClassification = "TrackingStock"
            }),
            Provenance: JsonSerializer.SerializeToElement(new { sourceSystem = "test" }),
            Version: 1,
            EffectiveFrom: effectiveFrom,
            EffectiveTo: null,
            Identifiers:
            [
                new SecurityIdentifierDto(
                    SecurityIdentifierKind.Ticker,
                    "ACMTS",
                    true,
                    effectiveFrom)
            ],
            Aliases: []));

        var equityStore = new PostgresEquityReferenceProjectionStore(_fixture.Options);
        var projected = await equityStore.GetEquityAsync(securityId);

        projected.Should().NotBeNull();
        projected!.Classification.Should().Be("TrackingStock");
    }

    [SecurityMasterDatabaseFact]
    public async Task AppendCorporateActionAsync_PersistsEconomicFingerprintAgainstPostgres()
    {
        var securityId = Guid.NewGuid();
        await SeedSecurityAsync(securityId);
        var action = Dividend(securityId, Guid.NewGuid());
        var eventStore = new PostgresSecurityMasterEventStore(
            _fixture.Options,
            NullLogger<PostgresSecurityMasterEventStore>.Instance);

        await eventStore.AppendCorporateActionAsync(action);

        var fingerprint = await ReadFingerprintAsync(action.CorpActId);
        fingerprint.Should().Be(CorporateActionEconomicFingerprint.Compute(action));
    }

    [SecurityMasterDatabaseFact]
    public async Task AppendCorporateActionAsync_MatchingLegacyNullFingerprint_ReusesAndBackfillsCanonicalAction()
    {
        var securityId = Guid.NewGuid();
        await SeedSecurityAsync(securityId);
        var legacyAction = Dividend(securityId, Guid.NewGuid());
        var duplicateAction = legacyAction with { CorpActId = Guid.NewGuid() };
        var eventStore = new PostgresSecurityMasterEventStore(
            _fixture.Options,
            NullLogger<PostgresSecurityMasterEventStore>.Instance);
        await eventStore.AppendCorporateActionAsync(legacyAction);
        await MakeFingerprintLegacyNullAsync(legacyAction.CorpActId);

        await eventStore.AppendCorporateActionAsync(duplicateAction);

        (await CountCorporateActionsAsync(securityId)).Should().Be(1);
        (await ReadFingerprintAsync(legacyAction.CorpActId))
            .Should().Be(CorporateActionEconomicFingerprint.Compute(duplicateAction));
    }

    [SecurityMasterDatabaseFact]
    public async Task AppendCorporateActionAsync_AmbiguousLegacyNullFingerprints_FailsClosed()
    {
        var securityId = Guid.NewGuid();
        await SeedSecurityAsync(securityId);
        var firstLegacyAction = Dividend(securityId, Guid.NewGuid());
        var secondLegacyAction = firstLegacyAction with { CorpActId = Guid.NewGuid() };
        var candidate = firstLegacyAction with { CorpActId = Guid.NewGuid() };
        var eventStore = new PostgresSecurityMasterEventStore(
            _fixture.Options,
            NullLogger<PostgresSecurityMasterEventStore>.Instance);
        await eventStore.AppendCorporateActionAsync(firstLegacyAction);
        await CreateLegacyNullFingerprintPairAsync(
            firstLegacyAction.CorpActId,
            secondLegacyAction.CorpActId);

        var act = () => eventStore.AppendCorporateActionAsync(candidate);

        var exception = await act.Should().ThrowAsync<CorporateActionSourceConflictException>();
        exception.Which.Code.Should().Be(CorporateActionProblemCodes.SourceConflict);
        exception.Which.Message.Should().Contain("legacy duplicates");
        (await CountCorporateActionsAsync(securityId)).Should().Be(2);
        (await CountNullFingerprintsAsync(securityId)).Should().Be(2);
    }

    [SecurityMasterDatabaseFact]
    public async Task AppendCorporateActionAsync_ConcurrentEconomicDuplicates_ConvergeOnOneCanonicalAction()
    {
        var securityId = Guid.NewGuid();
        await SeedSecurityAsync(securityId);
        var firstAction = Dividend(securityId, Guid.NewGuid());
        var secondAction = firstAction with { CorpActId = Guid.NewGuid() };
        var firstStore = new PostgresSecurityMasterEventStore(
            _fixture.Options,
            NullLogger<PostgresSecurityMasterEventStore>.Instance);
        var secondStore = new PostgresSecurityMasterEventStore(
            _fixture.Options,
            NullLogger<PostgresSecurityMasterEventStore>.Instance);

        await Task.WhenAll(
            firstStore.AppendCorporateActionAsync(firstAction),
            secondStore.AppendCorporateActionAsync(secondAction));

        (await CountCorporateActionsAsync(securityId)).Should().Be(1);
        var stored = await firstStore.LoadCorporateActionsAsync(securityId);
        var canonical = stored.Should().ContainSingle().Subject;
        new[] { firstAction.CorpActId, secondAction.CorpActId }
            .Should().Contain(canonical.CorpActId);
        (await ReadFingerprintAsync(canonical.CorpActId))
            .Should().Be(CorporateActionEconomicFingerprint.Compute(firstAction));
    }

    [SecurityMasterDatabaseFact]
    public async Task AppendCorporateActionAsync_SuccessorOfCancelledParent_FailsClosed()
    {
        var securityId = Guid.NewGuid();
        await SeedSecurityAsync(securityId);
        var parent = Dividend(securityId, Guid.NewGuid()) with
        {
            LifecycleState = CorporateActionLifecycleStates.Cancelled,
        };
        var successor = parent with
        {
            CorpActId = Guid.NewGuid(),
            SupersedesCorpActId = parent.CorpActId,
        };
        var eventStore = new PostgresSecurityMasterEventStore(
            _fixture.Options,
            NullLogger<PostgresSecurityMasterEventStore>.Instance);
        await eventStore.AppendCorporateActionAsync(parent);

        var act = () => eventStore.AppendCorporateActionAsync(successor);

        var exception = await act.Should().ThrowAsync<CorporateActionStateConflictException>();
        exception.Which.Code.Should().Be(CorporateActionProblemCodes.StateConflict);
        exception.Which.Message.Should().Contain("terminal");
        (await CountCorporateActionsAsync(securityId)).Should().Be(1);
    }

    [SecurityMasterDatabaseFact]
    public async Task AcceptSourceProposalAsync_CorrectionOfCancelledCanonicalParent_FailsClosed()
    {
        var securityId = Guid.NewGuid();
        await SeedSecurityAsync(securityId);
        var parentAction = Dividend(securityId, Guid.NewGuid()) with
        {
            LifecycleState = CorporateActionLifecycleStates.Cancelled,
        };
        var eventStore = new PostgresSecurityMasterEventStore(
            _fixture.Options,
            NullLogger<PostgresSecurityMasterEventStore>.Instance);
        await eventStore.AppendCorporateActionAsync(parentAction);

        var operations = new PostgresCorporateActionOperationsStore(_fixture.Options);
        var rootProposal = Proposal(parentAction) with
        {
            State = CorporateActionSourceProposalStates.Accepted,
            AcceptedCorporateActionId = parentAction.CorpActId,
            DecisionBy = "test-operator",
            DecisionAtUtc = DateTimeOffset.UtcNow,
        };
        await operations.RecordSourceProposalAsync(rootProposal);
        var correctionAction = parentAction with
        {
            CorpActId = Guid.NewGuid(),
            SupersedesCorpActId = parentAction.CorpActId,
        };
        var correctionProposal = Proposal(correctionAction) with
        {
            ProviderIdentity = rootProposal.ProviderIdentity with
            {
                SourceEventVersion = "v2",
                ObservedAtUtc = rootProposal.ProviderIdentity.ObservedAtUtc.AddMinutes(1),
            },
            SupersedesProposalId = rootProposal.ProposalId,
        };
        correctionProposal = await operations.RecordSourceProposalAsync(correctionProposal);

        var act = () => operations.AcceptSourceProposalAsync(
            AcceptRequest(correctionProposal),
            corporateActionId: Guid.NewGuid(),
            caseId: Guid.NewGuid(),
            transitionId: Guid.NewGuid(),
            restatement: null,
            requestFingerprint: new string('d', 64));

        var exception = await act.Should().ThrowAsync<CorporateActionStateConflictException>();
        exception.Which.Code.Should().Be(CorporateActionProblemCodes.StateConflict);
        exception.Which.Message.Should().Contain("terminal");
        (await CountCorporateActionsAsync(securityId)).Should().Be(1);
        var unchangedProposal = await operations.GetSourceProposalAsync(correctionProposal.ProposalId);
        unchangedProposal.Should().NotBeNull();
        unchangedProposal!.State.Should().Be(CorporateActionSourceProposalStates.Observed);
        unchangedProposal.AcceptedCorporateActionId.Should().BeNull();
    }

    [SecurityMasterDatabaseFact]
    public async Task AcceptSourceProposalAsync_OneMatchingLegacyNullFingerprint_ReusesAndBackfillsCanonicalAction()
    {
        var securityId = Guid.NewGuid();
        await SeedSecurityAsync(securityId);
        var legacyAction = Dividend(securityId, Guid.NewGuid());
        var eventStore = new PostgresSecurityMasterEventStore(
            _fixture.Options,
            NullLogger<PostgresSecurityMasterEventStore>.Instance);
        await eventStore.AppendCorporateActionAsync(legacyAction);
        await MakeFingerprintLegacyNullAsync(legacyAction.CorpActId);

        var proposedAction = legacyAction with { CorpActId = Guid.NewGuid() };
        var proposal = Proposal(proposedAction);
        var operations = new PostgresCorporateActionOperationsStore(_fixture.Options);
        await operations.RecordSourceProposalAsync(proposal);

        var result = await operations.AcceptSourceProposalAsync(
            AcceptRequest(proposal),
            corporateActionId: Guid.NewGuid(),
            caseId: Guid.NewGuid(),
            transitionId: Guid.NewGuid(),
            restatement: null,
            requestFingerprint: new string('b', 64));

        result.CorporateAction.CorpActId.Should().Be(legacyAction.CorpActId);
        result.Proposal.AcceptedCorporateActionId.Should().Be(legacyAction.CorpActId);
        (await ReadFingerprintAsync(legacyAction.CorpActId))
            .Should().Be(proposal.EconomicFingerprint);
        (await CountCorporateActionsAsync(securityId)).Should().Be(1);
        (await ReadCanonicalSourceActionIdAsync(proposal.ProposalId))
            .Should().Be(legacyAction.CorpActId);
    }

    [SecurityMasterDatabaseFact]
    public async Task AcceptSourceProposalAsync_AmbiguousLegacyNullFingerprints_FailsClosed()
    {
        var securityId = Guid.NewGuid();
        await SeedSecurityAsync(securityId);
        var eventStore = new PostgresSecurityMasterEventStore(
            _fixture.Options,
            NullLogger<PostgresSecurityMasterEventStore>.Instance);
        var firstLegacyAction = Dividend(securityId, Guid.NewGuid());
        await eventStore.AppendCorporateActionAsync(firstLegacyAction);
        var secondLegacyAction = firstLegacyAction with { CorpActId = Guid.NewGuid() };
        await CreateLegacyNullFingerprintPairAsync(
            firstLegacyAction.CorpActId,
            secondLegacyAction.CorpActId);

        var proposal = Proposal(firstLegacyAction with { CorpActId = Guid.NewGuid() });
        var operations = new PostgresCorporateActionOperationsStore(_fixture.Options);
        await operations.RecordSourceProposalAsync(proposal);

        var act = () => operations.AcceptSourceProposalAsync(
            AcceptRequest(proposal),
            corporateActionId: Guid.NewGuid(),
            caseId: Guid.NewGuid(),
            transitionId: Guid.NewGuid(),
            restatement: null,
            requestFingerprint: new string('c', 64));

        var exception = await act.Should().ThrowAsync<CorporateActionSourceConflictException>();
        exception.Which.Code.Should().Be(CorporateActionProblemCodes.SourceConflict);
        exception.Which.Message.Should().Contain("legacy duplicates");
        (await CountNullFingerprintsAsync(securityId)).Should().Be(2);
        (await CountCanonicalSourceLinksAsync(proposal.ProposalId)).Should().Be(0);
        (await CountCasesAsync(proposal.ProposalId)).Should().Be(0);
        var unchangedProposal = await operations.GetSourceProposalAsync(proposal.ProposalId);
        unchangedProposal.Should().NotBeNull();
        unchangedProposal!.State.Should().Be(CorporateActionSourceProposalStates.Observed);
        unchangedProposal.Version.Should().Be(1);
        unchangedProposal.AcceptedCorporateActionId.Should().BeNull();
    }

    private async Task SeedSecurityAsync(Guid securityId)
    {
        var effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1);
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        await store.UpsertProjectionAsync(new SecurityProjectionRecord(
            SecurityId: securityId,
            AssetClass: "Equity",
            Status: SecurityStatusDto.Active,
            DisplayName: $"Corporate action test {securityId:N}",
            Currency: "USD",
            PrimaryIdentifierKind: SecurityIdentifierKind.Ticker.ToString(),
            PrimaryIdentifierValue: $"CA{securityId:N}",
            CommonTerms: JsonSerializer.SerializeToElement(new { currency = "USD" }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new { schemaVersion = 1 }),
            Provenance: JsonSerializer.SerializeToElement(new { sourceSystem = "test" }),
            Version: 1,
            EffectiveFrom: effectiveFrom,
            EffectiveTo: null,
            Identifiers:
            [
                new SecurityIdentifierDto(
                    SecurityIdentifierKind.Ticker,
                    $"CA{securityId:N}",
                    true,
                    effectiveFrom),
            ],
            Aliases: []));
    }

    private static CorporateActionDto Dividend(Guid securityId, Guid corporateActionId) =>
        new(
            corporateActionId,
            securityId,
            CorporateActionEventTypes.Dividend,
            new DateOnly(2026, 8, 14),
            new DateOnly(2026, 8, 28),
            DividendPerShare: 0.24m,
            Currency: "USD",
            SplitRatio: null,
            NewSecurityId: null,
            DistributionRatio: null,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null,
            RecordDate: new DateOnly(2026, 8, 15),
            LifecycleState: CorporateActionLifecycleStates.Confirmed);

    private static CorporateActionSourceProposalDto Proposal(CorporateActionDto action)
    {
        var proposalId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        return new CorporateActionSourceProposalDto(
            proposalId,
            action.SecurityId,
            new CorporateActionProviderEventIdentityDto(
                "test-provider",
                $"event-{proposalId:N}",
                "v1",
                now,
                EvidenceHash: new string('a', 64),
                EvidenceReference: $"provider-event://corporate-actions/test/{proposalId:N}/v1",
                ReleaseStatus: CorporateActionProviderReleaseStatusDto.AcceptanceEligible),
            action,
            action.PayloadSchemaVersion,
            CorporateActionEconomicFingerprint.Compute(action),
            CorporateActionSourceProposalStates.Observed,
            Version: 1,
            SupersedesProposalId: null,
            AcceptedCorporateActionId: null,
            InitialCaseId: null,
            RecordedBy: "test-ingest",
            RecordedAtUtc: now,
            UpdatedAtUtc: now);
    }

    private static AcceptCorporateActionSourceProposalRequestDto AcceptRequest(
        CorporateActionSourceProposalDto proposal) =>
        new(
            proposal.ProposalId,
            ExpectedVersion: 1,
            IdempotencyKey: $"accept-{proposal.ProposalId:N}",
            Scope: new CorporateActionCaseScopeDto("tenant-test", "company-test"),
            Actor: "test-operator",
            MethodologyProfileId: "test-methodology-v1",
            Reason: "Accept matching corporate-action evidence.");

    private async Task MakeFingerprintLegacyNullAsync(Guid corporateActionId)
    {
        await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var clearFingerprint = connection.CreateCommand();
        clearFingerprint.CommandText =
            $"""
            update {_fixture.Options.Schema}.corporate_actions
            set economic_fingerprint = null
            where corp_act_id = @corp_act_id;
            """;
        clearFingerprint.Parameters.AddWithValue("corp_act_id", corporateActionId);
        (await clearFingerprint.ExecuteNonQueryAsync()).Should().Be(1);
    }

    private async Task CreateLegacyNullFingerprintPairAsync(
        Guid firstCorporateActionId,
        Guid secondCorporateActionId)
    {
        await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var clearFingerprint = connection.CreateCommand())
        {
            clearFingerprint.Transaction = transaction;
            clearFingerprint.CommandText =
                $"""
                update {_fixture.Options.Schema}.corporate_actions
                set economic_fingerprint = null
                where corp_act_id = @corp_act_id;
                """;
            clearFingerprint.Parameters.AddWithValue("corp_act_id", firstCorporateActionId);
            (await clearFingerprint.ExecuteNonQueryAsync()).Should().Be(1);
        }

        await using (var insertDuplicate = connection.CreateCommand())
        {
            insertDuplicate.Transaction = transaction;
            insertDuplicate.CommandText =
                $"""
                insert into {_fixture.Options.Schema}.corporate_actions (
                    corp_act_id, security_id, event_type, ex_date, pay_date, dividend_per_share,
                    currency, split_ratio, new_security_id, distribution_ratio, acquirer_security_id,
                    exchange_ratio, subscription_price_per_share, rights_per_share, record_date,
                    lifecycle_state, supersedes_corp_act_id, redemption_price_percent_of_par, payload,
                    payload_schema_version, economic_fingerprint)
                select @second_corp_act_id, security_id, event_type, ex_date, pay_date, dividend_per_share,
                       currency, split_ratio, new_security_id, distribution_ratio, acquirer_security_id,
                       exchange_ratio, subscription_price_per_share, rights_per_share, record_date,
                       lifecycle_state, supersedes_corp_act_id, redemption_price_percent_of_par, payload,
                       payload_schema_version, null
                from {_fixture.Options.Schema}.corporate_actions
                where corp_act_id = @first_corp_act_id;
                """;
            insertDuplicate.Parameters.AddWithValue("second_corp_act_id", secondCorporateActionId);
            insertDuplicate.Parameters.AddWithValue("first_corp_act_id", firstCorporateActionId);
            (await insertDuplicate.ExecuteNonQueryAsync()).Should().Be(1);
        }

        await transaction.CommitAsync();
    }

    private async Task<string?> ReadFingerprintAsync(Guid corporateActionId)
    {
        await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select economic_fingerprint
            from {_fixture.Options.Schema}.corporate_actions
            where corp_act_id = @corp_act_id;
            """;
        command.Parameters.AddWithValue("corp_act_id", corporateActionId);
        return await command.ExecuteScalarAsync() as string;
    }

    private async Task<Guid?> ReadCanonicalSourceActionIdAsync(Guid proposalId)
    {
        await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select corp_act_id
            from {_fixture.Options.Schema}.corporate_action_canonical_sources
            where proposal_id = @proposal_id;
            """;
        command.Parameters.AddWithValue("proposal_id", proposalId);
        return await command.ExecuteScalarAsync() as Guid?;
    }

    private async Task<long> CountCorporateActionsAsync(Guid securityId)
    {
        await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select count(*)
            from {_fixture.Options.Schema}.corporate_actions
            where security_id = @security_id;
            """;
        command.Parameters.AddWithValue("security_id", securityId);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private async Task<long> CountNullFingerprintsAsync(Guid securityId)
    {
        await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select count(*)
            from {_fixture.Options.Schema}.corporate_actions
            where security_id = @security_id
              and economic_fingerprint is null;
            """;
        command.Parameters.AddWithValue("security_id", securityId);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private async Task<long> CountCanonicalSourceLinksAsync(Guid proposalId)
    {
        await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select count(*)
            from {_fixture.Options.Schema}.corporate_action_canonical_sources
            where proposal_id = @proposal_id;
            """;
        command.Parameters.AddWithValue("proposal_id", proposalId);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private async Task<long> CountCasesAsync(Guid proposalId)
    {
        await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select count(*)
            from {_fixture.Options.Schema}.corporate_action_processing_cases
            where proposal_id = @proposal_id;
            """;
        command.Parameters.AddWithValue("proposal_id", proposalId);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    /// <summary>
    /// The current table has no append-only alias revision. A material correction must therefore
    /// fail closed instead of rewriting the row used by every recorded-as-of reconstruction.
    /// </summary>
    [SecurityMasterDatabaseFact]
    public async Task UpsertAliasAsync_RejectsMaterialCorrection_AndPreservesRecordedFacts()
    {
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var securityId = Guid.NewGuid();
        var aliasId = Guid.NewGuid();
        var recordedAt = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var effectiveFrom = recordedAt.AddDays(-1);

        await CreateMinimalSecurityAsync(store, securityId, effectiveFrom);

        var original = new SecurityAliasDto(
            aliasId,
            securityId,
            SecurityIdentifierKind.Ric.ToString(),
            "ACME.O",
            "refinitiv",
            SecurityAliasScope.Collector,
            "recorded in January",
            "january.operator",
            recordedAt,
            effectiveFrom,
            null,
            true);

        var inserted = await store.UpsertAliasAsync(original);
        inserted.Should().NotBeNull();
        inserted!.CreatedAt.Should().Be(recordedAt);

        var replayed = await store.UpsertAliasAsync(original with
        {
            CreatedBy = "retry.operator",
            CreatedAt = recordedAt.AddDays(1)
        });
        replayed.Should().NotBeNull();
        replayed!.CreatedBy.Should().Be("january.operator");
        replayed.CreatedAt.Should().Be(recordedAt);

        // The June correction supplies a new value AND a new creation stamp, as the service does.
        var corrected = original with
        {
            AliasValue = "ACME.OQ",
            Reason = "corrected in June",
            CreatedBy = "june.operator",
            CreatedAt = new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero)
        };

        var act = () => store.UpsertAliasAsync(corrected);

        await act.Should().ThrowAsync<SecurityAliasHistoryConflictException>()
            .WithMessage("*append-only alias revisions*");

        var storedValue = await ReadAliasColumnAsync(aliasId, "alias_value");
        storedValue.Should().Be("ACME.O");
        var storedReason = await ReadAliasColumnAsync(aliasId, "reason");
        storedReason.Should().Be("recorded in January");

        var storedCreatedAt = await ReadAliasColumnAsync(aliasId, "created_at");
        Convert.ToDateTime(storedCreatedAt, System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(recordedAt.UtcDateTime, "the durable column, not just the returned DTO, must be unchanged");
    }

    private Task<SecurityDetailDto> CreateMinimalSecurityAsync(
        PostgresSecurityMasterStore store,
        Guid securityId,
        DateTimeOffset effectiveFrom)
    {
        var eventStore = new PostgresSecurityMasterEventStore(_fixture.Options, NullLogger<PostgresSecurityMasterEventStore>.Instance);
        var snapshotStore = new PostgresSecurityMasterSnapshotStore(_fixture.Options);
        var service = new SecurityMasterService(
            eventStore,
            snapshotStore,
            store,
            new SecurityMasterAggregateRebuilder(eventStore, snapshotStore),
            _fixture.Options,
            NullLogger<SecurityMasterService>.Instance);

        return service.CreateAsync(new CreateSecurityRequest(
            securityId,
            "Equity",
            JsonSerializer.SerializeToElement(new
            {
                displayName = "Alias History Fixture",
                currency = "USD",
                countryOfRisk = "US",
                issuerName = "Acme Corp",
                exchange = "XNYS",
                lotSize = 1,
                tickSize = 0.01m
            }),
            JsonSerializer.SerializeToElement(new { shareClass = "Common" }),
            new[]
            {
                new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, $"AH{securityId:N}"[..8], true, effectiveFrom, null, null)
            },
            effectiveFrom,
            "test",
            "codex",
            null,
            "alias history fixture"));
    }

    private async Task<object?> ReadAliasColumnAsync(Guid aliasId, string column)
    {
        await using var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"select {column} from {_fixture.Options.Schema}.security_aliases where alias_id = @alias_id;";
        command.Parameters.AddWithValue("alias_id", aliasId);
        return await command.ExecuteScalarAsync();
    }
}
