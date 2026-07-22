using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Verifies the security-master coverage sweep: symbols active in registered sources but
/// lacking a validated security-master record surface as RC001 RefreshControl violations,
/// which the exception-casework auto-loop then turns into SLA-tracked cases.
/// </summary>
public sealed class SecurityMasterDataQualityServiceCoverageTests
{
    [Fact]
    public async Task RunQualityChecksAsync_FlagsUnmasteredActiveSymbol()
    {
        var resolver = Substitute.For<ISecurityResolver>();
        resolver.ResolveAsync(Arg.Any<ResolveSecurityRequest>(), Arg.Any<CancellationToken>())
            .Returns((Guid?)null);
        var service = CreateService(resolver, new StubCoverageSource("configured-streaming", "GME"));

        var report = await service.RunQualityChecksAsync();

        var violation = report.Violations.Should()
            .ContainSingle(static v => v.RuleId == "RC001").Subject;
        violation.Category.Should().Be(DataQualityRuleCategory.RefreshControl);
        violation.Severity.Should().Be(DataQualityRuleSeverity.Error);
        violation.FieldPath.Should().Be("symbol.GME");
        violation.Message.Should().Contain("configured-streaming");
        violation.SecurityId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task RunQualityChecksAsync_UnmasteredSymbolId_IsDeterministicAcrossRuns()
    {
        var resolver = Substitute.For<ISecurityResolver>();
        resolver.ResolveAsync(Arg.Any<ResolveSecurityRequest>(), Arg.Any<CancellationToken>())
            .Returns((Guid?)null);
        var service = CreateService(resolver, new StubCoverageSource("configured-streaming", "GME"));

        var first = await service.RunQualityChecksAsync();
        var second = await service.RunQualityChecksAsync();

        first.Violations.Single(static v => v.RuleId == "RC001").SecurityId
            .Should().Be(second.Violations.Single(static v => v.RuleId == "RC001").SecurityId,
                "casework dedupe keys on (rule, security, field), so the pseudo-id must be stable");
    }

    [Fact]
    public async Task RunQualityChecksAsync_MasteredSymbol_ProducesNoCoverageViolation()
    {
        var resolver = Substitute.For<ISecurityResolver>();
        resolver.ResolveAsync(Arg.Any<ResolveSecurityRequest>(), Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());
        var service = CreateService(resolver, new StubCoverageSource("configured-streaming", "AAPL"));

        var report = await service.RunQualityChecksAsync();

        report.Violations.Should().NotContain(static v => v.RuleId == "RC001");
    }

    [Fact]
    public async Task RunQualityChecksAsync_SameSymbolInTwoSources_ProducesOneViolationListingBoth()
    {
        var resolver = Substitute.For<ISecurityResolver>();
        resolver.ResolveAsync(Arg.Any<ResolveSecurityRequest>(), Arg.Any<CancellationToken>())
            .Returns((Guid?)null);
        var service = CreateService(
            resolver,
            new StubCoverageSource("configured-streaming", "GME"),
            new StubCoverageSource("ledger-positions", "gme"));

        var report = await service.RunQualityChecksAsync();

        var violation = report.Violations.Should()
            .ContainSingle(static v => v.RuleId == "RC001").Subject;
        violation.Message.Should().Contain("configured-streaming");
        violation.Message.Should().Contain("ledger-positions");
    }

    [Fact]
    public async Task RunQualityChecksAsync_WithoutCoverageSources_PreservesExistingBehaviour()
    {
        var service = new SecurityMasterDataQualityService(
            EmptyStore(),
            Substitute.For<ISecurityMasterQualityReportStore>(),
            NullLogger<SecurityMasterDataQualityService>.Instance);

        var report = await service.RunQualityChecksAsync();

        report.Violations.Should().BeEmpty();
    }

    private static SecurityMasterDataQualityService CreateService(
        ISecurityResolver resolver,
        params ISecurityCoverageSymbolSource[] sources)
        => new(
            EmptyStore(),
            Substitute.For<ISecurityMasterQualityReportStore>(),
            NullLogger<SecurityMasterDataQualityService>.Instance,
            sources,
            resolver);

    private static ISecurityMasterStore EmptyStore()
    {
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadActiveAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SecurityProjectionRecord>());
        return store;
    }

    private sealed class StubCoverageSource(string sourceName, params string[] symbols)
        : ISecurityCoverageSymbolSource
    {
        public string SourceName => sourceName;

        public Task<IReadOnlyCollection<string>> GetActiveSymbolsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyCollection<string>>(symbols);
    }
}
