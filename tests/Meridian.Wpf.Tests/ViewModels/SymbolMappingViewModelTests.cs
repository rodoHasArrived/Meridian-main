using System.Windows;
using Meridian.Ui.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class SymbolMappingViewModelTests
{
    [Fact]
    public async Task LoadAsync_ProjectsProvidersAndEmptyMappingState()
    {
        var client = new FakeSymbolMappingClient();
        var viewModel = new SymbolMappingViewModel(client);

        await viewModel.LoadAsync();

        client.Loaded.Should().BeTrue();
        viewModel.Providers.Select(provider => provider.Id).Should().Equal("IB", "Yahoo");
        viewModel.ProviderOptions.Select(provider => provider.Label)
            .Should().Equal("IB - Interactive Brokers", "Yahoo - Yahoo Finance");
        viewModel.SelectedProviderId.Should().Be("IB");
        viewModel.MappingCountText.Should().Be("0 mappings");
        viewModel.MappingsVisibility.Should().Be(Visibility.Collapsed);
        viewModel.EmptyMappingsVisibility.Should().Be(Visibility.Visible);
        viewModel.StatusMessage.Should().Be("Loaded 0 custom mappings.");
    }

    [Fact]
    public async Task AddMappingAsync_WithValidInput_SavesMappingAndRefreshesScope()
    {
        var client = new FakeSymbolMappingClient();
        var viewModel = new SymbolMappingViewModel(client);
        await viewModel.LoadAsync();

        viewModel.NewCanonicalSymbol = " brk.b ";
        viewModel.SelectedProviderId = "IB";
        viewModel.NewProviderSymbol = "BRK B";

        viewModel.CanAddMapping().Should().BeTrue();
        viewModel.AddMappingCommand.CanExecute(null).Should().BeTrue();
        viewModel.MappingReadinessTitle.Should().Be("Mapping ready");
        viewModel.MappingReadinessDetail.Should().Contain("BRK.B will map to BRK B");

        await viewModel.AddMappingAsync();

        client.Mappings.Should().ContainSingle();
        client.Mappings[0].CanonicalSymbol.Should().Be("BRK.B");
        client.Mappings[0].ProviderSymbols.Should().ContainKey("IB");
        client.Mappings[0].ProviderSymbols!["IB"].Should().Be("BRK B");
        viewModel.NewCanonicalSymbol.Should().BeEmpty();
        viewModel.NewProviderSymbol.Should().BeEmpty();
        viewModel.Mappings.Should().ContainSingle();
        viewModel.MappingCountText.Should().Be("1 mapping");
        viewModel.MappingsVisibility.Should().Be(Visibility.Visible);
        viewModel.EmptyMappingsVisibility.Should().Be(Visibility.Collapsed);
        viewModel.StatusMessage.Should().Be("Saved BRK.B mapping for IB.");
    }

    [Fact]
    public async Task TestMappingCommand_NormalizesSymbolAndProjectsProviderResults()
    {
        var client = new FakeSymbolMappingClient();
        var viewModel = new SymbolMappingViewModel(client);
        await viewModel.LoadAsync();

        viewModel.TestSymbol = " brk.b ";
        viewModel.TestMappingCommand.CanExecute(null).Should().BeTrue();

        viewModel.TestMappingCommand.Execute(null);

        viewModel.TestSymbol.Should().Be("BRK.B");
        viewModel.TestResults.Should().HaveCount(2);
        viewModel.TestResults[0].ProviderName.Should().Be("Interactive Brokers");
        viewModel.TestResults[0].MappedSymbol.Should().Be("BRK B");
        viewModel.TestResults[1].ProviderName.Should().Be("Yahoo Finance");
        viewModel.TestResults[1].MappedSymbol.Should().Be("BRK-B");
        viewModel.TestResultsVisibility.Should().Be(Visibility.Visible);
        viewModel.TestScopeText.Should().Be("BRK.B maps across 2 providers.");
    }

    [Fact]
    public async Task RemoveMapping_RequiresInlineConfirmationBeforeDeleting()
    {
        var client = new FakeSymbolMappingClient();
        client.Mappings.Add(new SymbolMapping
        {
            CanonicalSymbol = "BRK.B",
            ProviderSymbols = new Dictionary<string, string> { ["IB"] = "BRK B" },
            UpdatedAt = new DateTime(2026, 4, 28)
        });
        var viewModel = new SymbolMappingViewModel(client);
        await viewModel.LoadAsync();

        viewModel.RequestRemoveMappingCommand.Execute("BRK.B");

        client.Mappings.Should().ContainSingle();
        viewModel.PendingRemoveCanonicalSymbol.Should().Be("BRK.B");
        viewModel.RemoveConfirmationVisibility.Should().Be(Visibility.Visible);
        viewModel.StatusMessage.Should().Be("Review removal for BRK.B.");

        await viewModel.ConfirmRemoveMappingAsync();

        client.Mappings.Should().BeEmpty();
        viewModel.Mappings.Should().BeEmpty();
        viewModel.PendingRemoveCanonicalSymbol.Should().BeEmpty();
        viewModel.RemoveConfirmationVisibility.Should().Be(Visibility.Collapsed);
        viewModel.EmptyMappingsVisibility.Should().Be(Visibility.Visible);
        viewModel.StatusMessage.Should().Be("Removed mapping for BRK.B.");
    }

    [Fact]
    public void SymbolMappingPageSource_BindsMappingWorkflowThroughViewModel()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\SymbolMappingPage.xaml"));
        var codeBehind = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\SymbolMappingPage.xaml.cs"));

        xaml.Should().Contain("ItemsSource=\"{Binding Providers}\"");
        xaml.Should().Contain("ItemsSource=\"{Binding ProviderOptions}\"");
        xaml.Should().Contain("ItemsSource=\"{Binding Mappings}\"");
        xaml.Should().Contain("ItemsSource=\"{Binding TestResults}\"");
        xaml.Should().Contain("Command=\"{Binding TestMappingCommand}\"");
        xaml.Should().Contain("Command=\"{Binding AddMappingCommand}\"");
        xaml.Should().Contain("RequestRemoveMappingCommand");
        xaml.Should().Contain("Command=\"{Binding ConfirmRemoveMappingCommand}\"");
        xaml.Should().Contain("Command=\"{Binding CancelRemoveMappingCommand}\"");
        xaml.Should().Contain("SymbolMappingAddReadinessCard");
        xaml.Should().Contain("{Binding MappingReadinessTitle}");
        xaml.Should().Contain("{Binding EmptyMappingsVisibility}");
        xaml.Should().Contain("{Binding RemoveConfirmationVisibility}");
        xaml.Should().NotContain("Click=\"TestMapping_Click\"");
        xaml.Should().NotContain("Click=\"AddMapping_Click\"");
        xaml.Should().NotContain("Click=\"RemoveMapping_Click\"");

        codeBehind.Should().Contain("_viewModel.LoadAsync");
        codeBehind.Should().Contain("_viewModel.ImportCsvContentAsync");
        codeBehind.Should().Contain("_viewModel.ExportCsv");
        codeBehind.Should().NotContain("ProvidersList.ItemsSource");
        codeBehind.Should().NotContain("MappingsList.ItemsSource");
        codeBehind.Should().NotContain("TestResultsList.ItemsSource");
        codeBehind.Should().NotContain("private void TestMapping_Click");
        codeBehind.Should().NotContain("private async void AddMapping_Click");
        codeBehind.Should().NotContain("private async void RemoveMapping_Click");
    }

    private sealed class FakeSymbolMappingClient : ISymbolMappingClient
    {
        private readonly IReadOnlyList<MappingProviderInfo> _providers =
        [
            new("IB", "Interactive Brokers", "Dots to spaces", SymbolTransform.DotsToSpaces),
            new("Yahoo", "Yahoo Finance", "Dots to dashes", SymbolTransform.DotsToDashes),
        ];

        public List<SymbolMapping> Mappings { get; } = [];

        public bool Loaded { get; private set; }

        public IReadOnlyList<MappingProviderInfo> Providers => _providers;

        public Task LoadAsync(CancellationToken ct = default)
        {
            Loaded = true;
            return Task.CompletedTask;
        }

        public IReadOnlyList<SymbolMapping> GetMappings() => Mappings.ToList();

        public SymbolMapping? GetMapping(string canonicalSymbol)
        {
            return Mappings.FirstOrDefault(mapping =>
                string.Equals(mapping.CanonicalSymbol, canonicalSymbol, StringComparison.OrdinalIgnoreCase));
        }

        public Dictionary<string, string> TestMapping(string canonicalSymbol)
        {
            return Providers.ToDictionary(
                provider => provider.Id,
                provider => GetMapping(canonicalSymbol)?.ProviderSymbols?.GetValueOrDefault(provider.Id)
                    ?? SymbolMappingService.ApplyDefaultTransform(canonicalSymbol, provider.Id));
        }

        public Task AddOrUpdateMappingAsync(SymbolMapping mapping, CancellationToken ct = default)
        {
            var existing = Mappings.FindIndex(candidate =>
                string.Equals(candidate.CanonicalSymbol, mapping.CanonicalSymbol, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                Mappings[existing] = mapping;
            }
            else
            {
                Mappings.Add(mapping);
            }

            return Task.CompletedTask;
        }

        public Task RemoveMappingAsync(string canonicalSymbol, CancellationToken ct = default)
        {
            Mappings.RemoveAll(mapping =>
                string.Equals(mapping.CanonicalSymbol, canonicalSymbol, StringComparison.OrdinalIgnoreCase));
            return Task.CompletedTask;
        }

        public Task<int> ImportFromCsvAsync(string csvContent, CancellationToken ct = default)
        {
            Mappings.Add(new SymbolMapping
            {
                CanonicalSymbol = "SPY",
                ProviderSymbols = new Dictionary<string, string> { ["Yahoo"] = "SPY" }
            });
            return Task.FromResult(1);
        }

        public string ExportToCsv() => "Canonical,IB,Yahoo";
    }
}
