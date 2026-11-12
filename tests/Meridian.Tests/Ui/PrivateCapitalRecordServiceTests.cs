using Meridian.Contracts.PrivateCapital;
using Meridian.PortfolioRecords.PrivateCapital;
using Meridian.Storage.FundAccounts;

namespace Meridian.Tests.Ui;

public sealed class PrivateCapitalRecordServiceTests
{
    private static readonly PrivateCapitalScope Scope = new("tenant-a", "company-a");
    private static readonly PrivateCapitalEvidence Evidence = new("ev-012345678901234567890123", "source-1", new('a', 64));

    [Fact]
    public async Task Capture_Restart_Reload_ReplayRetainsExactDecimalsAndSource()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meridian-capital-" + Guid.NewGuid());
        try
        {
            var authority = new Authority();
            var service = new PrivateCapitalRecordService(new FilePrivateCapitalRecordStore(directory), authority);
            var create = Create();
            var commitment = await service.CreateAsync(Scope, "operator", create);
            var input = Row(commitment.CommitmentId) with
            {
                CommitmentAmount = "10000000000000.000001",
                CalledAmount = "0",
                UnfundedAmount = null,
                CumulativeDistributions = "123.00000001",
                NavAmount = "-5.125"
            };
            var preview = await service.PreviewAsync(Scope, "operator", new([input], null, Evidence, "Quarter-end source review"));
            var command = new AcceptPrivateCapitalImport(preview.BatchId, preview.Fingerprint, Guid.NewGuid());
            var receipt = await service.AcceptAsync(Scope, "operator", command);
            var restarted = new PrivateCapitalRecordService(new FilePrivateCapitalRecordStore(directory), authority);
            Assert.Equal(commitment, await restarted.CreateAsync(Scope, "operator", create));
            var replayed = await restarted.AcceptAsync(Scope, "operator", command);
            Assert.Equal(receipt.CommandId, replayed.CommandId);
            Assert.Equal(receipt.AcceptedAt, replayed.AcceptedAt);
            Assert.Equal(receipt.SnapshotIds, replayed.SnapshotIds);
            var records = await restarted.ReadAsync(Scope);
            var snapshot = Assert.Single(records.Snapshots);
            Assert.Equal("10000000000000.000001", snapshot.CommitmentAmount);
            Assert.Equal("0", snapshot.CalledAmount);
            Assert.Null(snapshot.UnfundedAmount);
            Assert.Equal("-5.125", snapshot.NavAmount);
            Assert.Equal(Evidence, snapshot.Evidence);
            Assert.Equal("Quarter-end source review", snapshot.Rationale);
            var inspection = await restarted.InspectImportAsync(Scope, preview.BatchId);
            Assert.Equal(input, Assert.Single(inspection!.SourceInput.Rows!));
            Assert.Equal(receipt.CommandId, inspection.Receipt!.CommandId);
            Assert.Empty((await restarted.ReadAsync(new("tenant-b", "company-a"))).Commitments);
            await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.AcceptAsync(Scope, "operator", command with { Fingerprint = new('b', 64) }));
            await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.CreateAsync(Scope, "operator", create with { SourceReference = "changed" }));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task CorrectionIsAppendOnlyAndConcurrentStaleCorrectionIsRefused()
    {
        var service = NewService();
        var commitment = await service.CreateAsync(Scope, "operator", Create());
        var initial = await service.PreviewAsync(Scope, "operator", new([Row(commitment.CommitmentId)], null, Evidence, "Initial reviewed source"));
        var initialReceipt = await service.AcceptAsync(Scope, "operator", new(initial.BatchId, initial.Fingerprint, Guid.NewGuid()));
        var correction = Row(commitment.CommitmentId) with { CalledAmount = "125", SupersedesSnapshotId = initialReceipt.SnapshotIds.Single() };
        var first = await service.PreviewAsync(Scope, "operator", new([correction], null, Evidence, "Corrected source revision"));
        var competing = await service.PreviewAsync(Scope, "operator", new([correction with { CalledAmount = "150" }], null, Evidence, "Another source revision"));
        await service.AcceptAsync(Scope, "operator", new(first.BatchId, first.Fingerprint, Guid.NewGuid()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcceptAsync(Scope, "operator", new(competing.BatchId, competing.Fingerprint, Guid.NewGuid())));
        var history = (await service.ReadAsync(Scope)).Snapshots;
        Assert.Equal(2, history.Count);
        Assert.Equal("100", history.Single(item => item.SnapshotId == initialReceipt.SnapshotIds.Single()).CalledAmount);
        Assert.Equal("125", history.Single(item => item.SupersedesSnapshotId.HasValue).CalledAmount);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PreviewAsync(Scope, "operator", new([Row(commitment.CommitmentId)], null, Evidence, "Implicit replacement refused")));
    }

    [Fact]
    public async Task BatchValidationAndEvidenceRecheckPreventPartialOrUnverifiedWrites()
    {
        var authority = new Authority();
        var service = NewService(authority);
        var commitment = await service.CreateAsync(Scope, "operator", Create());
        await Assert.ThrowsAsync<ArgumentException>(() => service.PreviewAsync(Scope, "operator",
            new([Row(commitment.CommitmentId), Row(Guid.NewGuid()) with { AsOfDate = "2026-10-01" }], null, Evidence, "Invalid row in a batch")));
        Assert.Empty((await service.ReadAsync(Scope)).Snapshots);
        var preview = await service.PreviewAsync(Scope, "operator", new([Row(commitment.CommitmentId)], null, Evidence, "Review before source unavailable"));
        authority.EvidenceAvailable = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcceptAsync(Scope, "operator", new(preview.BatchId, preview.Fingerprint, Guid.NewGuid())));
        Assert.Empty((await service.ReadAsync(Scope)).Snapshots);
    }

    [Theory]
    [InlineData("1e3")]
    [InlineData("NaN")]
    [InlineData("1,000")]
    [InlineData("-1")]
    [InlineData("0.12345678901234567890123456789")]
    [InlineData("123456789012345678901.123456789")]
    public async Task InvalidFinancialTextIsNotRoundedOrCoerced(string amount)
    {
        var service = NewService();
        var commitment = await service.CreateAsync(Scope, "operator", Create());
        await Assert.ThrowsAsync<ArgumentException>(() => service.PreviewAsync(Scope, "operator",
            new([Row(commitment.CommitmentId) with { CalledAmount = amount }], null, Evidence, "Invalid source text")));
    }

    [Fact]
    public async Task DatesZeroAndIndependentSourceValuesArePreservedWithoutInventedRollForward()
    {
        var service = NewService();
        var commitment = await service.CreateAsync(Scope, "operator", Create());
        var row = Row(commitment.CommitmentId) with { CommitmentAmount = "0", CalledAmount = "1000", UnfundedAmount = "250", CumulativeDistributions = "1500" };
        var preview = await service.PreviewAsync(Scope, "operator", new([row], null, Evidence, "Source-reported recallable amounts"));
        Assert.Equal("0", preview.Rows.Single().CommitmentAmount);
        await Assert.ThrowsAsync<ArgumentException>(() => service.PreviewAsync(Scope, "operator", new([row with { AsOfDate = "2026-02-30" }], null, Evidence, "Invalid date")));
        await Assert.ThrowsAsync<ArgumentException>(() => service.PreviewAsync(Scope, "operator", new([row with { NavValuationDate = "2027-01-01" }], null, Evidence, "Future valuation mismatch")));
    }

    [Fact]
    public void CanonicalCsvRetainsQuotedSourceReferencesAndRejectsMalformedShape()
    {
        var id = Guid.NewGuid();
        var csv = PrivateCapitalCsv.Header + $"\r\n{id},2026-09-30,2026-09-30,1000,100,900,0,110,\"Statement, page \"\"2\"\"\",\r\n";
        var row = Assert.Single(PrivateCapitalCsv.Parse(csv));
        Assert.Equal("Statement, page \"2\"", row.SourceReference);
        Assert.Equal("0", row.CumulativeDistributions);
        Assert.Null(row.SupersedesSnapshotId);
        Assert.Throws<ArgumentException>(() => PrivateCapitalCsv.Parse("wrong,header\n1,2"));
        Assert.Throws<ArgumentException>(() => PrivateCapitalCsv.Parse(PrivateCapitalCsv.Header + "\n\"unterminated"));
    }

    private static CreatePrivateCapitalCommitment Create() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "usd", "Subscription agreement p4", Evidence);
    private static PrivateCapitalSnapshotInput Row(Guid id) => new(id, "2026-09-30", "2026-09-30", "1000", "100", "900", "0", "110", "Quarterly statement p2");
    private static PrivateCapitalRecordService NewService(Authority? authority = null) => new(new MemoryStore(), authority ?? new Authority());

    private sealed class Authority : IPrivateCapitalRecordAuthority
    {
        public bool EvidenceAvailable { get; set; } = true;
        public Task<PrivateCapitalChoices> ChoicesAsync(PrivateCapitalScope scope, CancellationToken ct) => Task.FromResult(new PrivateCapitalChoices([], []));
        public Task<PrivateCapitalVehicle> ValidateRegisterAsync(PrivateCapitalScope scope, Guid entityId, Guid securityId, string currency, CancellationToken ct)
            => Task.FromResult(new PrivateCapitalVehicle("Source vehicle", "Buyout", 2020, 5));
        public Task ValidateEvidenceAsync(PrivateCapitalScope scope, PrivateCapitalEvidence evidence, CancellationToken ct)
            => EvidenceAvailable ? Task.CompletedTask : Task.FromException(new InvalidOperationException("Retained source bytes are unavailable."));
    }

    private sealed class MemoryStore : IPrivateCapitalRecordStore
    {
        private readonly Dictionary<PrivateCapitalScope, List<PrivateCapitalStoredRecord>> _rows = [];
        private readonly object _gate = new();
        public Task<IReadOnlyList<PrivateCapitalStoredRecord>> ReadAsync(PrivateCapitalScope scope, CancellationToken ct = default)
        {
            lock (_gate)
                return Task.FromResult<IReadOnlyList<PrivateCapitalStoredRecord>>(_rows.GetValueOrDefault(scope)?.ToArray() ?? []);
        }
        public Task AppendAsync(PrivateCapitalScope scope, Func<IReadOnlyList<PrivateCapitalStoredRecord>, IReadOnlyList<PrivateCapitalStoredRecord>> prepare, CancellationToken ct = default)
        {
            lock (_gate)
            {
                if (!_rows.TryGetValue(scope, out var rows))
                    _rows[scope] = rows = [];
                rows.AddRange(prepare(rows.ToArray()));
            }
            return Task.CompletedTask;
        }
    }
}
