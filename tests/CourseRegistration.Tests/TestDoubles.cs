using CourseRegistration.Application.Interfaces;
using CourseRegistration.Application.Models;
using CourseRegistration.Application.Services;
using CourseRegistration.Infrastructure.InMemory;

namespace CourseRegistration.Tests;

/// <summary>Configurable prerequisite checker: eligibility per learner, optional async yield and fault injection.</summary>
public sealed class FakePrerequisiteChecker : IPrerequisiteChecker
{
    private readonly InMemoryPrerequisiteChecker _inner = new();

    public bool YieldBeforeAnswer { get; set; }

    public Guid? ThrowFor { get; set; }

    public int CallCount;

    public void SetEligible(params Guid[] learnerIds)
    {
        foreach (var id in learnerIds)
        {
            _inner.SetEligibility(id, true);
        }
    }

    public void SetIneligible(params Guid[] learnerIds)
    {
        foreach (var id in learnerIds)
        {
            _inner.SetEligibility(id, false);
        }
    }

    public async Task<bool> IsEligibleAsync(Guid offeringId, Guid learnerId, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref CallCount);
        if (YieldBeforeAnswer)
        {
            await Task.Delay(1, cancellationToken);
        }

        if (ThrowFor == learnerId)
        {
            throw new InvalidOperationException("Prerequisite source unavailable.");
        }

        return await _inner.IsEligibleAsync(offeringId, learnerId, cancellationToken);
    }
}

public sealed class FixedTimeProvider : TimeProvider
{
    public static readonly DateTimeOffset Now = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Wires the real service and in-memory store for a single offering.</summary>
public sealed class RegistrationFixture
{
    public RegistrationFixture(int capacity)
    {
        Store = new InMemoryCourseRegistrationStore(new CourseOffering(OfferingId, capacity));
        Service = new CourseRegistrationService(Store, Checker, new FixedTimeProvider());
    }

    public Guid OfferingId { get; } = Guid.NewGuid();

    public FakePrerequisiteChecker Checker { get; } = new();

    public InMemoryCourseRegistrationStore Store { get; }

    public CourseRegistrationService Service { get; }

    public Task<RegistrationResult> EnrollAsync(Guid learnerId) => Service.EnrollAsync(OfferingId, learnerId);

    public Task<RegistrationResult> DropAsync(Guid learnerId) => Service.DropAsync(OfferingId, learnerId);

    public async Task<OfferingSnapshot> SnapshotAsync() =>
        await Store.GetSnapshotAsync(OfferingId) ?? throw new InvalidOperationException("Offering missing.");

    /// <summary>Creates eligible learners and enrolls them in order.</summary>
    public async Task<Guid[]> EnrollEligibleAsync(int count)
    {
        var ids = NewEligibleLearners(count);
        foreach (var id in ids)
        {
            await EnrollAsync(id);
        }

        return ids;
    }

    public Guid[] NewEligibleLearners(int count)
    {
        var ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();
        Checker.SetEligible(ids);
        return ids;
    }
}
