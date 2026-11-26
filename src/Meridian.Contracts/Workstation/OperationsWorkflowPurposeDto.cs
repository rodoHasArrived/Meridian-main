using System.Text.Json.Serialization;

namespace Meridian.Contracts.Workstation;

[JsonConverter(typeof(JsonStringEnumConverter<OperationsWorkflowPurposeDto>))]
public enum OperationsWorkflowPurposeDto
{
    Close = 0,
    SecurityMasterRevision = 1
}
