using System.Collections.Concurrent;
using CourseRegistration.Application.Interfaces;

namespace CourseRegistration.Infrastructure.InMemory;

/// <summary>
/// Prototype prerequisite source: a learner is eligible when marked as having satisfied
/// the offering's prerequisite. Eligibility can change at runtime.
/// </summary>
public sealed class InMemoryPrerequisiteChecker : IPrerequisiteChecker
{
    private readonly ConcurrentDictionary<Guid, bool> _eligibility = new();

    public InMemoryPrerequisiteChecker(IEnumerable<Guid>? eligibleLearnerIds = null)
    {
        foreach (var learnerId in eligibleLearnerIds ?? Enumerable.Empty<Guid>())
        {
            _eligibility[learnerId] = true;
        }
    }

    public void SetEligibility(Guid learnerId, bool isEligible) => _eligibility[learnerId] = isEligible;

    public Task<bool> IsEligibleAsync(Guid offeringId, Guid learnerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_eligibility.TryGetValue(learnerId, out var eligible) && eligible);
}
