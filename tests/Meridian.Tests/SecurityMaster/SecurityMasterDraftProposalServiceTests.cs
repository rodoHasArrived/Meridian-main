using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Core.SymbolResolution;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Verifies the coverage draft flow: resolved symbols produce a pre-filled machine-proposed
/// draft with external identifiers, unresolved symbols produce a minimal manual draft, and
/// every draft is provenance-marked so it can never pass as operator-entered data.
/// </summary>
public sealed class SecurityMasterDraftProposalServiceTests
{
    [Fact]
    public async Task BuildDraft_ResolvedSymbol_PreFillsIdentifiersAndMetadata()
    {
        var service = new SecurityMasterDraftProposalService(
            NullLogger<SecurityMasterDraftProposalService>.Instance,
            new StubSymbolResolver(new SymbolResolution(
                "GME",
                Figi: "BBG000BB5KF8",
                Isin: "US36467W1099",
                Cusip: "36467W109",
                Name: "GameStop Corp.",
                Exchange: "XNYS",
                Currency: "USD")));

        var draft = await service.BuildDraftAsync("gme");

        draft.Resolved.Should().BeTrue();
        draft.Symbol.Should().Be("GME");
        draft.DisplayName.Should().Be("GameStop Corp.");
        draft.Exchange.Should().Be("XNYS");
        draft.Currency.Should().Be("USD");
        draft.SourceSystem.Should().Be("coverage-draft");
        draft.Provenance.Should().Be("machine-proposed");
        draft.ProposedSecurityId.Should().NotBe(Guid.Empty);

        var ticker = draft.Identifiers.Should().ContainSingle(static id => id.Kind == SecurityIdentifierKind.Ticker).Subject;
        ticker.Value.Should().Be("GME");
        ticker.IsPrimary.Should().BeTrue();
        draft.Identifiers.Should().ContainSingle(static id => id.Kind == SecurityIdentifierKind.Isin)
            .Which.Value.Should().Be("US36467W1099");
        draft.Identifiers.Should().ContainSingle(static id => id.Kind == SecurityIdentifierKind.Cusip)
            .Which.Value.Should().Be("36467W109");
        draft.Identifiers.Should().ContainSingle(static id => id.Kind == SecurityIdentifierKind.Figi)
            .Which.Value.Should().Be("BBG000BB5KF8");
        draft.Identifiers.Should().NotContain(static id => id.Kind == SecurityIdentifierKind.Sedol);
    }

    [Fact]
    public async Task BuildDraft_UnresolvedSymbol_ProducesMinimalManualDraft()
    {
        var service = new SecurityMasterDraftProposalService(
            NullLogger<SecurityMasterDraftProposalService>.Instance,
            new StubSymbolResolver(resolution: null));

        var draft = await service.BuildDraftAsync("ZZZQ");

        draft.Resolved.Should().BeFalse();
        draft.Identifiers.Should().ContainSingle(static id => id.Kind == SecurityIdentifierKind.Ticker);
        draft.Notes.Should().Contain("manually");
        draft.Provenance.Should().Be("machine-proposed");
    }

    [Fact]
    public async Task BuildDraft_ResolverThrows_FallsBackToMinimalDraft()
    {
        var service = new SecurityMasterDraftProposalService(
            NullLogger<SecurityMasterDraftProposalService>.Instance,
            new ThrowingSymbolResolver());

        var draft = await service.BuildDraftAsync("GME");

        draft.Resolved.Should().BeFalse();
        draft.Symbol.Should().Be("GME");
    }

    [Fact]
    public async Task BuildDraft_WithoutResolver_StillProducesDraft()
    {
        var service = new SecurityMasterDraftProposalService(
            NullLogger<SecurityMasterDraftProposalService>.Instance);

        var draft = await service.BuildDraftAsync("GME");

        draft.Resolved.Should().BeFalse();
        draft.Identifiers.Should().ContainSingle(static id => id.Kind == SecurityIdentifierKind.Ticker);
    }

    private class StubSymbolResolver(SymbolResolution? resolution) : ISymbolResolver
    {
        public string Name => "stub";

        public Task<SymbolResolution?> ResolveAsync(string symbol, string? exchange = null, CancellationToken ct = default)
            => Task.FromResult(resolution);

        public Task<string?> MapSymbolAsync(string symbol, string fromProvider, string toProvider, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task<IReadOnlyList<SymbolSearchResult>> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SymbolSearchResult>>([]);
    }

    private sealed class ThrowingSymbolResolver : ISymbolResolver
    {
        public string Name => "throwing";

        public Task<SymbolResolution?> ResolveAsync(string symbol, string? exchange = null, CancellationToken ct = default)
            => throw new HttpRequestException("resolver unavailable");

        public Task<string?> MapSymbolAsync(string symbol, string fromProvider, string toProvider, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task<IReadOnlyList<SymbolSearchResult>> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SymbolSearchResult>>([]);
    }
}
