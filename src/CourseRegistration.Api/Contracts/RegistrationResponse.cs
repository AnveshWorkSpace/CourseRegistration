using System.Text.Json.Serialization;

namespace CourseRegistration.Api.Contracts;

/// <param name="Code">Stable machine-readable result code, e.g. <c>Enrolled</c> or <c>AlreadyWaitlisted</c>.</param>
public sealed record RegistrationResponse(
    string Code,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? PromotedLearnerId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? WaitlistSequence = null);
