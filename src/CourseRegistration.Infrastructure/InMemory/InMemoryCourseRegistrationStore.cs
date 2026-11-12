using CourseRegistration.Application.Interfaces;
using CourseRegistration.Application.Models;

namespace CourseRegistration.Infrastructure.InMemory;

/// <summary>
/// Single-offering, single-process store. One <see cref="SemaphoreSlim"/> serializes every
/// state transition so duplicate, capacity, queue and promotion checks are atomic.
/// State is lost on restart and the lock does not coordinate multiple instances.
/// </summary>
public sealed class InMemoryCourseRegistrationStore : ICourseRegistrationStore, IDisposable
{
    private readonly CourseOffering _offering;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public InMemoryCourseRegistrationStore(CourseOffering offering)
    {
        _offering = offering ?? throw new ArgumentNullException(nameof(offering));
    }

    public async Task<TResult> ExecuteAsync<TResult>(
        Guid offeringId,
        Func<CourseOffering?, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await operation(offeringId == _offering.Id ? _offering : null, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<OfferingSnapshot?> GetSnapshotAsync(Guid offeringId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(offeringId, (offering, _) => Task.FromResult(offering?.ToSnapshot()), cancellationToken);

    public void Dispose() => _gate.Dispose();
}
