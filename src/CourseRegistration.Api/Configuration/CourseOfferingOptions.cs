namespace CourseRegistration.Api.Configuration;

public sealed class CourseOfferingOptions
{
    public const string SectionName = "CourseOffering";

    public Guid Id { get; set; }

    public int Capacity { get; set; }

    /// <summary>Learners who satisfy the offering's prerequisite (prototype prerequisite source).</summary>
    public List<Guid> EligibleLearnerIds { get; set; } = new();
}
