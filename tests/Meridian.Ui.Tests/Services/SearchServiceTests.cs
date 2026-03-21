using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="SearchService"/> and its associated model types.
/// </summary>
public sealed class SearchServiceTests
{
    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnNonNull()
    {
        var instance = SearchService.Instance;
        instance.Should().NotBeNull();
    }

    [Fact]
    public void Instance_ShouldReturnSameSingleton()
    {
        var a = SearchService.Instance;
        var b = SearchService.Instance;
        a.Should().BeSameAs(b);
    }

    // ── SearchResults model ─────────────────────────────────────────

    [Fact]
    public void SearchResults_DefaultValues_ShouldBeCorrect()
    {
        var results = new SearchResults();

        results.Query.Should().BeEmpty();
        results.Symbols.Should().NotBeNull().And.BeEmpty();
        results.Providers.Should().NotBeNull().And.BeEmpty();
        results.Pages.Should().NotBeNull().And.BeEmpty();
        results.Actions.Should().NotBeNull().And.BeEmpty();
        results.Help.Should().NotBeNull().And.BeEmpty();
        results.TotalCount.Should().Be(0);
        results.AllResults.Should().BeEmpty();
    }

    [Fact]
    public void SearchResults_TotalCount_ShouldSumAllCategories()
    {
        var results = new SearchResults();
        results.Symbols.Add(new SearchResultItem { Title = "SPY" });
        results.Providers.Add(new SearchResultItem { Title = "Alpaca" });
        results.Pages.Add(new SearchResultItem { Title = "Dashboard" });
        results.Actions.Add(new SearchResultItem { Title = "Start" });
        results.Help.Add(new SearchResultItem { Title = "Getting Started" });

        results.TotalCount.Should().Be(5);
    }

    [Fact]
    public void SearchResults_AllResults_ShouldConcatenateAllCategories()
    {
        var results = new SearchResults();
        results.Symbols.Add(new SearchResultItem { Title = "SPY" });
        results.Providers.Add(new SearchResultItem { Title = "Alpaca" });

        results.AllResults.Should().HaveCount(2);
        results.AllResults.Select(r => r.Title).Should().Contain("SPY").And.Contain("Alpaca");
    }

    // ── SearchOptions model ─────────────────────────────────────────

    [Fact]
    public void SearchOptions_DefaultValues_ShouldBeCorrect()
    {
        var options = new SearchOptions();

        options.SearchSymbols.Should().BeTrue();
        options.SearchProviders.Should().BeTrue();
        options.SearchPages.Should().BeTrue();
        options.SearchActions.Should().BeTrue();
        options.SearchHelp.Should().BeTrue();
        options.MaxResults.Should().Be(20);
    }

    // ── SearchResultItem model ──────────────────────────────────────

    [Fact]
    public void SearchResultItem_DefaultValues_ShouldBeCorrect()
    {
        var item = new SearchResultItem();

        item.Title.Should().BeEmpty();
        item.Description.Should().BeEmpty();
        item.Icon.Should().NotBeNullOrEmpty("default icon is set");
        item.NavigationTarget.Should().BeEmpty();
        item.Category.Should().BeEmpty();
    }

    // ── SearchSuggestion model ──────────────────────────────────────

    [Fact]
    public void SearchSuggestion_DefaultValues_ShouldBeCorrect()
    {
        var suggestion = new SearchSuggestion();

        suggestion.Text.Should().BeEmpty();
        suggestion.Category.Should().BeEmpty();
        suggestion.Icon.Should().NotBeNullOrEmpty("default icon is set");
        suggestion.NavigationTarget.Should().BeEmpty();
    }

    // ── NavigationPage model ────────────────────────────────────────

    [Fact]
    public void NavigationPage_Constructor_ShouldSetProperties()
    {
        var page = new NavigationPage("Dashboard", "Dashboard", "\uE80F", "Main view", new[] { "home" });

        page.Name.Should().Be("Dashboard");
        page.Tag.Should().Be("Dashboard");
        page.Icon.Should().Be("\uE80F");
        page.Description.Should().Be("Main view");
        page.Keywords.Should().Contain("home");
    }

    // ── SearchAsync with empty query ────────────────────────────────

    [Fact]
    public async Task SearchAsync_WithEmptyQuery_ShouldReturnEmptyResults()
    {
        var service = SearchService.Instance;

        var results = await service.SearchAsync(string.Empty);

        results.Should().NotBeNull();
        results.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_WithNullQuery_ShouldReturnEmptyResults()
    {
        var service = SearchService.Instance;

        var results = await service.SearchAsync(null!);

        results.Should().NotBeNull();
        results.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_WithWhitespaceQuery_ShouldReturnEmptyResults()
    {
        var service = SearchService.Instance;

        var results = await service.SearchAsync("   ");

        results.Should().NotBeNull();
        results.TotalCount.Should().Be(0);
    }

    // ── SearchAsync with provider query ─────────────────────────────

    [Fact]
    public async Task SearchAsync_WithProviderQuery_ShouldFindProviders()
    {
        var service = SearchService.Instance;

        var results = await service.SearchAsync("Alpaca");

        results.Should().NotBeNull();
        results.Providers.Should().NotBeEmpty();
        results.Providers.Should().Contain(p => p.Title.Contains("Alpaca"));
    }

    [Fact]
    public async Task SearchAsync_WithActionQuery_ShouldFindActions()
    {
        var service = SearchService.Instance;

        var results = await service.SearchAsync("Backfill");

        results.Should().NotBeNull();
        results.Actions.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SearchAsync_WithHelpQuery_ShouldFindHelpTopics()
    {
        var service = SearchService.Instance;

        var results = await service.SearchAsync("troubleshoot");

        results.Should().NotBeNull();
        results.Help.Should().NotBeEmpty();
    }

    // ── SearchAsync with specific options ────────────────────────────

    [Fact]
    public async Task SearchAsync_WithProvidersDisabled_ShouldNotSearchProviders()
    {
        var service = SearchService.Instance;
        var options = new SearchOptions
        {
            SearchProviders = false,
            SearchPages = false,
            SearchActions = false,
            SearchHelp = false,
            SearchSymbols = false
        };

        var results = await service.SearchAsync("Alpaca", options);

        results.Providers.Should().BeEmpty();
        results.Pages.Should().BeEmpty();
        results.Actions.Should().BeEmpty();
        results.Help.Should().BeEmpty();
    }

    // ── GetSuggestionsAsync with empty query ────────────────────────

    [Fact]
    public async Task GetSuggestionsAsync_WithEmptyQuery_ShouldReturnEmpty()
    {
        var service = SearchService.Instance;

        var suggestions = await service.GetSuggestionsAsync(string.Empty);

        suggestions.Should().NotBeNull().And.BeEmpty();
    }
}
