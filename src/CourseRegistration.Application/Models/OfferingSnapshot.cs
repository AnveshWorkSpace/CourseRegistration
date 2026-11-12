namespace CourseRegistration.Application.Models;

public sealed record OfferingSnapshot(
    Guid Id,
    int Capacity,
    IReadOnlyList<Guid> EnrolledLearnerIds,
    IReadOnlyList<WaitlistEntry> Waitlist,
    long NextWaitlistSequence)
{
    public int EnrolledCount => EnrolledLearnerIds.Count;
}
