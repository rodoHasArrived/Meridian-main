using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Ui.Tests.Services;

public sealed class WorkstationOnboardingApiClientTests
{
    private const string WorkspaceId = "3f2c4c8e-4b61-4d55-9b0a-2b0e4f1c9d10";

    [Fact]
    public async Task ShadowMutations_PostToTheSharedRoutesWithTheRequestBody()
    {
        var calls = new List<(HttpMethod Method, string Path, string Body)>();
        using var api = Api(request =>
        {
            calls.Add((request.Method, request.RequestUri!.AbsolutePath, request.Content?.ReadAsStringAsync().Result ?? ""));
            return Json(OnboardingShadowWorkspaceTests.Workspace(WorkspaceId, 4));
        });
        var client = new WorkstationOnboardingApiClient(api);

        (await client.DesignateShadowAsync(WorkspaceId, new(3, "Parallel run."))).Success.Should().BeTrue();
        (await client.WithdrawShadowAsync(WorkspaceId, new(3, "Stopped."))).Success.Should().BeTrue();
        (await client.ExplainDifferenceAsync(WorkspaceId, "Balance|acct/1|USD|", new(3, "cmp-1", "Timing", "Wire in flight.", ["ev-1"], new(2026, 2, 3)))).Success.Should().BeTrue();
        (await client.PromoteAsync(WorkspaceId, new(3, "cert-1", "Cutover.", []))).Success.Should().BeTrue();

        calls.Select(call => call.Method).Should().AllBeEquivalentTo(HttpMethod.Post);
        calls.Select(call => call.Path).Should().Equal(
            $"/api/accounting/onboarding/workspaces/{WorkspaceId}/shadow-designation",
            $"/api/accounting/onboarding/workspaces/{WorkspaceId}/shadow-designation/withdraw",
            $"/api/accounting/onboarding/workspaces/{WorkspaceId}/differences/{Uri.EscapeDataString("Balance|acct/1|USD|")}/explanation",
            $"/api/accounting/onboarding/workspaces/{WorkspaceId}/promotion");
        calls[0].Body.Should().Contain("\"expectedVersion\":3").And.Contain("Parallel run.");
        calls[2].Body.Should().Contain("\"comparisonId\":\"cmp-1\"").And.Contain("\"expectedReversalDate\":\"2026-02-03\"");
    }

    [Fact]
    public async Task OnboardingRefusal_SurfacesTheServerSentenceAndStatus()
    {
        using var api = Api(_ => new(HttpStatusCode.Conflict)
        {
            Content = new StringContent("{\"error\":\"Another onboarding workspace already holds this ledger book.\"}", Encoding.UTF8, "application/json")
        });

        var response = await new WorkstationOnboardingApiClient(api).DesignateShadowAsync(WorkspaceId, new(1, "Race."));

        response.Success.Should().BeFalse();
        response.StatusCode.Should().Be(409);
        response.ErrorMessage.Should().Be("Another onboarding workspace already holds this ledger book.");
        WorkstationOnboardingApiClient.ReadErrorMessage("plain text").Should().Be("plain text");
    }

    [Fact]
    public async Task ResponsesForAnotherWorkspaceOrBook_AreRefused()
    {
        using var other = Api(_ => Json(OnboardingShadowWorkspaceTests.Workspace("another-workspace", 2)));
        var workspace = await new WorkstationOnboardingApiClient(other).GetWorkspaceAsync(WorkspaceId);
        workspace.Success.Should().BeFalse();
        workspace.StatusCode.Should().Be(502);

        var book = Guid.NewGuid();
        using var status = Api(_ => Json(new BookOfRecordStatusDto(Guid.NewGuid(), true, null, null, "Book of record.")));
        var bookStatus = await new WorkstationOnboardingApiClient(status).GetBookOfRecordStatusAsync(book);
        bookStatus.Success.Should().BeFalse();
        bookStatus.StatusCode.Should().Be(502);
    }

    [Fact]
    public async Task ListAndOpeningImport_UseTheSharedRoutes()
    {
        var paths = new List<string>();
        using var api = Api(request =>
        {
            paths.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            return request.Method == HttpMethod.Get
                ? Json(new[] { OnboardingShadowWorkspaceTests.Workspace(WorkspaceId, 1) })
                : Json(OnboardingShadowWorkspaceTests.Workspace(WorkspaceId, 2));
        });
        var client = new WorkstationOnboardingApiClient(api);

        (await client.ListWorkspacesAsync()).Data.Should().ContainSingle();
        (await client.ImportOpeningBalancesAsync(WorkspaceId, new(1, OnboardingOpeningSourceKinds.AdministratorPack, "map", "v1", [],
            "opening.csv", Convert.ToBase64String("x"u8.ToArray())))).Success.Should().BeTrue();

        paths.Should().Equal("GET /api/accounting/onboarding/workspaces",
            $"POST /api/accounting/onboarding/workspaces/{WorkspaceId}/opening-balances");
    }

    private static ApiClientService Api(Func<HttpRequestMessage, HttpResponseMessage> respond) => new(new Factory(respond));

    private static HttpResponseMessage Json<T>(T result) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json")
    };

    private sealed class Factory(Func<HttpRequestMessage, HttpResponseMessage> respond) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(respond));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}

public sealed class OnboardingShadowWorkspaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WithoutAClient_TheWorkspaceIsExplicitlyUnavailable()
    {
        using var model = new OnboardingShadowWorkspace(null);

        model.IsAvailable.Should().BeFalse();
        model.Status.Should().Contain("unavailable");
        model.LoadCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Designation_CarriesTheWorkspaceVersionAndShowsTheShadowState()
    {
        var client = new Client();
        client.Workspaces["w1"] = Workspace("w1", 3);
        using var model = await OpenAsync(client, "w1");

        model.IsNotDesignated.Should().BeTrue();
        model.DesignateCommand.CanExecute(null).Should().BeFalse("a rationale is required");
        model.ShadowRationale = "Run beside the administrator.";
        client.Next = Workspace("w1", 4) with { ShadowDesignation = Designation(OnboardingShadowStates.Shadow) };
        await model.DesignateCommand.ExecuteAsync(null);

        client.Requests.Should().ContainSingle().Which.Should().Be(("designate", "w1", 3));
        model.IsInShadow.Should().BeTrue();
        model.DesignationTitle.Should().Be("Shadow book — not the book of record");
        model.ShadowRationale.Should().BeEmpty();
        model.Status.Should().Be("Ledger book designated as a shadow book.");
    }

    [Fact]
    public async Task OpeningBreaksAndPeriodDifferences_AreExplainableButMissingLinesAreNot()
    {
        var client = new Client();
        client.Workspaces["w1"] = ShadowWithDifferences();
        using var model = await OpenAsync(client, "w1");

        model.Explainable.Select(item => (item.Label, item.ComparisonId, item.DifferenceKey)).Should().Equal(
            ("Opening", "open-1", "Balance|a1|USD|"),
            ("2026-01-31", "cmp-1", "Nav|a1|USD|"));
        model.HasUncoveredKinds.Should().BeTrue();
        model.UncoveredKindsMessage.Should().Contain("Position");
        model.OpeningSources.Should().ContainSingle().Which.Should().StartWith("StatementOpening · Balance");

        model.SelectedDifference = model.Explainable[0];
        model.ExplanationNotes = "Wire in flight.";
        model.ExplanationEvidence = "ev-1, ev-2";
        model.ExplainCommand.CanExecute(null).Should().BeFalse("a timing explanation needs its expected reversal date");
        model.ExpectedReversalDate = "2026-02-03";
        client.Next = ShadowWithDifferences() with { Version = 6 };
        await model.ExplainCommand.ExecuteAsync(null);

        client.Requests.Should().ContainSingle().Which.Should().Be(("explain:Balance|a1|USD|", "w1", 5));
        client.LastExplanation!.ComparisonId.Should().Be("open-1");
        client.LastExplanation.EvidenceIds.Should().Equal("ev-1", "ev-2");
        client.LastExplanation.ExpectedReversalDate.Should().Be(new DateOnly(2026, 2, 3));
    }

    [Fact]
    public async Task Conflict_KeepsInputsAndReloadsTheServerVersion()
    {
        var client = new Client();
        client.Workspaces["w1"] = Workspace("w1", 3);
        using var model = await OpenAsync(client, "w1");
        model.ShadowRationale = "Retry me.";
        client.Workspaces["w1"] = Workspace("w1", 5);
        client.Failure = ApiResponse<OnboardingWorkspaceDto>.Fail("Another onboarding workspace already holds this ledger book.", 409);

        await model.DesignateCommand.ExecuteAsync(null);

        model.Workspace!.Version.Should().Be(5);
        model.ShadowRationale.Should().Be("Retry me.");
        model.Status.Should().Contain("Conflict: Another onboarding workspace").And.Contain("version 5");
    }

    [Fact]
    public async Task OpeningImport_EncodesTheFileAndParsesAccountMappings()
    {
        var client = new Client();
        client.Workspaces["w1"] = Workspace("w1", 5) with { ShadowDesignation = Designation(OnboardingShadowStates.Shadow) };
        using var model = await OpenAsync(client, "w1");
        model.MappingProfileId = "admin-map";
        model.MappingVersion = "v1";
        model.AccountMappings = "ADMIN-001=a1\nDE89370400440532013000=a2";
        model.ImportOpeningCommand.CanExecute(null).Should().BeFalse("a file source needs its file");
        model.SetOpeningFile(@"C:\incumbent\opening.csv", "record_type"u8.ToArray());
        client.Next = Workspace("w1", 6) with { ShadowDesignation = Designation(OnboardingShadowStates.Shadow) };

        await model.ImportOpeningCommand.ExecuteAsync(null);

        var request = client.LastImport!;
        request.ExpectedVersion.Should().Be(5);
        request.FileName.Should().Be("opening.csv");
        request.ContentBase64.Should().Be(Convert.ToBase64String("record_type"u8.ToArray()));
        request.AccountMappings.Should().Equal(new OnboardingOpeningAccountMappingDto("ADMIN-001", "a1"),
            new OnboardingOpeningAccountMappingDto("DE89370400440532013000", "a2"));
        model.OpeningFileName.Should().BeEmpty();

        model.OpeningSourceKind = OnboardingOpeningSourceKinds.GlTrialBalance;
        model.OpeningProviderId = "quickbooks";
        model.OpeningImportId = "import-1";
        model.ImportOpeningCommand.CanExecute(null).Should().BeTrue();
        model.AccountMappings = "missing-separator";
        await model.ImportOpeningCommand.ExecuteAsync(null);
        model.Status.Should().Contain("EXTERNAL=account-id");
    }

    [Fact]
    public async Task CertificateAndPromotion_FollowReadinessAndTheSelectedCertificate()
    {
        var client = new Client();
        var certificate = Certificate("w1", "cert-1");
        client.Workspaces["w1"] = Workspace("w1", 7) with
        {
            ShadowDesignation = Designation(OnboardingShadowStates.Shadow),
            Readiness = Readiness(isReady: true)
        };
        using var model = await OpenAsync(client, "w1");
        model.IssueCertificateCommand.CanExecute(null).Should().BeTrue();
        client.IssuedCertificate = certificate;
        client.Workspaces["w1"] = client.Workspaces["w1"] with { Version = 8, Certificates = [certificate] };

        await model.IssueCertificateCommand.ExecuteAsync(null);

        model.SelectedCertificate!.CertificateId.Should().Be("cert-1");
        model.CertificateDetail.Should().Contain("sha256").And.Contain("2026-01-31");
        model.PromotionRationale = "Cutover.";
        client.Next = client.Workspaces["w1"] with { Version = 9, ShadowDesignation = Designation(OnboardingShadowStates.Promoted) };
        await model.PromoteCommand.ExecuteAsync(null);

        client.Requests.Should().Contain(("promote:cert-1", "w1", 8));
        model.DesignationTitle.Should().Be("Promoted to book of record");
        model.IsInShadow.Should().BeFalse();
        model.PromoteCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task LateResponseFromAnEarlierSelection_IsDiscarded()
    {
        var client = new Client();
        client.Workspaces["w1"] = Workspace("w1", 1) with { Name = "First" };
        client.Workspaces["w2"] = Workspace("w2", 1) with { Name = "Second" };
        var slow = new TaskCompletionSource<ApiResponse<OnboardingWorkspaceDto>>();
        client.SlowGet = slow;
        using var model = new OnboardingShadowWorkspace(client);
        await model.LoadCommand.ExecuteAsync(null);

        model.SelectedWorkspace = model.Workspaces.Single(item => item.WorkspaceId == "w1");
        client.SlowGet = null;
        model.SelectedWorkspace = model.Workspaces.Single(item => item.WorkspaceId == "w2");
        await WaitForAsync(() => model.Workspace?.WorkspaceId == "w2");
        slow.SetResult(ApiResponse<OnboardingWorkspaceDto>.Ok(client.Workspaces["w1"]));
        await Task.Yield();

        model.Workspace!.Name.Should().Be("Second");
    }

    internal static OnboardingWorkspaceDto Workspace(string id, int version) => new(
        id, "Parallel run", "owner", new("tenant", "company", "entity", "fund", Guid.Parse("c0749249-2738-44be-b336-fb586f5b200b"), ["a1"],
            new(2026, 1, 1), new(2026, 3, 31)),
        version, 1, Now, Now,
        new([new(2026, 1, 31)], ["Balance", "Position", "Nav"], 0.01m, 0.01m, 0.01m, 100m, ["reviewer"], 1, false, ""),
        [], [], [], [], [], [], Readiness(isReady: false), "ReadOnly");

    private static OnboardingReadinessDto Readiness(bool isReady, IReadOnlyList<OnboardingPeriodDifferenceDto>? differences = null,
        OnboardingOpeningBalanceReportDto? opening = null) => new(isReady ? "Ready" : "Blocked", isReady,
        differences?.Count ?? 0, [], [], isReady ? [] : ["Explain every remaining difference."], isReady ? 1 : 0, 1,
        differences ?? [], [], opening);

    private static OnboardingWorkspaceDto ShadowWithDifferences()
    {
        var opening = new OnboardingOpeningBalanceReportDto("open-1", OnboardingOpeningSourceKinds.StatementOpening, new(2026, 1, 1), "abcdef0123456789",
            0, 0, 1, 1, false,
            [
                new("Balance|a1|USD|", "Balance", "a1", "USD", null, 100m, 90m, 10m, 0.01m, "Break", null, null, "open-1", OnboardingOpeningSourceKinds.StatementOpening),
                new("Nav|a1|USD|", "Nav", "a1", "USD", null, 500m, null, null, 0.01m, "Missing", null, "No incumbent NAV.", "open-1", OnboardingOpeningSourceKinds.StatementOpening)
            ], [],
            [new(OnboardingOpeningSourceKinds.StatementOpening, "open-1", "abcdef0123456789", ["Balance"], Now)], ["Position", "Nav"]);
        var difference = new OnboardingDifferenceDto("Nav|a1|USD|", "Nav", "a1", "USD", null, 500m, 499m, 1m, 0.01m, "New", "owner",
            "cmp-1", "cmp-1", [], null);
        var missing = difference with { DifferenceKey = "Position|a1|USD|X", Kind = "Position", Difference = null, ExternalAmount = null };
        return Workspace("w1", 5) with
        {
            ShadowDesignation = Designation(OnboardingShadowStates.Shadow),
            Readiness = Readiness(false, [new("cmp-1", new(2026, 1, 31), difference), new("cmp-1", new(2026, 1, 31), missing)], opening)
        };
    }

    private static OnboardingShadowDesignationDto Designation(string state)
        => new(state, Guid.Parse("c0749249-2738-44be-b336-fb586f5b200b"), "owner", Now, "Parallel run.",
            state == OnboardingShadowStates.Shadow ? null : "reviewer", state == OnboardingShadowStates.Shadow ? null : Now);

    private static OnboardingParallelRunCertificateDto Certificate(string workspaceId, string id)
    {
        var workspace = Workspace(workspaceId, 7);
        var opening = new OnboardingOpeningBalanceReportDto("open-1", OnboardingOpeningSourceKinds.AdministratorPack, new(2026, 1, 1), "hash",
            3, 0, 0, 0, true, [], []);
        return new(id, Now, "owner", new(workspaceId, workspace.Name, workspace.Scope, 1, opening, [], [new(2026, 1, 31)], [], [],
            "packet-1", "packet-hash", "Meridian agreed with the incumbent for every required period."), "content-hash", "sha256");
    }

    private static async Task<OnboardingShadowWorkspace> OpenAsync(Client client, string id)
    {
        var model = new OnboardingShadowWorkspace(client);
        await model.LoadCommand.ExecuteAsync(null);
        model.SelectedWorkspace = model.Workspaces.Single(item => item.WorkspaceId == id);
        await WaitForAsync(() => model.Workspace?.WorkspaceId == id && !model.IsBusy);
        client.Requests.Clear();
        return model;
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
            await Task.Delay(5);
        condition().Should().BeTrue();
    }

    private sealed class Client : IWorkstationOnboardingApiClient
    {
        public Dictionary<string, OnboardingWorkspaceDto> Workspaces { get; } = [];
        public List<(string Action, string WorkspaceId, int Version)> Requests { get; } = [];
        public OnboardingWorkspaceDto? Next { get; set; }
        public ApiResponse<OnboardingWorkspaceDto>? Failure { get; set; }
        public TaskCompletionSource<ApiResponse<OnboardingWorkspaceDto>>? SlowGet { get; set; }
        public OnboardingParallelRunCertificateDto? IssuedCertificate { get; set; }
        public ExplainOnboardingDifferenceRequestDto? LastExplanation { get; private set; }
        public ImportOnboardingOpeningBalancesRequestDto? LastImport { get; private set; }

        public Task<ApiResponse<IReadOnlyList<OnboardingWorkspaceDto>>> ListWorkspacesAsync(CancellationToken ct = default)
            => Task.FromResult(ApiResponse<IReadOnlyList<OnboardingWorkspaceDto>>.Ok(Workspaces.Values.ToArray()));

        public Task<ApiResponse<OnboardingWorkspaceDto>> GetWorkspaceAsync(string workspaceId, CancellationToken ct = default)
            => SlowGet?.Task ?? Task.FromResult(ApiResponse<OnboardingWorkspaceDto>.Ok(Workspaces[workspaceId]));

        public Task<ApiResponse<OnboardingWorkspaceDto>> DesignateShadowAsync(string workspaceId, DesignateShadowBookRequestDto request, CancellationToken ct = default)
            => Mutate("designate", workspaceId, request.ExpectedVersion);

        public Task<ApiResponse<OnboardingWorkspaceDto>> WithdrawShadowAsync(string workspaceId, WithdrawShadowBookRequestDto request, CancellationToken ct = default)
            => Mutate("withdraw", workspaceId, request.ExpectedVersion);

        public Task<ApiResponse<OnboardingWorkspaceDto>> ImportOpeningBalancesAsync(string workspaceId, ImportOnboardingOpeningBalancesRequestDto request, CancellationToken ct = default)
        {
            LastImport = request;
            return Mutate("import", workspaceId, request.ExpectedVersion);
        }

        public Task<ApiResponse<OnboardingOpeningBalanceReportDto>> GetOpeningReportAsync(string workspaceId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<OnboardingWorkspaceDto>> ExplainDifferenceAsync(string workspaceId, string differenceKey, ExplainOnboardingDifferenceRequestDto request, CancellationToken ct = default)
        {
            LastExplanation = request;
            return Mutate("explain:" + differenceKey, workspaceId, request.ExpectedVersion);
        }

        public Task<ApiResponse<OnboardingParallelRunCertificateDto>> IssueCertificateAsync(string workspaceId, IssueParallelRunCertificateRequestDto request, CancellationToken ct = default)
        {
            Requests.Add(("issue", workspaceId, request.ExpectedVersion));
            return Task.FromResult(ApiResponse<OnboardingParallelRunCertificateDto>.Ok(IssuedCertificate!));
        }

        public Task<ApiResponse<OnboardingParallelRunCertificateDto>> GetCertificateAsync(string workspaceId, string certificateId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ApiResponse<OnboardingWorkspaceDto>> PromoteAsync(string workspaceId, PromoteShadowBookRequestDto request, CancellationToken ct = default)
            => Mutate("promote:" + request.CertificateId, workspaceId, request.ExpectedVersion);

        public Task<ApiResponse<BookOfRecordStatusDto>> GetBookOfRecordStatusAsync(Guid ledgerBookId, CancellationToken ct = default)
            => throw new NotSupportedException();

        private Task<ApiResponse<OnboardingWorkspaceDto>> Mutate(string action, string workspaceId, int version)
        {
            Requests.Add((action, workspaceId, version));
            if (Failure is { } failure)
                return Task.FromResult(failure);
            var next = Next ?? Workspaces[workspaceId];
            Workspaces[workspaceId] = next;
            return Task.FromResult(ApiResponse<OnboardingWorkspaceDto>.Ok(next));
        }
    }
}
