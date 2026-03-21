using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Storage.Services;

/// <summary>
/// Cross-platform service for managing file and directory permissions.
/// Ensures the data directory is properly access-restricted for security.
///
/// Unix/Linux: Sets 0755 for directories (rwxr-xr-x), 0644 for files (rw-r--r--)
/// Windows: Configures NTFS ACLs to restrict access to current user + administrators
/// </summary>
public sealed class FilePermissionsService
{
    private readonly ILogger _log;
    private readonly FilePermissionsOptions _options;

    public FilePermissionsService(FilePermissionsOptions? options = null)
    {
        _options = options ?? new FilePermissionsOptions();
        _log = LoggingSetup.ForContext<FilePermissionsService>();
    }

    /// <summary>
    /// Ensures the specified directory exists with proper security permissions.
    /// Creates the directory if it doesn't exist and sets OS-level permissions.
    /// </summary>
    /// <param name="directoryPath">Path to the directory to secure</param>
    /// <returns>Result indicating success or failure with details</returns>
    public FilePermissionsResult EnsureDirectoryPermissions(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return FilePermissionsResult.Failure("Directory path cannot be null or empty");
        }

        try
        {
            var fullPath = Path.GetFullPath(directoryPath);

            // Create directory if it doesn't exist
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
                _log.Information("Created data directory: {DirectoryPath}", fullPath);
            }

            // Set permissions based on OS
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return SetWindowsPermissions(fullPath);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                     RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return SetUnixPermissions(fullPath);
            }
            else
            {
                _log.Warning("Unknown operating system. Skipping permission configuration for {DirectoryPath}", fullPath);
                return FilePermissionsResult.CreateSuccess("Unknown OS - permissions not configured");
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _log.Error(ex, "Access denied setting permissions on {DirectoryPath}. " +
                "Run as administrator or adjust parent directory permissions.", directoryPath);
            return FilePermissionsResult.Failure($"Access denied: {ex.Message}");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to configure permissions for {DirectoryPath}", directoryPath);
            return FilePermissionsResult.Failure($"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Sets Unix/Linux file permissions using chmod.
    /// Directory: 0755 (rwxr-xr-x) - owner full access, others read/execute
    /// Files: 0644 (rw-r--r--) - owner read/write, others read only
    /// </summary>
    private FilePermissionsResult SetUnixPermissions(string directoryPath)
    {
        var dirMode = _options.DirectoryMode ?? "755";
        var fileMode = _options.FileMode ?? "644";

        try
        {
            // Set directory permissions
            var dirResult = ExecuteChmod(dirMode, directoryPath);
            if (!dirResult.Success)
            {
                return dirResult;
            }

            _log.Debug("Set directory permissions to {Mode} for {DirectoryPath}", dirMode, directoryPath);

            // Optionally set permissions on existing files
            if (_options.ApplyToExistingFiles)
            {
                var files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
                var dirs = Directory.GetDirectories(directoryPath, "*", SearchOption.AllDirectories);

                foreach (var dir in dirs)
                {
                    var subDirResult = ExecuteChmod(dirMode, dir);
                    if (!subDirResult.Success)
                    {
                        _log.Warning("Failed to set permissions on subdirectory {SubDir}: {Error}",
                            dir, subDirResult.Message);
                    }
                }

                foreach (var file in files)
                {
                    var fileResult = ExecuteChmod(fileMode, file);
                    if (!fileResult.Success)
                    {
                        _log.Warning("Failed to set permissions on file {File}: {Error}",
                            file, fileResult.Message);
                    }
                }

                _log.Information("Applied permissions to {DirCount} directories and {FileCount} files in {DirectoryPath}",
                    dirs.Length + 1, files.Length, directoryPath);
            }

            return FilePermissionsResult.CreateSuccess($"Unix permissions set: directory={dirMode}, files={fileMode}");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to set Unix permissions on {DirectoryPath}", directoryPath);
            return FilePermissionsResult.Failure($"Failed to set Unix permissions: {ex.Message}");
        }
    }

    /// <summary>
    /// Executes chmod command to set file/directory permissions.
    /// </summary>
    private FilePermissionsResult ExecuteChmod(string mode, string path)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"{mode} \"{path}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);

            if (process.ExitCode != 0)
            {
                return FilePermissionsResult.Failure($"chmod failed: {stderr}");
            }

            return FilePermissionsResult.CreateSuccess("chmod successful");
        }
        catch (Exception ex)
        {
            return FilePermissionsResult.Failure($"Failed to execute chmod: {ex.Message}");
        }
    }

    /// <summary>
    /// Sets Windows NTFS permissions using ACLs.
    /// Configures access for current user and administrators only.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private FilePermissionsResult SetWindowsPermissions(string directoryPath)
    {
        try
        {
            var directoryInfo = new DirectoryInfo(directoryPath);

            if (!_options.RestrictToCurrentUser)
            {
                // Default: inherit parent permissions, just verify write access
                var testResult = VerifyWriteAccess(directoryPath);
                if (!testResult.Success)
                {
                    return testResult;
                }
                return FilePermissionsResult.CreateSuccess("Windows permissions verified (inherited from parent)");
            }

            // Restrictive mode: limit access to current user and administrators
            var security = directoryInfo.GetAccessControl();

            // Disable inheritance and clear existing rules
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            var currentUser = WindowsIdentity.GetCurrent();
            var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

            // Grant full control to current user
            security.AddAccessRule(new FileSystemAccessRule(
                currentUser.User!,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

            // Grant full control to administrators
            security.AddAccessRule(new FileSystemAccessRule(
                adminsSid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

            // Grant full control to SYSTEM
            security.AddAccessRule(new FileSystemAccessRule(
                systemSid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));

            directoryInfo.SetAccessControl(security);

            _log.Information("Configured restrictive Windows ACLs for {DirectoryPath}: " +
                "Access limited to current user, administrators, and SYSTEM", directoryPath);

            return FilePermissionsResult.CreateSuccess("Windows ACLs configured successfully");
        }
        catch (PlatformNotSupportedException)
        {
            // ACL APIs not available on this Windows version, fall back to verification only
            _log.Debug("Windows ACL APIs not available, verifying write access only");
            return VerifyWriteAccess(directoryPath);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to set Windows permissions on {DirectoryPath}", directoryPath);
            return FilePermissionsResult.Failure($"Failed to set Windows permissions: {ex.Message}");
        }
    }

    /// <summary>
    /// Verifies that the application has write access to the directory.
    /// </summary>
    public FilePermissionsResult VerifyWriteAccess(string directoryPath)
    {
        try
        {
            var testFile = Path.Combine(directoryPath, $".permission_test_{Guid.NewGuid():N}");

            // Try to create and delete a test file
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);

            return FilePermissionsResult.CreateSuccess("Write access verified");
        }
        catch (UnauthorizedAccessException ex)
        {
            return FilePermissionsResult.Failure($"Write access denied: {ex.Message}");
        }
        catch (Exception ex)
        {
            return FilePermissionsResult.Failure($"Failed to verify write access: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks current permissions on a directory and returns a diagnostic report.
    /// </summary>
    public FilePermissionsDiagnostic GetPermissionsDiagnostic(string directoryPath)
    {
        var diagnostic = new FilePermissionsDiagnostic
        {
            DirectoryPath = directoryPath,
            Exists = Directory.Exists(directoryPath),
            Platform = RuntimeInformation.OSDescription
        };

        if (!diagnostic.Exists)
        {
            diagnostic.Issues.Add("Directory does not exist");
            return diagnostic;
        }

        try
        {
            var directoryInfo = new DirectoryInfo(directoryPath);
            diagnostic.CreationTime = directoryInfo.CreationTimeUtc;
            diagnostic.LastWriteTime = directoryInfo.LastWriteTimeUtc;

            // Test read access
            try
            {
                _ = Directory.GetFiles(directoryPath);
                diagnostic.CanRead = true;
            }
            catch
            {
                diagnostic.CanRead = false;
                diagnostic.Issues.Add("Cannot list directory contents (read access denied)");
            }

            // Test write access
            var writeResult = VerifyWriteAccess(directoryPath);
            diagnostic.CanWrite = writeResult.Success;
            if (!writeResult.Success)
            {
                diagnostic.Issues.Add($"Cannot write to directory: {writeResult.Message}");
            }

            // Get Unix permissions if applicable
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                diagnostic.UnixMode = GetUnixMode(directoryPath);
            }
        }
        catch (Exception ex)
        {
            diagnostic.Issues.Add($"Error checking permissions: {ex.Message}");
        }

        return diagnostic;
    }

    /// <summary>
    /// Gets Unix file mode string using stat command.
    /// </summary>
    private string? GetUnixMode(string path)
    {
        try
        {
            var statFormat = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "-f %Lp"
                : "-c %a";

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "stat",
                    Arguments = $"{statFormat} \"{path}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(3000);

            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Configuration options for file permissions service.
/// </summary>
public sealed class FilePermissionsOptions
{
    /// <summary>
    /// Unix directory permission mode (default: 755 = rwxr-xr-x).
    /// </summary>
    public string? DirectoryMode { get; init; } = "755";

    /// <summary>
    /// Unix file permission mode (default: 644 = rw-r--r--).
    /// </summary>
    public string? FileMode { get; init; } = "644";

    /// <summary>
    /// Whether to apply permissions to existing files in the directory.
    /// </summary>
    public bool ApplyToExistingFiles { get; init; } = false;

    /// <summary>
    /// Windows: Whether to restrict access to current user only.
    /// If false (default), inherits parent directory permissions.
    /// </summary>
    public bool RestrictToCurrentUser { get; init; } = false;

    /// <summary>
    /// Whether to validate permissions on startup.
    /// </summary>
    public bool ValidateOnStartup { get; init; } = true;
}

/// <summary>
/// Result of a file permissions operation.
/// </summary>
public sealed record FilePermissionsResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public List<string> Warnings { get; init; } = new();

    public static FilePermissionsResult Failure(string message) => new()
    {
        Success = false,
        Message = message
    };

    public static FilePermissionsResult CreateSuccess(string message) => new()
    {
        Success = true,
        Message = message
    };
}

/// <summary>
/// Diagnostic information about directory permissions.
/// </summary>
public sealed class FilePermissionsDiagnostic
{
    public string DirectoryPath { get; init; } = "";
    public bool Exists { get; init; }
    public bool CanRead { get; set; }
    public bool CanWrite { get; set; }
    public string? UnixMode { get; set; }
    public string Platform { get; init; } = "";
    public DateTime? CreationTime { get; set; }
    public DateTime? LastWriteTime { get; set; }
    public List<string> Issues { get; } = new();

    public bool HasIssues => Issues.Count > 0;

    public override string ToString()
    {
        var status = HasIssues ? "ISSUES DETECTED" : "OK";
        var modeInfo = UnixMode != null ? $", mode={UnixMode}" : "";
        return $"[{status}] {DirectoryPath} (read={CanRead}, write={CanWrite}{modeInfo})";
    }
}
