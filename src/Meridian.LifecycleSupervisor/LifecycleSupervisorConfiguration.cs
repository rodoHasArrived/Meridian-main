using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Lifecycle;

namespace Meridian.LifecycleSupervisor;

internal sealed record LifecycleSupervisorConfiguration
{
    public required string InstallRoot { get; init; }
    public required string ManifestPath { get; init; }
    public required string ServiceRoot { get; init; }
    public required string DataRoot { get; init; }
    public required string RuntimeRoot { get; init; }
    public required string ReceiptRoot { get; init; }
    public required string SecretPath { get; init; }
    public required string DatabaseSecretPath { get; init; }
    public required string HostPath { get; init; }
    public required string PipeName { get; init; }
    public required LifecycleSupervisorManifestDto Manifest { get; init; }

    public static LifecycleSupervisorConfiguration Load(string installRoot)
    {
        var canonicalInstallRoot = Path.GetFullPath(installRoot);
        var serviceRoot = Path.Combine(canonicalInstallRoot, "service");
        var manifestPath = Path.Combine(serviceRoot, "lifecycle-supervisor.json");
        Directory.CreateDirectory(serviceRoot);

        LifecycleSupervisorManifestDto manifest;
        if (File.Exists(manifestPath))
        {
            manifest = JsonSerializer.Deserialize(
                    File.ReadAllText(manifestPath),
                    LifecycleContractsJsonContext.Default.LifecycleSupervisorManifestDto)
                ?? throw new InvalidDataException("The lifecycle supervisor manifest is empty.");
        }
        else
        {
            manifest = new LifecycleSupervisorManifestDto();
            AtomicJsonFile.Write(
                manifestPath,
                JsonSerializer.SerializeToUtf8Bytes(
                    manifest,
                    LifecycleContractsJsonContext.Default.LifecycleSupervisorManifestDto));
        }

        Validate(manifest);
        var defaultDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Meridian",
            "Data");
        var dataRoot = ResolvePath(manifest.DataRoot, canonicalInstallRoot, defaultDataRoot);
        var runtimeRoot = Path.Combine(dataRoot, "runtime", "lifecycle");
        var secretRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Meridian",
            "service");

        return new LifecycleSupervisorConfiguration
        {
            InstallRoot = canonicalInstallRoot,
            ServiceRoot = serviceRoot,
            ManifestPath = manifestPath,
            DataRoot = dataRoot,
            RuntimeRoot = runtimeRoot,
            ReceiptRoot = Path.Combine(runtimeRoot, "receipts"),
            SecretPath = Path.Combine(secretRoot, "lifecycle-shutdown-token.dpapi"),
            DatabaseSecretPath = Path.Combine(secretRoot, "lifecycle-postgresql-password.dpapi"),
            HostPath = ResolvePath(manifest.HostRelativePath, canonicalInstallRoot, Path.Combine(canonicalInstallRoot, "host", "Meridian.exe")),
            PipeName = CreatePipeName(canonicalInstallRoot),
            Manifest = manifest
        };
    }

    public string? ResolveConfigPath()
        => string.IsNullOrWhiteSpace(Manifest.ConfigPath)
            ? null
            : ResolvePath(Manifest.ConfigPath, InstallRoot, Manifest.ConfigPath);

    public string ResolvePostgreSqlBinPath()
    {
        if (!string.IsNullOrWhiteSpace(Manifest.PostgreSqlBinPath))
        {
            return ResolvePath(Manifest.PostgreSqlBinPath, InstallRoot, Manifest.PostgreSqlBinPath);
        }

        var bundled = Path.Combine(InstallRoot, "database", "bin");
        if (Directory.Exists(bundled))
        {
            return bundled;
        }

        var home = Environment.GetEnvironmentVariable("MDC_POSTGRES_HOME");
        return string.IsNullOrWhiteSpace(home) ? string.Empty : Path.Combine(home, "bin");
    }

    private static string ResolvePath(string? configured, string basePath, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(configured) ? fallback : Environment.ExpandEnvironmentVariables(configured);
        return Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(basePath, value));
    }

    private static string CreatePipeName(string installRoot)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(installRoot.ToUpperInvariant()));
        return $"Meridian.LifecycleSupervisor.{Convert.ToHexString(hash.AsSpan(0, 8))}";
    }

    private static void Validate(LifecycleSupervisorManifestDto manifest)
    {
        if (manifest.SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported lifecycle supervisor manifest schema {manifest.SchemaVersion}.");
        if (!Enum.IsDefined(manifest.DatabaseMode))
            throw new InvalidDataException($"Unsupported lifecycle database mode '{manifest.DatabaseMode}'.");
        if (manifest.HttpPort is < 1 or > 65535)
            throw new InvalidDataException("httpPort must be between 1 and 65535.");
        if (manifest.DatabasePort is < 1 or > 65535)
            throw new InvalidDataException("databasePort must be between 1 and 65535.");
        if (manifest.StartupTimeoutSeconds is < 1 or > 600 ||
            manifest.ShutdownTimeoutSeconds is < 1 or > 600 ||
            manifest.DatabaseTimeoutSeconds is < 1 or > 600)
            throw new InvalidDataException("Lifecycle timeout values must be between 1 and 600 seconds.");
    }
}

internal static class AtomicJsonFile
{
    public static void Write(string path, ReadOnlySpan<byte> bytes)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("A parent directory is required.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
