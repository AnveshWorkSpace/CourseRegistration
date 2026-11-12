using System.Net;
using System.Net.Http.Json;
using CourseRegistration.Api.Contracts;
using CourseRegistration.Application.Interfaces;
using CourseRegistration.Application.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CourseRegistration.Tests;

public class EnrollmentsApiTests
{
    private static readonly Guid OfferingId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Carol = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Ineligible = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private static HttpClient CreateClient(int capacity = 1, Action<IServiceCollection>? configureServices = null)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("CourseOffering:Id", OfferingId.ToString());
            builder.UseSetting("CourseOffering:Capacity", capacity.ToString());
            if (configureServices is not null)
            {
                builder.ConfigureTestServices(configureServices);
            }
        });
        return factory.CreateClient();
    }

    private static Task<HttpResponseMessage> EnrollAsync(HttpClient client, Guid learnerId, Guid? offeringId = null) =>
        client.PostAsJsonAsync($"/api/offerings/{offeringId ?? OfferingId}/enrollments", new { learnerId });

    private static Task<HttpResponseMessage> DropAsync(HttpClient client, Guid learnerId) =>
        client.DeleteAsync($"/api/offerings/{OfferingId}/enrollments/{learnerId}");

    private static async Task<RegistrationResponse> BodyAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<RegistrationResponse>())!;

    [Fact]
    public async Task Enroll_returns_200_then_202_when_full_then_409_on_duplicate()
    {
        using var client = CreateClient(capacity: 1);

        var enrolled = await EnrollAsync(client, Alice);
        var waitlisted = await EnrollAsync(client, Bob);
        var duplicateEnrolled = await EnrollAsync(client, Alice);
        var duplicateWaitlisted = await EnrollAsync(client, Bob);

        Assert.Equal(HttpStatusCode.OK, enrolled.StatusCode);
        Assert.Equal("Enrolled", (await BodyAsync(enrolled)).Code);
        Assert.Equal(HttpStatusCode.Accepted, waitlisted.StatusCode);
        var waitlistedBody = await BodyAsync(waitlisted);
        Assert.Equal("Waitlisted", waitlistedBody.Code);
        Assert.Equal(1, waitlistedBody.WaitlistSequence);
        Assert.Equal(HttpStatusCode.Conflict, duplicateEnrolled.StatusCode);
        Assert.Equal("AlreadyEnrolled", (await BodyAsync(duplicateEnrolled)).Code);
        Assert.Equal(HttpStatusCode.Conflict, duplicateWaitlisted.StatusCode);
        Assert.Equal("AlreadyWaitlisted", (await BodyAsync(duplicateWaitlisted)).Code);
    }

    [Fact]
    public async Task Enroll_ineligible_learner_returns_403()
    {
        using var client = CreateClient();

        var response = await EnrollAsync(client, Ineligible);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("PrerequisiteNotMet", (await BodyAsync(response)).Code);
    }

    [Fact]
    public async Task Unknown_offering_returns_404()
    {
        using var client = CreateClient();

        var response = await EnrollAsync(client, Alice, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("OfferingNotFound", (await BodyAsync(response)).Code);
    }

    [Theory]
    [InlineData("/api/offerings/not-a-guid/enrollments", "{\"learnerId\":\"11111111-1111-1111-1111-111111111111\"}")]
    [InlineData("/api/offerings/aaaaaaaa-0000-0000-0000-000000000001/enrollments", "{\"learnerId\":\"nope\"}")]
    [InlineData("/api/offerings/aaaaaaaa-0000-0000-0000-000000000001/enrollments", "{}")]
    [InlineData("/api/offerings/aaaaaaaa-0000-0000-0000-000000000001/enrollments", "{\"learnerId\":\"00000000-0000-0000-0000-000000000000\"}")]
    public async Task Malformed_or_empty_identifiers_return_400(string url, string json)
    {
        using var client = CreateClient();

        var response = await client.PostAsync(url, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("InvalidRequest", (await BodyAsync(response)).Code);
    }

    [Fact]
    public async Task Drop_with_malformed_learner_id_returns_400()
    {
        using var client = CreateClient();

        var response = await client.DeleteAsync($"/api/offerings/{OfferingId}/enrollments/not-a-guid");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("InvalidRequest", (await BodyAsync(response)).Code);
    }

    [Fact]
    public async Task Drop_returns_promoted_learner_then_404_on_repeat()
    {
        using var client = CreateClient(capacity: 1);
        await EnrollAsync(client, Alice);
        await EnrollAsync(client, Bob);
        await EnrollAsync(client, Carol);

        var drop = await DropAsync(client, Alice);
        var repeat = await DropAsync(client, Alice);

        Assert.Equal(HttpStatusCode.OK, drop.StatusCode);
        var body = await BodyAsync(drop);
        Assert.Equal("Dropped", body.Code);
        Assert.Equal(Bob, body.PromotedLearnerId);
        Assert.Equal(HttpStatusCode.NotFound, repeat.StatusCode);
        Assert.Equal("EnrollmentNotFound", (await BodyAsync(repeat)).Code);
        Assert.Equal(HttpStatusCode.Conflict, (await EnrollAsync(client, Bob)).StatusCode);
    }

    [Fact]
    public async Task Unexpected_failure_returns_generic_500_without_exception_details()
    {
        using var client = CreateClient(configureServices: services =>
        {
            services.RemoveAll<ICourseRegistrationService>();
            services.AddSingleton<ICourseRegistrationService, ThrowingService>();
        });

        var response = await EnrollAsync(client, Alice);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(ThrowingService.SecretDetail, raw);
        Assert.Equal("ServerError", (await BodyAsync(response)).Code);
    }

    private sealed class ThrowingService : ICourseRegistrationService
    {
        public const string SecretDetail = "connection string leaked";

        public Task<RegistrationResult> EnrollAsync(Guid offeringId, Guid learnerId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(SecretDetail);

        public Task<RegistrationResult> DropAsync(Guid offeringId, Guid learnerId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(SecretDetail);
    }
}
