namespace CourseRegistration.Application.Models;

public enum RegistrationStatus
{
    Enrolled,
    Waitlisted,
    Dropped,
    AlreadyEnrolled,
    AlreadyWaitlisted,
    PrerequisiteNotMet,
    OfferingFull,
    OfferingNotFound,
    EnrollmentNotFound,
    InvalidRequest
}
