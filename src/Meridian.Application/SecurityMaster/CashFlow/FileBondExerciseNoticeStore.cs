using System.Text.Json;
using Meridian.Contracts.CashFlows;
using Meridian.Storage;

namespace Meridian.Application.SecurityMaster.CashFlow;

/// <summary>
/// Governed exercise-notice evidence: one write-once JSON record per notice under the security, a single
/// decision by a different actor, and verification that a selected scenario exercise restates exactly
/// what an approved notice says. Nothing here posts a redemption.
/// </summary>
public sealed class FileBondExerciseNoticeStore(StorageOptions options, TimeProvider? timeProvider = null) : IBondExerciseNoticeStore, IExerciseNoticeVerifier
{
    private readonly string _root = Path.Combine(options.RootPath, "exercise-notices");
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<BondExerciseNoticeDto> RecordAsync(BondExerciseNoticeDto notice, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notice);
        var issues = notice.Validate();
        if (issues.Count > 0 || notice.ApprovalState != ExerciseNoticeApprovalState.Recorded || notice.DecidedBy is not null)
            throw new ArgumentException("Exercise notice is incomplete or not in the Recorded state: " + string.Join(" ", issues));
        var path = FilePath(notice.SecurityId, notice.NoticeId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(path))
            {
                var retained = await ReadAsync(path, ct).ConfigureAwait(false);
                if (retained.SecurityId != notice.SecurityId || Identity(retained) != Identity(notice))
                    throw new InvalidOperationException("Notice identity is already bound to different evidence.");
                return retained;
            }
            await WriteAsync(path, notice, createNew: true, ct).ConfigureAwait(false);
            return notice;
        }
        finally { _gate.Release(); }
    }

    public async Task<BondExerciseNoticeDto?> GetAsync(Guid securityId, Guid noticeId, CancellationToken ct = default)
    {
        var path = FilePath(securityId, noticeId);
        if (!File.Exists(path))
            return null;
        var notice = await ReadAsync(path, ct).ConfigureAwait(false);
        return notice.SecurityId == securityId && notice.NoticeId == noticeId ? notice : throw new InvalidDataException("Exercise notice identity does not match its path.");
    }

    public async Task<IReadOnlyList<BondExerciseNoticeDto>> ListAsync(Guid securityId, CancellationToken ct = default)
    {
        if (securityId == Guid.Empty)
            throw new ArgumentException("A security identity is required.");
        var directory = Path.Combine(_root, securityId.ToString("N"));
        if (!Directory.Exists(directory))
            return [];
        var notices = new List<BondExerciseNoticeDto>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json").OrderBy(static f => f, StringComparer.Ordinal))
            notices.Add(await ReadAsync(file, ct).ConfigureAwait(false));
        return notices.OrderBy(n => n.ExerciseDate).ThenBy(n => n.RecordedAtUtc).ToArray();
    }

    public async Task<BondExerciseNoticeDto> DecideAsync(Guid securityId, Guid noticeId, bool approve, string actor, string? reason, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var notice = await GetAsync(securityId, noticeId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The exercise notice does not exist.");
            if (notice.ApprovalState != ExerciseNoticeApprovalState.Recorded)
                throw new InvalidOperationException("The exercise notice has already been decided; decisions are immutable.");
            if (string.Equals(notice.RecordedBy, actor.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The recording actor cannot approve or reject their own notice.");
            if (!approve && string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("A rejection needs a reason.");
            var decided = notice with
            {
                ApprovalState = approve ? ExerciseNoticeApprovalState.Approved : ExerciseNoticeApprovalState.Rejected,
                DecidedBy = actor.Trim(),
                DecidedAtUtc = _timeProvider.GetUtcNow(),
                DecisionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()
            };
            await WriteAsync(FilePath(securityId, noticeId), decided, createNew: false, ct).ConfigureAwait(false);
            return decided;
        }
        finally { _gate.Release(); }
    }

    public async Task<ExerciseNoticeVerification> VerifyAsync(Guid securityId, SelectedCashFlowExercise selection, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.HypotheticalNotice)
            return new(false, "A hypothetical notice needs no verification and never counts as retained.");
        if (!Guid.TryParse(selection.NoticeEvidenceId, out var noticeId))
            return new(false, "The retained notice reference is not a notice identity.");
        var notice = await GetAsync(securityId, noticeId, ct).ConfigureAwait(false);
        if (notice is null)
            return new(false, "No exercise notice with that identity is retained for this security.");
        if (notice.ApprovalState != ExerciseNoticeApprovalState.Approved)
            return new(false, $"The exercise notice is {notice.ApprovalState}; only an approved notice supports a retained scenario exercise.", notice);
        if (notice.Side != selection.Side || !string.Equals(notice.StreamId, selection.StreamId, StringComparison.Ordinal)
            || notice.ExerciseDate != selection.ExerciseDate || notice.NoticeDate != selection.NoticeDate
            || (notice.Amount is { } amount && amount != selection.Amount))
            return new(false, "The selected exercise does not restate the approved notice's side, stream, dates and amount.", notice);
        return new(true, "Approved notice evidence matches the selected exercise.", notice);
    }

    private static string Identity(BondExerciseNoticeDto n)
        => string.Join("|", n.StreamId, n.Side, n.NoticeDate, n.ExerciseDate, n.Price, n.Amount, n.SourceDocumentSha256, n.ReceivedDate, n.SourceReference, n.RecordedBy, n.RecordedAtUtc.UtcTicks);

    private string FilePath(Guid securityId, Guid noticeId)
    {
        if (securityId == Guid.Empty || noticeId == Guid.Empty)
            throw new ArgumentException("Non-empty security and notice identities are required.");
        return Path.Combine(_root, securityId.ToString("N"), noticeId.ToString("N") + ".json");
    }

    private static async Task<BondExerciseNoticeDto> ReadAsync(string path, CancellationToken ct)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        var notice = await JsonSerializer.DeserializeAsync(input, BondExerciseNoticeJsonContext.Default.BondExerciseNoticeDto, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("Empty exercise notice.");
        if (notice.Validate().Count > 0)
            throw new InvalidDataException("Retained exercise notice is invalid.");
        return notice;
    }

    private static async Task WriteAsync(string path, BondExerciseNoticeDto notice, bool createNew, CancellationToken ct)
    {
        var staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(file, notice, BondExerciseNoticeJsonContext.Default.BondExerciseNoticeDto, ct).ConfigureAwait(false);
                await file.FlushAsync(ct).ConfigureAwait(false);
                file.Flush(flushToDisk: true);
            }
            File.Move(staging, path, overwrite: !createNew);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }
}
