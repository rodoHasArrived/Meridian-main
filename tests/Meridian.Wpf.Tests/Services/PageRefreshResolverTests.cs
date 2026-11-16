using System.Windows.Controls;
using System.Windows.Input;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// Tests for <see cref="PageRefreshResolver"/>: how the shell Refresh control finds the
/// reload command of the page it is showing.
/// </summary>
public sealed class PageRefreshResolverTests
{
    [Fact]
    public void Resolve_NullContent_ReturnsNull()
    {
        PageRefreshResolver.Resolve(null).Should().BeNull();
    }

    [Fact]
    public void Resolve_ContentWithoutRefreshCommand_ReturnsNull()
    {
        PageRefreshResolver.Resolve(new object()).Should().BeNull();
        PageRefreshResolver.Resolve(new NonCommandRefreshProperty()).Should().BeNull();
        PageRefreshResolver.Resolve(new NullConventionalCommand()).Should().BeNull();
    }

    [Fact]
    public void Resolve_InterfaceTarget_ReturnsInterfaceCommand()
    {
        var command = new CountingCommand();

        PageRefreshResolver.Resolve(new InterfaceTarget(command)).Should().BeSameAs(command);
    }

    [Fact]
    public void Resolve_InterfaceTarget_WinsOverConventionalProperty()
    {
        var interfaceCommand = new CountingCommand();
        var conventionalCommand = new CountingCommand();

        PageRefreshResolver.Resolve(new InterfaceAndConventionTarget(interfaceCommand, conventionalCommand))
            .Should().BeSameAs(interfaceCommand);
    }

    [Fact]
    public void Resolve_ConventionalRefreshCommandProperty_ReturnsCommand()
    {
        var command = new CountingCommand();

        PageRefreshResolver.Resolve(new ConventionalTarget(command)).Should().BeSameAs(command);
    }

    [Fact]
    public void Resolve_ContentHost_UnwrapsToHostedContent()
    {
        var command = new CountingCommand();
        var host = new ContentHost(new ContentHost(new ConventionalTarget(command)));

        PageRefreshResolver.Resolve(host).Should().BeSameAs(command);
    }

    [Fact]
    public void Resolve_ContentHostWithoutHostedContent_ReturnsNull()
    {
        PageRefreshResolver.Resolve(new ContentHost(null)).Should().BeNull();
    }

    [Fact]
    public void Resolve_SelfReferencingContentHost_ReturnsNull()
    {
        PageRefreshResolver.Resolve(new SelfReferencingHost()).Should().BeNull();
    }

    [Fact]
    public void Resolve_PageDataContext_UsesInterfaceThenConvention()
    {
        WpfTestThread.Run(() =>
        {
            var interfaceCommand = new CountingCommand();
            var conventionalCommand = new CountingCommand();

            PageRefreshResolver.Resolve(new Page { DataContext = new InterfaceTarget(interfaceCommand) })
                .Should().BeSameAs(interfaceCommand);
            PageRefreshResolver.Resolve(new Page { DataContext = new ConventionalTarget(conventionalCommand) })
                .Should().BeSameAs(conventionalCommand);
            PageRefreshResolver.Resolve(new Page()).Should().BeNull();
        });
    }

    [Fact]
    public void TryRefreshInPlace_ExecutesCommandOnce_AndReportsHandled()
    {
        var command = new CountingCommand();

        PageRefreshResolver.TryRefreshInPlace(new ConventionalTarget(command)).Should().BeTrue();

        command.ExecuteCount.Should().Be(1);
    }

    [Fact]
    public void TryRefreshInPlace_BusyCommand_IsHandledWithoutExecuting()
    {
        var command = new CountingCommand(canExecute: false);

        PageRefreshResolver.TryRefreshInPlace(new ConventionalTarget(command))
            .Should().BeTrue("a page that is already reloading must not be rebuilt by re-navigation");

        command.ExecuteCount.Should().Be(0);
    }

    [Fact]
    public void TryRefreshInPlace_NoCommand_ReturnsFalseSoCallerCanNavigate()
    {
        PageRefreshResolver.TryRefreshInPlace(new object()).Should().BeFalse();
        PageRefreshResolver.TryRefreshInPlace(null).Should().BeFalse();
    }

    private sealed class InterfaceTarget : IPageRefreshTarget
    {
        private readonly ICommand _command;

        public InterfaceTarget(ICommand command)
        {
            _command = command;
        }

        ICommand IPageRefreshTarget.RefreshCommand => _command;
    }

    private sealed class InterfaceAndConventionTarget : IPageRefreshTarget
    {
        private readonly ICommand _interfaceCommand;

        public InterfaceAndConventionTarget(ICommand interfaceCommand, ICommand conventionalCommand)
        {
            _interfaceCommand = interfaceCommand;
            RefreshCommand = conventionalCommand;
        }

        public ICommand RefreshCommand { get; }

        ICommand IPageRefreshTarget.RefreshCommand => _interfaceCommand;
    }

    private sealed class ConventionalTarget
    {
        public ConventionalTarget(CountingCommand command)
        {
            RefreshCommand = command;
        }

        public CountingCommand RefreshCommand { get; }
    }

    private sealed class NonCommandRefreshProperty
    {
        public string RefreshCommand => "not a command";
    }

    private sealed class NullConventionalCommand
    {
        public ICommand? RefreshCommand => null;
    }

    private sealed class ContentHost : IPageRefreshContentHost
    {
        public ContentHost(object? content)
        {
            PageRefreshContent = content;
        }

        public object? PageRefreshContent { get; }
    }

    private sealed class SelfReferencingHost : IPageRefreshContentHost
    {
        public object? PageRefreshContent => this;
    }
}
