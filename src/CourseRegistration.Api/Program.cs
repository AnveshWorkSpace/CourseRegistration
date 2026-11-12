using CourseRegistration.Api.Configuration;
using CourseRegistration.Api.Contracts;
using CourseRegistration.Application.Interfaces;
using CourseRegistration.Application.Models;
using CourseRegistration.Application.Services;
using CourseRegistration.Infrastructure.InMemory;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.SwaggerUI;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<CourseOfferingOptions>()
    .Bind(builder.Configuration.GetSection(CourseOfferingOptions.SectionName))
    .Validate(o => o.Id != Guid.Empty, "CourseOffering:Id must be a non-empty GUID.")
    .Validate(o => o.Capacity > 0, "CourseOffering:Capacity must be a positive integer.")
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ICourseRegistrationStore>(sp =>
{
    var options = sp.GetRequiredService<IOptions<CourseOfferingOptions>>().Value;
    return new InMemoryCourseRegistrationStore(new CourseOffering(options.Id, options.Capacity));
});
builder.Services.AddSingleton<IPrerequisiteChecker>(sp =>
    new InMemoryPrerequisiteChecker(sp.GetRequiredService<IOptions<CourseOfferingOptions>>().Value.EligibleLearnerIds));
builder.Services.AddSingleton<ICourseRegistrationService, CourseRegistrationService>();

builder.Services
    .AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = _ => new BadRequestObjectResult(new RegistrationResponse(
            nameof(RegistrationStatus.InvalidRequest),
            "The request is malformed or contains invalid identifiers."));
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// The exception handler middleware logs the exception; clients only get a generic body.
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new RegistrationResponse(
        "ServerError",
        "An unexpected error occurred."));
}));

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();

public partial class Program;
