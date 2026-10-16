using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.Integrity;

namespace Meridian.Tests.Contracts.Integrity;

public sealed class Sha256DigestTests
{
    private const string LowercaseDigest =
        "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    private static string UppercaseDigest => LowercaseDigest.ToUpperInvariant();

    [Fact]
    public void Compute_EmitsCanonicalLowercaseHex()
    {
        var digest = Sha256Digest.ComputeUtf8("test");

        digest.Should().Be(LowercaseDigest);
        Sha256Digest.IsCanonical(digest).Should().BeTrue();
    }

    [Fact]
    public void Compute_Stream_EmitsCanonicalLowercaseHexFromCurrentPosition()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("skiptest"));
        stream.Position = 4;

        var digest = Sha256Digest.Compute(stream);

        digest.Should().Be(LowercaseDigest, "only the bytes after the current position are hashed");
        Sha256Digest.IsCanonical(digest).Should().BeTrue();
    }

    [Fact]
    public async Task ComputeAsync_EmitsCanonicalLowercaseHexMatchingCompute()
    {
        var payload = Encoding.UTF8.GetBytes("test");
        using var stream = new MemoryStream(payload);

        var digest = await Sha256Digest.ComputeAsync(stream);

        digest.Should().Be(LowercaseDigest);
        Sha256Digest.IsCanonical(digest).Should().BeTrue();
    }

    [Fact]
    public async Task ComputeAsync_HashesFromCurrentPosition()
    {
        var payload = Encoding.UTF8.GetBytes("skiptest");
        using var stream = new MemoryStream(payload);
        stream.Position = 4;

        var digest = await Sha256Digest.ComputeAsync(stream);

        digest.Should().Be(LowercaseDigest, "only the bytes after the current position are hashed");
    }

    [Fact]
    public void Compute_MatchesFrameworkHash()
    {
        var payload = Encoding.UTF8.GetBytes("meridian");

        Sha256Digest.Compute(payload)
            .Should()
            .Be(Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant());
    }

    // The byte-form members exist so non-hex consumers (deterministic IDs, binary receipts) can
    // migrate onto the primitive without changing a single output byte — so byte-for-byte equality
    // with the framework call is the whole contract.
    [Fact]
    public void ComputeBytes_MatchesFrameworkHashExactly()
    {
        var payload = Encoding.UTF8.GetBytes("meridian");

        Sha256Digest.ComputeBytes(payload).Should().Equal(SHA256.HashData(payload));
        Sha256Digest.ComputeBytesUtf8("meridian").Should().Equal(SHA256.HashData(payload));
    }

    [Fact]
    public void ComputeBytes_SpanDestination_ResetsBetweenChangingInputsAndBlockBoundaries()
    {
        int[] lengths = [0, 1, 55, 56, 63, 64, 65, 119, 120, 127, 128, 129, 256, 4096];
        Span<byte> digest = stackalloc byte[32];

        for (var repetition = 0; repetition < 4; repetition++)
        {
            foreach (var length in lengths)
            {
                var payload = new byte[length];
                for (var i = 0; i < payload.Length; i++)
                {
                    payload[i] = (byte)(i * 31 + repetition * 17 + length);
                }

                var expected = SHA256.HashData(payload);
                Sha256Digest.ComputeBytes(payload, digest);

                Assert.True(digest.SequenceEqual(expected), $"SHA-256 mismatch at length {length}, repetition {repetition}.");
            }
        }
    }

    [Fact]
    public void ComputeBytes_SpanDestination_IsolatesConcurrentDedicatedThreads()
    {
        const int workerCount = 4;
        int[] lengths = [0, 1, 55, 56, 63, 64, 65, 119, 120, 127, 128, 129, 256, 4096];
        using var start = new ManualResetEventSlim();
        using var ready = new CountdownEvent(workerCount);
        var failures = new Exception?[workerCount];
        var workers = new Thread[workerCount];

        for (var worker = 0; worker < workerCount; worker++)
        {
            var workerIndex = worker;
            workers[worker] = new Thread(() =>
            {
                try
                {
                    ready.Signal();
                    start.Wait();
                    Span<byte> digest = stackalloc byte[32];
                    for (var iteration = 0; iteration < 512; iteration++)
                    {
                        var payload = new byte[lengths[(iteration + workerIndex) % lengths.Length]];
                        for (var i = 0; i < payload.Length; i++)
                        {
                            payload[i] = (byte)(workerIndex * 53 + iteration * 17 + i);
                        }

                        var expected = SHA256.HashData(payload);
                        Sha256Digest.ComputeBytes(payload, digest);
                        Assert.True(digest.SequenceEqual(expected), $"SHA-256 mismatch on worker {workerIndex}, iteration {iteration}.");
                    }
                }
                catch (Exception exception)
                {
                    failures[workerIndex] = exception;
                }
            })
            { IsBackground = true };
            workers[worker].Start();
        }

        var allReady = ready.Wait(TimeSpan.FromSeconds(30));
        start.Set();
        foreach (var worker in workers)
        {
            Assert.True(worker.Join(TimeSpan.FromSeconds(30)), "A SHA-256 worker did not finish.");
        }

        Assert.True(allReady, "The dedicated SHA-256 workers did not become ready.");
        Assert.All(failures, failure => Assert.Null(failure));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(31)]
    public void ComputeBytes_SpanDestination_RejectsShortOutputWithoutContaminatingNextHash(int length)
    {
        var digest = new byte[32];
        Sha256Digest.ComputeBytes("previous input"u8, digest);
        var destination = Enumerable.Repeat((byte)0xA5, length).ToArray();

        var exception = Assert.Throws<ArgumentException>(() =>
            Sha256Digest.ComputeBytes("rejected input"u8, destination));

        Assert.Equal("destination", exception.ParamName);
        Assert.All(destination, value => Assert.Equal((byte)0xA5, value));
        Sha256Digest.ComputeBytes("next input"u8, digest);
        Assert.Equal(SHA256.HashData("next input"u8), digest);
    }

    [Fact]
    public void ComputeBytes_SpanDestination_PreservesBytesAfterDigest()
    {
        var destination = Enumerable.Repeat((byte)0xA5, 48).ToArray();

        Sha256Digest.ComputeBytes("test"u8, destination);

        Assert.True(destination.AsSpan(0, 32).SequenceEqual(Convert.FromHexString(LowercaseDigest)));
        Assert.All(destination.Skip(32), value => Assert.Equal((byte)0xA5, value));
    }

    [Fact]
    public void ComputeBytes_SpanDestination_WarmedCallsAllocateNoManagedBytes()
    {
        var payload = new byte[4096];
        Span<byte> digest = stackalloc byte[32];
        for (var iteration = 0; iteration < 256; iteration++)
        {
            Sha256Digest.ComputeBytes(payload, digest);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < 1024; iteration++)
        {
            payload[0] = (byte)iteration;
            Sha256Digest.ComputeBytes(payload.AsSpan(0, iteration + 1), digest);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0L, allocated);
        Assert.True(digest.SequenceEqual(SHA256.HashData(payload.AsSpan(0, 1024))));
    }

    [Fact]
    public void ComputeBytes_IsTheDecodedFormOfCompute()
    {
        var payload = Encoding.UTF8.GetBytes("meridian");

        Convert.ToHexString(Sha256Digest.ComputeBytes(payload))
            .ToLowerInvariant()
            .Should()
            .Be(Sha256Digest.Compute(payload));
    }

    [Fact]
    public async Task ComputeBytesAsync_HashesFromCurrentPosition()
    {
        var payload = Encoding.UTF8.GetBytes("skiptest");
        using var stream = new MemoryStream(payload);
        stream.Position = 4;

        var digest = await Sha256Digest.ComputeBytesAsync(stream);

        digest.Should().Equal(
            Sha256Digest.ComputeBytesUtf8("test"),
            "only the bytes after the current position are hashed");
    }

    [Fact]
    public void ComputeBytesUtf8_RejectsNull()
    {
        var act = () => Sha256Digest.ComputeBytesUtf8(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("zz86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")] // non-hex
    [InlineData("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a0")] // 63 chars
    [InlineData("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a088")] // 65 chars
    public void MalformedValues_AreNeitherCanonicalNorWellFormed(string? value)
    {
        Sha256Digest.IsCanonical(value).Should().BeFalse();
        Sha256Digest.IsWellFormed(value).Should().BeFalse();
    }

    [Fact]
    public void UppercaseDigest_IsWellFormedButNotCanonical()
    {
        Sha256Digest.IsWellFormed(UppercaseDigest).Should().BeTrue();
        Sha256Digest.IsCanonical(UppercaseDigest).Should().BeFalse();
    }

    [Fact]
    public void MixedCaseDigest_IsWellFormedButNotCanonical()
    {
        var mixed = string.Concat(LowercaseDigest[..32], UppercaseDigest[32..]);

        Sha256Digest.IsWellFormed(mixed).Should().BeTrue();
        Sha256Digest.IsCanonical(mixed).Should().BeFalse();
    }

    [Fact]
    public void Normalize_RepairsNonCanonicalCasing()
    {
        Sha256Digest.Normalize(UppercaseDigest).Should().Be(LowercaseDigest);
        Sha256Digest.Normalize(LowercaseDigest).Should().Be(LowercaseDigest);
    }

    [Fact]
    public void Normalize_ReturnsNullForMalformedInput() =>
        Sha256Digest.Normalize("not-a-digest").Should().BeNull();

    // The regression this whole primitive exists for: before consolidation, a retained uppercase
    // digest verified under the permissive reporting-side check but was reported as a hash mismatch
    // by the lowercase-only store-side check — a phantom tamper alert on intact bytes.
    [Fact]
    public void Compare_TreatsCasingDifferenceAsMatch_NotTamper()
    {
        Sha256Digest.Compare(UppercaseDigest, LowercaseDigest)
            .Should()
            .Be(Sha256DigestComparison.Match);

        Sha256Digest.FixedEquals(UppercaseDigest, LowercaseDigest).Should().BeTrue();
        Sha256Digest.FixedEquals(LowercaseDigest, UppercaseDigest).Should().BeTrue();
    }

    [Fact]
    public void Compare_ReportsGenuineMismatch()
    {
        var other = Sha256Digest.ComputeUtf8("different");

        Sha256Digest.Compare(LowercaseDigest, other).Should().Be(Sha256DigestComparison.Mismatch);
        Sha256Digest.FixedEquals(LowercaseDigest, other).Should().BeFalse();
    }

    [Theory]
    [InlineData("bad", LowercaseDigest, Sha256DigestComparison.MalformedLeft)]
    [InlineData(LowercaseDigest, "bad", Sha256DigestComparison.MalformedRight)]
    [InlineData("bad", "worse", Sha256DigestComparison.MalformedBoth)]
    [InlineData(null, LowercaseDigest, Sha256DigestComparison.MalformedLeft)]
    [InlineData(LowercaseDigest, null, Sha256DigestComparison.MalformedRight)]
    public void Compare_SeparatesMalformedInputFromMismatch(
        string? left,
        string? right,
        Sha256DigestComparison expected)
    {
        Sha256Digest.Compare(left, right).Should().Be(expected);

        // FixedEquals still collapses to false, but callers can now tell the two apart.
        Sha256Digest.FixedEquals(left, right).Should().BeFalse();
    }

    [Fact]
    public void Compare_IsReflexiveForCanonicalDigest() =>
        Sha256Digest.Compare(LowercaseDigest, LowercaseDigest)
            .Should()
            .Be(Sha256DigestComparison.Match);

    [Fact]
    public void ComputeUtf8_RejectsNull()
    {
        var act = () => Sha256Digest.ComputeUtf8(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
