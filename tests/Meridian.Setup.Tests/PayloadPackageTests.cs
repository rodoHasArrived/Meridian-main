using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Meridian.Setup.Tests;

/// <summary>
/// Pins the behaviours that make the appended-payload format safe to read off a downloaded file:
/// it finds the payload behind an Authenticode signature block, it is not fooled by a decoy magic
/// in that block, it refuses a modified archive, and it selects the right runtime subtree.
/// </summary>
public sealed class PayloadPackageTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"meridian-payload-tests-{Guid.NewGuid():N}");

    public PayloadPackageTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Open_ReadsOnlyTheRequestedRuntimeSubtree()
    {
        var path = WritePackage(new Dictionary<string, string>
        {
            ["win-x64/Meridian.exe"] = "x64-app",
            ["win-x64/host/appsettings.json"] = "x64-config",
            ["win-arm64/Meridian.exe"] = "arm64-app"
        });

        using var package = PayloadPackage.Open(path);

        package.GetRuntimes().Should().Equal("win-arm64", "win-x64");
        var payload = package.GetRuntimePayload("win-x64");
        payload.Select(source => source.RelativePath)
            .Should().BeEquivalentTo("Meridian.exe", "host/appsettings.json");
        ReadAll(payload.Single(source => source.RelativePath == "Meridian.exe")).Should().Be("x64-app");
        ReadAll(payload.Single(source => source.RelativePath == "host/appsettings.json"))
            .Should().Be("x64-config");
    }

    [Fact]
    public void GetRuntimePayload_ReturnsNothingForAnAbsentRuntime()
    {
        var path = WritePackage(new Dictionary<string, string> { ["win-x64/Meridian.exe"] = "x64-app" });

        using var package = PayloadPackage.Open(path);

        package.GetRuntimePayload("win-arm64").Should().BeEmpty();
    }

    [Fact]
    public void GetRuntimePayload_SkipsDirectoryEntries()
    {
        var path = WritePackage(
            new Dictionary<string, string> { ["win-x64/host/Meridian.exe"] = "x64-app" },
            directoryEntries: ["win-x64/", "win-x64/host/"]);

        using var package = PayloadPackage.Open(path);

        package.GetRuntimePayload("win-x64").Select(source => source.RelativePath)
            .Should().Equal("host/Meridian.exe");
    }

    [Fact]
    public void Open_FindsThePayloadBehindASignatureBlock()
    {
        // signtool appends the certificate table after the trailer, so the trailer is not at EOF
        // on any artifact a user actually receives.
        var path = WritePackage(
            new Dictionary<string, string> { ["win-x64/Meridian.exe"] = "x64-app" },
            trailingBytes: RandomNumberGenerator.GetBytes(8 * 1024));

        using var package = PayloadPackage.Open(path);

        package.GetRuntimePayload("win-x64").Should().ContainSingle();
    }

    [Fact]
    public void Open_IgnoresADecoyTrailerAfterTheRealOne()
    {
        // The scan walks backwards, so a well-formed but wrongly-positioned trailer in the
        // signature block is reached first. Only `offset + length == its own position` separates
        // the real trailer from it.
        var decoy = Trailer(offset: 0, length: 16, sha256: new string('a', 64));
        var path = WritePackage(
            new Dictionary<string, string> { ["win-x64/Meridian.exe"] = "x64-app" },
            trailingBytes: [.. Encoding.ASCII.GetBytes(decoy), .. RandomNumberGenerator.GetBytes(512)]);

        using var package = PayloadPackage.Open(path);

        package.GetRuntimePayload("win-x64").Should().ContainSingle();
    }

    [Fact]
    public void Open_RejectsAModifiedArchive()
    {
        var path = WritePackage(new Dictionary<string, string> { ["win-x64/Meridian.exe"] = "x64-app" });
        var bytes = File.ReadAllBytes(path);
        // Flip a byte inside the archive, leaving the trailer's offset and length intact.
        var target = ImagePrefix.Length + 32;
        bytes[target] ^= 0xFF;
        File.WriteAllBytes(path, bytes);

        var open = () => PayloadPackage.Open(path);

        open.Should().Throw<InvalidDataException>().WithMessage("*SHA-256*");
    }

    [Fact]
    public void Open_RejectsAnExecutableWithNoPayload()
    {
        var path = Path.Combine(_root, "Meridian-Setup.exe");
        File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(64 * 1024));

        var open = () => PayloadPackage.Open(path);

        open.Should().Throw<InvalidDataException>().WithMessage("*does not contain a readable product payload*");
    }

    [Fact]
    public void Open_RejectsAnExecutableTruncatedIntoThePayload()
    {
        var path = WritePackage(new Dictionary<string, string> { ["win-x64/Meridian.exe"] = "x64-app" });
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..(bytes.Length - PayloadPackage.TrailerLength - 8)]);

        var open = () => PayloadPackage.Open(path);

        open.Should().Throw<InvalidDataException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static readonly byte[] ImagePrefix = Encoding.ASCII.GetBytes("MZ fake portable executable image ");

    /// <summary>
    /// Writes a file shaped like a packaged installer. This mirrors the writer in
    /// <c>build/scripts/install/build-consumer-setup.ps1</c>; the release build proves the two
    /// agree by running the packaged executable with <c>--verify-payload</c>.
    /// </summary>
    private string WritePackage(
        IReadOnlyDictionary<string, string> entries,
        IReadOnlyList<string>? directoryEntries = null,
        byte[]? trailingBytes = null)
    {
        using var archiveBuffer = new MemoryStream();
        using (var archive = new ZipArchive(archiveBuffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in directoryEntries ?? Array.Empty<string>())
            {
                archive.CreateEntry(name);
            }

            foreach (var (name, content) in entries)
            {
                using var entry = archive.CreateEntry(name).Open();
                entry.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        var archiveBytes = archiveBuffer.ToArray();
        var trailer = Trailer(
            ImagePrefix.Length,
            archiveBytes.Length,
            Convert.ToHexString(SHA256.HashData(archiveBytes)).ToLowerInvariant());

        var path = Path.Combine(_root, "Meridian-Setup.exe");
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
        file.Write(ImagePrefix);
        file.Write(archiveBytes);
        file.Write(Encoding.ASCII.GetBytes(trailer));
        file.Write(trailingBytes ?? Array.Empty<byte>());
        return path;
    }

    private static string Trailer(long offset, long length, string sha256)
    {
        var trailer = string.Create(
            CultureInfo.InvariantCulture,
            $"{PayloadPackage.TrailerMagic}offset={offset:D20}\nlength={length:D20}\nsha256={sha256}\n");
        trailer.Length.Should().Be(PayloadPackage.TrailerLength);
        return trailer;
    }

    private static string ReadAll(PayloadSource source)
    {
        using var stream = source.OpenRead();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
