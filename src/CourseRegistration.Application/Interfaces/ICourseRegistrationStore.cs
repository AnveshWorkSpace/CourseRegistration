using CourseRegistration.Application.Models;

namespace CourseRegistration.Application.Interfaces;

public interface ICourseRegistrationStore
{
    /// <summary>
    /// Runs <paramref name="operation"/> as one atomic read-check-mutate unit against the
    /// offering's state. The offering is <c>null</c> when it does not exist. No other
    /// operation on the same offering may interleave with it.
    /// </summary>
    Task<TResult> ExecuteAsync<TResult>(
        Guid offeringId,
        Func<CourseOffering?, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);

    Task<OfferingSnapshot?> GetSnapshotAsync(
        Guid offeringId,
        CancellationToken cancellationToken = default);
}
