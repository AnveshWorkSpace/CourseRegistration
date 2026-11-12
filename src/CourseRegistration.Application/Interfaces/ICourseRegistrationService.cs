using CourseRegistration.Application.Models;

namespace CourseRegistration.Application.Interfaces;

public interface ICourseRegistrationService
{
    Task<RegistrationResult> EnrollAsync(
        Guid offeringId,
        Guid learnerId,
        CancellationToken cancellationToken = default);

    Task<RegistrationResult> DropAsync(
        Guid offeringId,
        Guid learnerId,
        CancellationToken cancellationToken = default);
}
