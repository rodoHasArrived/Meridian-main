using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.DataIntegration.AccountingSystem.QuickBooks;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Moq;
using Npgsql;

namespace Meridian.Tests.Storage;

[Trait("Category", "Integration")]
public sealed class ExternalGlPostingPostgresTests
{
    private const string Tenant = "tenant-gl";
    private const string Company = "company-gl";
    private const string Fund = "fund-gl";
    private const string Connection = "quickbooks-sandbox";
    private const string PrepareActor = "fund-accountant";
    private const string ApproveActor = "controller";
    private const OperationsActionOriginDto Human = OperationsActionOriginDto.HumanOperator;
    private static readonly DateTimeOffset Now = new(2026, 5, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly ExternalGlDestination Destination = new("quickbooks", Connection, "10001", "sandbox");
    private static readonly LedgerAccount Cash = new("Cash", LedgerAccountType.Asset);
    private static readonly LedgerAccount Capital = new("Capital", LedgerAccountType.Equity);

    [LedgerDatabaseFact]
    public async Task LostCreateResponse_RecoversExactReceiptWithoutSecondCreate_AndLateJournalBlocksFinalClose()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        using var authority = FundScopeTenantAuthority.Enter(Tenant, "external GL synthetic test");
        var (store, period, scope) = await SeedAsync(database, ct);
        var journal = await AddJournalAsync(store, scope, Now, ct);
        using var http = new SyntheticQuickBooks { DropFirstCreateResponse = true };
        using var client = new HttpClient(http);
        var adapter = Adapter(client);
        var accounting = new AccountingSystemIntegrationService([], store);
        var isBookOfRecord = true;
        var bookGuard = new Mock<IBookOfRecordGuard>();
        bookGuard.Setup(guard => guard.GetStatusAsync(Tenant, Company, scope.LedgerBookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new BookOfRecordStatusDto(scope.LedgerBookId, isBookOfRecord, null, null, "Book is in shadow mode."));
        var service = new ExternalGlPostingService(store, accounting, [adapter], TimeProvider.System, bookGuard.Object);
        await service.ConfigureBookAsync(scope.LedgerBookId, Tenant, Company, Fund, "quickbooks", Connection, false, ApproveActor, Human, ct);
        await store.RequireConfirmationAsync(scope.LedgerBookId, scope.PeriodId, ct);
        await service.ConfigureBookAsync(scope.LedgerBookId, Tenant, Company, Fund, "quickbooks", Connection, true, ApproveActor, Human, ct);
        await Assert.ThrowsAsync<LedgerPeriodTransitionException>(() => FinalizeAsync(store, period, ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListAsync(scope with { CompanyId = "another-company" }, ct));
        await Assert.ThrowsAsync<LedgerPeriodTransitionException>(() => store.RequireConfirmationAsync(scope.LedgerBookId, Guid.NewGuid(), ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RetainMappingAsync(accounting, scope, ct, destination: Destination with { ExternalLedgerId = "10002" }));

        await RetainMappingAsync(accounting, scope, ct);
        var package = await PrepareAsync(service, scope, [journal.JournalEntryId], ct);
        (await PrepareAsync(service, scope, [journal.JournalEntryId], ct)).PackageId.Should().Be(package.PackageId);
        package.Payload.Mapping.Version.Should().HaveLength(64);
        package.Payload.Source.LastJournalSequence.Should().BePositive();
        package.Payload.Source.ContentHash.Should().HaveLength(64);
        package.Payload.Documents.Single().Lines.Select(line => line.MeridianLineId)
            .Should().Equal(journal.Lines.Select(line => line.EntryId));
        var dispatcher = new ExternalGlPostingDispatcher(store, [adapter], TimeProvider.System);
        (await dispatcher.ProcessNextAsync(ct)).Should().BeFalse();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DecideAsync(scope, package.PackageId, true,
            package.PayloadHash, "Approve", ApproveActor, Human, ct));
        await service.DecideAsync(scope, package.PackageId, false, package.PayloadHash, "Certify", ApproveActor, Human, ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DecideAsync(scope, package.PackageId, true,
            package.PayloadHash, "Approve", PrepareActor, Human, ct));

        isBookOfRecord = false;
        await Assert.ThrowsAsync<ShadowBookNotBookOfRecordException>(() => service.DecideAsync(scope, package.PackageId, true,
            package.PayloadHash, "Attempt approval during shadow mode", ApproveActor, Human, ct));
        isBookOfRecord = true;

        await Task.WhenAll(
            service.DecideAsync(scope, package.PackageId, true, package.PayloadHash, "First approval", ApproveActor, Human, ct),
            service.DecideAsync(scope, package.PackageId, true, package.PayloadHash, "Duplicate approval", ApproveActor, Human, ct));
        (await dispatcher.ProcessNextAsync(ct)).Should().BeTrue();
        http.Documents.Should().ContainSingle();
        (await service.GetAsync(scope, package.PackageId, ct))!.Status.Should().Be(ExternalGlPostingStatus.OutcomeUnknown);
        await Assert.ThrowsAsync<LedgerPeriodTransitionException>(() => FinalizeAsync(store, period, ct));

        // A new process has no mapping/import cache. It recovers from the retained approval,
        // approval's exact mapping and journal payload, and the provider key.
        await MakeDueAsync(database, ct);
        var restarted = Restart(database);
        var recovery = new ExternalGlPostingDispatcher(restarted, [Adapter(client)], TimeProvider.System);
        (await recovery.ProcessNextAsync(ct)).Should().BeTrue();
        (await recovery.ProcessNextAsync(ct)).Should().BeFalse();
        http.Calls.Should().Equal("create", "lookup");
        var retained = (await restarted.GetExternalGlPackageAsync(scope, package.PackageId, ct))!;
        retained.Approval.Should().NotBeNull();
        retained.PayloadHash.Should().Be(package.PayloadHash);
        retained.Status.Should().Be(ExternalGlPostingStatus.FullyAccepted);
        var last = retained.Progress.Should().ContainSingle().Which.Observations.Last();
        last.IsLookup.Should().BeTrue();
        last.Receipt.ProviderDocumentId.Should().Be("100");
        last.Receipt.Lines.Select(line => line.ProviderLineId).Should().Equal("0", "1");
        last.Receipt.Lines.Select(line => line.MeridianLineId).Should().Equal(journal.Lines.Select(line => line.EntryId));
        last.Receipt.Lines.Should().OnlyContain(line => line.Status == ExternalGlLineStatus.Accepted);
        await restarted.RequireConfirmationForRangeAsync(scope.LedgerBookId, period.StartDate, period.EndDate, ct);

        await AddJournalAsync(restarted, scope, Now.AddDays(1), ct);
        await Assert.ThrowsAsync<LedgerPeriodTransitionException>(() => FinalizeAsync(restarted, period, ct));
        await service.ConfigureBookAsync(scope.LedgerBookId, Tenant, Company, Fund, "quickbooks", Connection, false, ApproveActor, Human, ct);
        (await FinalizeAsync(restarted, period, ct)).Status.Should().Be("HardClosed");
        http.Documents.Should().ContainSingle();
    }

    [LedgerDatabaseFact]
    public async Task InterruptedBeforeWrite_QueriesBeforeReplay_AndFencesTheExpiredWorker()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        using var authority = FundScopeTenantAuthority.Enter(Tenant, "external GL synthetic test");
        var (store, period, scope) = await SeedAsync(database, ct);
        var journal = await AddJournalAsync(store, scope, Now, ct);
        using var http = new SyntheticQuickBooks();
        using var client = new HttpClient(http);
        var adapter = Adapter(client);
        var accounting = new AccountingSystemIntegrationService([], store);
        var service = new ExternalGlPostingService(store, accounting, [adapter], TimeProvider.System);
        await service.ConfigureBookAsync(scope.LedgerBookId, Tenant, Company, Fund, "quickbooks", Connection, true, ApproveActor, Human, ct);
        await RetainMappingAsync(accounting, scope, ct);
        var package = await PrepareAsync(service, scope, [journal.JournalEntryId], ct);
        await ApproveAsync(service, scope, package, ct);

        var abandoned = await store.ClaimExternalGlDocumentAsync(ct);
        abandoned.Should().NotBeNull();
        (await store.MarkExternalGlSubmittingAsync(abandoned!, ct)).Should().BeTrue();
        await MakeDueAsync(database, ct);

        var restarted = Restart(database);
        var repeated = (await restarted.ClaimExternalGlDocumentAsync(ct))!;
        var lookup = await adapter.FindAsync(Tenant, Destination, repeated.Document, ct);
        lookup.SafeToReplay.Should().BeTrue();
        await restarted.RecordExternalGlReceiptAsync(repeated, new(Now, true, lookup),
            ExternalGlPostingStatus.OutcomeUnknown, keepLease: true, ct);
        (await restarted.MarkExternalGlSubmittingAsync(repeated, ct)).Should().BeTrue();
        await MakeDueAsync(database, ct);

        var dispatcher = new ExternalGlPostingDispatcher(restarted, [Adapter(client)], TimeProvider.System);
        (await dispatcher.ProcessNextAsync(ct)).Should().BeTrue();
        http.Calls.Should().Equal("lookup", "lookup", "create");
        http.Documents.Should().ContainSingle();
        (await store.MarkExternalGlSubmittingAsync(abandoned!, ct)).Should().BeFalse();
        (await FinalizeAsync(restarted, period, ct)).Status.Should().Be("HardClosed");
    }

    [LedgerDatabaseFact]
    public async Task PartialPackage_RepairsOnlyDefinitiveRejection_AndExportsASeparateLedgerReversal()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        using var authority = FundScopeTenantAuthority.Enter(Tenant, "external GL synthetic test");
        var (store, period, scope) = await SeedAsync(database, ct);
        var accepted = await AddJournalAsync(store, scope, Now, ct);
        var rejected = await AddJournalAsync(store, scope, Now.AddDays(1), ct);
        using var http = new SyntheticQuickBooks { RejectDate = "2026-05-16" };
        using var client = new HttpClient(http);
        var adapter = Adapter(client);
        var accounting = new AccountingSystemIntegrationService([], store);
        var service = new ExternalGlPostingService(store, accounting, [adapter], TimeProvider.System);
        await service.ConfigureBookAsync(scope.LedgerBookId, Tenant, Company, Fund, "quickbooks", Connection, true, ApproveActor, Human, ct);
        await RetainMappingAsync(accounting, scope, ct);
        var first = await PrepareAsync(service, scope, [], ct);
        await ApproveAsync(service, scope, first, ct);

        var dispatcher = new ExternalGlPostingDispatcher(store, [adapter], TimeProvider.System);
        while (await dispatcher.ProcessNextAsync(ct))
        {
        }
        var after = (await service.GetAsync(scope, first.PackageId, ct))!;
        after.Status.Should().Be(ExternalGlPostingStatus.PartiallyAccepted);
        after.Progress.Single(row => row.Status == ExternalGlPostingStatus.Rejected).Observations.Single().Receipt.Lines
            .Should().OnlyContain(line => line.Status == ExternalGlLineStatus.Rejected && line.ErrorCode == ExternalGlErrorCode.ValidationFailed);
        await Assert.ThrowsAsync<LedgerPeriodTransitionException>(() => FinalizeAsync(store, period, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PrepareAsync(scope, "v1", [accepted.JournalEntryId],
            "Must not create a duplicate", PrepareActor, Human, ExternalGlPostingPurpose.Correction, first.PackageId, null, ct));

        http.RejectDate = null;
        await RetainMappingAsync(accounting, scope, ct, capitalId: "33");
        var correctedService = new ExternalGlPostingService(store, new AccountingSystemIntegrationService([], store), [adapter], TimeProvider.System);
        var correction = await correctedService.PrepareAsync(scope, "v1", [], "Repair rejected account mapping", PrepareActor,
            Human, ExternalGlPostingPurpose.Correction, first.PackageId, null, ct);
        var correctedDocument = correction.Payload.Documents.Should().ContainSingle().Which;
        correctedDocument.MeridianJournalId.Should().Be(rejected.JournalEntryId);
        correctedDocument.SupersedesIdempotencyKey.Should().Be(first.Payload.Documents.Single(row => row.MeridianJournalId == rejected.JournalEntryId).IdempotencyKey);
        await ApproveAsync(correctedService, scope, correction, ct);
        (await dispatcher.ProcessNextAsync(ct)).Should().BeTrue();

        var reversal = await AddJournalAsync(store, scope, Now.AddDays(2), ct, accepted.JournalEntryId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => PrepareAsync(correctedService, scope, [reversal.JournalEntryId], ct));
        var reversalPackage = await correctedService.PrepareAsync(scope, null, [reversal.JournalEntryId], "Approved Meridian reversal",
            PrepareActor, Human, ExternalGlPostingPurpose.Reversal, first.PackageId, null, ct);
        reversalPackage.Payload.Mapping.Version.Should().Be(first.Payload.Mapping.Version);
        reversalPackage.Payload.Documents.Single().ReversesJournalId.Should().Be(accepted.JournalEntryId);
        reversalPackage.Payload.Documents.Single().Lines.Single(line => line.Debit == 12.35m).ExternalAccountId.Should().Be("22");
        await ApproveAsync(correctedService, scope, reversalPackage, ct);
        (await dispatcher.ProcessNextAsync(ct)).Should().BeTrue();
        (await FinalizeAsync(store, period, ct)).Status.Should().Be("HardClosed");
        http.Documents.Should().HaveCount(3);
    }

    private static async Task<(PostgresLedgerJournalStore Store, LedgerAccountingPeriod Period, ExternalGlPostingScope Scope)> SeedAsync(
        LedgerPostgresTestDatabase database, CancellationToken ct)
    {
        var store = Restart(database);
        var registry = new PostgresFundProfileTenancyRegistry(database.Options);
        await registry.BindAsync(Fund, Tenant, Company, ct);
        var book = await store.SaveLedgerBookAsync(new(Guid.NewGuid(), Fund, Guid.NewGuid(),
            FundStructureNodeKindDto.Fund, "Synthetic fund book", "USD", Now, Now), ct);
        var period = await store.SavePeriodAsync(new(Guid.NewGuid(), book.LedgerBookId, 2026, 5, "May",
            new(2026, 5, 1), new(2026, 5, 31), "Open", Now, null, 0), 0, ct: ct);
        return (store, period, new(Tenant, Company, Fund, book.LedgerBookId, period.PeriodId));
    }

    private static PostgresLedgerJournalStore Restart(LedgerPostgresTestDatabase database) =>
        new(database.Options, new AmbientTenantAccessor(), TenantScopeEnforcementOptions.FailClosed,
            postingAuthorities: LedgerPostgresTestDatabase.SyntheticFinancialPostingAuthorities);

    private static async Task<JournalEntry> AddJournalAsync(PostgresLedgerJournalStore store, ExternalGlPostingScope scope,
        DateTimeOffset when, CancellationToken ct, Guid? reverses = null)
    {
        var id = Guid.NewGuid();
        const string memo = "Synthetic accounting journal";
        var debit = reverses is null ? Cash : Capital;
        var credit = reverses is null ? Capital : Cash;
        var dims = new LedgerLineDimensionSet(FundId: Fund, EntityId: "entity-gl");
        var journal = new JournalEntry(id, when, memo,
            [new(Guid.NewGuid(), id, when, debit, 12.35m, 0, memo, dims), new(Guid.NewGuid(), id, when, credit, 0, 12.35m, memo, dims)]);
        await store.AppendAsync(new(journal, scope.LedgerBookId, scope.PeriodId,
            SourceJournalEntryId: reverses, PostingKind: reverses is null ? LedgerPostingKindDto.Originating : LedgerPostingKindDto.Adjustment,
            AdjustmentApproval: reverses is null ? null : new("reversal-approval", LedgerAdjustmentApprovalStatusDto.Approved,
                ApproveActor, when, "approved-reversal", GovernanceCaseId: "synthetic-reversal-case"),
            LedgerBookId: scope.LedgerBookId), ct);
        return journal;
    }

    private static async Task RetainMappingAsync(AccountingSystemIntegrationService service,
        ExternalGlPostingScope scope, CancellationToken ct, string capitalId = "22", ExternalGlDestination? destination = null)
    {
        var mapping = new DimensionMappingProfileDto("class-map", "Certified class", "quickbooks",
            new(FundId: Fund, EntityId: "entity-gl"), new(ExternalGlDimensions: new Dictionary<string, string> { ["class"] = "9" }),
            AccountingCertificationStateDto.Certified);
        var profile = new ExternalGlMappingProfileDto("v1", "quickbooks", "Fund mapping", Now, [mapping],
            new Dictionary<string, string> { [Cash.Name] = "11", [Capital.Name] = capitalId }, AccountingCertificationStateDto.Certified);
        await service.UpsertMappingProfileAsync(new(profile, ApproveActor, "quickbooks", Fund, scope.LedgerBookId,
            EvidenceLinks: ["approval:external-gl-mapping-profile:v1"], TenantId: Tenant, CompanyId: Company,
            PostingDestination: destination ?? Destination), ct);
    }

    private static Task<ExternalGlPostingPackage> PrepareAsync(ExternalGlPostingService service, ExternalGlPostingScope scope,
        IReadOnlyList<Guid> journals, CancellationToken ct) =>
        service.PrepareAsync(scope, "v1", journals, "Send posted journals to the mirror.", PrepareActor, Human,
            ExternalGlPostingPurpose.Export, null, null, ct);

    private static async Task ApproveAsync(ExternalGlPostingService service, ExternalGlPostingScope scope,
        ExternalGlPostingPackage package, CancellationToken ct)
    {
        await service.DecideAsync(scope, package.PackageId, false, package.PayloadHash, "Certified exact mapping", ApproveActor, Human, ct);
        await service.DecideAsync(scope, package.PackageId, true, package.PayloadHash, "Approved external export", ApproveActor, Human, ct);
    }

    private static Task<LedgerAccountingPeriod> FinalizeAsync(PostgresLedgerJournalStore store, LedgerAccountingPeriod period, CancellationToken ct) =>
        store.SaveHardClosedPeriodAsync(period with { Status = "HardClosed" }, period.Version,
            new(Guid.NewGuid(), period.PeriodId, period.Status, "HardClosed", ApproveActor, "Review complete", Now), ct);

    private static async Task MakeDueAsync(LedgerPostgresTestDatabase database, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            $"update \"{database.Options.SchemaName}\".external_gl_posting_outbox set next_check_at = now(), lease_until = now()", connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static QuickBooksOnlinePostingProvider Adapter(HttpClient client)
    {
        var resolver = new Mock<IQuickBooksPostingConnectionResolver>();
        var credential = new QuickBooksOnlineConnection("synthetic-client", "synthetic-secret", "synthetic-refresh", Destination.ExternalLedgerId,
            Destination.Environment, "Synthetic company");
        var store = new Mock<IQuickBooksOnlineConnectionStore>();
        store.Setup(row => row.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(credential);
        store.Setup(row => row.GetMetadataAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new AccountingSystemConnectionMetadataDto("quickbooks", "sandbox", Destination.ExternalLedgerId, "Synthetic company",
                true, true, Now, "Verified", "", []));
        store.Setup(row => row.SaveRefreshTokenAsync("synthetic-rotate", credential, CancellationToken.None)).ReturnsAsync("generation");
        resolver.Setup(row => row.Resolve(Tenant, Connection)).Returns(new QuickBooksPostingConnection(Destination, store.Object));
        return new QuickBooksOnlinePostingProvider(resolver.Object, new QuickBooksOnlineHttpClient(client), client);
    }

    private sealed class AmbientTenantAccessor : IFundScopeTenantAccessor
    {
        public string? ResolveCallerTenant() => FundScopeTenantAuthority.CurrentTenantId;
    }

    private sealed class SyntheticQuickBooks : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];
        public Dictionary<string, JsonObject> Documents { get; } = new(StringComparer.Ordinal);
        public bool DropFirstCreateResponse { get; init; }
        public string? RejectDate { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.Host == "oauth.platform.intuit.com")
                return Json(new { access_token = "synthetic-access", refresh_token = "synthetic-rotate", expires_in = 3600 });
            uri.Host.Should().Be("sandbox-quickbooks.api.intuit.com");
            uri.AbsolutePath.Should().StartWith("/v3/company/10001/");
            request.Headers.Authorization!.Parameter.Should().Be("synthetic-access");
            if (uri.AbsolutePath.Contains("/companyinfo/", StringComparison.Ordinal))
                return Json(new { CompanyInfo = new { Country = "US" } });
            if (uri.AbsolutePath.EndsWith("/preferences", StringComparison.Ordinal))
                return Json(new
                {
                    Preferences = new
                    {
                        CurrencyPrefs = new { MultiCurrencyEnabled = false, HomeCurrency = new { value = "USD" } },
                        AccountingInfoPrefs = new { ClassTrackingPerTxnLine = true },
                        OtherPrefs = new { NameValue = new[] { new { Name = "WarnDuplicateJournalNumber", Value = "true" } } }
                    }
                });
            if (uri.AbsolutePath.EndsWith("/query", StringComparison.Ordinal))
            {
                Calls.Add("lookup");
                var docNumber = Uri.UnescapeDataString(uri.Query).Split('\'')[1];
                return Documents.TryGetValue(docNumber, out var match)
                    ? Json(new { QueryResponse = new { JournalEntry = new[] { match } } })
                    : Json(new { QueryResponse = new { } });
            }

            request.Method.Should().Be(HttpMethod.Post);
            Calls.Add("create");
            var journal = (await request.Content!.ReadFromJsonAsync<JsonObject>(cancellationToken))!;
            var number = journal["DocNumber"]!.GetValue<string>();
            var key = journal["PrivateNote"]!.GetValue<string>()["Meridian:".Length..];
            uri.Query.Should().Be($"?requestid={key}");
            number.Length.Should().BeLessThanOrEqualTo(21);
            if (journal["TxnDate"]!.GetValue<string>() == RejectDate)
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = JsonContent.Create(new { Fault = new { type = "ValidationFault", Error = new[] { new { code = "2500" } } } }, options: System.Text.Json.JsonSerializerOptions.Default)
                };

            Documents.Should().NotContainKey(number, "a duplicate create must never reach the provider in this fixture");
            journal["Id"] = (100 + Documents.Count).ToString(System.Globalization.CultureInfo.InvariantCulture);
            journal["MetaData"] = new JsonObject { ["CreateTime"] = Now.ToString("O") };
            var lines = journal["Line"]!.AsArray();
            for (var index = 0; index < lines.Count; index++)
            {
                lines[index]!["Id"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                lines[index]!["JournalEntryLineDetail"]!["ClassRef"]!["value"]!.GetValue<string>().Should().Be("9");
            }
            Documents.Add(number, journal);
            if (DropFirstCreateResponse && Documents.Count == 1)
                throw new HttpRequestException("Synthetic response lost after commit.");
            return Json(new { JournalEntry = journal });
        }

        private static HttpResponseMessage Json<T>(T value)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(value, options: System.Text.Json.JsonSerializerOptions.Default) };
            response.Headers.Add("intuit_tid", "synthetic-provider-trace");
            return response;
        }
    }
}
