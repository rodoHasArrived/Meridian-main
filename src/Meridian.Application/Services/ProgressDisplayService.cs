using System.Diagnostics;

namespace Meridian.Application.Services;

/// <summary>
/// Service for displaying real-time progress feedback during long-running operations.
/// Provides terminal-based progress bars, spinners, and status updates.
/// </summary>
public sealed class ProgressDisplayService : IDisposable
{
    private readonly TextWriter _output;
    private readonly bool _isInteractive;
    private readonly object _lock = new();
    private readonly Stopwatch _stopwatch = new();
    private Timer? _spinnerTimer;
    private int _spinnerIndex;
    private string _currentOperation = "";
    private bool _disposed;

    private static readonly string[] SpinnerFrames = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
    private static readonly string[] SimpleSpinner = { "|", "/", "-", "\\" };

    public ProgressDisplayService(TextWriter? output = null)
    {
        _output = output ?? Console.Out;
        _isInteractive = !Console.IsOutputRedirected;
    }

    /// <summary>
    /// Creates a progress tracker for a specific operation.
    /// </summary>
    public OperationProgress StartOperation(string operationName, int totalItems = 0)
    {
        return new OperationProgress(this, operationName, totalItems);
    }

    /// <summary>
    /// Displays a progress bar with percentage and ETA.
    /// </summary>
    public void DisplayProgress(string operation, int current, int total, string? currentItem = null)
    {
        if (total <= 0)
            return;

        lock (_lock)
        {
            var percent = Math.Min(100, (current * 100) / total);
            var elapsed = _stopwatch.Elapsed;
            var eta = current > 0 ? TimeSpan.FromTicks(elapsed.Ticks * (total - current) / current) : TimeSpan.Zero;

            var barWidth = 30;
            var filled = (percent * barWidth) / 100;
            var bar = new string('█', filled) + new string('░', barWidth - filled);

            var itemText = !string.IsNullOrEmpty(currentItem) ? $" [{currentItem}]" : "";
            var etaText = eta > TimeSpan.Zero ? $" ETA: {FormatTimeSpan(eta)}" : "";

            var line = $"\r  {operation}: [{bar}] {percent,3}% ({current}/{total}){itemText}{etaText}    ";

            if (_isInteractive)
            {
                _output.Write(line);
            }
            else if (percent % 10 == 0 || current == total)
            {
                // Non-interactive: only print at 10% intervals
                _output.WriteLine($"  {operation}: {percent}% ({current}/{total}){itemText}");
            }
        }
    }

    /// <summary>
    /// Displays a spinner for indeterminate progress.
    /// </summary>
    public IDisposable StartSpinner(string message)
    {
        _currentOperation = message;
        _spinnerIndex = 0;

        if (_isInteractive)
        {
            _spinnerTimer = new Timer(_ => UpdateSpinner(), null, 0, 100);
        }
        else
        {
            _output.WriteLine($"  {message}...");
        }

        return new SpinnerDisposable(this);
    }

    /// <summary>
    /// Displays a status line that can be updated.
    /// </summary>
    public void DisplayStatus(string status)
    {
        lock (_lock)
        {
            if (_isInteractive)
            {
                _output.Write($"\r  {status}".PadRight(80));
            }
            else
            {
                _output.WriteLine($"  {status}");
            }
        }
    }

    /// <summary>
    /// Displays a success message.
    /// </summary>
    public void DisplaySuccess(string message)
    {
        lock (_lock)
        {
            if (_isInteractive)
            {
                _output.Write("\r");
            }
            _output.WriteLine($"  [OK] {message}");
        }
    }

    /// <summary>
    /// Displays a warning message.
    /// </summary>
    public void DisplayWarning(string message)
    {
        lock (_lock)
        {
            if (_isInteractive)
            {
                _output.Write("\r");
            }
            _output.WriteLine($"  [WARN] {message}");
        }
    }

    /// <summary>
    /// Displays an error message.
    /// </summary>
    public void DisplayError(string message)
    {
        lock (_lock)
        {
            if (_isInteractive)
            {
                _output.Write("\r");
            }
            _output.WriteLine($"  [FAIL] {message}");
        }
    }

    /// <summary>
    /// Clears the current line.
    /// </summary>
    public void ClearLine()
    {
        if (_isInteractive)
        {
            lock (_lock)
            {
                _output.Write("\r" + new string(' ', 80) + "\r");
            }
        }
    }

    /// <summary>
    /// Displays a multi-step checklist.
    /// </summary>
    public ChecklistDisplay StartChecklist(string title, string[] steps)
    {
        return new ChecklistDisplay(this, title, steps);
    }

    /// <summary>
    /// Displays a summary table.
    /// </summary>
    public void DisplayTable(string title, IEnumerable<(string Key, string Value)> rows)
    {
        lock (_lock)
        {
            _output.WriteLine();
            _output.WriteLine($"  {title}");
            _output.WriteLine("  " + new string('-', 50));

            foreach (var (key, value) in rows)
            {
                _output.WriteLine($"    {key,-25} {value}");
            }
            _output.WriteLine();
        }
    }

    /// <summary>
    /// Starts the internal stopwatch for ETA calculations.
    /// </summary>
    public void StartTiming()
    {
        _stopwatch.Restart();
    }

    /// <summary>
    /// Stops the internal stopwatch.
    /// </summary>
    public TimeSpan StopTiming()
    {
        _stopwatch.Stop();
        return _stopwatch.Elapsed;
    }

    private void UpdateSpinner()
    {
        if (_disposed)
            return;

        lock (_lock)
        {
            var frames = _isInteractive && Environment.GetEnvironmentVariable("TERM") != "dumb"
                ? SpinnerFrames
                : SimpleSpinner;

            var frame = frames[_spinnerIndex % frames.Length];
            _spinnerIndex++;

            _output.Write($"\r  {frame} {_currentOperation}...    ");
        }
    }

    private void StopSpinner()
    {
        _spinnerTimer?.Dispose();
        _spinnerTimer = null;
        ClearLine();
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        if (ts.TotalMinutes >= 1)
            return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
        return $"{ts.Seconds}s";
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _spinnerTimer?.Dispose();
    }

    private sealed class SpinnerDisposable : IDisposable
    {
        private readonly ProgressDisplayService _parent;

        public SpinnerDisposable(ProgressDisplayService parent) => _parent = parent;

        public void Dispose() => _parent.StopSpinner();
    }
}

/// <summary>
/// Tracks progress for a specific operation.
/// </summary>
public sealed class OperationProgress : IDisposable
{
    private readonly ProgressDisplayService _display;
    private readonly string _operationName;
    private readonly int _totalItems;
    private int _currentItem;
    private string? _currentItemName;

    internal OperationProgress(ProgressDisplayService display, string operationName, int totalItems)
    {
        _display = display;
        _operationName = operationName;
        _totalItems = totalItems;
        _display.StartTiming();
    }

    /// <summary>
    /// Updates progress to the next item.
    /// </summary>
    public void Next(string? itemName = null)
    {
        _currentItem++;
        _currentItemName = itemName;
        _display.DisplayProgress(_operationName, _currentItem, _totalItems, _currentItemName);
    }

    /// <summary>
    /// Sets progress to a specific position.
    /// </summary>
    public void SetProgress(int current, string? itemName = null)
    {
        _currentItem = current;
        _currentItemName = itemName;
        _display.DisplayProgress(_operationName, _currentItem, _totalItems, _currentItemName);
    }

    /// <summary>
    /// Gets the current progress percentage.
    /// </summary>
    public int PercentComplete => _totalItems > 0 ? (_currentItem * 100) / _totalItems : 0;

    /// <summary>
    /// Completes the operation.
    /// </summary>
    public void Complete()
    {
        var elapsed = _display.StopTiming();
        _display.ClearLine();
        _display.DisplaySuccess($"{_operationName} completed ({_currentItem} items in {FormatTimeSpan(elapsed)})");
    }

    /// <summary>
    /// Marks the operation as failed.
    /// </summary>
    public void Failed(string? reason = null)
    {
        _display.StopTiming();
        _display.ClearLine();
        _display.DisplayError($"{_operationName} failed{(reason != null ? $": {reason}" : "")}");
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalMinutes >= 1)
            return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
        if (ts.TotalSeconds >= 1)
            return $"{ts.TotalSeconds:F1}s";
        return $"{ts.TotalMilliseconds:F0}ms";
    }

    public void Dispose()
    {
        _display.ClearLine();
    }
}

/// <summary>
/// Displays a multi-step checklist with status indicators.
/// </summary>
public sealed class ChecklistDisplay
{
    private readonly ProgressDisplayService _display;
    private readonly string _title;
    private readonly string[] _steps;
    private readonly StepStatus[] _statuses;
    private int _currentStep;

    internal ChecklistDisplay(ProgressDisplayService display, string title, string[] steps)
    {
        _display = display;
        _title = title;
        _steps = steps;
        _statuses = new StepStatus[steps.Length];

        PrintChecklist();
    }

    /// <summary>
    /// Marks the current step as in progress and moves to next.
    /// </summary>
    public void StartStep(int stepIndex)
    {
        _currentStep = stepIndex;
        _statuses[stepIndex] = StepStatus.InProgress;
        PrintChecklist();
    }

    /// <summary>
    /// Marks a step as completed.
    /// </summary>
    public void CompleteStep(int stepIndex)
    {
        _statuses[stepIndex] = StepStatus.Completed;
        PrintChecklist();
    }

    /// <summary>
    /// Marks a step as failed.
    /// </summary>
    public void FailStep(int stepIndex)
    {
        _statuses[stepIndex] = StepStatus.Failed;
        PrintChecklist();
    }

    /// <summary>
    /// Marks a step as skipped.
    /// </summary>
    public void SkipStep(int stepIndex)
    {
        _statuses[stepIndex] = StepStatus.Skipped;
        PrintChecklist();
    }

    private void PrintChecklist()
    {
        // Move cursor up if we've printed before
        // For simplicity, we'll just print each update on new lines in non-interactive mode

        Console.WriteLine();
        Console.WriteLine($"  {_title}");
        Console.WriteLine("  " + new string('-', 40));

        for (int i = 0; i < _steps.Length; i++)
        {
            var status = _statuses[i] switch
            {
                StepStatus.Pending => "[ ]",
                StepStatus.InProgress => "[..]",
                StepStatus.Completed => "[OK]",
                StepStatus.Failed => "[X]",
                StepStatus.Skipped => "[--]",
                _ => "[ ]"
            };

            Console.WriteLine($"    {status} {_steps[i]}");
        }
        Console.WriteLine();
    }

    private enum StepStatus
    {
        Pending,
        InProgress,
        Completed,
        Failed,
        Skipped
    }
}
