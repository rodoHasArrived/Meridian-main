using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Instruments.FixedIncome;
using Meridian.Storage.FixedIncome;
using NSubstitute;

namespace Meridian.Tests.FixedIncome;

public sealed class BondMonitoringVersionAuthorityTests
{
    [Fact]
    public async Task LegacyPlanRevalidatesWithItsExactModelAfterTheDefaultAdvances()
    {
        using var fixture = new Fixture();
        fixture.DefaultVersion = BondWorkflowModelVersions.Legacy;
        var plan = await fixture.PlanAsync();
        plan.Obligations.Should().OnlyContain(row => row.ModelVersion == BondWorkflowModelVersions.Legacy);
        fixture.DefaultVersion = BondWorkflowModelVersions.Current;
        fixture.Calls.Clear();

        var captured = await fixture.Monitor.CaptureAsync(plan);

        fixture.Calls.Should().ContainSingle().Which.ModelVersion.Should().Be(BondWorkflowModelVersions.Legacy);
        captured.Inputs.Obligations.Should().BeEquivalentTo(plan.Obligations);
        captured.Inputs.Obligations.Single(row => row.FlowType == "Coupon").Amount.Should().Be(10m,
            "the new default would have produced 20, so revalidation must select the retained v1 equation");
        (await fixture.Monitor.GetHistoryAsync(Fixture.Scope)).Should().ContainSingle();
    }

    [Fact]
    public async Task CurrentPlanRevalidatesWithItsExactModel()
    {
        using var fixture = new Fixture();
        var plan = await fixture.PlanAsync();
        fixture.Calls.Clear();

        var captured = await fixture.Monitor.CaptureAsync(plan);

        fixture.Calls.Should().ContainSingle().Which.ModelVersion.Should().Be(BondWorkflowModelVersions.Current);
        captured.Inputs.Obligations.Single(row => row.FlowType == "Coupon").Amount.Should().Be(20m);
    }

    [Theory]
    [InlineData("unsupported")]
    [InlineData("missing-manifest")]
    [InlineData("different-manifest")]
    [InlineData("duplicate-manifest")]
    [InlineData("mixed-models")]
    public async Task AmbiguousOrUnsupportedRetainedModelEvidenceRefusesBeforeRepricingOrStoring(string defect)
    {
        using var fixture = new Fixture();
        var plan = await fixture.PlanAsync();
        var first = plan.Obligations[0];
        var altered = defect switch
        {
            "unsupported" => first with
            {
                ModelVersion = "bond-workflow-future",
                Inputs = ReplaceModel(first, "bond-workflow-future")
            },
            "missing-manifest" => first with { Inputs = first.Inputs.Where(row => row.Kind != "CalculationModel").ToArray() },
            "different-manifest" => first with { Inputs = ReplaceModel(first, BondWorkflowModelVersions.Legacy) },
            "duplicate-manifest" => first with
            { Inputs = first.Inputs.Append(new BondCalculationInput("CalculationModel", first.ModelVersion)).ToArray() },
            _ => first with
            { ModelVersion = BondWorkflowModelVersions.Legacy, Inputs = ReplaceModel(first, BondWorkflowModelVersions.Legacy) }
        };
        plan = plan with { Obligations = plan.Obligations.Select((row, index) => index == 0 ? altered : row).ToArray() };
        fixture.Calls.Clear();

        var capture = () => fixture.Monitor.CaptureAsync(plan);

        await capture.Should().ThrowAsync<InvalidOperationException>();
        fixture.Calls.Should().BeEmpty();
        (await fixture.Monitor.GetHistoryAsync(Fixture.Scope)).Should().BeEmpty();
    }

    [Fact]
    public async Task SourceReturningAnotherModelCannotCertifyTheRetainedObligations()
    {
        using var fixture = new Fixture();
        var plan = await fixture.PlanAsync();
        fixture.ReturnedVersion = BondWorkflowModelVersions.Legacy;

        var capture = () => fixture.Monitor.CaptureAsync(plan);

        await capture.Should().ThrowAsync<InvalidOperationException>();
        (await fixture.Monitor.GetHistoryAsync(Fixture.Scope)).Should().BeEmpty();
    }

    private static BondCalculationInput[] ReplaceModel(BondMonitoringObligationDto obligation, string model)
        => obligation.Inputs.Where(row => row.Kind != "CalculationModel")
            .Append(new BondCalculationInput("CalculationModel", model)).ToArray();

    private sealed class Fixture : IDisposable
    {
        private static readonly DateOnly Today = new(2026, 1, 1);
        private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        private static readonly Guid Account = Guid.Parse("594c38ca-f39a-44a1-8273-2e306c1dfb6a");
        private static readonly Guid Security = Guid.Parse("9a512793-2cf7-4c7a-a966-c95c4ab53998");
        private static readonly Guid Batch = Guid.Parse("f7b56c93-0f44-4462-bd3d-8de5edcd3830");
        public static readonly AccountCashForecastScopeDto Scope = new("synthetic-tenant", "synthetic-company",
            Account.ToString("D"), "synthetic-book", "USD");
        private readonly string _root = Path.Combine(Path.GetTempPath(), "bond-monitor-model-" + Guid.NewGuid().ToString("N"));
        private readonly AccountBondIncomePlanningService _planner;
        public string DefaultVersion { get; set; } = BondWorkflowModelVersions.Current;
        public string? ReturnedVersion { get; set; }
        public List<BondWorkflowRequest> Calls { get; } = [];
        public BondMonitoringService Monitor { get; }

        public Fixture()
        {
            var holdings = Substitute.For<IAccountBondHoldingsSource>();
            holdings.GetHoldingsAsync(Account, Today, Now, Arg.Any<CancellationToken>()).Returns(
                new AccountBondHoldingsSnapshotDto(Account, Today, Batch, Sha256Digest.ComputeUtf8("synthetic held face"), "synthetic",
                    [new(Guid.NewGuid(), Batch, Account, Today, "synthetic-bond", "ISIN", 1000m, 1000m,
                        "USD", "Synthetic bond", "Bond", false, SecurityId: Security,
                        Bond: new(1000m, 1000m, 1m, 1m, BondHoldingQuoteBasis.PercentOfCurrentFace, 100m, false, 0m,
                            InvoiceBasis: BondHoldingInvoiceBasis.Nominal))]));
            var bonds = Substitute.For<IBondWorkflowService>();
            bonds.CalculateAsync(Arg.Any<BondWorkflowRequest>(), Arg.Any<CancellationToken>()).Returns(info =>
            {
                var request = info.Arg<BondWorkflowRequest>();
                Calls.Add(request);
                var model = ReturnedVersion ?? request.ModelVersion ?? DefaultVersion;
                var interest = model == BondWorkflowModelVersions.Legacy ? 10m : 20m;
                return Task.FromResult<BondWorkflowResult?>(new(Security, "USD", model, request.Context with { SecurityVersion = 1 },
                    request.OriginalFace, 1000m, 0m, null, null, 0m, null, null, null, null, null, null, null,
                    [new(Today.AddDays(30), Today, Today.AddDays(30), interest, 1000m, 0m, 1m, false, [])],
                    [new("CalculationModel", model), new("SecurityTerms", Security.ToString("D"), 1, Sha256Digest.ComputeUtf8("terms"))], [], []));
            });
            Monitor = new(new BondMonitoringFileStore(_root), holdings, bonds);
            _planner = new(holdings, bonds, Monitor, new Clock());
        }

        public async Task<BondMonitoringCaptureRequest> PlanAsync()
            => (await _planner.PlanAsync(new(Scope, Today, Today.AddDays(60), new(Now, Today, Now), [], []))).MonitoringInputs;

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    }
}
