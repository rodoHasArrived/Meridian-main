using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Tests.TestHelpers;
using Meridian.Ui.Shared.Evidence;
using Microsoft.Extensions.Logging;

namespace Meridian.Tests.Ui.Evidence;

public sealed class FileEvidenceArtifactStoreLoggingTests : IDisposable
{
    private const string Tenant = "private-tenant-marker", Scope = "private-company-marker";
    private const string Subject = "private-account-marker\r\nFORGED operational log entry";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "private-evidence-path-marker-" + Guid.NewGuid().ToString("N"));
    private readonly RecordingLogger<FileEvidenceArtifactStore> _logger = new();

    [Fact]
    public async Task IntakeRetainsExactSubjectAndSourceBytes_WhileLoggingOnlyItsGeneratedVaultIdentity()
    {
        var store = new FileEvidenceArtifactStore(_root, _logger);
        var intake = await RetainAsync(store);

        intake.SubjectKind.Should().Be("account");
        intake.SubjectId.Should().Be(Subject);
        var identity = await store.TryGetVaultIdentityAsync(intake.VaultIdentity.VaultId, Tenant, Scope);
        identity.Should().NotBeNull();
        identity!.SubjectId.Should().Be(Subject);
        (await File.ReadAllBytesAsync(Path.Combine(_root, intake.RelativePath))).Should().Equal("synthetic retained source"u8.ToArray());
        var entry = _logger.Entries.Should().ContainSingle().Which;
        entry.LogLevel.Should().Be(LogLevel.Information);
        entry.Message.Should().Be($"Retained evidence vault intake {identity.VaultId}.");
        entry.Message.Should().MatchRegex("^Retained evidence vault intake ev-[a-f0-9]{24}\\.$");
        AssertOperationalLogPrivacy();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnreadableRetainedManifestFailsClosed_WithoutLoggingScopedPathsOrRawExceptions(bool locked)
    {
        var store = new FileEvidenceArtifactStore(_root, _logger);
        var intake = await RetainAsync(store);
        var manifestPath = Path.Combine(_root, intake.VaultIdentity.ManifestPath.Replace('/', Path.DirectorySeparatorChar));
        var originalBytes = await File.ReadAllBytesAsync(manifestPath);
        FileStream? lease = null;
        try
        {
            if (locked)
                lease = new FileStream(manifestPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            else
                await File.WriteAllTextAsync(manifestPath, "{\"private-manifest-field-marker\": invalid-json");
            _logger.Entries.Clear();

            (await store.TryGetVaultIdentityAsync(intake.VaultIdentity.VaultId, Tenant, Scope)).Should().BeNull();

            var warning = _logger.Entries.Should().ContainSingle().Which;
            warning.LogLevel.Should().Be(LogLevel.Warning);
            warning.Message.Should().Be(locked
                ? "Evidence vault manifest could not be read (IOException)."
                : "Evidence vault manifest could not be deserialized (JsonException).");
            AssertOperationalLogPrivacy();
        }
        finally
        {
            lease?.Dispose();
            await File.WriteAllBytesAsync(manifestPath, originalBytes);
        }
        // The read failure never promotes stale locator data or rewrites retained authority.
        var restored = await store.TryGetVaultIdentityAsync(intake.VaultIdentity.VaultId, Tenant, Scope);
        restored.Should().NotBeNull();
        restored!.SubjectId.Should().Be(Subject);
        (await File.ReadAllBytesAsync(manifestPath)).Should().Equal(originalBytes);
    }

    [Fact]
    public async Task LockedPinnedArtifactVerificationFailsClosed_WithoutLoggingScopedPathsOrRawExceptions()
    {
        var store = new FileEvidenceArtifactStore(_root, _logger);
        var intake = await RetainAsync(store);
        var identity = intake.VaultIdentity;
        intake.Document.Should().NotBeNull();
        var document = intake.Document!;
        var artifact = identity.Artifacts.Single(row => row.Document?.DocumentId == document.DocumentId);
        var artifactPath = Path.Combine(_root, artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        (await store.VerifyDocumentContentAsync(identity.VaultId, document.DocumentId, artifact.ArtifactId,
            intake.ContentHashSha256, Tenant, Scope)).Should().BeTrue();
        using (var lease = new FileStream(artifactPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            _logger.Entries.Clear();

            (await store.VerifyDocumentContentAsync(identity.VaultId, document.DocumentId, artifact.ArtifactId,
                intake.ContentHashSha256, Tenant, Scope)).Should().BeFalse();

            var warning = _logger.Entries.Should().ContainSingle().Which;
            warning.LogLevel.Should().Be(LogLevel.Warning);
            warning.Message.Should().Be("Evidence vault artifact could not be read for verification (IOException).");
            AssertOperationalLogPrivacy();
        }
        (await store.VerifyDocumentContentAsync(identity.VaultId, document.DocumentId, artifact.ArtifactId,
            intake.ContentHashSha256, Tenant, Scope)).Should().BeTrue();
        (await File.ReadAllBytesAsync(artifactPath)).Should().Equal("synthetic retained source"u8.ToArray());
    }

    private static Task<EvidenceVaultIntakeResponseDto> RetainAsync(FileEvidenceArtifactStore store)
        => store.WriteIntakeArtifactAsync(new("account", Subject, "api", "private-document-marker.txt",
            ContentBase64: Convert.ToBase64String("synthetic retained source"u8.ToArray()))
        { TenantId = Tenant, Scope = Scope });

    private void AssertOperationalLogPrivacy()
    {
        foreach (var entry in _logger.Entries)
        {
            entry.Exception.Should().BeNull("raw exception messages and paths are not operational logging fields");
            foreach (var marker in new[] { Subject, Tenant, Scope, _root, "FORGED", "private-manifest-field-marker", "private-document-marker" })
                entry.Message.Should().NotContain(marker);
            entry.Message.Should().NotContain("\r").And.NotContain("\n");
        }
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
