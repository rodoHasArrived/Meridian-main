using System;
using System.IO;
using Meridian.Ui.Services.Services;

namespace Meridian.Wpf.Services;

/// <summary>
/// WPF export preset service.
/// Passes the platform-specific presets directory to the base class constructor.
/// All business logic is in <see cref="ExportPresetServiceBase"/>.
/// </summary>
public sealed class ExportPresetService : ExportPresetServiceBase
{
    private static readonly Lazy<ExportPresetService> _instance = new(() => new ExportPresetService());
    public static ExportPresetService Instance => _instance.Value;

    private ExportPresetService()
        : base(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Meridian"))
    {
    }
}
