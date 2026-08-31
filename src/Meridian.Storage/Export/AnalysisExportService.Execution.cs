namespace Meridian.Storage.Export;

/// <summary>
/// Failure cleanup and artifact-evidence guards for analysis export execution.
/// </summary>
public sealed partial class AnalysisExportService
{
    private static string? ValidateArtifactFormatEvidence(
        ExportFormat expectedFormat,
        IReadOnlyCollection<ExportedFile> exportedFiles)
    {
        var expected = ToArtifactFormat(expectedFormat);
        var missingFormat = exportedFiles.FirstOrDefault(
            static file => string.IsNullOrWhiteSpace(file.Format));
        if (missingFormat is not null)
            return $"Export artifact '{missingFormat.Path}' did not identify its generated format.";

        var mismatch = exportedFiles.FirstOrDefault(
            file => !string.Equals(file.Format, expected, StringComparison.OrdinalIgnoreCase));
        return mismatch is null
            ? null
            : $"Export profile format '{expected}' does not match artifact " +
              $"'{mismatch.Path}' format '{mismatch.Format}'.";
    }

    private static string ToArtifactFormat(ExportFormat format) =>
        format switch
        {
            ExportFormat.Parquet => "parquet",
            ExportFormat.Csv => "csv",
            ExportFormat.Jsonl => "jsonl",
            ExportFormat.Lean => "lean",
            ExportFormat.Xlsx => "xlsx",
            ExportFormat.Sql => "sql",
            ExportFormat.Arrow => "arrow",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown export format.")
        };

    private static void ResetArtifactEvidence(ExportResult result)
    {
        result.Files = Array.Empty<ExportedFile>();
        result.FilesGenerated = 0;
        result.TotalRecords = 0;
        result.TotalBytes = 0;
        result.DataDictionaryPath = null;
        result.LoaderScriptPath = null;
        result.LineageManifestPath = null;
        result.QualitySummary = null;
    }

    private void CleanupNewExportArtifacts(OutputArtifactSnapshot? snapshot)
    {
        if (snapshot is null)
            return;

        var rollbackComplete = false;
        try
        {
            rollbackComplete = RestoreProtectedExportArtifacts(snapshot);
            if (!Directory.Exists(snapshot.Root))
                return;

            foreach (var path in Directory.EnumerateFiles(snapshot.Root, "*", SearchOption.AllDirectories))
            {
                if (snapshot.ExistingFiles.Contains(path))
                    continue;

                try
                {
                    File.Delete(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _log.Warning(ex, "Could not remove failed export artifact {ExportArtifactPath}", path);
                }
            }

            foreach (var directory in Directory.EnumerateDirectories(
                         snapshot.Root,
                         "*",
                         SearchOption.AllDirectories)
                     .OrderByDescending(static path => path.Length))
            {
                if (snapshot.ExistingDirectories.Contains(directory) ||
                    Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    continue;
                }

                Directory.Delete(directory);
            }

            if (!snapshot.RootExisted &&
                Directory.Exists(snapshot.Root) &&
                !Directory.EnumerateFileSystemEntries(snapshot.Root).Any())
            {
                Directory.Delete(snapshot.Root);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning(ex, "Could not fully clean failed export output {ExportOutputDirectory}", snapshot.Root);
        }
        finally
        {
            if (rollbackComplete)
                snapshot.DiscardBackups();
        }
    }

    private static void EnsureExportArtifactMayBeWritten(string path, bool overwriteExisting)
    {
        if (!overwriteExisting && File.Exists(path))
        {
            throw new IOException(
                $"Export artifact '{path}' already exists and overwriteExisting is false.");
        }
    }

    private static string CreateExportArtifactStagingPath(string finalPath) =>
        $"{finalPath}.{Guid.NewGuid():N}.tmp";

    private void CommitStagedExportArtifact(
        string stagedPath,
        string finalPath,
        bool overwriteExisting,
        OutputArtifactSnapshot? snapshot)
    {
        EnsureExportArtifactMayBeWritten(finalPath, overwriteExisting);

        if (File.Exists(finalPath) && snapshot is null)
        {
            throw new InvalidOperationException(
                $"Cannot safely overwrite export artifact '{finalPath}' without rollback evidence.");
        }

        if (File.Exists(finalPath))
        {
            // The null case fails closed above.
            snapshot!.ProtectExistingFile(finalPath);
        }

        File.Move(stagedPath, finalPath, overwriteExisting);
    }

    private void DeleteStagedExportArtifact(string stagedPath)
    {
        if (!File.Exists(stagedPath))
            return;

        try
        {
            File.Delete(stagedPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning(ex, "Could not remove staged export artifact {ExportArtifactPath}", stagedPath);
        }
    }

    private bool RestoreProtectedExportArtifacts(OutputArtifactSnapshot snapshot)
    {
        var restoredAll = true;
        foreach (var (finalPath, backupPath) in snapshot.ProtectedFiles)
        {
            try
            {
                var restorePath = CreateExportArtifactStagingPath(finalPath);
                try
                {
                    File.Copy(backupPath, restorePath, overwrite: false);
                    File.Move(restorePath, finalPath, overwrite: true);
                }
                finally
                {
                    DeleteStagedExportArtifact(restorePath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                restoredAll = false;
                _log.Error(
                    ex,
                    "Could not restore pre-existing export artifact {ExportArtifactPath} from {BackupPath}",
                    finalPath,
                    backupPath);
            }
        }

        return restoredAll;
    }

    private static StringComparer GetPathComparer() =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private sealed class OutputArtifactSnapshot
    {
        private readonly Dictionary<string, string> _protectedFiles;
        private string? _backupRoot;

        private OutputArtifactSnapshot(
            string root,
            bool rootExisted,
            HashSet<string> existingFiles,
            HashSet<string> existingDirectories)
        {
            Root = root;
            RootExisted = rootExisted;
            ExistingFiles = existingFiles;
            ExistingDirectories = existingDirectories;
            _protectedFiles = new Dictionary<string, string>(GetPathComparer());
        }

        public string Root { get; }
        public bool RootExisted { get; }
        public HashSet<string> ExistingFiles { get; }
        public HashSet<string> ExistingDirectories { get; }
        public IReadOnlyDictionary<string, string> ProtectedFiles => _protectedFiles;

        public static OutputArtifactSnapshot? Capture(string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
                return null;

            var fullPath = Path.GetFullPath(outputDirectory);
            if (string.Equals(fullPath, Path.GetPathRoot(fullPath), GetPathComparison()))
                return null;

            var root = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var rootExisted = Directory.Exists(root);
            var comparer = GetPathComparer();
            var files = rootExisted
                ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToHashSet(comparer)
                : new HashSet<string>(comparer);
            var directories = rootExisted
                ? Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).ToHashSet(comparer)
                : new HashSet<string>(comparer);

            return new OutputArtifactSnapshot(root, rootExisted, files, directories);
        }

        public void ProtectExistingFile(string path)
        {
            var fullPath = Path.GetFullPath(path);
            if (!ExistingFiles.Contains(fullPath) || _protectedFiles.ContainsKey(fullPath))
                return;

            _backupRoot ??= Path.Combine(
                Path.GetTempPath(),
                $"meridian-export-rollback-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_backupRoot);

            var backupPath = Path.Combine(
                _backupRoot,
                $"{_protectedFiles.Count:D6}-{Path.GetFileName(fullPath)}");
            File.Copy(fullPath, backupPath, overwrite: false);
            _protectedFiles.Add(fullPath, backupPath);
        }

        public void DiscardBackups()
        {
            if (_backupRoot is null || !Directory.Exists(_backupRoot))
                return;

            try
            {
                Directory.Delete(_backupRoot, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup. The rollback directory contains only copies of
                // caller-owned export artifacts and no durable authority state.
            }
        }

        private static StringComparison GetPathComparison() =>
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
    }
}
