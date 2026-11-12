using CourseRegistration.Api.Contracts;
using CourseRegistration.Application.Interfaces;
using CourseRegistration.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace CourseRegistration.Api.Controllers;

[ApiController]
[Route("api/offerings/{offeringId}/enrollments")]
[Produces("application/json")]
public sealed class EnrollmentsController : ControllerBase
{
    private readonly ICourseRegistrationService _registrationService;

    public EnrollmentsController(ICourseRegistrationService registrationService)
    {
        _registrationService = registrationService;
    }

    /// <summary>Enrolls the learner, or adds them to the waitlist when the offering is full.</summary>
    [HttpPost]
    [ProducesResponseType<RegistrationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<RegistrationResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<RegistrationResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<RegistrationResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<RegistrationResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<RegistrationResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Enroll(Guid offeringId, [FromBody] EnrollRequest request, CancellationToken cancellationToken)
    {
        var result = await _registrationService.EnrollAsync(offeringId, request.LearnerId, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>Drops an active enrollment and promotes the earliest eligible waitlisted learner.</summary>
    [HttpDelete("{learnerId}")]
    [ProducesResponseType<RegistrationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<RegistrationResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<RegistrationResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Drop(Guid offeringId, Guid learnerId, CancellationToken cancellationToken)
    {
        var result = await _registrationService.DropAsync(offeringId, learnerId, cancellationToken);
        return ToActionResult(result);
    }

    private ObjectResult ToActionResult(RegistrationResult result)
    {
        var statusCode = result.Status switch
        {
            RegistrationStatus.Enrolled => StatusCodes.Status200OK,
            RegistrationStatus.Dropped => StatusCodes.Status200OK,
            RegistrationStatus.Waitlisted => StatusCodes.Status202Accepted,
            RegistrationStatus.AlreadyEnrolled => StatusCodes.Status409Conflict,
            RegistrationStatus.AlreadyWaitlisted => StatusCodes.Status409Conflict,
            RegistrationStatus.OfferingFull => StatusCodes.Status409Conflict,
            RegistrationStatus.PrerequisiteNotMet => StatusCodes.Status403Forbidden,
            RegistrationStatus.OfferingNotFound => StatusCodes.Status404NotFound,
            RegistrationStatus.EnrollmentNotFound => StatusCodes.Status404NotFound,
            RegistrationStatus.InvalidRequest => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };

        var body = new RegistrationResponse(
            result.Status.ToString(),
            result.Message,
            result.PromotedLearnerId,
            result.WaitlistSequence);

        return StatusCode(statusCode, body);
    }
}
