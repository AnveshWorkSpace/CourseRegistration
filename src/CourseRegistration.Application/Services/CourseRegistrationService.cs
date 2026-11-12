using CourseRegistration.Application.Interfaces;
using CourseRegistration.Application.Models;

namespace CourseRegistration.Application.Services;

public sealed class CourseRegistrationService : ICourseRegistrationService
{
    private readonly ICourseRegistrationStore _store;
    private readonly IPrerequisiteChecker _prerequisiteChecker;
    private readonly TimeProvider _timeProvider;

    public CourseRegistrationService(
        ICourseRegistrationStore store,
        IPrerequisiteChecker prerequisiteChecker,
        TimeProvider timeProvider)
    {
        _store = store;
        _prerequisiteChecker = prerequisiteChecker;
        _timeProvider = timeProvider;
    }

    public Task<RegistrationResult> EnrollAsync(
        Guid offeringId,
        Guid learnerId,
        CancellationToken cancellationToken = default)
    {
        if (ValidateIds(offeringId, learnerId) is { } invalid)
        {
            return Task.FromResult(invalid);
        }

        return _store.ExecuteAsync(offeringId, async (offering, ct) =>
        {
            if (offering is null)
            {
                return OfferingNotFound(offeringId);
            }

            if (offering.IsEnrolled(learnerId))
            {
                return new RegistrationResult(RegistrationStatus.AlreadyEnrolled, "Learner is already enrolled in this offering.");
            }

            if (offering.IsWaitlisted(learnerId))
            {
                return new RegistrationResult(RegistrationStatus.AlreadyWaitlisted, "Learner is already on the waitlist for this offering.");
            }

            if (!await _prerequisiteChecker.IsEligibleAsync(offering.Id, learnerId, ct))
            {
                return new RegistrationResult(RegistrationStatus.PrerequisiteNotMet, "Learner does not meet the prerequisite for this offering.");
            }

            ct.ThrowIfCancellationRequested();

            if (offering.HasAvailableSeat)
            {
                offering.Enroll(learnerId);
                return new RegistrationResult(RegistrationStatus.Enrolled, "Learner enrolled.");
            }

            var entry = offering.AddToWaitlist(learnerId, _timeProvider.GetUtcNow());
            return new RegistrationResult(
                RegistrationStatus.Waitlisted,
                "Offering is full; learner added to the waitlist.",
                WaitlistSequence: entry.Sequence);
        }, cancellationToken);
    }

    public Task<RegistrationResult> DropAsync(
        Guid offeringId,
        Guid learnerId,
        CancellationToken cancellationToken = default)
    {
        if (ValidateIds(offeringId, learnerId) is { } invalid)
        {
            return Task.FromResult(invalid);
        }

        return _store.ExecuteAsync(offeringId, async (offering, ct) =>
        {
            if (offering is null)
            {
                return OfferingNotFound(offeringId);
            }

            if (!offering.IsEnrolled(learnerId))
            {
                return new RegistrationResult(RegistrationStatus.EnrollmentNotFound, "Learner has no active enrollment in this offering.");
            }

            // Find the promotion candidate before mutating anything, so a failing or cancelled
            // prerequisite check leaves the offering untouched (no half-applied drop).
            var candidate = await FindEarliestEligibleWaiterAsync(offering, ct);
            ct.ThrowIfCancellationRequested();

            offering.RemoveEnrollment(learnerId);
            if (candidate is null)
            {
                return new RegistrationResult(RegistrationStatus.Dropped, "Enrollment dropped; no eligible learner was waiting.");
            }

            offering.PromoteFromWaitlist(candidate.LearnerId);
            return new RegistrationResult(
                RegistrationStatus.Dropped,
                "Enrollment dropped; earliest eligible waitlisted learner promoted.",
                PromotedLearnerId: candidate.LearnerId);
        }, cancellationToken);
    }

    /// <summary>
    /// Skip policy: ineligible waiters are passed over but keep their position.
    /// </summary>
    private async Task<WaitlistEntry?> FindEarliestEligibleWaiterAsync(CourseOffering offering, CancellationToken ct)
    {
        foreach (var entry in offering.Waitlist.OrderBy(e => e.Sequence).ToArray())
        {
            if (await _prerequisiteChecker.IsEligibleAsync(offering.Id, entry.LearnerId, ct))
            {
                return entry;
            }
        }

        return null;
    }

    private static RegistrationResult? ValidateIds(Guid offeringId, Guid learnerId)
    {
        if (offeringId == Guid.Empty)
        {
            return new RegistrationResult(RegistrationStatus.InvalidRequest, "Offering id is required.");
        }

        if (learnerId == Guid.Empty)
        {
            return new RegistrationResult(RegistrationStatus.InvalidRequest, "Learner id is required.");
        }

        return null;
    }

    private static RegistrationResult OfferingNotFound(Guid offeringId) =>
        new(RegistrationStatus.OfferingNotFound, $"Offering {offeringId} was not found.");
}
