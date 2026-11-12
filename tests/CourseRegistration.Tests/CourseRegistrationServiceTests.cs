using CourseRegistration.Application.Models;

namespace CourseRegistration.Tests;

public class CourseRegistrationServiceTests
{
    [Fact]
    public async Task Enrollment_with_available_capacity_enrolls_learner()
    {
        var fx = new RegistrationFixture(capacity: 2);
        var learner = fx.NewEligibleLearners(1)[0];

        var result = await fx.EnrollAsync(learner);

        Assert.Equal(RegistrationStatus.Enrolled, result.Status);
        var state = await fx.SnapshotAsync();
        Assert.Equal(new[] { learner }, state.EnrolledLearnerIds);
        Assert.Empty(state.Waitlist);
    }

    [Fact]
    public async Task Enrollment_at_full_capacity_adds_learner_to_waitlist_once()
    {
        var fx = new RegistrationFixture(capacity: 1);
        await fx.EnrollEligibleAsync(1);
        var waiter = fx.NewEligibleLearners(1)[0];

        var result = await fx.EnrollAsync(waiter);

        Assert.Equal(RegistrationStatus.Waitlisted, result.Status);
        Assert.Equal(1, result.WaitlistSequence);
        var state = await fx.SnapshotAsync();
        Assert.Equal(1, state.EnrolledCount);
        var entry = Assert.Single(state.Waitlist);
        Assert.Equal(waiter, entry.LearnerId);
        Assert.Equal(1, entry.Sequence);
        Assert.Equal(FixedTimeProvider.Now, entry.CreatedAtUtc);
        Assert.DoesNotContain(waiter, state.EnrolledLearnerIds);
    }

    [Fact]
    public async Task Duplicate_enrollment_does_not_create_duplicate_state()
    {
        var fx = new RegistrationFixture(capacity: 2);
        var learner = fx.NewEligibleLearners(1)[0];

        var first = await fx.EnrollAsync(learner);
        var second = await fx.EnrollAsync(learner);

        Assert.Equal(RegistrationStatus.Enrolled, first.Status);
        Assert.Equal(RegistrationStatus.AlreadyEnrolled, second.Status);
        var state = await fx.SnapshotAsync();
        Assert.Single(state.EnrolledLearnerIds);
        Assert.Empty(state.Waitlist);
    }

    [Fact]
    public async Task Already_enrolled_learner_requesting_again_gets_AlreadyEnrolled_and_state_is_unchanged()
    {
        var fx = new RegistrationFixture(capacity: 1);
        var learner = (await fx.EnrollEligibleAsync(1))[0];
        var before = await fx.SnapshotAsync();

        var result = await fx.EnrollAsync(learner);

        Assert.Equal(RegistrationStatus.AlreadyEnrolled, result.Status);
        AssertSameState(before, await fx.SnapshotAsync());
    }

    [Fact]
    public async Task Already_waitlisted_learner_requesting_again_gets_AlreadyWaitlisted_and_state_is_unchanged()
    {
        var fx = new RegistrationFixture(capacity: 1);
        await fx.EnrollEligibleAsync(1);
        var waiter = fx.NewEligibleLearners(1)[0];
        await fx.EnrollAsync(waiter);
        var before = await fx.SnapshotAsync();

        var result = await fx.EnrollAsync(waiter);

        Assert.Equal(RegistrationStatus.AlreadyWaitlisted, result.Status);
        AssertSameState(before, await fx.SnapshotAsync());
    }

    [Fact]
    public async Task Duplicate_checks_take_precedence_over_prerequisite_check()
    {
        var fx = new RegistrationFixture(capacity: 1);
        var learner = (await fx.EnrollEligibleAsync(1))[0];
        fx.Checker.SetIneligible(learner);

        var result = await fx.EnrollAsync(learner);

        Assert.Equal(RegistrationStatus.AlreadyEnrolled, result.Status);
    }

    [Fact]
    public async Task Prerequisite_failure_on_enrollment_neither_enrolls_nor_waitlists()
    {
        var fx = new RegistrationFixture(capacity: 2);
        var learner = Guid.NewGuid();
        fx.Checker.SetIneligible(learner);

        var result = await fx.EnrollAsync(learner);

        Assert.Equal(RegistrationStatus.PrerequisiteNotMet, result.Status);
        var state = await fx.SnapshotAsync();
        Assert.Empty(state.EnrolledLearnerIds);
        Assert.Empty(state.Waitlist);
    }

    [Fact]
    public async Task Prerequisite_failure_when_full_does_not_join_waitlist()
    {
        var fx = new RegistrationFixture(capacity: 1);
        await fx.EnrollEligibleAsync(1);
        var before = await fx.SnapshotAsync();

        var result = await fx.EnrollAsync(Guid.NewGuid());

        Assert.Equal(RegistrationStatus.PrerequisiteNotMet, result.Status);
        AssertSameState(before, await fx.SnapshotAsync());
    }

    [Fact]
    public async Task Drop_active_learner_with_no_waitlist_frees_one_seat()
    {
        var fx = new RegistrationFixture(capacity: 2);
        var learners = await fx.EnrollEligibleAsync(2);

        var result = await fx.DropAsync(learners[0]);

        Assert.Equal(RegistrationStatus.Dropped, result.Status);
        Assert.Null(result.PromotedLearnerId);
        var state = await fx.SnapshotAsync();
        Assert.Equal(new[] { learners[1] }, state.EnrolledLearnerIds);
        Assert.Equal(1, state.Capacity - state.EnrolledCount);
    }

    [Fact]
    public async Task Drop_promotes_earliest_waitlisted_learner_FIFO()
    {
        var fx = new RegistrationFixture(capacity: 1);
        var enrolled = (await fx.EnrollEligibleAsync(1))[0];
        var waiters = fx.NewEligibleLearners(3);
        foreach (var waiter in waiters)
        {
            await fx.EnrollAsync(waiter);
        }

        var result = await fx.DropAsync(enrolled);

        Assert.Equal(RegistrationStatus.Dropped, result.Status);
        Assert.Equal(waiters[0], result.PromotedLearnerId);
        var state = await fx.SnapshotAsync();
        Assert.Equal(new[] { waiters[0] }, state.EnrolledLearnerIds);
        Assert.Equal(new[] { waiters[1], waiters[2] }, state.Waitlist.Select(e => e.LearnerId));
        Assert.Equal(new long[] { 2, 3 }, state.Waitlist.Select(e => e.Sequence));
    }

    [Fact]
    public async Task Successive_drops_promote_waiters_in_FIFO_order()
    {
        var fx = new RegistrationFixture(capacity: 2);
        var enrolled = await fx.EnrollEligibleAsync(2);
        var waiters = fx.NewEligibleLearners(2);
        foreach (var waiter in waiters)
        {
            await fx.EnrollAsync(waiter);
        }

        var first = await fx.DropAsync(enrolled[1]);
        var second = await fx.DropAsync(enrolled[0]);

        Assert.Equal(waiters[0], first.PromotedLearnerId);
        Assert.Equal(waiters[1], second.PromotedLearnerId);
        var state = await fx.SnapshotAsync();
        Assert.Equal(waiters.OrderBy(x => x), state.EnrolledLearnerIds.OrderBy(x => x));
        Assert.Empty(state.Waitlist);
    }

    [Fact]
    public async Task Ineligible_first_waiter_is_skipped_and_retains_position()
    {
        var fx = new RegistrationFixture(capacity: 1);
        var enrolled = (await fx.EnrollEligibleAsync(1))[0];
        var waiters = fx.NewEligibleLearners(2);
        await fx.EnrollAsync(waiters[0]);
        await fx.EnrollAsync(waiters[1]);
        fx.Checker.SetIneligible(waiters[0]);

        var result = await fx.DropAsync(enrolled);

        Assert.Equal(waiters[1], result.PromotedLearnerId);
        var state = await fx.SnapshotAsync();
        Assert.Equal(new[] { waiters[1] }, state.EnrolledLearnerIds);
        var remaining = Assert.Single(state.Waitlist);
        Assert.Equal(waiters[0], remaining.LearnerId);
        Assert.Equal(1, remaining.Sequence);
    }

    [Fact]
    public async Task Eligibility_is_rechecked_at_promotion_time()
    {
        var fx = new RegistrationFixture(capacity: 1);
        var enrolled = (await fx.EnrollEligibleAsync(1))[0];
        var waiter = fx.NewEligibleLearners(1)[0];
        Assert.Equal(RegistrationStatus.Waitlisted, (await fx.EnrollAsync(waiter)).Status);

        fx.Checker.SetIneligible(waiter);
        var result = await fx.DropAsync(enrolled);

        Assert.Equal(RegistrationStatus.Dropped, result.Status);
        Assert.Null(result.PromotedLearnerId);
        var state = await fx.SnapshotAsync();
        Assert.Empty(state.EnrolledLearnerIds);
        Assert.Equal(waiter, Assert.Single(state.Waitlist).LearnerId);
    }

    [Fact]
    public async Task Skipped_waiter_that_becomes_eligible_is_promoted_on_a_later_drop()
    {
        var fx = new RegistrationFixture(capacity: 2);
        var enrolled = await fx.EnrollEligibleAsync(2);
        var waiters = fx.NewEligibleLearners(2);
        await fx.EnrollAsync(waiters[0]);
        await fx.EnrollAsync(waiters[1]);

        fx.Checker.SetIneligible(waiters[0]);
        var first = await fx.DropAsync(enrolled[0]);
        fx.Checker.SetEligible(waiters[0]);
        var second = await fx.DropAsync(enrolled[1]);

        Assert.Equal(waiters[1], first.PromotedLearnerId);
        Assert.Equal(waiters[0], second.PromotedLearnerId);
        Assert.Empty((await fx.SnapshotAsync()).Waitlist);
    }

    [Fact]
    public async Task Repeated_drop_returns_EnrollmentNotFound_and_state_is_unchanged()
    {
        var fx = new RegistrationFixture(capacity: 1);
        var enrolled = (await fx.EnrollEligibleAsync(1))[0];
        var waiter = Guid.NewGuid();
        fx.Checker.SetEligible(waiter);
        await fx.EnrollAsync(waiter);
        await fx.DropAsync(enrolled);
        var before = await fx.SnapshotAsync();

        var result = await fx.DropAsync(enrolled);

        Assert.Equal(RegistrationStatus.EnrollmentNotFound, result.Status);
        Assert.Null(result.PromotedLearnerId);
        AssertSameState(before, await fx.SnapshotAsync());
    }

    [Fact]
    public async Task Dropping_a_waitlisted_learner_returns_EnrollmentNotFound_and_keeps_them_waiting()
    {
        var fx = new RegistrationFixture(capacity: 1);
        await fx.EnrollEligibleAsync(1);
        var waiter = fx.NewEligibleLearners(1)[0];
        await fx.EnrollAsync(waiter);
        var before = await fx.SnapshotAsync();

        var result = await fx.DropAsync(waiter);

        Assert.Equal(RegistrationStatus.EnrollmentNotFound, result.Status);
        AssertSameState(before, await fx.SnapshotAsync());
    }

    [Fact]
    public async Task Unknown_offering_returns_OfferingNotFound()
    {
        var fx = new RegistrationFixture(capacity: 1);
        var learner = fx.NewEligibleLearners(1)[0];

        var enroll = await fx.Service.EnrollAsync(Guid.NewGuid(), learner);
        var drop = await fx.Service.DropAsync(Guid.NewGuid(), learner);

        Assert.Equal(RegistrationStatus.OfferingNotFound, enroll.Status);
        Assert.Equal(RegistrationStatus.OfferingNotFound, drop.Status);
        Assert.Empty((await fx.SnapshotAsync()).EnrolledLearnerIds);
    }

    [Fact]
    public async Task Empty_identifiers_return_InvalidRequest_without_consulting_prerequisites()
    {
        var fx = new RegistrationFixture(capacity: 1);

        var results = new[]
        {
            await fx.Service.EnrollAsync(Guid.Empty, Guid.NewGuid()),
            await fx.Service.EnrollAsync(fx.OfferingId, Guid.Empty),
            await fx.Service.DropAsync(Guid.Empty, Guid.NewGuid()),
            await fx.Service.DropAsync(fx.OfferingId, Guid.Empty),
        };

        Assert.All(results, r => Assert.Equal(RegistrationStatus.InvalidRequest, r.Status));
        Assert.Equal(0, fx.Checker.CallCount);
    }

    [Fact]
    public async Task Prerequisite_failure_during_promotion_does_not_partially_apply_drop()
    {
        var fx = new RegistrationFixture(capacity: 1);
        var enrolled = (await fx.EnrollEligibleAsync(1))[0];
        var waiter = fx.NewEligibleLearners(1)[0];
        await fx.EnrollAsync(waiter);
        var before = await fx.SnapshotAsync();
        fx.Checker.ThrowFor = waiter;

        await Assert.ThrowsAsync<InvalidOperationException>(() => fx.DropAsync(enrolled));

        AssertSameState(before, await fx.SnapshotAsync());
    }

    [Fact]
    public async Task Capacity_invariant_holds_across_a_sequence_of_operations()
    {
        const int capacity = 3;
        var fx = new RegistrationFixture(capacity);
        var learners = fx.NewEligibleLearners(10);
        var random = new Random(12345);

        for (var i = 0; i < 500; i++)
        {
            var learner = learners[random.Next(learners.Length)];
            if (random.Next(5) == 0)
            {
                fx.Checker.SetIneligible(learner);
            }
            else if (random.Next(5) == 0)
            {
                fx.Checker.SetEligible(learner);
            }

            _ = random.Next(2) == 0 ? await fx.EnrollAsync(learner) : await fx.DropAsync(learner);

            AssertInvariants(await fx.SnapshotAsync());
        }
    }

    [Fact]
    public async Task Concurrent_enrollment_does_not_exceed_capacity_and_waitlists_the_rest()
    {
        const int capacity = 5;
        const int requests = 50;
        var fx = new RegistrationFixture(capacity);
        fx.Checker.YieldBeforeAnswer = true;
        var learners = fx.NewEligibleLearners(requests);

        var results = await Task.WhenAll(learners.Select(l => Task.Run(() => fx.EnrollAsync(l))));

        Assert.Equal(capacity, results.Count(r => r.Status == RegistrationStatus.Enrolled));
        Assert.Equal(requests - capacity, results.Count(r => r.Status == RegistrationStatus.Waitlisted));
        var state = await fx.SnapshotAsync();
        Assert.Equal(capacity, state.EnrolledCount);
        Assert.Equal(requests - capacity, state.Waitlist.Count);
        Assert.Equal(Enumerable.Range(1, requests - capacity).Select(i => (long)i), state.Waitlist.Select(e => e.Sequence));
        AssertInvariants(state);
    }

    [Fact]
    public async Task Concurrent_duplicate_requests_for_same_learner_create_one_record()
    {
        var fx = new RegistrationFixture(capacity: 2);
        fx.Checker.YieldBeforeAnswer = true;
        var learner = fx.NewEligibleLearners(1)[0];

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => fx.EnrollAsync(learner))));

        Assert.Single(results, r => r.Status == RegistrationStatus.Enrolled);
        Assert.All(results.Where(r => r.Status != RegistrationStatus.Enrolled),
            r => Assert.Equal(RegistrationStatus.AlreadyEnrolled, r.Status));
        var state = await fx.SnapshotAsync();
        Assert.Single(state.EnrolledLearnerIds);
        Assert.Empty(state.Waitlist);
    }

    [Fact]
    public async Task Concurrent_drops_and_enrollments_preserve_invariants()
    {
        const int capacity = 4;
        var fx = new RegistrationFixture(capacity);
        fx.Checker.YieldBeforeAnswer = true;
        var initial = await fx.EnrollEligibleAsync(capacity);
        var newcomers = fx.NewEligibleLearners(20);

        var operations = initial.Select(l => Task.Run(() => fx.DropAsync(l)))
            .Concat(newcomers.Select(l => Task.Run(() => fx.EnrollAsync(l))));
        var results = await Task.WhenAll(operations);

        Assert.Equal(capacity, results.Count(r => r.Status == RegistrationStatus.Dropped));
        var state = await fx.SnapshotAsync();
        AssertInvariants(state);
        Assert.Equal(capacity, state.EnrolledCount);
        Assert.Equal(newcomers.Length, state.EnrolledCount + state.Waitlist.Count);
    }

    [Fact]
    public async Task Concurrent_drops_each_promote_a_distinct_waiter_in_FIFO_order()
    {
        const int capacity = 5;
        var fx = new RegistrationFixture(capacity);
        var enrolled = await fx.EnrollEligibleAsync(capacity);
        var waiters = fx.NewEligibleLearners(8);
        foreach (var waiter in waiters)
        {
            await fx.EnrollAsync(waiter);
        }

        fx.Checker.YieldBeforeAnswer = true;
        var results = await Task.WhenAll(enrolled.Select(l => Task.Run(() => fx.DropAsync(l))));

        var promoted = results.Select(r => r.PromotedLearnerId!.Value).ToArray();
        Assert.Equal(capacity, promoted.Distinct().Count());
        Assert.Equal(waiters.Take(capacity).OrderBy(x => x), promoted.OrderBy(x => x));
        var state = await fx.SnapshotAsync();
        AssertInvariants(state);
        Assert.Equal(waiters.Skip(capacity), state.Waitlist.Select(e => e.LearnerId));
    }

    private static void AssertInvariants(OfferingSnapshot state)
    {
        Assert.InRange(state.EnrolledCount, 0, state.Capacity);
        Assert.Equal(state.EnrolledLearnerIds.Count, state.EnrolledLearnerIds.Distinct().Count());
        Assert.Equal(state.Waitlist.Count, state.Waitlist.Select(e => e.LearnerId).Distinct().Count());
        Assert.Empty(state.EnrolledLearnerIds.Intersect(state.Waitlist.Select(e => e.LearnerId)));
        Assert.Equal(state.Waitlist.OrderBy(e => e.Sequence).Select(e => e.Sequence), state.Waitlist.Select(e => e.Sequence));
        Assert.All(state.Waitlist, e => Assert.True(e.Sequence < state.NextWaitlistSequence));
    }

    private static void AssertSameState(OfferingSnapshot expected, OfferingSnapshot actual)
    {
        Assert.Equal(expected.EnrolledLearnerIds.OrderBy(x => x), actual.EnrolledLearnerIds.OrderBy(x => x));
        Assert.Equal(expected.Waitlist, actual.Waitlist);
        Assert.Equal(expected.NextWaitlistSequence, actual.NextWaitlistSequence);
    }
}
