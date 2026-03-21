using FluentAssertions;
using Meridian.Contracts.Configuration;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="SymbolGroupService"/> and its associated model types.
/// Validates the singleton pattern, model defaults, and predefined templates.
/// </summary>
public sealed class SymbolGroupServiceTests
{
    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnNonNull()
    {
        var instance = SymbolGroupService.Instance;
        instance.Should().NotBeNull();
    }

    [Fact]
    public void Instance_ShouldReturnSameSingleton()
    {
        var a = SymbolGroupService.Instance;
        var b = SymbolGroupService.Instance;
        a.Should().BeSameAs(b);
    }

    // ── SymbolGroupDto (aliased as SymbolGroup) model ───────────────

    [Fact]
    public void SymbolGroup_DefaultValues_ShouldBeCorrect()
    {
        var group = new SymbolGroupDto();

        group.Id.Should().NotBeNullOrEmpty("Id is auto-generated as a Guid");
        group.Name.Should().BeEmpty();
        group.Description.Should().BeNull();
        group.Color.Should().Be("#0078D4");
        group.Icon.Should().NotBeNullOrEmpty();
        group.Symbols.Should().NotBeNull().And.BeEmpty();
        group.IsExpanded.Should().BeTrue();
        group.SortOrder.Should().Be(0);
        group.Tags.Should().BeNull();
        group.SmartCriteria.Should().BeNull();
    }

    [Fact]
    public void SymbolGroup_Id_ShouldBeUniqueAcrossInstances()
    {
        var a = new SymbolGroupDto();
        var b = new SymbolGroupDto();

        a.Id.Should().NotBe(b.Id);
    }

    [Fact]
    public void SymbolGroup_CreatedAt_ShouldBeCloseToUtcNow()
    {
        var before = DateTime.UtcNow;
        var group = new SymbolGroupDto();
        var after = DateTime.UtcNow;

        group.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void SymbolGroup_UpdatedAt_ShouldBeCloseToUtcNow()
    {
        var before = DateTime.UtcNow;
        var group = new SymbolGroupDto();
        var after = DateTime.UtcNow;

        group.UpdatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    // ── SymbolGroupsConfigDto (aliased as SymbolGroupsConfig) model ─

    [Fact]
    public void SymbolGroupsConfig_DefaultValues_ShouldBeCorrect()
    {
        var config = new SymbolGroupsConfigDto();

        config.Groups.Should().BeNull();
        config.DefaultGroupId.Should().BeNull();
        config.ShowUngroupedSymbols.Should().BeTrue();
        config.SortBy.Should().Be("Name");
        config.ViewMode.Should().Be("Tree");
    }

    // ── Predefined templates ────────────────────────────────────────

    [Fact]
    public void GetTemplates_ShouldReturnExpectedTemplates()
    {
        var service = SymbolGroupService.Instance;
        var templates = service.GetTemplates().ToList();

        templates.Should().NotBeEmpty();
        templates.Select(t => t.Id).Should().Contain("FAANG");
        templates.Select(t => t.Id).Should().Contain("MagnificentSeven");
        templates.Select(t => t.Id).Should().Contain("MajorETFs");
        templates.Select(t => t.Id).Should().Contain("Semiconductors");
        templates.Select(t => t.Id).Should().Contain("Financials");
        templates.Select(t => t.Id).Should().Contain("Technology");
        templates.Select(t => t.Id).Should().Contain("Healthcare");
        templates.Select(t => t.Id).Should().Contain("Energy");
    }

    [Fact]
    public void GetTemplates_ShouldHaveEightTemplates()
    {
        var service = SymbolGroupService.Instance;
        var templates = service.GetTemplates().ToList();

        templates.Should().HaveCount(8);
    }

    [Fact]
    public void GetTemplates_FAANG_ShouldContainExpectedSymbols()
    {
        var service = SymbolGroupService.Instance;
        var faang = service.GetTemplates().First(t => t.Id == "FAANG");

        faang.Name.Should().Be("FAANG");
        faang.Symbols.Should().Contain("META");
        faang.Symbols.Should().Contain("AAPL");
        faang.Symbols.Should().Contain("AMZN");
        faang.Symbols.Should().Contain("NFLX");
        faang.Symbols.Should().Contain("GOOGL");
        faang.Symbols.Should().HaveCount(5);
    }

    [Fact]
    public void GetTemplates_MagnificentSeven_ShouldContainSevenSymbols()
    {
        var service = SymbolGroupService.Instance;
        var mag7 = service.GetTemplates().First(t => t.Id == "MagnificentSeven");

        mag7.Name.Should().Be("Magnificent 7");
        mag7.Symbols.Should().HaveCount(7);
        mag7.Symbols.Should().Contain("AAPL");
        mag7.Symbols.Should().Contain("MSFT");
        mag7.Symbols.Should().Contain("GOOGL");
        mag7.Symbols.Should().Contain("AMZN");
        mag7.Symbols.Should().Contain("NVDA");
        mag7.Symbols.Should().Contain("META");
        mag7.Symbols.Should().Contain("TSLA");
    }

    [Fact]
    public void GetTemplates_MajorETFs_ShouldContainExpectedSymbols()
    {
        var service = SymbolGroupService.Instance;
        var etfs = service.GetTemplates().First(t => t.Id == "MajorETFs");

        etfs.Name.Should().Be("Major ETFs");
        etfs.Symbols.Should().Contain("SPY");
        etfs.Symbols.Should().Contain("QQQ");
    }

    [Fact]
    public void GetTemplates_AllTemplates_ShouldHaveNonEmptyColors()
    {
        var service = SymbolGroupService.Instance;
        var templates = service.GetTemplates().ToList();

        foreach (var template in templates)
        {
            template.Color.Should().NotBeNullOrEmpty($"Template '{template.Name}' should have a color");
        }
    }

    [Fact]
    public void GetTemplates_AllTemplates_ShouldHaveNonEmptySymbolArrays()
    {
        var service = SymbolGroupService.Instance;
        var templates = service.GetTemplates().ToList();

        foreach (var template in templates)
        {
            template.Symbols.Should().NotBeNullOrEmpty($"Template '{template.Name}' should have symbols");
        }
    }

    // ── SymbolGroupEventArgs ────────────────────────────────────────

    [Fact]
    public void SymbolGroupEventArgs_DefaultValues_ShouldBeCorrect()
    {
        var args = new SymbolGroupEventArgs();
        args.Group.Should().BeNull();
    }

    // ── Events ──────────────────────────────────────────────────────

    [Fact]
    public void Events_ShouldBeSubscribable()
    {
        var service = SymbolGroupService.Instance;
        var fired = false;

        service.GroupCreated += (_, _) => fired = true;
        service.GroupCreated -= (_, _) => fired = true;

        fired.Should().BeFalse("no event was raised");
    }
}
