using Meridian.Storage.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

public sealed partial class SecurityMasterWorkbenchCommandService
{
    private async Task<IDisposable> AcquireFieldEditGateAsync(Guid securityId, CancellationToken ct)
        => _overrides is ISecurityMasterMutationParticipant
            ? NoLocalGate.Instance
            : await FieldEditGates.AcquireAsync(securityId, ct).ConfigureAwait(false);

    private sealed class NoLocalGate : IDisposable
    {
        public static readonly NoLocalGate Instance = new();
        public void Dispose() { }
    }

    private Task<T> ExecuteMutationAsync<T>(Guid securityId, Func<Task<T>> operation, CancellationToken ct)
    {
        if (_overrides is ISecurityMasterMutationParticipant overlay)
        {
            if (_revisions is not ISecurityMasterMutationParticipant revisions
                || !PostgresSecurityMasterMutation.SameDatabase(overlay.MutationOptions, revisions.MutationOptions))
            {
                throw new InvalidOperationException(
                    "Durable Security Master edits require overlay and revision stores in the same database and schema.");
            }

            return PostgresSecurityMasterMutation.ExecuteAsync(overlay.MutationOptions, securityId, operation, ct);
        }

        if (_revisions is ISecurityMasterMutationParticipant)
        {
            throw new InvalidOperationException("A durable revision store requires a durable overlay mutation participant.");
        }

        // Test/offline stores retain the existing local gate. This path is never used by a
        // partially configured PostgreSQL composition: mismatched participants fail above.
        return operation();
    }

    private Task ExecuteMutationAsync(Guid securityId, Func<Task> operation, CancellationToken ct)
        => ExecuteMutationAsync(securityId, async () =>
        {
            await operation().ConfigureAwait(false);
            return true;
        }, ct);
}
