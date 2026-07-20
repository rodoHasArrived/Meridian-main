using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Execution;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Storage.Ledger;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Ui;

[Collection("Sequential")]
public sealed class TradeFillLedgerPostingHostCompositionTests
{
    [Fact]
    public void BuildContext_DerivesOneCanonicalBookPeriodAggregateScope()
    {
        var aggregateId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var periodId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var ledgerBookId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var options = new TradeFillLedgerPostingHostOptions
        {
            Enabled = true,
            AggregateId = aggregateId,
            PeriodId = periodId,
            LedgerBookId = ledgerBookId,
            ExpectedPeriodVersion = 1
        };

        var context = options.BuildContext();

        context.AggregateId.Should().Be(aggregateId);
        context.PeriodId.Should().Be(periodId);
        context.LedgerBookId.Should().Be(ledgerBookId);
        context.PostingScope.Should().Be(
            $"ledger-book/{ledgerBookId:D}/period/{periodId:D}/aggregate/{aggregateId:D}");
    }

    [Fact]
    public void UiServer_EnabledTradeFillPostingWithoutDurableLedger_FailsDuringComposition()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "meridian-tests",
            "trade-fill-host-composition",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "appsettings.json");
        File.WriteAllText(configPath, BuildConfig(root));

        var priorLedgerConnection = Environment.GetEnvironmentVariable("MERIDIAN_LEDGER_CONNECTION_STRING");
        var priorGovernance = Environment.GetEnvironmentVariable("MERIDIAN_USE_INMEMORY_GOVERNANCE");
        var priorAspNetCoreEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var priorDotnetEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        try
        {
            Environment.SetEnvironmentVariable("MERIDIAN_LEDGER_CONNECTION_STRING", null);
            Environment.SetEnvironmentVariable("MERIDIAN_USE_INMEMORY_GOVERNANCE", "true");
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", Environments.Development);
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", Environments.Development);

            Action compose = () => _ = new UiServer(configPath, port: 0);

            compose.Should().Throw<InvalidOperationException>()
                .WithMessage("*Execution:TradeFillLedgerPosting:Enabled requires MERIDIAN_LEDGER_CONNECTION_STRING*");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MERIDIAN_LEDGER_CONNECTION_STRING", priorLedgerConnection);
            Environment.SetEnvironmentVariable("MERIDIAN_USE_INMEMORY_GOVERNANCE", priorGovernance);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", priorAspNetCoreEnvironment);
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", priorDotnetEnvironment);
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }

    [Fact]
    public async Task AddBrokerageExecution_ExactPostingPrerequisites_ReplaysFallbackThroughOrderManager()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "meridian-tests",
            "trade-fill-service-composition",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var context = new TradeFillLedgerPostingHostOptions
        {
            Enabled = true,
            AggregateId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            PeriodId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            LedgerBookId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            ExpectedPeriodVersion = 1
        }.BuildContext();
        var retainedFill = new TradeExecutedEvent(
            Guid.NewGuid(),
            "prestart-order",
            "AAPL",
            OrderSide.Buy,
            10m,
            125m,
            0m,
            0m,
            98_750m,
            DateTimeOffset.UtcNow);
        var primaryStore = new WalTradeFillPostingStore(
            new TradeFillPostingStoreOptions(root, context),
            NullLogger<WalTradeFillPostingStore>.Instance);
        var fallbackStore = new AtomicTradeFillHandoffFailureStore(
            new TradeFillHandoffFailureStoreOptions(root, context));

        try
        {
            await fallbackStore.RetainAsync(retainedFill, "publisher unavailable before restart");
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ISecurityValidationGateService, NullSecurityValidationGateService>();
            services.AddTradeFillLedgerPosting(
                context,
                _ => new UnexpectedTradeFillLedgerPostingTarget(),
                _ => primaryStore,
                _ => fallbackStore,
                configure: options =>
                {
                    options.DrainTimeout = TimeSpan.FromSeconds(1);
                    options.CancellationTimeout = TimeSpan.FromSeconds(1);
                });
            services.AddBrokerageExecution();

            await using (var provider = services.BuildServiceProvider())
            {
                provider.GetRequiredService<IOrderManager>()
                    .Should()
                    .BeOfType<OrderManagementSystem>();

                await WaitUntilAsync(
                    async () => (await fallbackStore.LoadPendingAsync()).Count == 0,
                    "OMS did not replay the retained accounting handoff through its scoped publisher.");

                fallbackStore.ScopeIdentity.Should().Be(
                    TradeFillPostingScopeIdentity.FromContext(context));
                primaryStore.ScopeIdentity.Should().Be(
                    TradeFillPostingScopeIdentity.FromContext(context));
                (await primaryStore.LoadPendingAsync()).Should().ContainSingle(item =>
                    item.TradeEvent.FillId == retainedFill.FillId &&
                    item.PostingScope == context.PostingScope);
            }
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }

    private static string BuildConfig(string root)
    {
        var config = new
        {
            DataRoot = Path.Combine(root, "data"),
            Compress = false,
            DataSource = "Synthetic",
            ApiHost = new
            {
                Urls = new[] { "http://127.0.0.1:0" },
                ServeWorkstationAssets = false
            },
            Storage = new
            {
                NamingConvention = "BySymbol",
                DatePartition = "Daily",
                IncludeProvider = false
            },
            Backfill = new
            {
                Enabled = false,
                Provider = "stooq",
                Symbols = new[] { "SPY" }
            },
            Execution = new
            {
                TradeFillLedgerPosting = new
                {
                    Enabled = true,
                    AggregateId = "11111111-1111-1111-1111-111111111111",
                    PeriodId = "22222222-2222-2222-2222-222222222222",
                    LedgerBookId = "33333333-3333-3333-3333-333333333333"
                }
            }
        };

        return JsonSerializer.Serialize(config);
    }

    private static async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        string timeoutMessage)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!await condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException(timeoutMessage);

            await Task.Delay(20);
        }
    }

    private sealed class UnexpectedTradeFillLedgerPostingTarget : ITradeFillLedgerPostingTarget
    {
        public Task<TradeFillLedgerPostingConfirmation> PostAndConfirmAsync(
            LedgerJournalEntryWrite write,
            CancellationToken ct = default)
            => throw new InvalidOperationException(
                "The unconfigured Security Master gate must block journal posting in this composition test.");
    }
}
