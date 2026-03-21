using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Meridian.Storage.Services;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class StorageChecksumServiceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly StorageChecksumService _service;

    public StorageChecksumServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"mdc_checksum_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
        _service = new StorageChecksumService();
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
    public void ComputeChecksum_Bytes_ReturnsLowercaseHexString()
    {
        var data = Encoding.UTF8.GetBytes("test data");

        var checksum = _service.ComputeChecksum(data);

        checksum.Should().MatchRegex("^[0-9a-f]{64}$", "SHA256 produces 64 hex chars");
    }

    [Fact]
    public void ComputeChecksum_Bytes_IsDeterministic()
    {
        var data = Encoding.UTF8.GetBytes("deterministic");

        var first = _service.ComputeChecksum(data);
        var second = _service.ComputeChecksum(data);

        first.Should().Be(second);
    }

    [Fact]
    public void ComputeChecksum_Bytes_DifferentDataProducesDifferentHash()
    {
        var hash1 = _service.ComputeChecksum(Encoding.UTF8.GetBytes("data1"));
        var hash2 = _service.ComputeChecksum(Encoding.UTF8.GetBytes("data2"));

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeChecksum_Bytes_MatchesSHA256()
    {
        var data = Encoding.UTF8.GetBytes("verify against .NET SHA256");
        var expected = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

        var actual = _service.ComputeChecksum(data);

        actual.Should().Be(expected);
    }

    [Fact]
    public void ComputeChecksum_String_ReturnsValidHash()
    {
        var checksum = _service.ComputeChecksum("hello world");

        checksum.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void ComputeChecksum_String_MatchesBytesVersion()
    {
        var text = "consistency check";
        var fromString = _service.ComputeChecksum(text);
        var fromBytes = _service.ComputeChecksum(Encoding.UTF8.GetBytes(text));

        fromString.Should().Be(fromBytes);
    }

    [Fact]
    public void ComputeChecksum_NullBytes_ThrowsArgumentNullException()
    {
        var act = () => _service.ComputeChecksum((byte[])null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ComputeChecksum_NullString_ThrowsArgumentNullException()
    {
        var act = () => _service.ComputeChecksum((string)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ComputeFileChecksumAsync_ReturnsCorrectHash()
    {
        var path = Path.Combine(_testRoot, "test.txt");
        var content = "file content for hashing";
        await File.WriteAllTextAsync(path, content);

        var expected = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

        // NOTE: File.WriteAllTextAsync uses UTF-8 with BOM by default on some platforms,
        // so we compute expected from the actual file bytes
        var fileBytes = await File.ReadAllBytesAsync(path);
        var expectedFromFile = Convert.ToHexString(SHA256.HashData(fileBytes)).ToLowerInvariant();

        var actual = await _service.ComputeFileChecksumAsync(path);

        actual.Should().Be(expectedFromFile);
    }

    [Fact]
    public async Task ComputeFileChecksumAsync_MissingFile_ThrowsFileNotFoundException()
    {
        var path = Path.Combine(_testRoot, "nonexistent.txt");

        var act = () => _service.ComputeFileChecksumAsync(path);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task ComputeFileChecksumAsync_EmptyPath_ThrowsArgumentException()
    {
        var act = () => _service.ComputeFileChecksumAsync("");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ComputeChecksumAsync_Stream_ReturnsCorrectHash()
    {
        var data = Encoding.UTF8.GetBytes("stream data");
        var expected = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

        using var stream = new MemoryStream(data);
        var actual = await _service.ComputeChecksumAsync(stream);

        actual.Should().Be(expected);
    }

    [Fact]
    public async Task ComputeChecksumAsync_NullStream_ThrowsArgumentNullException()
    {
        var act = () => _service.ComputeChecksumAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task VerifyFileChecksumAsync_CorrectChecksum_ReturnsTrue()
    {
        var path = Path.Combine(_testRoot, "verify.txt");
        await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("verify content"));
        var checksum = await _service.ComputeFileChecksumAsync(path);

        var result = await _service.VerifyFileChecksumAsync(path, checksum);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyFileChecksumAsync_WrongChecksum_ReturnsFalse()
    {
        var path = Path.Combine(_testRoot, "wrong.txt");
        await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("data"));

        var result = await _service.VerifyFileChecksumAsync(path, "0000000000000000000000000000000000000000000000000000000000000000");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyFileChecksumAsync_IsCaseInsensitive()
    {
        var path = Path.Combine(_testRoot, "case.txt");
        await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("case check"));
        var checksum = await _service.ComputeFileChecksumAsync(path);

        var result = await _service.VerifyFileChecksumAsync(path, checksum.ToUpperInvariant());

        result.Should().BeTrue("checksum comparison should be case-insensitive");
    }

    [Fact]
    public async Task ComputeFileChecksumsAsync_ProcessesMultipleFiles()
    {
        var files = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            var path = Path.Combine(_testRoot, $"file{i}.txt");
            await File.WriteAllTextAsync(path, $"content {i}");
            files.Add(path);
        }

        var checksums = await _service.ComputeFileChecksumsAsync(files);

        checksums.Should().HaveCount(3);
        checksums.Values.Should().OnlyContain(c => c.Length == 64, "all should be SHA256 hashes");
        checksums.Values.Distinct().Should().HaveCount(3, "different files should have different checksums");
    }

    [Fact]
    public async Task ComputeFileChecksumsAsync_EmptyList_ReturnsEmptyDictionary()
    {
        var checksums = await _service.ComputeFileChecksumsAsync(Array.Empty<string>());

        checksums.Should().BeEmpty();
    }

    [Fact]
    public void ComputeChecksum_EmptyBytes_ReturnsHashOfEmpty()
    {
        var expected = Convert.ToHexString(SHA256.HashData(Array.Empty<byte>())).ToLowerInvariant();

        var actual = _service.ComputeChecksum(Array.Empty<byte>());

        actual.Should().Be(expected);
    }
}
