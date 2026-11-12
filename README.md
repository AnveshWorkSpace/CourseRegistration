# Course Waitlist (.NET 8)

Implements the *Course Waitlist — Design Specification*: enrollment, dropping and FIFO waitlist
promotion for a single course offering. No starter project was supplied, so the spec's fallback is
used: a minimal ASP.NET Core Web API with an in-memory store, and xUnit tests.

## Layout

```text
src/
  CourseRegistration.Api/             HTTP transport only (validation of request shape, outcome -> HTTP mapping)
    Controllers/EnrollmentsController.cs
    Contracts/EnrollRequest.cs, RegistrationResponse.cs
    Configuration/CourseOfferingOptions.cs
  CourseRegistration.Application/     Business rules
    Interfaces/ICourseRegistrationService.cs, IPrerequisiteChecker.cs, ICourseRegistrationStore.cs
    Services/CourseRegistrationService.cs
    Models/CourseOffering.cs, WaitlistEntry.cs, RegistrationResult.cs, RegistrationStatus.cs, OfferingSnapshot.cs
  CourseRegistration.Infrastructure/
    InMemory/InMemoryCourseRegistrationStore.cs, InMemoryPrerequisiteChecker.cs
tests/
  CourseRegistration.Tests/
    CourseRegistrationServiceTests.cs   service rules, invariants, concurrency
    CourseOfferingTests.cs              model guards (capacity, duplicates, sequences)
    EnrollmentsApiTests.cs              request/response behaviour via WebApplicationFactory
```

## Policies (spec section 3, applied as written)

| Topic | Behaviour |
|---|---|
| Capacity | Positive integer, validated when the offering is created and at startup (`ValidateOnStart`). |
| Prerequisite | Comes from `IPrerequisiteChecker`, never from the request. The prototype checker is a set of eligible learner IDs (`CourseOffering:EligibleLearnerIds` in `appsettings.json`) and can change at runtime. |
| Full offering | An eligible learner is added to the waitlist. An ineligible learner gets `PrerequisiteNotMet` and is **not** waitlisted. |
| Check order on enroll | empty IDs -> offering exists -> already enrolled -> already waitlisted -> prerequisite -> seat or waitlist. |
| Duplicates | One enrollment **or** one waitlist entry per learner; repeat requests return `AlreadyEnrolled` / `AlreadyWaitlisted` and change nothing. |
| FIFO | Each waitlist entry gets a sequence number (`NextWaitlistSequence`, starting at 1) when it is admitted. The number never changes. |
| Promotion | A successful drop frees one seat; at most one learner is promoted for it. |
| Ineligible waiter (skip) | Eligibility is checked again at promotion time. An ineligible waiter is skipped but stays in the queue at the same sequence; the earliest eligible waiter is promoted. If no waiter is eligible, the seat stays open. |
| Drop | Only an active enrollment can be dropped. Unknown, repeated or waitlisted-learner drops return `EnrollmentNotFound` and change nothing. |
| Atomicity | Each enroll and each drop+promotion runs inside one `SemaphoreSlim` critical section, released in `finally`. During a drop, the promotion candidate is chosen **before** any state changes, so if the prerequisite check fails or is cancelled the state is left untouched (invariant 7). |

### Deviations from the illustrative contract (spec section 5/10)

- `RegistrationStatus` adds `Dropped` (the result of a successful drop) and `OfferingNotFound` (so a missing offering can map to 404). `OfferingFull` is kept for contract parity, but under the waitlist policy it is never returned.
- `RegistrationResult` adds an optional `WaitlistSequence`, set when the result is `Waitlisted`.
- The HTTP enroll body carries only `learnerId`, because the offering ID is in the route (spec section 6). No `DropRequest` type is needed because both IDs are in the route.
- `ICourseRegistrationStore.ExecuteAsync` takes a delegate and runs it atomically. The service decides *what* to change; the store decides *how* to make it atomic, so a database version could run the same delegate inside a transaction.

## API

| Method | Route | Outcomes |
|---|---|---|
| `POST` | `/api/offerings/{offeringId}/enrollments` body `{"learnerId":"..."}` | `200 Enrolled`, `202 Waitlisted`, `409 AlreadyEnrolled/AlreadyWaitlisted`, `403 PrerequisiteNotMet`, `404 OfferingNotFound`, `400 InvalidRequest` |
| `DELETE` | `/api/offerings/{offeringId}/enrollments/{learnerId}` | `200 Dropped` (+ `promotedLearnerId` when someone was promoted), `404 EnrollmentNotFound/OfferingNotFound`, `400 InvalidRequest` |

Every response body is `{"code": "<stable code>", "message": "...", "promotedLearnerId"?, "waitlistSequence"?}`.
Duplicate requests consistently return `409`. Unhandled exceptions are logged by the exception-handler
middleware and returned as `500 {"code":"ServerError","message":"An unexpected error occurred."}` with no exception details.

## Setup, build, test, run

These are the commands actually run (Ubuntu, .NET SDK 8.0.428):

```bash
# solution scaffolding
dotnet new sln -n CourseRegistration
dotnet new webapi    -n CourseRegistration.Api            -o src/CourseRegistration.Api --use-controllers -f net8.0
dotnet new classlib  -n CourseRegistration.Application    -o src/CourseRegistration.Application -f net8.0
dotnet new classlib  -n CourseRegistration.Infrastructure -o src/CourseRegistration.Infrastructure -f net8.0
dotnet new xunit     -n CourseRegistration.Tests          -o tests/CourseRegistration.Tests -f net8.0
dotnet sln add src/*/*.csproj tests/*/*.csproj
# (project references + Microsoft.AspNetCore.Mvc.Testing 8.0.11 for API tests)

dotnet build      # Build succeeded. 0 Warning(s), 0 Error(s)
dotnet test       # Passed! - Failed: 0, Passed: 40, Skipped: 0, Total: 40
dotnet run --project src/CourseRegistration.Api --urls http://localhost:5269
```

The seeded offering is `aaaaaaaa-0000-0000-0000-000000000001` with capacity 2. Learners `1111…`–`4444…` meet the prerequisite.
Swagger UI is at `/swagger` in Development. A manual smoke run against the running API gave these results:

```text
POST 1111…  -> 200 Enrolled
POST 2222…  -> 200 Enrolled
POST 3333…  -> 202 Waitlisted (waitlistSequence 1)
POST 2222…  -> 409 AlreadyEnrolled
POST 9999…  -> 403 PrerequisiteNotMet
DELETE 1111… -> 200 Dropped, promotedLearnerId 3333…
DELETE 1111… -> 404 EnrollmentNotFound
DELETE not-a-guid -> 400 InvalidRequest
POST to unknown offering -> 404 OfferingNotFound
```

## Test coverage vs. spec section 13

| Spec test | Test(s) |
|---|---|
| Enrollment with available capacity | `Enrollment_with_available_capacity_enrolls_learner` |
| Enrollment at full capacity | `Enrollment_at_full_capacity_adds_learner_to_waitlist_once` |
| Duplicate enrollment | `Duplicate_enrollment_does_not_create_duplicate_state` |
| Already enrolled requests again | `Already_enrolled_learner_requesting_again_gets_AlreadyEnrolled_and_state_is_unchanged` |
| Already waitlisted requests again | `Already_waitlisted_learner_requesting_again_gets_AlreadyWaitlisted_and_state_is_unchanged` |
| Prerequisite failure on enrollment | `Prerequisite_failure_on_enrollment_neither_enrolls_nor_waitlists`, `Prerequisite_failure_when_full_does_not_join_waitlist` |
| Drop active learner | `Drop_active_learner_with_no_waitlist_frees_one_seat` |
| FIFO promotion | `Drop_promotes_earliest_waitlisted_learner_FIFO`, `Successive_drops_promote_waiters_in_FIFO_order` |
| Skip ineligible learner | `Ineligible_first_waiter_is_skipped_and_retains_position`, `Skipped_waiter_that_becomes_eligible_is_promoted_on_a_later_drop` |
| Eligibility rechecked | `Eligibility_is_rechecked_at_promotion_time` |
| Repeated/invalid drop | `Repeated_drop_returns_EnrollmentNotFound_and_state_is_unchanged`, `Dropping_a_waitlisted_learner_returns_EnrollmentNotFound_and_keeps_them_waiting` |
| Capacity invariant | `Capacity_invariant_holds_across_a_sequence_of_operations` (500 seeded random enroll/drop/eligibility steps, all invariants checked after each step) |
| Concurrent enrollment | `Concurrent_enrollment_does_not_exceed_capacity_and_waitlists_the_rest`, `Concurrent_duplicate_requests_for_same_learner_create_one_record`, `Concurrent_drops_each_promote_a_distinct_waiter_in_FIFO_order`, `Concurrent_drops_and_enrollments_preserve_invariants` |
| Validation / errors (section 12) | `Empty_identifiers_return_InvalidRequest_without_consulting_prerequisites`, `Unknown_offering_returns_OfferingNotFound`, `CourseOfferingTests.*`, `EnrollmentsApiTests.*` |
| Regression: no half-applied drop | `Prerequisite_failure_during_promotion_does_not_partially_apply_drop` |

Tests check both the returned result and the resulting state (taken under the store lock with `GetSnapshotAsync`).

## AI-assisted engineering record

The implementation, tests and this README were written by Devin (an AI software engineering agent, by Cognition) from the
spec, at the user's request. The records below describe what the AI did and how its output was checked. The
reviewer should add their own independent verification before submitting.

| Activity | Prompt used | AI suggestion / summary | Decision | Files / decisions affected | Independent verification |
|---|---|---|---|---|---|
| 1. Implement the spec | "please implement the below Spec document" + the spec | Generated the full 4-project solution, service, in-memory store, API, tests | Accepted, with the deviations listed above made on purpose | All files | `dotnet build` (0 warnings), `dotnet test` (40/40 passed, 8 consecutive runs without flakiness), manual curl smoke run (results above) |
| 2. Self-review of waitlist logic | The spec's suggested prompt (section 15), applied to this code | (a) Following spec section 9 literally (remove the enrollment, *then* `await` the prerequisite checks) leaves a half-applied drop if the checker throws or is cancelled. (b) Removing the lock was **not** caught by the first concurrent-capacity test, because the capacity check and insert run synchronously after the last `await`. (c) A skipped waiter who later becomes eligible while a seat is open cannot take that seat: re-enrolling returns `AlreadyWaitlisted`, and a newcomer can take the seat directly. | (a) Accepted: the promotion candidate is chosen before any change. (b) Accepted: added `Concurrent_drops_each_promote_a_distinct_waiter_in_FIFO_order`. (c) Not changed: the spec's check order (section 8) and promotion trigger (drop only) produce this behaviour, and changing it would add requirements the spec does not state. Listed under limitations instead. | `CourseRegistrationService.DropAsync`; `CourseRegistrationServiceTests` | Mutation checks: moving `RemoveEnrollment` before the scan fails `Prerequisite_failure_during_promotion_does_not_partially_apply_drop`. Removing the `SemaphoreSlim` wait/release fails `Concurrent_duplicate_requests_for_same_learner_create_one_record` and `Concurrent_drops_each_promote_a_distinct_waiter_in_FIFO_order` (3 of 3 runs). Both changes were reverted afterwards; the restored code passes 40/40. |

The prompts contained no credentials or personal data.

## Limitations and follow-up risks

- State is in memory and is lost when the process restarts.
- The `SemaphoreSlim` only coordinates a single process. Running several instances needs database transactions plus a unique `(OfferingId, LearnerId)` constraint, or row-level or optimistic locking.
- One lock serializes every operation on the offering. That is fine for one offering, but it would become a throughput bottleneck at scale.
- Prerequisite checks run while the lock is held, so a slow prerequisite source slows every operation. A real implementation needs a timeout and resilience policy for that source.
- The prerequisite source is a prototype allow-list. A real deployment would plug in the authoritative rule through `IPrerequisiteChecker`.
- Skip-and-retain can leave ineligible learners in the queue indefinitely. There is also no trigger to promote a waiter whose eligibility comes back while a seat is open (see AI record 2c). Expiry, admin removal and re-evaluation policies are out of scope.
- Learners cannot leave the waitlist themselves (not in scope).
- Single offering only. There are no cross-offering or term-level rules, and no authentication, authorization, auditing or production observability.
