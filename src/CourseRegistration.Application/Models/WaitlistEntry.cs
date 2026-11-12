namespace CourseRegistration.Application.Models;

public sealed record WaitlistEntry(Guid LearnerId, long Sequence, DateTimeOffset CreatedAtUtc);
