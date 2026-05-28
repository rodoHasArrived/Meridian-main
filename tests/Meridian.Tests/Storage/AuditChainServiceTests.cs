using Meridian.Storage.Services;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class AuditChainServiceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly AuditChainService _service;

    public AuditChainServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"mdc_audit_chain_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
        _service = new AuditChainService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task VerifyChainAsync_DetectsDataFileTampering()
    {
        var dataPath = Path.Combine(_testRoot, "sample.jsonl");
        await File.WriteAllTextAsync(dataPath, "before tamper");

        await _service.AppendEntryAsync(dataPath);

        await File.WriteAllTextAsync(dataPath, "after tamper");

        var chainLogPath = Path.Combine(_testRoot, "chain.log");
        var result = await _service.VerifyChainAsync(chainLogPath);

        Assert.False(result.IsValid);
        Assert.Equal(dataPath, result.FirstTamperPath);
    }

    [Fact]
    public async Task VerifyChainAsync_MissingChainLog_IsInvalid()
    {
        var chainLogPath = Path.Combine(_testRoot, "chain.log");

        var result = await _service.VerifyChainAsync(chainLogPath);

        Assert.False(result.IsValid);
        Assert.Equal(chainLogPath, result.FirstTamperPath);
    }

    [Fact]
    public async Task VerifyChainAsync_ValidChain_IsValid()
    {
        var dataPath = Path.Combine(_testRoot, "valid.jsonl");
        await File.WriteAllTextAsync(dataPath, "line-1");

        await _service.AppendEntryAsync(dataPath);

        var chainLogPath = Path.Combine(_testRoot, "chain.log");
        var result = await _service.VerifyChainAsync(chainLogPath);

        Assert.True(result.IsValid);
        Assert.Equal(1, result.EntriesChecked);
    }
}
