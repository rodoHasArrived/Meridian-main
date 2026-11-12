using FluentAssertions;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.Logging;

namespace Meridian.Tests.Application;

public sealed partial class OperationsContinuityWorkflowServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FileRepository_CorruptSnapshotDiagnostic_DoesNotLogWorkflowIdentityOrPayload(bool list)
    {
        var directory = Path.Combine(Path.GetTempPath(), "meridian-tests", "private-workflow-root", Guid.NewGuid().ToString("N"));
        var logger = new RecordingLogger<FileOperationsContinuityRepository>();
        var repository = new FileOperationsContinuityRepository(directory, new OperationsStatusDerivationService(), logger);
        var workflowId = Guid.NewGuid();
        const string payloadField = "synthetic-private-account";
        var path = Path.Combine(directory, "operations-continuity", "workflows", $"{workflowId:N}.json");
        try
        {
            await File.WriteAllTextAsync(path, "{\"" + payloadField + "\": [}");
            Func<Task> read = list
                ? async () => { await repository.ListAsync(); }
            : async () => { await repository.GetAsync(workflowId); };

            await read.Should().ThrowAsync<InvalidDataException>();

            var entry = logger.Entries.Should().ContainSingle().Which;
            entry.LogLevel.Should().Be(LogLevel.Error);
            entry.Message.Should().Contain("workflow snapshot is corrupt").And.Contain("Json");
            entry.Message.Should().NotContain(directory).And.NotContain(workflowId.ToString("N"))
                .And.NotContain(payloadField);
            entry.Exception.Should().BeNull("JSON exceptions can contain payload field names and file identities");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
