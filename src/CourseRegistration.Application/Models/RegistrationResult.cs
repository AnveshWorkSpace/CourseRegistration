namespace CourseRegistration.Application.Models;

public sealed record RegistrationResult(
    RegistrationStatus Status,
    string Message,
    Guid? PromotedLearnerId = null,
    long? WaitlistSequence = null)
{
    public bool IsSuccess => Status is RegistrationStatus.Enrolled
        or RegistrationStatus.Waitlisted
        or RegistrationStatus.Dropped;
}
