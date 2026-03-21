using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="CommandPaletteService"/> — command registration, fuzzy search,
/// execution tracking, recent commands, and palette model types.
/// </summary>
public sealed class CommandPaletteServiceTests
{
    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnNonNullSingleton()
    {
        // Act
        var instance = CommandPaletteService.Instance;

        // Assert
        instance.Should().NotBeNull();
    }

    [Fact]
    public void Instance_ShouldReturnSameInstanceOnMultipleCalls()
    {
        // Act
        var instance1 = CommandPaletteService.Instance;
        var instance2 = CommandPaletteService.Instance;

        // Assert
        instance1.Should().BeSameAs(instance2);
    }

    // ── GetAllCommands ───────────────────────────────────────────────

    [Fact]
    public void GetAllCommands_ShouldReturnDefaultRegisteredCommands()
    {
        // Arrange
        var service = CommandPaletteService.Instance;

        // Act
        var commands = service.GetAllCommands();

        // Assert
        commands.Should().NotBeNull();
        commands.Should().NotBeEmpty("default navigation and action commands are registered in the constructor");
    }

    [Fact]
    public void GetAllCommands_ShouldContainNavigationCommands()
    {
        // Arrange
        var service = CommandPaletteService.Instance;

        // Act
        var commands = service.GetAllCommands();

        // Assert
        commands.Should().Contain(c => c.Id == "nav-dashboard");
        commands.Should().Contain(c => c.Id == "nav-settings");
        commands.Should().Contain(c => c.Id == "nav-symbols");
    }

    [Fact]
    public void GetAllCommands_ShouldContainActionCommands()
    {
        // Arrange
        var service = CommandPaletteService.Instance;

        // Act
        var commands = service.GetAllCommands();

        // Assert
        commands.Should().Contain(c => c.Id == "action-refresh");
        commands.Should().Contain(c => c.Id == "action-run-backfill");
    }

    // ── RegisterCommand ──────────────────────────────────────────────

    [Fact]
    public void RegisterCommand_NewCommand_ShouldAppearInAllCommands()
    {
        // Arrange
        var service = CommandPaletteService.Instance;
        var uniqueId = "test-cmd-" + Guid.NewGuid();
        var command = new PaletteCommand
        {
            Id = uniqueId,
            Title = "Test Command",
            Description = "A test command",
            Category = PaletteCommandCategory.Action,
            ActionId = "TestAction",
            Keywords = "test custom",
            Icon = "\uE710",
            Shortcut = ""
        };

        // Act
        service.RegisterCommand(command);

        // Assert
        service.GetAllCommands().Should().Contain(c => c.Id == uniqueId);
    }

    [Fact]
    public void RegisterCommand_DuplicateId_ShouldReplaceExistingCommand()
    {
        // Arrange
        var service = CommandPaletteService.Instance;
        var uniqueId = "test-replace-" + Guid.NewGuid();
        var originalCommand = new PaletteCommand
        {
            Id = uniqueId,
            Title = "Original",
            Description = "First version",
            Category = PaletteCommandCategory.Action,
            ActionId = "Original",
            Keywords = "original",
            Icon = "",
            Shortcut = ""
        };
        var replacementCommand = new PaletteCommand
        {
            Id = uniqueId,
            Title = "Replacement",
            Description = "Second version",
            Category = PaletteCommandCategory.Action,
            ActionId = "Replacement",
            Keywords = "replacement",
            Icon = "",
            Shortcut = ""
        };

        // Act
        service.RegisterCommand(originalCommand);
        service.RegisterCommand(replacementCommand);

        // Assert
        var found = service.GetAllCommands().First(c => c.Id == uniqueId);
        found.Title.Should().Be("Replacement");
    }

    // ── Search ───────────────────────────────────────────────────────

    [Fact]
    public void Search_EmptyQuery_ShouldReturnRecentAndPopularCommands()
    {
        // Arrange
        var service = CommandPaletteService.Instance;

        // Act
        var results = service.Search("");

        // Assert
        results.Should().NotBeNull();
        results.Should().NotBeEmpty("empty query returns recent and popular commands");
    }

    [Fact]
    public void Search_NullQuery_ShouldReturnRecentAndPopularCommands()
    {
        // Arrange
        var service = CommandPaletteService.Instance;

        // Act
        var results = service.Search(null!);

        // Assert
        results.Should().NotBeNull();
        results.Should().NotBeEmpty();
    }

    [Fact]
    public void Search_WhitespaceQuery_ShouldReturnRecentAndPopularCommands()
    {
        // Arrange
        var service = CommandPaletteService.Instance;

        // Act
        var results = service.Search("   ");

        // Assert
        results.Should().NotBeNull();
        results.Should().NotBeEmpty();
    }

    [Fact]
    public void Search_ExactTitleMatch_ShouldReturnMatchingCommand()
    {
        // Arrange
        var service = CommandPaletteService.Instance;

        // Act
        var results = service.Search("Navigate to Dashboard");

        // Assert
        results.Should().NotBeEmpty();
        results.Should().Contain(c => c.Id == "nav-dashboard");
    }

    [Fact]
    public void Search_PartialQuery_ShouldReturnMatchingCommands()
    {
        // Arrange
        var service = CommandPaletteService.Instance;

        // Act
        var results = service.Search("backfill");

        // Assert
        results.Should().NotBeEmpty();
        results.Should().Contain(c => c.Id == "nav-backfill" || c.Id == "action-run-backfill");
    }

    [Fact]
    public void Search_KeywordMatch_ShouldReturnMatchingCommands()
    {
        // Arrange
        var service = CommandPaletteService.Instance;

        // Act
        var results = service.Search("dash");

        // Assert
        results.Should().NotBeEmpty();
        results.Should().Contain(c => c.Id == "nav-dashboard");
    }

    [Fact]
    public void Search_NonMatchingQuery_ShouldReturnEmptyList()
    {
        // Arrange
        var service = CommandPaletteService.Instance;

        // Act
        var results = service.Search("zzzzzzzznonexistent9999");

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public void Search_CaseInsensitive_ShouldReturnMatchingCommands()
    {
        // Arrange
        var service = CommandPaletteService.Instance;

        // Act
        var resultsLower = service.Search("dashboard");
        var resultsUpper = service.Search("DASHBOARD");

        // Assert
        resultsLower.Should().NotBeEmpty();
        resultsUpper.Should().NotBeEmpty();
        resultsLower.Select(c => c.Id).Should().BeEquivalentTo(resultsUpper.Select(c => c.Id));
    }

    [Fact]
    public void Search_ShouldLimitResultsToMaxFifteen()
    {
        // Arrange
        var service = CommandPaletteService.Instance;

        // Act — "navigate" matches many commands
        var results = service.Search("navigate");

        // Assert
        results.Count.Should().BeLessThanOrEqualTo(15);
    }

    // ── Execute ──────────────────────────────────────────────────────

    [Fact]
    public void Execute_ValidCommandId_ShouldRaiseCommandExecutedEvent()
    {
        // Arrange
        var service = CommandPaletteService.Instance;
        PaletteCommandEventArgs? received = null;
        service.CommandExecuted += (_, args) => received = args;

        // Act
        service.Execute("nav-dashboard");

        // Assert
        received.Should().NotBeNull();
        received!.CommandId.Should().Be("nav-dashboard");
        received.Category.Should().Be(PaletteCommandCategory.Navigation);
    }

    [Fact]
    public void Execute_InvalidCommandId_ShouldNotThrow()
    {
        // Arrange
        var service = CommandPaletteService.Instance;

        // Act
        var act = () => service.Execute("non-existent-command-id");

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Execute_ValidCommandId_ShouldTrackInRecentCommands()
    {
        // Arrange
        var service = CommandPaletteService.Instance;
        var uniqueId = "test-recent-" + Guid.NewGuid();
        service.RegisterCommand(new PaletteCommand
        {
            Id = uniqueId,
            Title = "Recent Test",
            Description = "Test for recent tracking",
            Category = PaletteCommandCategory.Action,
            ActionId = "RecentTest",
            Keywords = "recent test",
            Icon = "",
            Shortcut = ""
        });

        // Act
        service.Execute(uniqueId);
        var recentResults = service.Search("");

        // Assert — the recently executed command should appear in the empty-query results
        recentResults.Should().Contain(c => c.Id == uniqueId);
    }

    // ── PaletteCommand model ─────────────────────────────────────────

    [Fact]
    public void PaletteCommand_DefaultValues_ShouldBeEmptyStrings()
    {
        // Act
        var command = new PaletteCommand();

        // Assert
        command.Id.Should().BeEmpty();
        command.Title.Should().BeEmpty();
        command.Description.Should().BeEmpty();
        command.ActionId.Should().BeEmpty();
        command.Keywords.Should().BeEmpty();
        command.Icon.Should().BeEmpty();
        command.Shortcut.Should().BeEmpty();
    }

    [Fact]
    public void PaletteCommand_CanStoreAllProperties()
    {
        // Act
        var command = new PaletteCommand
        {
            Id = "cmd-1",
            Title = "My Command",
            Description = "Does something useful",
            Category = PaletteCommandCategory.Setting,
            ActionId = "myAction",
            Keywords = "my keyword",
            Icon = "\uE713",
            Shortcut = "Ctrl+M"
        };

        // Assert
        command.Id.Should().Be("cmd-1");
        command.Title.Should().Be("My Command");
        command.Description.Should().Be("Does something useful");
        command.Category.Should().Be(PaletteCommandCategory.Setting);
        command.ActionId.Should().Be("myAction");
        command.Keywords.Should().Be("my keyword");
        command.Icon.Should().Be("\uE713");
        command.Shortcut.Should().Be("Ctrl+M");
    }

    // ── PaletteCommandCategory enum ──────────────────────────────────

    [Theory]
    [InlineData(PaletteCommandCategory.Navigation)]
    [InlineData(PaletteCommandCategory.Action)]
    [InlineData(PaletteCommandCategory.Setting)]
    [InlineData(PaletteCommandCategory.Recent)]
    public void PaletteCommandCategory_AllValues_ShouldBeDefined(PaletteCommandCategory category)
    {
        // Assert
        Enum.IsDefined(typeof(PaletteCommandCategory), category).Should().BeTrue();
    }

    // ── PaletteCommandEventArgs model ────────────────────────────────

    [Fact]
    public void PaletteCommandEventArgs_DefaultValues_ShouldBeEmptyStrings()
    {
        // Act
        var args = new PaletteCommandEventArgs();

        // Assert
        args.CommandId.Should().BeEmpty();
        args.ActionId.Should().BeEmpty();
    }

    [Fact]
    public void PaletteCommandEventArgs_CanStoreAllProperties()
    {
        // Act
        var args = new PaletteCommandEventArgs
        {
            CommandId = "nav-settings",
            ActionId = "Settings",
            Category = PaletteCommandCategory.Navigation
        };

        // Assert
        args.CommandId.Should().Be("nav-settings");
        args.ActionId.Should().Be("Settings");
        args.Category.Should().Be(PaletteCommandCategory.Navigation);
    }

    // ── Default command counts ───────────────────────────────────────

    [Fact]
    public void GetAllCommands_ShouldContainBothNavigationAndActionCategories()
    {
        // Arrange
        var service = CommandPaletteService.Instance;

        // Act
        var commands = service.GetAllCommands();

        // Assert
        commands.Should().Contain(c => c.Category == PaletteCommandCategory.Navigation);
        commands.Should().Contain(c => c.Category == PaletteCommandCategory.Action);
    }

    [Fact]
    public void GetAllCommands_NavigationCommands_ShouldHaveShortcuts()
    {
        // Arrange
        var service = CommandPaletteService.Instance;

        // Act
        var dashboardCommand = service.GetAllCommands().FirstOrDefault(c => c.Id == "nav-dashboard");

        // Assert
        dashboardCommand.Should().NotBeNull();
        dashboardCommand!.Shortcut.Should().Be("Ctrl+D");
    }

    [Theory]
    [InlineData("nav-dashboard", "Dashboard")]
    [InlineData("nav-backfill", "Backfill")]
    [InlineData("nav-settings", "Settings")]
    [InlineData("action-refresh", "RefreshStatus")]
    public void GetAllCommands_KnownCommand_ShouldHaveExpectedActionId(string commandId, string expectedActionId)
    {
        // Arrange
        var service = CommandPaletteService.Instance;

        // Act
        var command = service.GetAllCommands().FirstOrDefault(c => c.Id == commandId);

        // Assert
        command.Should().NotBeNull();
        command!.ActionId.Should().Be(expectedActionId);
    }
}
