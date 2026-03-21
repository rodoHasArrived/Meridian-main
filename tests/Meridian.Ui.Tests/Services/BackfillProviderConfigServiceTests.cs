using FluentAssertions;
using Meridian.Contracts.Configuration;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="BackfillProviderConfigService"/> — provider metadata,
/// fallback chain, dry-run planning, and audit trail.
/// </summary>
public sealed class BackfillProviderConfigServiceTests
{
    [Fact]
    public void Instance_ReturnsNonNullSingleton()
    {
        var instance = BackfillProviderConfigService.Instance;
        instance.Should().NotBeNull();
    }

    [Fact]
    public void Instance_ReturnsSameInstance()
    {
        var a = BackfillProviderConfigService.Instance;
        var b = BackfillProviderConfigService.Instance;
        a.Should().BeSameAs(b);
    }

    [Fact]
    public async Task GetProviderMetadataAsync_ReturnsAllKnownProviders()
    {
        var service = BackfillProviderConfigService.Instance;

        var metadata = await service.GetProviderMetadataAsync();

        metadata.Should().NotBeEmpty();
        metadata.Select(m => m.ProviderId).Should().Contain("alpaca");
        metadata.Select(m => m.ProviderId).Should().Contain("polygon");
        metadata.Select(m => m.ProviderId).Should().Contain("stooq");
        metadata.Select(m => m.ProviderId).Should().Contain("yahoo");
    }

    [Fact]
    public async Task GetProviderMetadataAsync_EachProviderHasRequiredFields()
    {
        var service = BackfillProviderConfigService.Instance;

        var metadata = await service.GetProviderMetadataAsync();

        foreach (var m in metadata)
        {
            m.ProviderId.Should().NotBeNullOrEmpty();
            m.DisplayName.Should().NotBeNullOrEmpty();
            m.Description.Should().NotBeNullOrEmpty();
            m.DataTypes.Should().NotBeEmpty();
            m.DefaultPriority.Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [Fact]
    public async Task GetProviderStatusesAsync_WithNullConfig_ReturnsDefaults()
    {
        var service = BackfillProviderConfigService.Instance;

        var statuses = await service.GetProviderStatusesAsync(null);

        statuses.Should().NotBeEmpty();
        statuses.Should().AllSatisfy(s =>
        {
            s.Metadata.Should().NotBeNull();
            s.Options.Should().NotBeNull();
            s.Options.Enabled.Should().BeTrue("default should be enabled");
        });
    }

    [Fact]
    public async Task GetProviderStatusesAsync_SortedByPriority()
    {
        var service = BackfillProviderConfigService.Instance;

        var statuses = await service.GetProviderStatusesAsync(null);

        var priorities = statuses
            .Select(s => s.Options.Priority ?? s.Metadata.DefaultPriority)
            .ToList();

        priorities.Should().BeInAscendingOrder("providers should be sorted by priority");
    }

    [Fact]
    public async Task GetProviderStatusesAsync_WithCustomConfig_UsesConfigValues()
    {
        var service = BackfillProviderConfigService.Instance;

        var config = new BackfillProvidersConfigDto
        {
            Alpaca = new BackfillProviderOptionsDto
            {
                Enabled = false,
                Priority = 99,
                RateLimitPerMinute = 10,
            },
        };

        var statuses = await service.GetProviderStatusesAsync(config);

        var alpaca = statuses.FirstOrDefault(s => s.Metadata.ProviderId == "alpaca");
        alpaca.Should().NotBeNull();
        alpaca!.Options.Enabled.Should().BeFalse();
        alpaca.Options.Priority.Should().Be(99);
        alpaca.Options.RateLimitPerMinute.Should().Be(10);
    }

    [Fact]
    public async Task GetFallbackChainAsync_OnlyIncludesEnabledProviders()
    {
        var service = BackfillProviderConfigService.Instance;

        var config = new BackfillProvidersConfigDto
        {
            Alpaca = new BackfillProviderOptionsDto { Enabled = false },
            Polygon = new BackfillProviderOptionsDto { Enabled = false },
        };

        var chain = await service.GetFallbackChainAsync(config);

        chain.Should().NotContain(s => s.Metadata.ProviderId == "alpaca");
        chain.Should().NotContain(s => s.Metadata.ProviderId == "polygon");
        chain.Should().OnlyContain(s => s.Options.Enabled, "chain should only contain enabled providers");
    }

    [Fact]
    public async Task GetFallbackChainAsync_WithAllDefaults_ReturnsAllProviders()
    {
        var service = BackfillProviderConfigService.Instance;

        var chain = await service.GetFallbackChainAsync(null);

        chain.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public async Task GenerateDryRunPlanAsync_ReturnsSymbolPlans()
    {
        var service = BackfillProviderConfigService.Instance;
        var symbols = new[] { "SPY", "AAPL" };

        var plan = await service.GenerateDryRunPlanAsync(null, symbols);

        plan.Should().NotBeNull();
        plan.Symbols.Should().HaveCount(2);
        plan.Symbols[0].Symbol.Should().Be("SPY");
        plan.Symbols[1].Symbol.Should().Be("AAPL");
        plan.ValidationErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateDryRunPlanAsync_EachSymbolHasProviderSequence()
    {
        var service = BackfillProviderConfigService.Instance;
        var symbols = new[] { "MSFT" };

        var plan = await service.GenerateDryRunPlanAsync(null, symbols);

        plan.Symbols[0].ProviderSequence.Should().NotBeEmpty();
        plan.Symbols[0].SelectedProvider.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GenerateDryRunPlanAsync_WithAllDisabled_ReturnsValidationError()
    {
        var service = BackfillProviderConfigService.Instance;
        var config = new BackfillProvidersConfigDto
        {
            Alpaca = new BackfillProviderOptionsDto { Enabled = false },
            Polygon = new BackfillProviderOptionsDto { Enabled = false },
            Tiingo = new BackfillProviderOptionsDto { Enabled = false },
            Finnhub = new BackfillProviderOptionsDto { Enabled = false },
            Stooq = new BackfillProviderOptionsDto { Enabled = false },
            Yahoo = new BackfillProviderOptionsDto { Enabled = false },
            AlphaVantage = new BackfillProviderOptionsDto { Enabled = false },
            NasdaqDataLink = new BackfillProviderOptionsDto { Enabled = false },
        };

        var plan = await service.GenerateDryRunPlanAsync(config, ["SPY"]);

        plan.ValidationErrors.Should().NotBeEmpty();
        plan.ValidationErrors.Should().Contain(e => e.Contains("No enabled providers"));
    }

    [Fact]
    public async Task GenerateDryRunPlanAsync_DetectsDuplicatePriorities()
    {
        var service = BackfillProviderConfigService.Instance;
        var config = new BackfillProvidersConfigDto
        {
            Alpaca = new BackfillProviderOptionsDto { Enabled = true, Priority = 10 },
            Polygon = new BackfillProviderOptionsDto { Enabled = true, Priority = 10 },
        };

        var plan = await service.GenerateDryRunPlanAsync(config, ["SPY"]);

        plan.Warnings.Should().Contain(w => w.Contains("share priority"));
    }

    [Fact]
    public void RecordAuditEntry_AddsEntry()
    {
        var service = BackfillProviderConfigService.Instance;
        var initialCount = service.GetAuditLog().Count;

        service.RecordAuditEntry("test-provider", "test-action", "old", "new");

        var entries = service.GetAuditLog();
        entries.Count.Should().BeGreaterThan(initialCount);
        entries.Should().Contain(e => e.ProviderId == "test-provider" && e.Action == "test-action");
    }

    [Fact]
    public void GetAuditLog_RespectsMaxEntries()
    {
        var service = BackfillProviderConfigService.Instance;

        // Add multiple entries
        for (int i = 0; i < 10; i++)
        {
            service.RecordAuditEntry($"provider-{i}", "bulk-test", null, null);
        }

        var entries = service.GetAuditLog(maxEntries: 3);
        entries.Should().HaveCountLessThanOrEqualTo(3);
    }

    [Fact]
    public void GetAuditLog_OrderedByTimestampDescending()
    {
        var service = BackfillProviderConfigService.Instance;
        service.RecordAuditEntry("first", "create", null, "v1");
        service.RecordAuditEntry("second", "create", null, "v2");

        var entries = service.GetAuditLog();
        var timestamps = entries.Select(e => e.Timestamp).ToList();
        timestamps.Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task GetDefaultOptionsAsync_ReturnsMatchingDefaults()
    {
        var service = BackfillProviderConfigService.Instance;

        var defaults = await service.GetDefaultOptionsAsync("alpaca");

        defaults.Enabled.Should().BeTrue();
        defaults.Priority.Should().Be(5);
        defaults.RateLimitPerMinute.Should().Be(200);
    }

    [Fact]
    public async Task GetDefaultOptionsAsync_UnknownProvider_ReturnsGenericDefaults()
    {
        var service = BackfillProviderConfigService.Instance;

        var defaults = await service.GetDefaultOptionsAsync("unknown-provider");

        defaults.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task GetProviderStatusesAsync_ConfigSourceBadge_IsDefault_WhenNoOverrides()
    {
        var service = BackfillProviderConfigService.Instance;

        var statuses = await service.GetProviderStatusesAsync(null);

        statuses.Should().AllSatisfy(s =>
        {
            s.EffectiveConfigSource.Should().Be("default");
        });
    }

    [Fact]
    public async Task GetProviderStatusesAsync_ConfigSourceBadge_IsUser_WhenOverridden()
    {
        var service = BackfillProviderConfigService.Instance;
        var config = new BackfillProvidersConfigDto
        {
            Alpaca = new BackfillProviderOptionsDto
            {
                Enabled = true,
                Priority = 999,
                RateLimitPerMinute = 200,
            },
        };

        var statuses = await service.GetProviderStatusesAsync(config);

        var alpaca = statuses.First(s => s.Metadata.ProviderId == "alpaca");
        alpaca.EffectiveConfigSource.Should().Be("user");
    }

    // Phase 3: Feature flag tests

    [Fact]
    public async Task GetProviderMetadataAsync_SomeProvidersHaveFeatureFlags()
    {
        var service = BackfillProviderConfigService.Instance;

        var metadata = await service.GetProviderMetadataAsync();

        var alpaca = metadata.First(m => m.ProviderId == "alpaca");
        alpaca.FeatureFlags.Should().NotBeNull();
        alpaca.FeatureFlags.Should().ContainKey("supportsCrypto");
    }

    [Fact]
    public async Task GetProviderFeatureFlagsAsync_ReturnsFlags()
    {
        var service = BackfillProviderConfigService.Instance;

        var flags = await service.GetProviderFeatureFlagsAsync("alpaca");

        flags.Should().ContainKey("supportsCrypto");
        flags["supportsCrypto"].Should().BeTrue();
    }

    [Fact]
    public async Task GetProviderFeatureFlagsAsync_UnknownProvider_ReturnsEmptyDictionary()
    {
        var service = BackfillProviderConfigService.Instance;

        var flags = await service.GetProviderFeatureFlagsAsync("nonexistent");

        flags.Should().BeEmpty();
    }

    [Fact]
    public async Task IsProviderFeatureEnabledAsync_ReturnsTrueForKnownFlag()
    {
        var service = BackfillProviderConfigService.Instance;

        var result = await service.IsProviderFeatureEnabledAsync("polygon", "supportsAggregates");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsProviderFeatureEnabledAsync_ReturnsFalseForUnknownFlag()
    {
        var service = BackfillProviderConfigService.Instance;

        var result = await service.IsProviderFeatureEnabledAsync("alpaca", "nonexistentFlag");

        result.Should().BeFalse();
    }

    // Phase 4: Audit delta summary tests

    [Fact]
    public void ComputeAuditDeltaSummary_NullPrevious_ReturnsInitial()
    {
        var summary = BackfillProviderConfigService.ComputeAuditDeltaSummary(null, "{}");

        summary.Should().Be("Initial configuration set");
    }

    [Fact]
    public void ComputeAuditDeltaSummary_NullNew_ReturnsRemoved()
    {
        var summary = BackfillProviderConfigService.ComputeAuditDeltaSummary("{}", null);

        summary.Should().Be("Configuration removed");
    }

    [Fact]
    public void ComputeAuditDeltaSummary_PriorityChanged_ShowsDelta()
    {
        var prev = System.Text.Json.JsonSerializer.Serialize(new BackfillProviderOptionsDto
        {
            Enabled = true,
            Priority = 5,
        });
        var next = System.Text.Json.JsonSerializer.Serialize(new BackfillProviderOptionsDto
        {
            Enabled = true,
            Priority = 10,
        });

        var summary = BackfillProviderConfigService.ComputeAuditDeltaSummary(prev, next);

        summary.Should().Contain("Priority: 5");
        summary.Should().Contain("10");
    }

    [Fact]
    public void ComputeAuditDeltaSummary_EnabledChanged_ShowsToggle()
    {
        var prev = System.Text.Json.JsonSerializer.Serialize(new BackfillProviderOptionsDto
        {
            Enabled = true,
            Priority = 5,
        });
        var next = System.Text.Json.JsonSerializer.Serialize(new BackfillProviderOptionsDto
        {
            Enabled = false,
            Priority = 5,
        });

        var summary = BackfillProviderConfigService.ComputeAuditDeltaSummary(prev, next);

        summary.Should().Contain("Disabled");
    }

    [Fact]
    public void ComputeAuditDeltaSummary_NoChange_ReturnsNoChange()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new BackfillProviderOptionsDto
        {
            Enabled = true,
            Priority = 5,
        });

        var summary = BackfillProviderConfigService.ComputeAuditDeltaSummary(json, json);

        summary.Should().Be("No change");
    }

    [Fact]
    public void ComputeAuditDeltaSummary_MultipleChanges_ShowsAll()
    {
        var prev = System.Text.Json.JsonSerializer.Serialize(new BackfillProviderOptionsDto
        {
            Enabled = true,
            Priority = 5,
            RateLimitPerMinute = 200,
        });
        var next = System.Text.Json.JsonSerializer.Serialize(new BackfillProviderOptionsDto
        {
            Enabled = false,
            Priority = 10,
            RateLimitPerMinute = 100,
        });

        var summary = BackfillProviderConfigService.ComputeAuditDeltaSummary(prev, next);

        summary.Should().Contain("Disabled");
        summary.Should().Contain("Priority");
        summary.Should().Contain("Rate/min");
    }
}
