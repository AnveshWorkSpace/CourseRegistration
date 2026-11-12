namespace CourseRegistration.Application.Interfaces;

public interface IPrerequisiteChecker
{
    Task<bool> IsEligibleAsync(
        Guid offeringId,
        Guid learnerId,
        CancellationToken cancellationToken = default);
}
