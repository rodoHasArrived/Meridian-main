using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Meridian.Storage.Archival;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class AtomicFileWriterTests : IDisposable
{
    private readonly string _testRoot;

    public AtomicFileWriterTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"mdc_atomic_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(_testRoot))
                    Directory.Delete(_testRoot, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4) { Thread.Sleep(10); }
            catch (UnauthorizedAccessException) when (attempt < 4) { Thread.Sleep(10); }
        }
    }

    [Fact]
    public async Task WriteAsync_String_CreatesFile()
    {
        var path = Path.Combine(_testRoot, "test.txt");

        await AtomicFileWriter.WriteAsync(path, "hello world");

        File.Exists(path).Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be("hello world");
    }

    [Fact]
    public async Task WriteAsync_String_OverwritesExistingFile()
    {
        var path = Path.Combine(_testRoot, "overwrite.txt");
        await File.WriteAllTextAsync(path, "original");

        await AtomicFileWriter.WriteAsync(path, "updated");

        (await File.ReadAllTextAsync(path)).Should().Be("updated");
    }

    [Fact]
    public async Task WriteAsync_String_CreatesDirectoryIfNeeded()
    {
        var path = Path.Combine(_testRoot, "sub", "dir", "test.txt");

        await AtomicFileWriter.WriteAsync(path, "nested");

        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task WriteAsync_String_DoesNotLeaveTemporaryFiles()
    {
        var path = Path.Combine(_testRoot, "clean.txt");

        await AtomicFileWriter.WriteAsync(path, "data");

        var tmpFiles = Directory.GetFiles(_testRoot, "*.tmp");
        tmpFiles.Should().BeEmpty("atomic write should clean up temp files");
    }

    [Fact]
    public async Task WriteAsync_Bytes_WritesCorrectContent()
    {
        var path = Path.Combine(_testRoot, "binary.bin");
        var content = new byte[] { 0x01, 0x02, 0x03, 0xFF, 0xFE };

        await AtomicFileWriter.WriteAsync(path, content);

        var readBack = await File.ReadAllBytesAsync(path);
        readBack.Should().Equal(content);
    }

    [Fact]
    public async Task WriteAsync_StreamWriter_ExecutesWriteAction()
    {
        var path = Path.Combine(_testRoot, "stream.txt");

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
        var path = Path.Combine(_testRoot, "append.txt");
        await File.WriteAllLinesAsync(path, ["first", "second"]);

        await AtomicFileWriter.AppendLinesAsync(path, ["third", "fourth"]);

        var lines = await File.ReadAllLinesAsync(path);
        lines.Should().Equal("first", "second", "third", "fourth");
    }

    [Fact]
    public async Task WriteWithChecksumAsync_CreatesChecksumSidecar()
    {
        var path = Path.Combine(_testRoot, "checksummed.bin");
        var content = Encoding.UTF8.GetBytes("test content for checksum");

        var checksum = await AtomicFileWriter.WriteWithChecksumAsync(path, content);

        checksum.Should().NotBeNullOrEmpty();
        File.Exists(path + ".sha256").Should().BeTrue("should create checksum sidecar file");
    }

    [Fact]
    public async Task WriteWithChecksumAsync_ChecksumMatchesSHA256()
    {
        var path = Path.Combine(_testRoot, "verify_checksum.bin");
        var content = Encoding.UTF8.GetBytes("verify me");

        var expectedChecksum = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var actualChecksum = await AtomicFileWriter.WriteWithChecksumAsync(path, content);

        actualChecksum.Should().Be(expectedChecksum);
    }

    [Fact]
    public async Task VerifyChecksumAsync_ReturnsTrueForValidFile()
    {
        var path = Path.Combine(_testRoot, "valid.bin");
        var content = Encoding.UTF8.GetBytes("valid data");

        await AtomicFileWriter.WriteWithChecksumAsync(path, content);

        var result = await AtomicFileWriter.VerifyChecksumAsync(path);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyChecksumAsync_ReturnsFalseForTamperedFile()
    {
        var path = Path.Combine(_testRoot, "tampered.bin");
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
        var path = Path.Combine(_testRoot, "no_sidecar.bin");
        await File.WriteAllTextAsync(path, "some data");

        var result = await AtomicFileWriter.VerifyChecksumAsync(path);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ReplaceAsync_ReplacesFileContent()
    {
        var path = Path.Combine(_testRoot, "replace.txt");
        await File.WriteAllTextAsync(path, "original");

        await AtomicFileWriter.ReplaceAsync(path, "replaced", keepBackup: false);

        (await File.ReadAllTextAsync(path)).Should().Be("replaced");
    }

    [Fact]
    public async Task ReplaceAsync_KeepsBackup_WhenRequested()
    {
        var path = Path.Combine(_testRoot, "backup_test.txt");
        await File.WriteAllTextAsync(path, "original content");

        await AtomicFileWriter.ReplaceAsync(path, "new content", keepBackup: true);

        File.Exists(path + ".bak").Should().BeTrue();
        (await File.ReadAllTextAsync(path + ".bak")).Should().Be("original content");
    }

    [Fact]
    public async Task ReplaceAsync_RemovesBackup_WhenNotRequested()
    {
        var path = Path.Combine(_testRoot, "no_backup.txt");
        await File.WriteAllTextAsync(path, "original");

        await AtomicFileWriter.ReplaceAsync(path, "updated", keepBackup: false);

        File.Exists(path + ".bak").Should().BeFalse();
    }

    [Fact]
    public async Task ReplaceAsync_WorksForNewFile()
    {
        var path = Path.Combine(_testRoot, "new_file.txt");

        await AtomicFileWriter.ReplaceAsync(path, "brand new");

        File.Exists(path).Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be("brand new");
    }

    [Fact]
    public async Task WriteAsync_String_UsesUTF8Encoding()
    {
        var path = Path.Combine(_testRoot, "utf8.txt");
        var content = "Hello 世界 🌍";

        await AtomicFileWriter.WriteAsync(path, content);

        var readBack = await File.ReadAllTextAsync(path, Encoding.UTF8);
        readBack.Should().Be(content);
    }

    [Fact]
    public async Task WriteAsync_Bytes_HandlesEmptyContent()
    {
        var path = Path.Combine(_testRoot, "empty.bin");

        await AtomicFileWriter.WriteAsync(path, Array.Empty<byte>());

        File.Exists(path).Should().BeTrue();
        (await File.ReadAllBytesAsync(path)).Should().BeEmpty();
    }
}
