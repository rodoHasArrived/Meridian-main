namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Runtime configuration for the Security Master Passport Workbench governed-write surface.
/// Use <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/> so the source-precedence
/// ladder hot-reloads.
/// </summary>
public sealed class SecurityMasterWorkbenchOptions
{
    public const string SectionName = "SecurityMasterWorkbench";

    /// <summary>
    /// Source precedence ladder, highest authority first. The first non-operator entry is treated as
    /// the golden-copy source unless <see cref="GoldenCopySource"/> is set explicitly.
    /// </summary>
    public List<string> SourcePrecedence { get; init; } = ["GoldenCopy", "Edgar", "Polygon", "Operator"];

    /// <summary>Explicit golden-copy source; when null it is derived from <see cref="SourcePrecedence"/>.</summary>
    public string? GoldenCopySource { get; init; }

    /// <summary>Whether governed edits require an independent reviewer (mirrors the approval matrix row).</summary>
    public bool RequireIndependentReviewer { get; init; } = true;

    /// <summary>Maximum number of conflicts a single bulk-resolve request may carry.</summary>
    public int MaxBulkResolveBatch { get; init; } = 200;
}
