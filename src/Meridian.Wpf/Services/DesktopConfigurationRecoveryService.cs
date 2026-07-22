using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Contracts.Configuration;
using Meridian.Storage.Archival;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Services;

/// <summary>
/// Result of the desktop configuration preflight. Recovery artifacts are retained beside the
/// configuration so an operator can inspect exactly what was displaced and why.
/// </summary>
public sealed record DesktopConfigurationRecoveryResult(
    ConfigurationProvisioningResult Outcome,
    string ConfigPath,
    string? RestoredFromPath,
    string? InvalidConfigurationPath,
    string? RecoveryReceiptPath,
    string? FailureReason)
{
    public bool Recovered => Outcome is ConfigurationProvisioningResult.RestoredLastKnownGood
        or ConfigurationProvisioningResult.RepairedInvalid;

    public IReadOnlyList<string> RetainedArtifacts
    {
        get
        {
            var artifacts = new List<string>(2);
            if (!string.IsNullOrWhiteSpace(InvalidConfigurationPath))
                artifacts.Add(InvalidConfigurationPath);
            if (!string.IsNullOrWhiteSpace(RecoveryReceiptPath))
                artifacts.Add(RecoveryReceiptPath);
            return artifacts;
        }
    }

    public string OperatorMessage => Outcome switch
    {
        ConfigurationProvisioningResult.CreatedDefault =>
            "Created a valid desktop configuration and retained a last-known-good copy.",
        ConfigurationProvisioningResult.RestoredLastKnownGood =>
            "Recovered the desktop configuration from the last-known-good copy. The invalid file was retained for review.",
        ConfigurationProvisioningResult.RepairedInvalid =>
            "The desktop configuration was invalid. Defaults were restored and the invalid file was retained for review.",
        _ => "Loaded the existing desktop configuration."
    };
}

/// <summary>
/// Performs configuration validation, last-known-good recovery, and atomic persistence without
/// depending on WPF application state. This service is intentionally path-injected for focused
/// startup-recovery tests.
/// </summary>
public sealed class DesktopConfigurationRecoveryService
{
    private static readonly JsonSerializerOptions ConfigurationJsonOptions =
        new(DesktopJsonOptions.PrettyPrint)
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

    private readonly string _configPath;
    private readonly Func<DateTimeOffset> _utcNow;

    public DesktopConfigurationRecoveryService(
        string configPath,
        Func<DateTimeOffset>? utcNow = null)
    {
        if (string.IsNullOrWhiteSpace(configPath))
            throw new ArgumentException("A desktop configuration path is required.", nameof(configPath));

        _configPath = Path.GetFullPath(configPath);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public string ConfigPath => _configPath;

    public string LastKnownGoodPath => $"{_configPath}.last-known-good";

    public DesktopConfigurationRecoveryResult EnsureReadableConfiguration(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        EnsureParentDirectory();

        if (!File.Exists(_configPath))
        {
            var defaultJson = SerializeDefaultConfiguration();
            PersistValidConfiguration(defaultJson, ct);
            return new DesktopConfigurationRecoveryResult(
                ConfigurationProvisioningResult.CreatedDefault,
                _configPath,
                RestoredFromPath: null,
                InvalidConfigurationPath: null,
                RecoveryReceiptPath: null,
                FailureReason: null);
        }

        if (TryReadValidConfiguration(_configPath, out var currentJson, out _))
        {
            // A valid operator edit becomes the new recovery point. Failure to refresh this
            // auxiliary copy must not make an otherwise readable configuration fatal.
            try
            {
                AtomicFileWriter.Write(LastKnownGoodPath, currentJson!, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new DesktopConfigurationRecoveryResult(
                    ConfigurationProvisioningResult.AlreadyValid,
                    _configPath,
                    RestoredFromPath: null,
                    InvalidConfigurationPath: null,
                    RecoveryReceiptPath: null,
                    FailureReason: $"Unable to refresh the last-known-good copy: {ex.Message}");
            }

            return new DesktopConfigurationRecoveryResult(
                ConfigurationProvisioningResult.AlreadyValid,
                _configPath,
                RestoredFromPath: null,
                InvalidConfigurationPath: null,
                RecoveryReceiptPath: null,
                FailureReason: null);
        }

        _ = TryReadValidConfiguration(_configPath, out _, out var invalidReason);
        var suffix = $"{_utcNow():yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        var invalidPath = $"{_configPath}.invalid-{suffix}.bak";
        File.Move(_configPath, invalidPath);

        string replacementJson;
        string? restoredFromPath = null;
        ConfigurationProvisioningResult outcome;

        if (TryReadValidConfiguration(LastKnownGoodPath, out var lastKnownGoodJson, out _))
        {
            replacementJson = lastKnownGoodJson!;
            restoredFromPath = LastKnownGoodPath;
            outcome = ConfigurationProvisioningResult.RestoredLastKnownGood;
        }
        else
        {
            replacementJson = SerializeDefaultConfiguration();
            outcome = ConfigurationProvisioningResult.RepairedInvalid;
        }

        PersistValidConfiguration(replacementJson, ct);
        var receiptPath = $"{_configPath}.recovery-{suffix}.json";
        var receipt = new ConfigurationRecoveryReceipt(
            SchemaVersion: 1,
            OccurredAtUtc: _utcNow(),
            Outcome: outcome.ToString(),
            ConfigPath: _configPath,
            RestoredFromPath: restoredFromPath,
            InvalidConfigurationPath: invalidPath,
            FailureReason: invalidReason ?? "Configuration could not be deserialized.");
        AtomicFileWriter.Write(
            receiptPath,
            JsonSerializer.Serialize(receipt, DesktopJsonOptions.PrettyPrint),
            ct);

        return new DesktopConfigurationRecoveryResult(
            outcome,
            _configPath,
            restoredFromPath,
            invalidPath,
            receiptPath,
            invalidReason);
    }

    public async Task PersistValidConfigurationAsync(string json, CancellationToken ct = default)
    {
        ValidateConfigurationJson(json);
        EnsureParentDirectory();
        await AtomicFileWriter.WriteAsync(_configPath, json, ct).ConfigureAwait(false);
        await AtomicFileWriter.WriteAsync(LastKnownGoodPath, json, ct).ConfigureAwait(false);
    }

    private void PersistValidConfiguration(string json, CancellationToken ct)
    {
        ValidateConfigurationJson(json);
        AtomicFileWriter.Write(_configPath, json, ct);
        AtomicFileWriter.Write(LastKnownGoodPath, json, ct);
    }

    private void EnsureParentDirectory()
    {
        var directory = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }

    private static string SerializeDefaultConfiguration()
        => JsonSerializer.Serialize(AppConfigDefaults.CreateDefaultAppConfig(), ConfigurationJsonOptions);

    private static void ValidateConfigurationJson(string json)
    {
        if (!TryDeserializeConfiguration(json, out var error))
            throw new JsonException($"Desktop configuration is invalid: {error}");
    }

    private static bool TryReadValidConfiguration(
        string path,
        out string? json,
        out string? error)
    {
        json = null;
        error = null;
        if (!File.Exists(path))
        {
            error = "File does not exist.";
            return false;
        }

        try
        {
            json = File.ReadAllText(path);
            if (TryDeserializeConfiguration(json, out error))
                return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
        }

        return false;
    }

    private static bool TryDeserializeConfiguration(string json, out string? error)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Configuration file is empty.";
            return false;
        }

        try
        {
            var config = JsonSerializer.Deserialize<AppConfigDto>(json, ConfigurationJsonOptions);
            if (config is null)
            {
                error = "Configuration document did not contain an object.";
                return false;
            }

            error = null;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            error = ex.Message;
            return false;
        }
    }

    private sealed record ConfigurationRecoveryReceipt(
        int SchemaVersion,
        DateTimeOffset OccurredAtUtc,
        string Outcome,
        string ConfigPath,
        string? RestoredFromPath,
        string InvalidConfigurationPath,
        string FailureReason);
}
