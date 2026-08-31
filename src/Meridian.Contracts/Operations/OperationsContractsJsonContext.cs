using System.Text.Json.Serialization;

namespace Meridian.Contracts.Operations;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(OperationTerminalState))]
[JsonSerializable(typeof(OperationPostconditionState))]
[JsonSerializable(typeof(OperationIssueSeverity))]
[JsonSerializable(typeof(OperationPostcondition))]
[JsonSerializable(typeof(IReadOnlyList<OperationPostcondition>))]
[JsonSerializable(typeof(OperationEvidenceReference))]
[JsonSerializable(typeof(IReadOnlyList<OperationEvidenceReference>))]
[JsonSerializable(typeof(OperationArtifactReference))]
[JsonSerializable(typeof(IReadOnlyList<OperationArtifactReference>))]
[JsonSerializable(typeof(OperationIssue))]
[JsonSerializable(typeof(IReadOnlyList<OperationIssue>))]
[JsonSerializable(typeof(OperationRecoveryAction))]
[JsonSerializable(typeof(IReadOnlyList<OperationRecoveryAction>))]
[JsonSerializable(typeof(DataProvenance))]
[JsonSerializable(typeof(DataProvenanceBadge))]
[JsonSerializable(typeof(VerifiedOperationOutcome))]
[JsonSerializable(typeof(OperationalCaseStateTransition))]
[JsonSerializable(typeof(OperationalCaseAssignment))]
[JsonSerializable(typeof(OperationalCaseRetry))]
[JsonSerializable(typeof(IReadOnlyList<OperationalCaseRetry>))]
[JsonSerializable(typeof(OperationalCaseException))]
[JsonSerializable(typeof(IReadOnlyList<OperationalCaseException>))]
[JsonSerializable(typeof(OperationalCaseApproval))]
[JsonSerializable(typeof(IReadOnlyList<OperationalCaseApproval>))]
[JsonSerializable(typeof(OperationalCaseRecoveryAttempt))]
[JsonSerializable(typeof(IReadOnlyList<OperationalCaseRecoveryAttempt>))]
[JsonSerializable(typeof(OperationalCaseHistoryAppendRequest))]
[JsonSerializable(typeof(OperationalCaseHistoryRecord))]
[JsonSerializable(typeof(IReadOnlyList<OperationalCaseHistoryRecord>))]
[JsonSerializable(typeof(OperationalCaseHistoryQuery))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(SortedDictionary<string, string>))]
public sealed partial class OperationsContractsJsonContext : JsonSerializerContext;
