using Meridian.Infrastructure.Contracts;

namespace Meridian.Application.Scheduling;

/// <summary>
/// Types of operations that can be scheduled.
/// </summary>
public enum OperationType : byte
{
    /// <summary>Routine maintenance (archival, cleanup).</summary>
    Maintenance,

    /// <summary>Historical data backfill.</summary>
    Backfill,

    /// <summary>Data integrity checks and repairs.</summary>
    IntegrityCheck,

    /// <summary>Report generation.</summary>
    Reporting,

    /// <summary>System health checks.</summary>
    HealthCheck,

    /// <summary>Cache refresh operations.</summary>
    CacheRefresh,

    /// <summary>Provider credential refresh.</summary>
    CredentialRefresh,

    /// <summary>Index rebuilding.</summary>
    IndexRebuild
}

/// <summary>
/// Resource requirements for an operation.
/// </summary>
/// <param name="RequiresCpuIntensive">Whether the operation is CPU-intensive.</param>
/// <param name="RequiresIoIntensive">Whether the operation is I/O-intensive.</param>
/// <param name="RequiresNetwork">Whether the operation requires network access.</param>
/// <param name="EstimatedDuration">Estimated duration of the operation.</param>
/// <param name="CanBeInterrupted">Whether the operation can be safely interrupted.</param>
public sealed record ResourceRequirements(
    bool RequiresCpuIntensive = false,
    bool RequiresIoIntensive = false,
    bool RequiresNetwork = false,
    TimeSpan? EstimatedDuration = null,
    bool CanBeInterrupted = true);

/// <summary>
/// Decision about whether an operation can be executed.
/// </summary>
/// <param name="CanExecute">Whether the operation can execute now.</param>
/// <param name="Reason">Reason for the decision.</param>
/// <param name="SuggestedDelay">Suggested delay before retrying if not allowed.</param>
/// <param name="Context">Additional context.</param>
public sealed record ScheduleDecision(
    bool CanExecute,
    string? Reason = null,
    TimeSpan? SuggestedDelay = null,
    IReadOnlyDictionary<string, object>? Context = null)
{
    /// <summary>
    /// Decision indicating operation can execute.
    /// </summary>
    public static readonly ScheduleDecision Allowed = new(true);

    /// <summary>
    /// Creates a denied decision with reason.
    /// </summary>
    public static ScheduleDecision Denied(string reason, TimeSpan? suggestedDelay = null)
        => new(false, reason, suggestedDelay);
}

/// <summary>
/// A scheduled time slot for an operation.
/// </summary>
/// <param name="StartTime">When the slot starts.</param>
/// <param name="EndTime">When the slot ends.</param>
/// <param name="SlotType">Type of slot (trading hours, maintenance window, etc.).</param>
public sealed record ScheduleSlot(
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    string SlotType)
{
    /// <summary>
    /// Duration of the slot.
    /// </summary>
    public TimeSpan Duration => EndTime - StartTime;

    /// <summary>
    /// Whether the slot is currently active.
    /// </summary>
    public bool IsActive => DateTimeOffset.UtcNow >= StartTime && DateTimeOffset.UtcNow < EndTime;
}

/// <summary>
/// Represents a trading session.
/// </summary>
/// <param name="Exchange">Exchange identifier.</param>
/// <param name="Market">Market name (e.g., "US", "EU").</param>
/// <param name="SessionType">Type of session (pre-market, regular, after-hours).</param>
/// <param name="OpenTime">Session open time.</param>
/// <param name="CloseTime">Session close time.</param>
public sealed record TradingSession(
    string Exchange,
    string Market,
    string SessionType,
    DateTimeOffset OpenTime,
    DateTimeOffset CloseTime);

/// <summary>
/// Maintenance window definition.
/// </summary>
/// <param name="Name">Window name.</param>
/// <param name="StartTime">Window start time.</param>
/// <param name="EndTime">Window end time.</param>
/// <param name="AllowedOperations">Operations allowed during this window.</param>
/// <param name="Priority">Priority for conflicting windows.</param>
public sealed record MaintenanceWindow(
    string Name,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    IReadOnlyList<OperationType>? AllowedOperations = null,
    int Priority = 0);

/// <summary>
/// Centralized operational scheduler for coordinating maintenance tasks,
/// backfill operations, and other scheduled activities with trading hours.
/// </summary>
/// <remarks>
/// The operational scheduler provides:
/// - Awareness of trading hours across markets
/// - Maintenance window management
/// - Operation scheduling with resource requirements
/// - Conflict detection and resolution
/// </remarks>
[ImplementsAdr("ADR-001", "Centralized operational scheduling")]
public interface IOperationalScheduler
{
    /// <summary>
    /// Checks if an operation can execute now based on current conditions.
    /// </summary>
    /// <param name="operationType">Type of operation.</param>
    /// <param name="requirements">Resource requirements for the operation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Decision about whether the operation can execute.</returns>
    Task<ScheduleDecision> CanExecuteAsync(
        OperationType operationType,
        ResourceRequirements? requirements = null,
        CancellationToken ct = default);

    /// <summary>
    /// Finds the next available slot for an operation.
    /// </summary>
    /// <param name="operationType">Type of operation.</param>
    /// <param name="minDuration">Minimum duration required.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Next available slot or null if none found within search window.</returns>
    Task<ScheduleSlot?> FindNextAvailableSlotAsync(
        OperationType operationType,
        TimeSpan minDuration,
        CancellationToken ct = default);

    /// <summary>
    /// Gets whether we are currently within trading hours for any market.
    /// </summary>
    bool IsWithinTradingHours { get; }

    /// <summary>
    /// Gets whether we are currently within a maintenance window.
    /// </summary>
    bool IsWithinMaintenanceWindow { get; }

    /// <summary>
    /// Gets the current trading session if any.
    /// </summary>
    /// <returns>Current trading session or null.</returns>
    TradingSession? GetCurrentTradingSession();

    /// <summary>
    /// Gets the next trading session.
    /// </summary>
    /// <returns>Next trading session.</returns>
    TradingSession? GetNextTradingSession();

    /// <summary>
    /// Gets the current maintenance window if any.
    /// </summary>
    /// <returns>Current maintenance window or null.</returns>
    MaintenanceWindow? GetCurrentMaintenanceWindow();

    /// <summary>
    /// Gets the next maintenance window.
    /// </summary>
    /// <returns>Next maintenance window or null.</returns>
    MaintenanceWindow? GetNextMaintenanceWindow();

    /// <summary>
    /// Registers a maintenance window.
    /// </summary>
    /// <param name="window">Maintenance window to register.</param>
    void RegisterMaintenanceWindow(MaintenanceWindow window);

    /// <summary>
    /// Removes a maintenance window.
    /// </summary>
    /// <param name="windowName">Name of the window to remove.</param>
    void RemoveMaintenanceWindow(string windowName);
}

/// <summary>
/// Provides trading calendar information.
/// </summary>
[ImplementsAdr("ADR-001", "Trading calendar provider")]
public interface ITradingCalendarProvider
{
    /// <summary>
    /// Checks if a specific date is a trading day.
    /// </summary>
    /// <param name="date">Date to check.</param>
    /// <param name="market">Market to check (default: "US").</param>
    /// <returns>True if the date is a trading day.</returns>
    bool IsTradingDay(DateOnly date, string market = "US");

    /// <summary>
    /// Gets trading hours for a specific date.
    /// </summary>
    /// <param name="date">Date to check.</param>
    /// <param name="market">Market to check.</param>
    /// <returns>List of trading sessions for the date.</returns>
    IReadOnlyList<TradingSession> GetTradingSessions(DateOnly date, string market = "US");

    /// <summary>
    /// Gets the next trading day.
    /// </summary>
    /// <param name="after">Date to start searching from.</param>
    /// <param name="market">Market to check.</param>
    /// <returns>Next trading day.</returns>
    DateOnly GetNextTradingDay(DateOnly after, string market = "US");

    /// <summary>
    /// Gets market holidays for a year.
    /// </summary>
    /// <param name="year">Year to get holidays for.</param>
    /// <param name="market">Market to check.</param>
    /// <returns>List of holiday dates.</returns>
    IReadOnlyList<DateOnly> GetHolidays(int year, string market = "US");
}
