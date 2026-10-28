using System;
using System.Collections.Generic;

namespace Meridian.Ui.Services;

// DTO classes for storage API responses
// Shared across desktop applications.

/// <summary>
/// Summarizes storage capacity, utilization, files, symbols, and retained date coverage.
/// </summary>
public sealed class StorageStatsSummary
{
    public long TotalSizeBytes { get; set; }
    public long UsedSizeBytes { get; set; }
    public long FreeSizeBytes { get; set; }
    public double UsedPercentage { get; set; }
    public int TotalFiles { get; set; }
    public int TotalSymbols { get; set; }
    public DateTime OldestData { get; set; }
    public DateTime NewestData { get; set; }
}

/// <summary>
/// Describes a storage category's file count, size, and share of overall usage.
/// </summary>
public sealed class StorageCategory
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int FileCount { get; set; }
    public double Percentage { get; set; }
}

/// <summary>
/// Describes a symbol's stored data types, provider, subscription state, and timestamp coverage.
/// </summary>
public sealed class SymbolStorageInfo
{
    public string Symbol { get; set; } = string.Empty;
    public string? Exchange { get; set; }
    public string? Provider { get; set; }
    public bool IsSubscribed { get; set; }
    public DateTime? FirstDataPoint { get; set; }
    public DateTime? LastDataPoint { get; set; }
    public List<string> DataTypes { get; set; } = new();
}

/// <summary>
/// Summarizes a symbol's stored bytes, events, files, quality, and per-type usage.
/// </summary>
public sealed class SymbolStorageStats
{
    public string Symbol { get; set; } = string.Empty;
    public long TotalSizeBytes { get; set; }
    public long TotalEvents { get; set; }
    public int FileCount { get; set; }
    public double DataQuality { get; set; }
    public int GapCount { get; set; }
    public Dictionary<string, long> SizeByType { get; set; } = new();
    public Dictionary<string, long> EventsByType { get; set; } = new();
}

/// <summary>
/// Describes a stored symbol file's type, size, modification time, records, and compression.
/// </summary>
public sealed class SymbolFileDto
{
    public string FileName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime ModifiedAt { get; set; }
    public long RecordCount { get; set; }
    public string? CompressionType { get; set; }
}

/// <summary>
/// Carries the API storage-folder path for a symbol.
/// </summary>
public sealed class SymbolPathResponse
{
    public string Symbol { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
}

/// <summary>
/// Summarizes storage health, detected issues, recommendations, and the check timestamp.
/// </summary>
public sealed class StorageHealthReport
{
    public string Status { get; set; } = string.Empty;
    public double HealthScore { get; set; }
    public List<StorageIssue> Issues { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
    public DateTime CheckedAt { get; set; }
}

/// <summary>
/// Describes a storage problem's type, severity, explanation, and affected path.
/// </summary>
public sealed class StorageIssue
{
    public string Type { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? AffectedPath { get; set; }
}

/// <summary>
/// Identifies a file proposed for cleanup with its size, last access, and selection reason.
/// </summary>
public sealed class CleanupCandidate
{
    public string Path { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime LastAccessed { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Reports cleanup success, removed files, reclaimed bytes, and encountered errors.
/// </summary>
public sealed class CleanupResult
{
    public bool Success { get; set; }
    public int FilesRemoved { get; set; }
    public long BytesFreed { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Summarizes archive counts, size, compression, and oldest and newest archive dates.
/// </summary>
public sealed class ArchiveStats
{
    public int TotalArchives { get; set; }
    public long TotalSizeBytes { get; set; }
    public int CompressedFiles { get; set; }
    public double CompressionRatio { get; set; }
    public DateTime OldestArchive { get; set; }
    public DateTime NewestArchive { get; set; }
}
