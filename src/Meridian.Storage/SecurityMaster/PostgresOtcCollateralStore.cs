using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// PostgreSQL store for exposure snapshots, collateral balances and margin calls (migration 054).
/// Snapshots are append-only: recomputing a date appends the next version and the table rejects
/// updates and deletes. Balances and calls are versioned in place. Every write appends to
/// <c>security_governance_events</c> on the <see cref="SecurityGovernanceStreams.OtcCollateral"/>
/// stream (migration 055) in the same transaction, naming the actor, so the change and its record
/// commit or roll back together. Every write passes the pure rules first
/// (<see cref="OtcExposureRules"/>, <see cref="OtcCollateralRules"/>, <see cref="OtcMarginCallRules"/>),
/// so the database constraints are a backstop rather than the first line of validation. A call is
/// issued only from the netting set's latest snapshot, and only while every record the snapshot
/// was computed from is unchanged and no market data for its valued securities has been recorded
/// since its knowledge time; <see cref="OtcAdvisoryLocks"/> states the locks that keep the record
/// check true until the call commits and the order they are taken in.
/// </summary>
public sealed class PostgresOtcCollateralStore : IOtcCollateralStore
{
    /// <summary>Governance event types recorded on the <see cref="SecurityGovernanceStreams.OtcCollateral"/> stream.</summary>
    public const string SnapshotRecordedEvent = "ExposureSnapshotRecorded";
    public const string BalanceRecordedEvent = "CollateralBalanceRecorded";
    public const string BalanceAmendedEvent = "CollateralBalanceAmended";
    public const string CallIssuedEvent = "MarginCallIssued";

    private const string OpenCallStatuses = "('Issued', 'Agreed', 'Disputed')";

    private const string BalanceColumns =
        "balance_id, collateral_agreement_id, direction, kind, eligible_collateral_description, security_id, currency, " +
        "quantity, unit_value, valued_amount, haircut_percent, valued_as_of, reference, notes, version, updated_by, updated_at";

    private const string CallColumns =
        "call_id, netting_set_id, collateral_agreement_id, snapshot_id, movement_kind, mover, currency, amount_called, amount_agreed, " +
        "status, issued_on, due_on, dispute_reason, settled_on, settled_amount, notes, version, updated_by, updated_at";

    private readonly SecurityMasterOptions _options;

    public PostgresOtcCollateralStore(SecurityMasterOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<OtcExposureSnapshotDto> AppendExposureSnapshotAsync(OtcExposureSnapshotDto snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.NettingSetId == Guid.Empty)
            throw new ArgumentException("An exposure snapshot belongs to a netting set.", nameof(snapshot));
        if (string.IsNullOrWhiteSpace(snapshot.RuleVersion) || string.IsNullOrWhiteSpace(snapshot.Currency) || string.IsNullOrWhiteSpace(snapshot.ComputedBy))
            throw new ArgumentException("An exposure snapshot states its rule version, currency and who computed it.", nameof(snapshot));
        if (!Enum.IsDefined(snapshot.NettingBasis) || !Enum.IsDefined(snapshot.Status) || !Enum.IsDefined(snapshot.Provenance))
            throw new ArgumentException("An exposure snapshot states its netting basis, status and provenance.", nameof(snapshot));
        if (snapshot.Contracts is null || snapshot.CollateralHeld is null || snapshot.Movements is null || snapshot.Blockers is null)
            throw new ArgumentException("An exposure snapshot carries its contracts, collateral, movements and blockers, even when empty.", nameof(snapshot));

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        // Computations for one netting set are serialized so concurrent recomputations of one date
        // take distinct versions rather than colliding on the unique index.
        await OtcAdvisoryLocks.LockAsync(connection, transaction, OtcAdvisoryLocks.ExposureSnapshot(snapshot.NettingSetId), ct).ConfigureAwait(false);
        await RequireRowAsync(connection, transaction, "otc_netting_sets", "netting_set_id", snapshot.NettingSetId, "netting set", ct).ConfigureAwait(false);
        if (snapshot.CollateralAgreementId is { } collateralAgreementId)
            await RequireRowAsync(connection, transaction, "otc_collateral_agreements", "collateral_agreement_id", collateralAgreementId, "collateral agreement", ct).ConfigureAwait(false);

        var version = await LatestVersionAsync(connection, transaction, snapshot.NettingSetId, snapshot.AsOfDate, ct).ConfigureAwait(false) + 1;
        var stored = snapshot with
        {
            SnapshotId = snapshot.SnapshotId == Guid.Empty ? Guid.NewGuid() : snapshot.SnapshotId,
            Version = version,
            PayloadSha256 = string.Empty
        };
        var payload = OtcExposureRules.SerializePayload(stored);
        stored = stored with { PayloadSha256 = OtcExposureRules.Digest(stored) };

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                $"""
                insert into {_options.Schema}.otc_exposure_snapshots (
                    snapshot_id, netting_set_id, collateral_agreement_id, as_of_date, version, rule_version, currency,
                    netting_basis, status, provenance, net_exposure, gross_positive, gross_negative,
                    payload, payload_version, payload_sha256, computed_by, computed_at)
                values (
                    @id, @netting_set_id, @csa, @as_of, @version, @rule_version, @currency,
                    @basis, @status, @provenance, @net, @gross_positive, @gross_negative,
                    @payload, 1, @sha256, @computed_by, @computed_at);
                """;
            command.Parameters.AddWithValue("id", stored.SnapshotId);
            command.Parameters.AddWithValue("netting_set_id", stored.NettingSetId);
            command.Parameters.AddWithValue("csa", (object?)stored.CollateralAgreementId ?? DBNull.Value);
            command.Parameters.AddWithValue("as_of", stored.AsOfDate);
            command.Parameters.AddWithValue("version", stored.Version);
            command.Parameters.AddWithValue("rule_version", stored.RuleVersion);
            command.Parameters.AddWithValue("currency", stored.Currency);
            command.Parameters.AddWithValue("basis", stored.NettingBasis.ToString());
            command.Parameters.AddWithValue("status", stored.Status.ToString());
            command.Parameters.AddWithValue("provenance", stored.Provenance.ToString());
            command.Parameters.AddWithValue("net", stored.NetExposure);
            command.Parameters.AddWithValue("gross_positive", stored.GrossPositive);
            command.Parameters.AddWithValue("gross_negative", stored.GrossNegative);
            command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = payload });
            command.Parameters.AddWithValue("sha256", stored.PayloadSha256);
            command.Parameters.AddWithValue("computed_by", stored.ComputedBy);
            command.Parameters.AddWithValue("computed_at", stored.ComputedAt.UtcDateTime);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await PostgresSecurityGovernanceEventLog.AppendAsync(
            connection,
            transaction,
            _options.Schema,
            securityId: null,
            SecurityGovernanceStreams.OtcCollateral,
            SnapshotRecordedEvent,
            stored.ComputedBy,
            stored.ComputedAt,
            new
            {
                snapshotId = stored.SnapshotId,
                nettingSetId = stored.NettingSetId,
                collateralAgreementId = stored.CollateralAgreementId,
                asOfDate = stored.AsOfDate,
                version = stored.Version,
                status = stored.Status.ToString(),
                nettingBasis = stored.NettingBasis.ToString(),
                provenance = stored.Provenance.ToString(),
                currency = stored.Currency,
                netExposure = stored.NetExposure,
                grossPositive = stored.GrossPositive,
                grossNegative = stored.GrossNegative,
                movements = stored.Movements.Select(Describe).ToArray(),
                blockerCount = stored.Blockers.Count,
                payloadSha256 = stored.PayloadSha256
            },
            ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return stored;
    }

    public async Task<OtcExposureSnapshotDto?> GetExposureSnapshotAsync(Guid snapshotId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        return await ReadSnapshotAsync(connection, null, snapshotId, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OtcExposureSnapshotDto>> ListExposureSnapshotsAsync(Guid nettingSetId, DateOnly? asOfDate = null, bool latestVersionOnly = true, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var selection = latestVersionOnly ? "select distinct on (as_of_date)" : "select";
        var dateFilter = asOfDate is null ? string.Empty : " and as_of_date = @as_of";
        command.CommandText =
            $"""
            {selection} payload::text, version, payload_sha256, as_of_date
            from {_options.Schema}.otc_exposure_snapshots
            where netting_set_id = @id{dateFilter}
            order by as_of_date desc, version desc;
            """;
        command.Parameters.AddWithValue("id", nettingSetId);
        if (asOfDate is { } date)
            command.Parameters.AddWithValue("as_of", date);
        return await ReadSnapshotsAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<OtcCollateralBalanceDto> UpsertCollateralBalanceAsync(UpsertOtcCollateralBalanceRequest request, CancellationToken ct = default)
    {
        var normalized = OtcCollateralRules.Normalize(request);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        // Balance writes under one agreement wait for, and are waited on by, a call being issued
        // under it, so an issue cannot miss a balance recorded or amended while it checks the
        // snapshot's inputs. Taken before any row lock (see OtcAdvisoryLocks for the order).
        await OtcAdvisoryLocks.LockAsync(connection, transaction, OtcAdvisoryLocks.CollateralBalances(normalized.CollateralAgreementId), ct).ConfigureAwait(false);
        var agreement = await PostgresOtcContractStore.ReadCollateralAgreementAsync(connection, transaction, _options.Schema, normalized.CollateralAgreementId, ct).ConfigureAwait(false)
            ?? throw new ArgumentException($"The collateral agreement {normalized.CollateralAgreementId:D} does not exist.", nameof(request));
        var eligible = OtcCollateralRules.ResolveEligible(agreement, normalized.EligibleCollateralDescription);
        if (!string.Equals(normalized.Currency, agreement.Currency, StringComparison.Ordinal))
            throw new ArgumentException($"A balance under this collateral agreement is recorded in {agreement.Currency}, not {normalized.Currency}; this increment does no FX.", nameof(request));
        if (normalized.SecurityId is { } securityId)
            await RequireRowAsync(connection, transaction, "securities", "security_id", securityId, "security", ct).ConfigureAwait(false);

        var id = normalized.BalanceId ?? Guid.NewGuid();
        var previous = (await ReadBalancesAsync(connection, transaction, "where balance_id = @id", ("id", id), ct, forUpdate: true).ConfigureAwait(false)).SingleOrDefault();
        if (normalized.BalanceId is not null && previous is null)
            throw new ArgumentException($"The collateral balance {id:D} does not exist.", nameof(request));
        if (previous is not null && previous.CollateralAgreementId != normalized.CollateralAgreementId)
            throw new ArgumentException("A collateral balance cannot be moved under another collateral agreement; record a new balance.", nameof(request));

        var balance = new OtcCollateralBalanceDto(
            id,
            agreement.CollateralAgreementId,
            normalized.Direction,
            normalized.Kind,
            eligible.Description,
            normalized.SecurityId,
            normalized.Currency,
            normalized.Quantity,
            normalized.UnitValue,
            OtcCollateralRules.ValuedAmount(normalized.Quantity, normalized.UnitValue),
            eligible.HaircutPercent,
            normalized.ValuedAsOf,
            normalized.Reference,
            normalized.Notes,
            previous is null ? 1 : previous.Version + 1,
            normalized.UpdatedBy!,
            DateTimeOffset.UtcNow);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                $"""
                insert into {_options.Schema}.otc_collateral_balances ({BalanceColumns})
                values (
                    @id, @csa, @direction, @kind, @description, @security_id, @currency,
                    @quantity, @unit_value, @valued_amount, @haircut, @valued_as_of, @reference, @notes, @version, @updated_by, @updated_at)
                on conflict (balance_id) do update set
                    direction = excluded.direction,
                    kind = excluded.kind,
                    eligible_collateral_description = excluded.eligible_collateral_description,
                    security_id = excluded.security_id,
                    currency = excluded.currency,
                    quantity = excluded.quantity,
                    unit_value = excluded.unit_value,
                    valued_amount = excluded.valued_amount,
                    haircut_percent = excluded.haircut_percent,
                    valued_as_of = excluded.valued_as_of,
                    reference = excluded.reference,
                    notes = excluded.notes,
                    version = excluded.version,
                    updated_by = excluded.updated_by,
                    updated_at = excluded.updated_at;
                """;
            command.Parameters.AddWithValue("id", balance.BalanceId);
            command.Parameters.AddWithValue("csa", balance.CollateralAgreementId);
            command.Parameters.AddWithValue("direction", balance.Direction.ToString());
            command.Parameters.AddWithValue("kind", balance.Kind.ToString());
            command.Parameters.AddWithValue("description", balance.EligibleCollateralDescription);
            command.Parameters.AddWithValue("security_id", (object?)balance.SecurityId ?? DBNull.Value);
            command.Parameters.AddWithValue("currency", balance.Currency);
            command.Parameters.AddWithValue("quantity", balance.Quantity);
            command.Parameters.AddWithValue("unit_value", balance.UnitValue);
            command.Parameters.AddWithValue("valued_amount", balance.ValuedAmount);
            command.Parameters.AddWithValue("haircut", balance.HaircutPercent);
            command.Parameters.AddWithValue("valued_as_of", balance.ValuedAsOf);
            command.Parameters.AddWithValue("reference", (object?)balance.Reference ?? DBNull.Value);
            command.Parameters.AddWithValue("notes", (object?)balance.Notes ?? DBNull.Value);
            command.Parameters.AddWithValue("version", balance.Version);
            command.Parameters.AddWithValue("updated_by", balance.UpdatedBy);
            command.Parameters.AddWithValue("updated_at", balance.UpdatedAt.UtcDateTime);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await PostgresSecurityGovernanceEventLog.AppendAsync(
            connection,
            transaction,
            _options.Schema,
            securityId: null,
            SecurityGovernanceStreams.OtcCollateral,
            previous is null ? BalanceRecordedEvent : BalanceAmendedEvent,
            balance.UpdatedBy,
            balance.UpdatedAt,
            new
            {
                masterAgreementId = agreement.MasterAgreementId,
                balance = Describe(balance),
                previous = previous is null ? null : Describe(previous),
                changes = previous is null ? [] : ChangedFields(previous, balance)
            },
            ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return balance;
    }

    public async Task<IReadOnlyList<OtcCollateralBalanceDto>> ListCollateralBalancesAsync(Guid collateralAgreementId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        return await ReadBalancesAsync(connection, null, "where collateral_agreement_id = @id", ("id", collateralAgreementId), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Guid>> ListNettingSetsUnderCollateralAgreementAsync(Guid collateralAgreementId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        return await ReadNettingSetIdsUnderAgreementAsync(connection, null, collateralAgreementId, ct).ConfigureAwait(false);
    }

    public async Task<OtcMarginCallDto> IssueMarginCallAsync(IssueOtcMarginCallRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SnapshotId == Guid.Empty)
            throw new ArgumentException("A margin call is issued from an exposure snapshot.", nameof(request));
        var actor = OtcCollateralRules.RequireActor(request.UpdatedBy);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        var snapshot = await ReadSnapshotAsync(connection, transaction, request.SnapshotId, ct).ConfigureAwait(false)
            ?? throw new ArgumentException($"The exposure snapshot {request.SnapshotId:D} does not exist.", nameof(request));

        // Every lock is taken before any row lock, in the order OtcAdvisoryLocks states. The snapshot
        // lock comes first so a concurrent recompute cannot append a later version or a later date
        // between the checks below and this call's commit. The call lock serializes issues and
        // transitions for the netting set, so the one-open-call-per-mover-and-kind rule is checked
        // against a settled picture. The contracts and balances locks hold off every contract and
        // balance write the snapshot's inputs cover, including ones that would add a row, so the
        // input check cannot be overtaken before this call commits. Price and hierarchy writers take
        // no OTC lock; the market data check reads what they have committed (see
        // ReadMarketDataRecordedSinceAsync).
        await OtcAdvisoryLocks.LockAsync(connection, transaction, OtcAdvisoryLocks.ExposureSnapshot(snapshot.NettingSetId), ct).ConfigureAwait(false);
        await OtcAdvisoryLocks.LockAsync(connection, transaction, OtcAdvisoryLocks.MarginCall(snapshot.NettingSetId), ct).ConfigureAwait(false);
        await OtcAdvisoryLocks.LockNettingSetContractsAsync(connection, transaction, [snapshot.NettingSetId], ct).ConfigureAwait(false);
        if (snapshot.CollateralAgreementId is { } snapshotAgreementId)
            await OtcAdvisoryLocks.LockAsync(connection, transaction, OtcAdvisoryLocks.CollateralBalances(snapshotAgreementId), ct).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var latest = await LatestVersionAsync(connection, transaction, snapshot.NettingSetId, snapshot.AsOfDate, ct).ConfigureAwait(false);
        if (latest != snapshot.Version)
            throw new ArgumentException($"Exposure snapshot {snapshot.SnapshotId:D} (version {snapshot.Version}) has been superseded by version {latest} for {snapshot.AsOfDate:yyyy-MM-dd}; issue the call from the latest snapshot.", nameof(request));
        if (await LaterSnapshotAsync(connection, transaction, snapshot, DateOnly.FromDateTime(now.UtcDateTime), ct).ConfigureAwait(false) is { } later)
        {
            throw new ArgumentException(
                $"Exposure snapshot {snapshot.SnapshotId:D} values {snapshot.AsOfDate:yyyy-MM-dd}, but netting set {snapshot.NettingSetId:D} also has snapshot {later.SnapshotId:D} " +
                $"(version {later.Version}, {later.Status}) for the later date {later.AsOfDate:yyyy-MM-dd}; a call is issued only from the netting set's latest valuation, " +
                "so issue it from the latest snapshot for that date, recomputing the date if that snapshot cannot be called.", nameof(request));
        }

        var terms = await RequireCurrentTermsAsync(connection, transaction, snapshot, ct).ConfigureAwait(false);
        var inputs = await ReadCurrentInputsAsync(connection, transaction, snapshot, terms, ct).ConfigureAwait(false);
        var marketData = await ReadMarketDataRecordedSinceAsync(connection, transaction, snapshot, ct).ConfigureAwait(false);
        OtcMarginCallRules.RequireUnchangedInputs(snapshot, inputs, marketData);

        // The open calls of the netting set and every call already issued from this snapshot, so the
        // rule can refuse both a second open call and a repeat of a movement already called.
        var existingCalls = await ReadCallsAsync(
            connection,
            transaction,
            $"where netting_set_id = @id and (status in {OpenCallStatuses} or snapshot_id = @snapshot_id)",
            ct,
            ("id", snapshot.NettingSetId),
            ("snapshot_id", snapshot.SnapshotId)).ConfigureAwait(false);
        var call = OtcMarginCallRules.Issue(snapshot, request with { UpdatedBy = actor }, existingCalls, now);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                $"""
                insert into {_options.Schema}.otc_margin_calls ({CallColumns})
                values (
                    @id, @netting_set_id, @csa, @snapshot_id, @kind, @mover, @currency, @amount_called, @amount_agreed,
                    @status, @issued_on, @due_on, @dispute_reason, @settled_on, @settled_amount, @notes, @version, @updated_by, @updated_at);
                """;
            AddCallParameters(command, call);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await PostgresSecurityGovernanceEventLog.AppendAsync(
            connection,
            transaction,
            _options.Schema,
            securityId: null,
            SecurityGovernanceStreams.OtcCollateral,
            CallIssuedEvent,
            call.UpdatedBy,
            call.UpdatedAt,
            new
            {
                call = Describe(call),
                snapshot = new { snapshotId = snapshot.SnapshotId, version = snapshot.Version, asOfDate = snapshot.AsOfDate, payloadSha256 = snapshot.PayloadSha256 }
            },
            ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return call;
    }

    public async Task<OtcMarginCallDto> TransitionMarginCallAsync(TransitionOtcMarginCallRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CallId == Guid.Empty)
            throw new ArgumentException("A transition names the margin call it moves.", nameof(request));
        var actor = OtcCollateralRules.RequireActor(request.UpdatedBy);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        var probe = (await ReadCallsAsync(connection, transaction, "where call_id = @id", ("id", request.CallId), ct).ConfigureAwait(false)).SingleOrDefault()
            ?? throw new ArgumentException($"The margin call {request.CallId:D} does not exist.", nameof(request));
        await OtcAdvisoryLocks.LockAsync(connection, transaction, OtcAdvisoryLocks.MarginCall(probe.NettingSetId), ct).ConfigureAwait(false);
        var current = (await ReadCallsAsync(connection, transaction, "where call_id = @id", ("id", request.CallId), ct, forUpdate: true).ConfigureAwait(false)).Single();
        var next = OtcMarginCallRules.Transition(current, request with { UpdatedBy = actor }, DateTimeOffset.UtcNow);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                $"""
                update {_options.Schema}.otc_margin_calls set
                    amount_agreed = @amount_agreed,
                    status = @status,
                    dispute_reason = @dispute_reason,
                    settled_on = @settled_on,
                    settled_amount = @settled_amount,
                    notes = @notes,
                    version = @version,
                    updated_by = @updated_by,
                    updated_at = @updated_at
                where call_id = @id and version = @expected_version;
                """;
            AddCallParameters(command, next);
            command.Parameters.AddWithValue("expected_version", current.Version);
            if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                throw new InvalidOperationException($"Margin call {current.CallId:D} changed under the transition; retry.");
        }

        await PostgresSecurityGovernanceEventLog.AppendAsync(
            connection,
            transaction,
            _options.Schema,
            securityId: null,
            SecurityGovernanceStreams.OtcCollateral,
            OtcMarginCallRules.EventType(request.Action),
            next.UpdatedBy,
            next.UpdatedAt,
            new
            {
                call = Describe(next),
                previous = Describe(current),
                action = request.Action.ToString(),
                reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim()
            },
            ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return next;
    }

    public async Task<OtcMarginCallDto?> GetMarginCallAsync(Guid callId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        return (await ReadCallsAsync(connection, null, "where call_id = @id", ("id", callId), ct).ConfigureAwait(false)).SingleOrDefault();
    }

    public async Task<IReadOnlyList<OtcMarginCallDto>> ListMarginCallsAsync(Guid nettingSetId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        return await ReadCallsAsync(connection, null, "where netting_set_id = @id", ("id", nettingSetId), ct).ConfigureAwait(false);
    }

    /// <summary>The declared facts an amendment of a balance changes, in the order the balance states them.</summary>
    internal static IReadOnlyList<string> ChangedFields(OtcCollateralBalanceDto previous, OtcCollateralBalanceDto next)
    {
        var changes = new List<string>();
        if (previous.Direction != next.Direction)
            changes.Add("direction");
        if (previous.Kind != next.Kind)
            changes.Add("kind");
        if (!string.Equals(previous.EligibleCollateralDescription, next.EligibleCollateralDescription, StringComparison.Ordinal))
            changes.Add("eligibleCollateralDescription");
        if (previous.SecurityId != next.SecurityId)
            changes.Add("securityId");
        if (!string.Equals(previous.Currency, next.Currency, StringComparison.Ordinal))
            changes.Add("currency");
        if (previous.Quantity != next.Quantity)
            changes.Add("quantity");
        if (previous.UnitValue != next.UnitValue)
            changes.Add("unitValue");
        if (previous.ValuedAmount != next.ValuedAmount)
            changes.Add("valuedAmount");
        if (previous.HaircutPercent != next.HaircutPercent)
            changes.Add("haircutPercent");
        if (previous.ValuedAsOf != next.ValuedAsOf)
            changes.Add("valuedAsOf");
        if (!string.Equals(previous.Reference, next.Reference, StringComparison.Ordinal))
            changes.Add("reference");
        if (!string.Equals(previous.Notes, next.Notes, StringComparison.Ordinal))
            changes.Add("notes");
        return changes;
    }

    private static object Describe(OtcCollateralBalanceDto balance) => new
    {
        balanceId = balance.BalanceId,
        collateralAgreementId = balance.CollateralAgreementId,
        direction = balance.Direction.ToString(),
        kind = balance.Kind.ToString(),
        eligibleCollateralDescription = balance.EligibleCollateralDescription,
        securityId = balance.SecurityId,
        currency = balance.Currency,
        quantity = balance.Quantity,
        unitValue = balance.UnitValue,
        valuedAmount = balance.ValuedAmount,
        haircutPercent = balance.HaircutPercent,
        valuedAsOf = balance.ValuedAsOf,
        reference = balance.Reference,
        notes = balance.Notes,
        version = balance.Version,
        updatedBy = balance.UpdatedBy,
        updatedAt = balance.UpdatedAt
    };

    private static object Describe(OtcMarginCallDto call) => new
    {
        callId = call.CallId,
        nettingSetId = call.NettingSetId,
        collateralAgreementId = call.CollateralAgreementId,
        snapshotId = call.SnapshotId,
        kind = call.Kind.ToString(),
        mover = call.Mover.ToString(),
        currency = call.Currency,
        amountCalled = call.AmountCalled,
        amountAgreed = call.AmountAgreed,
        status = call.Status.ToString(),
        issuedOn = call.IssuedOn,
        dueOn = call.DueOn,
        disputeReason = call.DisputeReason,
        settledOn = call.SettledOn,
        settledAmount = call.SettledAmount,
        notes = call.Notes,
        version = call.Version,
        updatedBy = call.UpdatedBy,
        updatedAt = call.UpdatedAt
    };

    private static object Describe(OtcCollateralMovementDto movement) => new
    {
        kind = movement.Kind.ToString(),
        mover = movement.Mover.ToString(),
        amount = movement.Amount,
        currency = movement.Currency
    };

    private static void AddCallParameters(NpgsqlCommand command, OtcMarginCallDto call)
    {
        command.Parameters.AddWithValue("id", call.CallId);
        command.Parameters.AddWithValue("netting_set_id", call.NettingSetId);
        command.Parameters.AddWithValue("csa", call.CollateralAgreementId);
        command.Parameters.AddWithValue("snapshot_id", call.SnapshotId);
        command.Parameters.AddWithValue("kind", call.Kind.ToString());
        command.Parameters.AddWithValue("mover", call.Mover.ToString());
        command.Parameters.AddWithValue("currency", call.Currency);
        command.Parameters.AddWithValue("amount_called", call.AmountCalled);
        command.Parameters.AddWithValue("amount_agreed", (object?)call.AmountAgreed ?? DBNull.Value);
        command.Parameters.AddWithValue("status", call.Status.ToString());
        command.Parameters.AddWithValue("issued_on", call.IssuedOn);
        command.Parameters.AddWithValue("due_on", (object?)call.DueOn ?? DBNull.Value);
        command.Parameters.AddWithValue("dispute_reason", (object?)call.DisputeReason ?? DBNull.Value);
        command.Parameters.AddWithValue("settled_on", (object?)call.SettledOn ?? DBNull.Value);
        command.Parameters.AddWithValue("settled_amount", (object?)call.SettledAmount ?? DBNull.Value);
        command.Parameters.AddWithValue("notes", (object?)call.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("version", call.Version);
        command.Parameters.AddWithValue("updated_by", call.UpdatedBy);
        command.Parameters.AddWithValue("updated_at", call.UpdatedAt.UtcDateTime);
    }

    private async Task RequireRowAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string table, string keyColumn, Guid id, string noun, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select 1 from {_options.Schema}.{table} where {keyColumn} = @id;";
        command.Parameters.AddWithValue("id", id);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is null)
            throw new ArgumentException($"The {noun} {id:D} does not exist.");
    }

    /// <summary>
    /// Refuses a call from a snapshot computed under credit support terms that no longer stand: the
    /// netting set now points at another collateral agreement (or none), the master agreement or
    /// the collateral agreement has been amended since, or another netting set now references the
    /// collateral agreement. Returns the netting set's current version and the netting sets under
    /// its agreement for the input check. The netting set and master rows are share-locked, so an
    /// amendment of either waits for this call to commit (or this call for the amendment). The
    /// collateral agreement row is locked for update: besides holding off an amendment, that
    /// conflicts with the key-share lock the foreign key takes when a netting set is created under
    /// the agreement or re-pointed to it, so no netting set can start referencing it before this
    /// call commits. Each contract-store write locks only the row it writes before the key-share
    /// locks on the rows it references, and this call never locks another netting set's row, so no
    /// cycle can form.
    /// </summary>
    private async Task<CurrentTerms> RequireCurrentTermsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, OtcExposureSnapshotDto snapshot, CancellationToken ct)
    {
        Guid? currentAgreementId;
        Guid currentMasterId;
        long nettingSetVersion;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = $"select collateral_agreement_id, master_agreement_id, version from {_options.Schema}.otc_netting_sets where netting_set_id = @id for share;";
            command.Parameters.AddWithValue("id", snapshot.NettingSetId);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                throw new ArgumentException($"The netting set {snapshot.NettingSetId:D} does not exist.");
            currentAgreementId = reader.IsDBNull(0) ? null : reader.GetGuid(0);
            currentMasterId = reader.GetGuid(1);
            nettingSetVersion = reader.GetInt64(2);
        }

        // The master agreement decides close-out netting versus gross exposure, so an amendment of it
        // (or a re-pointed netting set) changes the amount a call may demand.
        long? currentMasterVersion;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = $"select version from {_options.Schema}.otc_master_agreements where agreement_id = @id for share;";
            command.Parameters.AddWithValue("id", currentMasterId);
            currentMasterVersion = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is long version ? version : null;
        }

        if (currentMasterId != snapshot.MasterAgreementId || currentMasterVersion != snapshot.MasterAgreementVersion)
        {
            throw new ArgumentException(
                $"Exposure snapshot {snapshot.SnapshotId:D} was computed under master agreement {snapshot.MasterAgreementId:D} version {snapshot.MasterAgreementVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)}; " +
                $"netting set {snapshot.NettingSetId:D} is now under master agreement {currentMasterId:D} version {currentMasterVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}. Recompute the snapshot before issuing a call.");
        }

        long? currentAgreementVersion = null;
        if (currentAgreementId is { } agreementId)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"select version from {_options.Schema}.otc_collateral_agreements where collateral_agreement_id = @id for update;";
            command.Parameters.AddWithValue("id", agreementId);
            currentAgreementVersion = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is long version ? version : null;
        }

        if (currentAgreementId != snapshot.CollateralAgreementId || currentAgreementVersion != snapshot.CollateralAgreementVersion)
        {
            throw new ArgumentException(
                $"Exposure snapshot {snapshot.SnapshotId:D} was computed under {Terms(snapshot.CollateralAgreementId, snapshot.CollateralAgreementVersion)}; " +
                $"netting set {snapshot.NettingSetId:D} is now under {Terms(currentAgreementId, currentAgreementVersion)}. Recompute the snapshot before issuing a call.");
        }

        IReadOnlyList<Guid> nettingSetsUnderAgreement = [];
        if (currentAgreementId is { } sharedAgreementId)
        {
            nettingSetsUnderAgreement = await ReadNettingSetIdsUnderAgreementAsync(connection, transaction, sharedAgreementId, ct).ConfigureAwait(false);
            var others = nettingSetsUnderAgreement.Where(id => id != snapshot.NettingSetId).ToArray();
            if (others.Length > 0)
            {
                throw new ArgumentException(
                    $"Collateral agreement {sharedAgreementId:D} is also referenced by netting set(s) {string.Join(", ", others.Select(id => id.ToString("D")))}; " +
                    "credit support is not allocated across netting sets in this increment, so no call is issued under it.");
            }
        }

        return new CurrentTerms(nettingSetVersion, nettingSetsUnderAgreement);

        static string Terms(Guid? agreementId, long? version)
            => agreementId is { } id
                ? $"collateral agreement {id:D} version {version?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}"
                : "no collateral agreement";
    }

    /// <summary>
    /// The snapshot's input records as the store holds them now: the netting set version and the
    /// netting sets under its agreement from <see cref="RequireCurrentTermsAsync"/>, every contract
    /// in the netting set and every balance under the snapshot's collateral agreement, each with its
    /// version. The contract and balance rows are share-locked; the contracts and balances advisory
    /// locks this call already holds keep any writer from adding to either set before it commits.
    /// </summary>
    private async Task<OtcExposureSnapshotInputsDto> ReadCurrentInputsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, OtcExposureSnapshotDto snapshot, CurrentTerms terms, CancellationToken ct)
    {
        var contracts = await ReadInputVersionsAsync(
            connection,
            transaction,
            $"select contract_id, version from {_options.Schema}.otc_contracts where netting_set_id = @id order by contract_id for share;",
            snapshot.NettingSetId,
            ct).ConfigureAwait(false);
        IReadOnlyList<OtcExposureInputVersionDto> balances = snapshot.CollateralAgreementId is { } agreementId
            ? await ReadInputVersionsAsync(
                connection,
                transaction,
                $"select balance_id, version from {_options.Schema}.otc_collateral_balances where collateral_agreement_id = @id order by balance_id for share;",
                agreementId,
                ct).ConfigureAwait(false)
            : [];
        return new OtcExposureSnapshotInputsDto(terms.NettingSetVersion, contracts, balances, terms.NettingSetsUnderAgreement);
    }

    private static async Task<IReadOnlyList<OtcExposureInputVersionDto>> ReadInputVersionsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, Guid id, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("id", id);
        var rows = new List<OtcExposureInputVersionDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            rows.Add(new OtcExposureInputVersionDto(reader.GetGuid(0), reader.GetInt64(1)));
        return rows;
    }

    /// <summary>
    /// The latest version of the netting set's latest snapshot for a date after the snapshot's and
    /// no later than <paramref name="today"/>, whatever its status, or null when there is none. A
    /// snapshot dated after today is not yet a valuation of the netting set (computations refuse such
    /// a date; <see cref="OtcExposureRules.RequireNotFutureDated"/>), so a stray one cannot shadow
    /// the real latest valuation and lock calls out until its date comes.
    /// </summary>
    private async Task<LaterSnapshot?> LaterSnapshotAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, OtcExposureSnapshotDto snapshot, DateOnly today, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select snapshot_id, as_of_date, version, status
            from {_options.Schema}.otc_exposure_snapshots
            where netting_set_id = @id and as_of_date > @as_of and as_of_date <= @today
            order by as_of_date desc, version desc
            limit 1;
            """;
        command.Parameters.AddWithValue("id", snapshot.NettingSetId);
        command.Parameters.AddWithValue("as_of", snapshot.AsOfDate);
        command.Parameters.AddWithValue("today", today);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false)
            ? new LaterSnapshot(reader.GetGuid(0), reader.GetFieldValue<DateOnly>(1), reader.GetInt32(2), reader.GetString(3))
            : null;
    }

    /// <summary>
    /// Every price observation and pricing hierarchy version for the snapshot's valued securities
    /// recorded after <see cref="OtcMarginCallRules.MarketDataKnownAt"/> that bears on its as-of
    /// date: a price observed by the end of that date, or a hierarchy, for any account, in effect
    /// by then. Any of them may change what a recomputation selects, so each refuses the call.
    /// Price and hierarchy writers take no OTC lock, so this reads what has committed when it runs;
    /// a write committing after that is later than the call, as one committing after the call is.
    /// One gap remains: a record's recorded time is its writer's transaction start on the database
    /// clock, compared here, as by the valuation, with a knowledge time on the application clock,
    /// so a write that began before the knowledge time but committed only after the computation had
    /// read the market data, or one the clocks' skew places before it, is neither read by the
    /// valuation nor counted here.
    /// </summary>
    private async Task<IReadOnlyList<OtcMarketDataRecordDto>> ReadMarketDataRecordedSinceAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, OtcExposureSnapshotDto snapshot, CancellationToken ct)
    {
        if (snapshot.Inputs?.Securities is not { Count: > 0 } securities)
            return [];

        const string hierarchy = "case when account_id = '' then 'pricing hierarchy' else 'pricing hierarchy for account ' || account_id end";
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select security_id, 'price from ' || source_id, price_as_of, recorded_at
            from {_options.Schema}.security_raw_prices
            where security_id = any(@securities) and price_as_of < @valued_until and recorded_at > @known_at
            union
            select security_id, {hierarchy}, as_of, recorded_at
            from {_options.Schema}.security_pricing_hierarchy_history
            where security_id = any(@securities) and as_of < @valued_until and recorded_at > @known_at
            union
            select security_id, {hierarchy}, as_of, recorded_at
            from {_options.Schema}.security_pricing_hierarchy
            where security_id = any(@securities) and as_of < @valued_until and recorded_at > @known_at
            order by 1, 4, 2, 3;
            """;
        command.Parameters.AddWithValue("securities", securities.ToArray());
        command.Parameters.AddWithValue("valued_until", snapshot.AsOfDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        command.Parameters.AddWithValue("known_at", OtcMarginCallRules.MarketDataKnownAt(snapshot).UtcDateTime);
        var records = new List<OtcMarketDataRecordDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            records.Add(new OtcMarketDataRecordDto(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetFieldValue<DateTimeOffset>(3)));
        }

        return records;
    }

    /// <summary>What <see cref="RequireCurrentTermsAsync"/> read that the input check also needs.</summary>
    private sealed record CurrentTerms(long NettingSetVersion, IReadOnlyList<Guid> NettingSetsUnderAgreement);

    /// <summary>A snapshot of the netting set for a later date than the one being called.</summary>
    private sealed record LaterSnapshot(Guid SnapshotId, DateOnly AsOfDate, int Version, string Status);

    private async Task<IReadOnlyList<Guid>> ReadNettingSetIdsUnderAgreementAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid collateralAgreementId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select netting_set_id from {_options.Schema}.otc_netting_sets where collateral_agreement_id = @id order by netting_set_id;";
        command.Parameters.AddWithValue("id", collateralAgreementId);
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            ids.Add(reader.GetGuid(0));
        return ids;
    }

    private async Task<int> LatestVersionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid nettingSetId, DateOnly asOfDate, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select coalesce(max(version), 0) from {_options.Schema}.otc_exposure_snapshots where netting_set_id = @id and as_of_date = @as_of;";
        command.Parameters.AddWithValue("id", nettingSetId);
        command.Parameters.AddWithValue("as_of", asOfDate);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<OtcExposureSnapshotDto?> ReadSnapshotAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid snapshotId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select payload::text, version, payload_sha256, as_of_date
            from {_options.Schema}.otc_exposure_snapshots
            where snapshot_id = @id;
            """;
        command.Parameters.AddWithValue("id", snapshotId);
        return (await ReadSnapshotsAsync(command, ct).ConfigureAwait(false)).SingleOrDefault();
    }

    private static async Task<IReadOnlyList<OtcExposureSnapshotDto>> ReadSnapshotsAsync(NpgsqlCommand command, CancellationToken ct)
    {
        var snapshots = new List<OtcExposureSnapshotDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var payload = JsonSerializer.Deserialize(reader.GetString(0), OtcCollateralJsonContext.Default.OtcExposureSnapshotDto)
                ?? throw new InvalidOperationException("An exposure snapshot payload is empty.");
            snapshots.Add(payload with { Version = reader.GetInt32(1), PayloadSha256 = reader.GetString(2) });
        }

        return snapshots;
    }

    private async Task<IReadOnlyList<OtcCollateralBalanceDto>> ReadBalancesAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, string whereClause, (string Name, object Value) parameter, CancellationToken ct, bool forUpdate = false)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select {BalanceColumns}
            from {_options.Schema}.otc_collateral_balances
            {whereClause}
            order by valued_as_of, direction, balance_id{(forUpdate ? " for update" : string.Empty)};
            """;
        command.Parameters.AddWithValue(parameter.Name, parameter.Value);

        var rows = new List<OtcCollateralBalanceDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new OtcCollateralBalanceDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                Enum.Parse<OtcCollateralDirection>(reader.GetString(2)),
                Enum.Parse<OtcCollateralKind>(reader.GetString(3)),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.GetString(6),
                reader.GetDecimal(7),
                reader.GetDecimal(8),
                reader.GetDecimal(9),
                reader.GetDecimal(10),
                reader.GetFieldValue<DateOnly>(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.GetInt64(14),
                reader.GetString(15),
                Utc(reader.GetDateTime(16))));
        }

        return rows;
    }

    private Task<IReadOnlyList<OtcMarginCallDto>> ReadCallsAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, string whereClause, (string Name, object Value) parameter, CancellationToken ct, bool forUpdate = false)
        => ReadCallsAsync(connection, transaction, whereClause, [parameter], forUpdate, ct);

    private Task<IReadOnlyList<OtcMarginCallDto>> ReadCallsAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, string whereClause, CancellationToken ct, params (string Name, object Value)[] parameters)
        => ReadCallsAsync(connection, transaction, whereClause, parameters, forUpdate: false, ct);

    private async Task<IReadOnlyList<OtcMarginCallDto>> ReadCallsAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, string whereClause, IReadOnlyList<(string Name, object Value)> parameters, bool forUpdate, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select {CallColumns}
            from {_options.Schema}.otc_margin_calls
            {whereClause}
            order by issued_on, call_id{(forUpdate ? " for update" : string.Empty)};
            """;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);

        var rows = new List<OtcMarginCallDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new OtcMarginCallDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                Enum.Parse<OtcMovementKind>(reader.GetString(4)),
                Enum.Parse<OtcMovementParty>(reader.GetString(5)),
                reader.GetString(6),
                reader.GetDecimal(7),
                reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                Enum.Parse<OtcMarginCallStatus>(reader.GetString(9)),
                reader.GetFieldValue<DateOnly>(10),
                reader.IsDBNull(11) ? null : reader.GetFieldValue<DateOnly>(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetFieldValue<DateOnly>(13),
                reader.IsDBNull(14) ? null : reader.GetDecimal(14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                reader.GetInt64(16),
                reader.GetString(17),
                Utc(reader.GetDateTime(18))));
        }

        return rows;
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("SecurityMasterOptions.ConnectionString is not configured.");
        }

        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }
}
