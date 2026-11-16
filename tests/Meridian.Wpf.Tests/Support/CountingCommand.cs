using System.Windows.Input;

namespace Meridian.Wpf.Tests.Support;

/// <summary>
/// Minimal <see cref="ICommand"/> that records executions; used by shell-refresh tests.
/// </summary>
internal sealed class CountingCommand : ICommand
{
    private readonly bool _canExecute;

    public CountingCommand(bool canExecute = true)
    {
        _canExecute = canExecute;
    }

    public int ExecuteCount { get; private set; }

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => _canExecute;

    public void Execute(object? parameter) => ExecuteCount++;
}
