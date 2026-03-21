using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using Meridian.Contracts.Api;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Services;

/// <summary>
/// Service for managing data storage, file operations, and storage statistics.
/// Provides access to storage paths, file listings, and space usage.
/// Passes <see cref="ApiClientService"/> to the base class constructor.
/// </summary>
public sealed class StorageService : StorageServiceBase
{
    private static readonly Lazy<StorageService> _instance = new(() => new StorageService());
    public static StorageService Instance => _instance.Value;

    private StorageService()
        : base(ApiClientService.Instance)
    {
    }

    /// <summary>
    /// Gets list of data files for a symbol.
    /// </summary>
    public async Task<List<DataFileInfo>> GetSymbolFilesAsync(string symbol, CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.GetAsync<List<SymbolFileDto>>(
            UiApiRoutes.WithParam(UiApiRoutes.StorageSymbolFiles, "symbol", symbol), ct);

        if (response == null) return new List<DataFileInfo>();

        var files = new List<DataFileInfo>();
        foreach (var dto in response)
        {
            files.Add(new DataFileInfo
            {
                FileName = dto.FileName,
                FileType = dto.DataType,
                FileSize = FormatBytes(dto.SizeBytes),
                ModifiedDate = dto.ModifiedAt.ToString("yyyy-MM-dd HH:mm"),
                EventCount = dto.RecordCount.ToString("N0"),
                FileIcon = GetFileIcon(dto.DataType),
                TypeBackground = GetTypeBackground(dto.DataType)
            });
        }

        return files;
    }

    private static SolidColorBrush GetTypeBackground(string dataType)
    {
        return dataType.ToLowerInvariant() switch
        {
            "trades" => new SolidColorBrush(Color.FromArgb(30, 63, 185, 80)),    // Green
            "quotes" => new SolidColorBrush(Color.FromArgb(30, 88, 166, 255)),   // Blue
            "depth" => new SolidColorBrush(Color.FromArgb(30, 210, 153, 34)),    // Orange
            "bars" => new SolidColorBrush(Color.FromArgb(30, 163, 113, 247)),    // Purple
            _ => new SolidColorBrush(Color.FromArgb(30, 139, 148, 158))          // Gray
        };
    }
}
