using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Meridian.Storage.Archival;
using Meridian.Tests.Infrastructure;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class AtomicFileWriterTests : TempDirectoryTestBase
{
    [Theory]
    [InlineData("sync")]
    [InlineData("text")]
    [InlineData("bytes")]
    [InlineData("writer")]
    [InlineData("stream")]
    [InlineData("append")]
    [InlineData("checksum")]
    public async Task Write_WithOpenSnapshot_PublishesNewContentAndRetainsOldReader(string surface)
    {
        var path = Path.Combine(TestDataRoot, "snapshot.txt");
        await File.WriteAllTextAsync(path, "original");
        using var snapshot = new StreamReader(new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete));

        await PublishAsync(surface, path);

        (await snapshot.ReadToEndAsync()).Should().Be("original");
        (await File.ReadAllTextAsync(path)).Should().Be(
            surface == "append" ? "originalupdated" : "updated");
        Directory.GetFiles(TestDataRoot, "*.tmp").Should().BeEmpty();
        if (surface == "checksum")
        {
            (await AtomicFileWriter.VerifyChecksumAsync(path)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task WriteAsync_ReaderRefusesReplacement_PreservesOriginalAndCleansTemporaryFiles()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(TestDataRoot, "blocked.txt");
        await File.WriteAllTextAsync(path, "original");
        using var snapshot = new StreamReader(new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read));

        var failure = await Record.ExceptionAsync(() => AtomicFileWriter.WriteAsync(path, "updated"));

        (failure is IOException or UnauthorizedAccessException).Should().BeTrue(
            "a reader denying deletion must block publication; actual error: {0}", failure);
        (await snapshot.ReadToEndAsync()).Should().Be("original");
        (await File.ReadAllTextAsync(path)).Should().Be("original");
        Directory.GetFiles(TestDataRoot, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task WriteStreamAsync_FailedWrite_PreservesOriginalAndCleansTemporaryFiles()
    {
        var path = Path.Combine(TestDataRoot, "failed-write.txt");
        await File.WriteAllTextAsync(path, "original");
        var expected = new IOException("The write failed before publication.");

        var failure = await Record.ExceptionAsync(() => AtomicFileWriter.WriteStreamAsync(
            path, async stream =>
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes("incomplete"));
                throw expected;
            }));

        failure.Should().BeSameAs(expected);
        (await File.ReadAllTextAsync(path)).Should().Be("original");
        Directory.GetFiles(TestDataRoot, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task WriteAsync_ConcurrentPublications_PreserveTheOpenedGeneration()
    {
        var path = Path.Combine(TestDataRoot, "concurrent.txt");
        await File.WriteAllTextAsync(path, "original");
        using var snapshot = new StreamReader(new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete));
        var generations = Enumerable.Range(0, 16)
            .Select(index => $"generation-{index}:" + new string((char)('a' + index), 8192))
            .ToArray();

        await Task.WhenAll(generations.Select(content => AtomicFileWriter.WriteAsync(path, content)));

        (await snapshot.ReadToEndAsync()).Should().Be("original");
        generations.Should().Contain(await File.ReadAllTextAsync(path));
        Directory.GetFiles(TestDataRoot, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task WriteAsync_WithOpenWindowsSnapshot_PreservesProtectedAccessRules()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(TestDataRoot, "protected.txt");
        await File.WriteAllTextAsync(path, "original");
        var file = new FileInfo(path);
        var access = file.GetAccessControl();
        access.SetAccessRuleProtection(isProtected: true, preserveInheritance: true);
        file.SetAccessControl(access);
        var expected = file.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        using var snapshot = new StreamReader(new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete));

        await AtomicFileWriter.WriteAsync(path, "updated");

        (await snapshot.ReadToEndAsync()).Should().Be("original");
        (await File.ReadAllTextAsync(path)).Should().Be("updated");
        new FileInfo(path).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access)
            .Should().Be(expected);
        Directory.GetFiles(TestDataRoot, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task WriteAsync_ReadOnlyWindowsDestination_PreservesOriginal()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(TestDataRoot, "read-only.txt");
        await File.WriteAllTextAsync(path, "original");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            var failure = await Record.ExceptionAsync(() => AtomicFileWriter.WriteAsync(path, "updated"));

            failure.Should().BeOfType<UnauthorizedAccessException>();
            (await File.ReadAllTextAsync(path)).Should().Be("original");
            Directory.GetFiles(TestDataRoot, "*.tmp").Should().BeEmpty();
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    private static async Task PublishAsync(string surface, string path)
    {
        var bytes = Encoding.UTF8.GetBytes("updated");
        switch (surface)
        {
            case "sync":
                AtomicFileWriter.Write(path, "updated");
                break;
            case "text":
                await AtomicFileWriter.WriteAsync(path, "updated");
                break;
            case "bytes":
                await AtomicFileWriter.WriteAsync(path, bytes);
                break;
            case "writer":
                await AtomicFileWriter.WriteAsync(path, writer => writer.WriteAsync("updated"));
                break;
            case "stream":
                await AtomicFileWriter.WriteStreamAsync(path, stream => stream.WriteAsync(bytes).AsTask());
                break;
            case "append":
                await AtomicFileWriter.AppendAsync(path, stream => stream.WriteAsync(bytes).AsTask());
                break;
            case "checksum":
                await AtomicFileWriter.WriteWithChecksumAsync(path, bytes);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(surface), surface, "Unknown write surface.");
        }
    }

    [Fact]
    public async Task WriteAsync_String_CreatesFile()
    {
        var path = Path.Combine(TestDataRoot, "test.txt");

        await AtomicFileWriter.WriteAsync(path, "hello world");

        File.Exists(path).Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be("hello world");
    }

    [Fact]
    public async Task WriteAsync_String_OverwritesExistingFile()
    {
        var path = Path.Combine(TestDataRoot, "overwrite.txt");
        await File.WriteAllTextAsync(path, "original");

        await AtomicFileWriter.WriteAsync(path, "updated");

        (await File.ReadAllTextAsync(path)).Should().Be("updated");
    }

    [Fact]
    public async Task WriteAsync_String_PreservesExistingUnixFileMode()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(TestDataRoot, "permissions.txt");
        await File.WriteAllTextAsync(path, "original");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        await AtomicFileWriter.WriteAsync(path, "updated");

        var mode = File.GetUnixFileMode(path);
        mode.Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public async Task WriteAsync_String_CreatesDirectoryIfNeeded()
    {
        var path = Path.Combine(TestDataRoot, "sub", "dir", "test.txt");

        await AtomicFileWriter.WriteAsync(path, "nested");

        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task WriteAsync_String_DoesNotLeaveTemporaryFiles()
    {
        var path = Path.Combine(TestDataRoot, "clean.txt");

        await AtomicFileWriter.WriteAsync(path, "data");

        var tmpFiles = Directory.GetFiles(TestDataRoot, "*.tmp");
        tmpFiles.Should().BeEmpty("atomic write should clean up temp files");
    }

    [Fact]
    public async Task WriteAsync_Bytes_WritesCorrectContent()
    {
        var path = Path.Combine(TestDataRoot, "binary.bin");
        var content = new byte[] { 0x01, 0x02, 0x03, 0xFF, 0xFE };

        await AtomicFileWriter.WriteAsync(path, content);

        var readBack = await File.ReadAllBytesAsync(path);
        readBack.Should().Equal(content);
    }

    [Fact]
    public async Task WriteAsync_StreamWriter_ExecutesWriteAction()
    {
        var path = Path.Combine(TestDataRoot, "stream.txt");

        await AtomicFileWriter.WriteAsync(path, async writer =>
        {
            await writer.WriteLineAsync("line 1");
            await writer.WriteLineAsync("line 2");
            await writer.WriteLineAsync("line 3");
        });

        var lines = await File.ReadAllLinesAsync(path);
        lines.Should().HaveCount(3);
        lines[0].Should().Be("line 1");
    }

    [Fact]
    public async Task AppendLinesAsync_PreservesExistingContent_AndAddsNewLines()
    {
        var path = Path.Combine(TestDataRoot, "append.txt");
        await File.WriteAllLinesAsync(path, ["first", "second"]);

        await AtomicFileWriter.AppendLinesAsync(path, ["third", "fourth"]);

        var lines = await File.ReadAllLinesAsync(path);
        lines.Should().Equal("first", "second", "third", "fourth");
    }

    [Fact]
    public async Task WriteWithChecksumAsync_CreatesChecksumSidecar()
    {
        var path = Path.Combine(TestDataRoot, "checksummed.bin");
        var content = Encoding.UTF8.GetBytes("test content for checksum");

        var checksum = await AtomicFileWriter.WriteWithChecksumAsync(path, content);

        checksum.Should().NotBeNullOrEmpty();
        File.Exists(path + ".sha256").Should().BeTrue("should create checksum sidecar file");
    }

    [Fact]
    public async Task WriteWithChecksumAsync_ChecksumMatchesSHA256()
    {
        var path = Path.Combine(TestDataRoot, "verify_checksum.bin");
        var content = Encoding.UTF8.GetBytes("verify me");

        var expectedChecksum = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var actualChecksum = await AtomicFileWriter.WriteWithChecksumAsync(path, content);

        actualChecksum.Should().Be(expectedChecksum);
    }

    [Fact]
    public async Task VerifyChecksumAsync_ReturnsTrueForValidFile()
    {
        var path = Path.Combine(TestDataRoot, "valid.bin");
        var content = Encoding.UTF8.GetBytes("valid data");

        await AtomicFileWriter.WriteWithChecksumAsync(path, content);

        var result = await AtomicFileWriter.VerifyChecksumAsync(path);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyChecksumAsync_ReturnsFalseForTamperedFile()
    {
        var path = Path.Combine(TestDataRoot, "tampered.bin");
        var content = Encoding.UTF8.GetBytes("original data");

        await AtomicFileWriter.WriteWithChecksumAsync(path, content);

        // Tamper with the file
        await File.WriteAllTextAsync(path, "tampered data");

        var result = await AtomicFileWriter.VerifyChecksumAsync(path);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyChecksumAsync_ReturnsFalseWhenNoSidecarExists()
    {
        var path = Path.Combine(TestDataRoot, "no_sidecar.bin");
        await File.WriteAllTextAsync(path, "some data");

        var result = await AtomicFileWriter.VerifyChecksumAsync(path);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ReplaceAsync_ReplacesFileContent()
    {
        var path = Path.Combine(TestDataRoot, "replace.txt");
        await File.WriteAllTextAsync(path, "original");

        await AtomicFileWriter.ReplaceAsync(path, "replaced", keepBackup: false);

        (await File.ReadAllTextAsync(path)).Should().Be("replaced");
    }

    [Fact]
    public async Task ReplaceAsync_KeepsBackup_WhenRequested()
    {
        var path = Path.Combine(TestDataRoot, "backup_test.txt");
        await File.WriteAllTextAsync(path, "original content");

        await AtomicFileWriter.ReplaceAsync(path, "new content", keepBackup: true);

        File.Exists(path + ".bak").Should().BeTrue();
        (await File.ReadAllTextAsync(path + ".bak")).Should().Be("original content");
    }

    [Fact]
    public async Task ReplaceAsync_RemovesBackup_WhenNotRequested()
    {
        var path = Path.Combine(TestDataRoot, "no_backup.txt");
        await File.WriteAllTextAsync(path, "original");

        await AtomicFileWriter.ReplaceAsync(path, "updated", keepBackup: false);

        File.Exists(path + ".bak").Should().BeFalse();
    }

    [Fact]
    public async Task ReplaceAsync_WorksForNewFile()
    {
        var path = Path.Combine(TestDataRoot, "new_file.txt");

        await AtomicFileWriter.ReplaceAsync(path, "brand new");

        File.Exists(path).Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be("brand new");
    }

    [Fact]
    public async Task WriteAsync_String_UsesUTF8Encoding()
    {
        var path = Path.Combine(TestDataRoot, "utf8.txt");
        var content = "Hello 世界 🌍";

        await AtomicFileWriter.WriteAsync(path, content);

        var readBack = await File.ReadAllTextAsync(path, Encoding.UTF8);
        readBack.Should().Be(content);
    }

    [Fact]
    public async Task WriteAsync_Bytes_HandlesEmptyContent()
    {
        var path = Path.Combine(TestDataRoot, "empty.bin");

        await AtomicFileWriter.WriteAsync(path, Array.Empty<byte>());

        File.Exists(path).Should().BeTrue();
        (await File.ReadAllBytesAsync(path)).Should().BeEmpty();
    }

    [Fact]
    public async Task WriteAsync_Bytes_AppliesRequestedUnixCreateMode()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(TestDataRoot, "secret.bin");

        var content = new byte[] { 1, 2, 3 };

        await AtomicFileWriter.WriteAsync(
            path, content, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        File.GetUnixFileMode(path).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        (await File.ReadAllBytesAsync(path)).Should().Equal(content);
    }

    // Without an explicit mode the destination's permissions win, so replacing an over-permissive
    // secret would silently restore the mode that made it over-permissive. An explicit mode has to
    // override the destination rather than inherit from it.
    [Fact]
    public async Task WriteAsync_Bytes_ExplicitModeOverridesAnExistingWiderMode()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(TestDataRoot, "widened.bin");
        await File.WriteAllBytesAsync(path, new byte[] { 9 });
        File.SetUnixFileMode(
            path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        await AtomicFileWriter.WriteAsync(
            path, new byte[] { 1, 2, 3 }, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        File.GetUnixFileMode(path).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public async Task WriteAsync_Bytes_WithoutModeStillPreservesExistingPermissions()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(TestDataRoot, "inherited.bin");
        await File.WriteAllBytesAsync(path, new byte[] { 9 });
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        await AtomicFileWriter.WriteAsync(path, new byte[] { 1, 2, 3 });

        File.GetUnixFileMode(path).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public async Task WriteAsync_String_AppliesRequestedUnixCreateMode()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(TestDataRoot, "secret.json");

        await AtomicFileWriter.WriteAsync(
            path, "{\"token\":\"secret\"}", UnixFileMode.UserRead | UnixFileMode.UserWrite);

        File.GetUnixFileMode(path).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        (await File.ReadAllTextAsync(path)).Should().Be("{\"token\":\"secret\"}");
    }

    [Fact]
    public async Task WriteAsync_String_ExplicitModeOverridesAnExistingWiderMode()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(TestDataRoot, "widened.json");
        await File.WriteAllTextAsync(path, "old");
        File.SetUnixFileMode(
            path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        await AtomicFileWriter.WriteAsync(path, "new", UnixFileMode.UserRead | UnixFileMode.UserWrite);

        File.GetUnixFileMode(path).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    // The text overload writes through a byte path when a mode is requested; a BOM there would
    // corrupt readers that token-split the raw content, so encoding must not change with the mode.
    [Fact]
    public async Task WriteAsync_String_WithModeStillWritesUtf8WithoutABom()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(TestDataRoot, "nobom.txt");

        await AtomicFileWriter.WriteAsync(
            path, "Hello 世界", UnixFileMode.UserRead | UnixFileMode.UserWrite);

        // Byte-exact against a BOM-free encode: this pins both "no BOM" and the content in one
        // assertion, where a prefix check would only rule out the three bytes it names.
        var bytes = await File.ReadAllBytesAsync(path);
        bytes.Should().Equal(Encoding.UTF8.GetBytes("Hello 世界"));
    }
}
