using CourseRegistration.Application.Models;

namespace CourseRegistration.Tests;

public class CourseOfferingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_rejects_non_positive_capacity(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CourseOffering(Guid.NewGuid(), capacity));
    }

    [Fact]
    public void Constructor_rejects_empty_id()
    {
        Assert.Throws<ArgumentException>(() => new CourseOffering(Guid.Empty, 1));
    }

    [Fact]
    public void Enroll_beyond_capacity_throws_and_does_not_mutate()
    {
        var offering = new CourseOffering(Guid.NewGuid(), 1);
        offering.Enroll(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => offering.Enroll(Guid.NewGuid()));
        Assert.Equal(1, offering.EnrolledCount);
    }

    [Fact]
    public void Learner_cannot_be_enrolled_and_waitlisted()
    {
        var offering = new CourseOffering(Guid.NewGuid(), 1);
        var learner = Guid.NewGuid();
        offering.Enroll(learner);

        Assert.Throws<InvalidOperationException>(() => offering.AddToWaitlist(learner, DateTimeOffset.UtcNow));
        Assert.Empty(offering.Waitlist);
    }

    [Fact]
    public void Waitlist_sequences_are_monotonic()
    {
        var offering = new CourseOffering(Guid.NewGuid(), 1);
        var first = offering.AddToWaitlist(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var second = offering.AddToWaitlist(Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(1, first.Sequence);
        Assert.Equal(2, second.Sequence);
        Assert.Equal(3, offering.NextWaitlistSequence);
    }
}
