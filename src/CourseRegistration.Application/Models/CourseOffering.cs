namespace CourseRegistration.Application.Models;

/// <summary>
/// State of a single course offering. Mutators guard the structural invariants
/// (capacity, no duplicates, enrolled XOR waitlisted); business rules such as
/// prerequisites and promotion order live in <c>CourseRegistrationService</c>.
/// Not thread-safe: callers must hold the store's lock.
/// </summary>
public sealed class CourseOffering
{
    private readonly HashSet<Guid> _enrolledLearnerIds = new();
    private readonly List<WaitlistEntry> _waitlist = new();

    public CourseOffering(Guid id, int capacity)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Offering id must not be empty.", nameof(id));
        }

        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be a positive integer.");
        }

        Id = id;
        Capacity = capacity;
    }

    public Guid Id { get; }

    public int Capacity { get; }

    public IReadOnlyCollection<Guid> EnrolledLearnerIds => _enrolledLearnerIds;

    /// <summary>Waitlist entries in ascending <see cref="WaitlistEntry.Sequence"/> order.</summary>
    public IReadOnlyList<WaitlistEntry> Waitlist => _waitlist;

    public long NextWaitlistSequence { get; private set; } = 1;

    public int EnrolledCount => _enrolledLearnerIds.Count;

    public bool HasAvailableSeat => _enrolledLearnerIds.Count < Capacity;

    public bool IsEnrolled(Guid learnerId) => _enrolledLearnerIds.Contains(learnerId);

    public bool IsWaitlisted(Guid learnerId) => _waitlist.Exists(e => e.LearnerId == learnerId);

    public void Enroll(Guid learnerId)
    {
        EnsureNotRegistered(learnerId);
        if (!HasAvailableSeat)
        {
            throw new InvalidOperationException("Offering is at capacity.");
        }

        _enrolledLearnerIds.Add(learnerId);
    }

    public WaitlistEntry AddToWaitlist(Guid learnerId, DateTimeOffset createdAtUtc)
    {
        EnsureNotRegistered(learnerId);
        var entry = new WaitlistEntry(learnerId, NextWaitlistSequence, createdAtUtc);
        _waitlist.Add(entry);
        NextWaitlistSequence++;
        return entry;
    }

    public bool RemoveEnrollment(Guid learnerId) => _enrolledLearnerIds.Remove(learnerId);

    public void PromoteFromWaitlist(Guid learnerId)
    {
        var index = _waitlist.FindIndex(e => e.LearnerId == learnerId);
        if (index < 0)
        {
            throw new InvalidOperationException("Learner is not on the waitlist.");
        }

        if (!HasAvailableSeat)
        {
            throw new InvalidOperationException("Offering is at capacity.");
        }

        _waitlist.RemoveAt(index);
        _enrolledLearnerIds.Add(learnerId);
    }

    public OfferingSnapshot ToSnapshot() => new(
        Id,
        Capacity,
        _enrolledLearnerIds.ToArray(),
        _waitlist.ToArray(),
        NextWaitlistSequence);

    private void EnsureNotRegistered(Guid learnerId)
    {
        if (IsEnrolled(learnerId) || IsWaitlisted(learnerId))
        {
            throw new InvalidOperationException("Learner is already enrolled or waitlisted.");
        }
    }
}
