using System.Net;
using ApartmentBooking.Tests.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ApartmentBooking.Tests.Api;

/// <summary>Integration: health check, Correlation ID and safe error contract (TC-OBS-002/005).</summary>
[Collection(ApiCollection.Name)]
[Trait("Level", "Integration")]
public class ObservabilityApiTests(ApiFactory api)
{
    private const string Header = "X-Correlation-ID";

    [ApiFact]
    public async Task Health_IsAnonymous_AndHealthy()
    {
        var response = await api.ClientFor(null).GetAsync("/health");

        await response.EnsureStatusAsync(HttpStatusCode.OK);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [ApiFact]
    public async Task CorrelationId_FromRequest_IsEchoedInResponse()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/apartments/districts");
        request.Headers.Add(Header, "qa-zinchenko-001");

        var response = await api.ClientFor(null).SendAsync(request);

        Assert.Equal("qa-zinchenko-001", response.Headers.GetValues(Header).Single());
    }

    [ApiFact]
    public async Task CorrelationId_IsGenerated_WhenMissingOrTooLong()
    {
        var generated = await api.ClientFor(null).GetAsync("/health");
        var tooLong = new HttpRequestMessage(HttpMethod.Get, "/health");
        tooLong.Headers.Add(Header, new string('x', 101));
        var replaced = await api.ClientFor(null).SendAsync(tooLong);

        Assert.True(Guid.TryParse(generated.Headers.GetValues(Header).Single(), out _));
        Assert.True(Guid.TryParse(replaced.Headers.GetValues(Header).Single(), out _));
    }

    [ApiFact]
    public async Task ErrorResponses_AreProblemDetails_WithoutStackTrace()
    {
        var response = await api.ClientFor(null).GetAsync($"/api/apartments/{Guid.NewGuid()}");

        await response.EnsureStatusAsync(HttpStatusCode.NotFound);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        var problem = await response.ReadAsync<ProblemDetails>();
        Assert.Equal(404, problem.Status);
        Assert.Equal("Not found", problem.Title);
        Assert.DoesNotContain("   at ", body);
        Assert.DoesNotContain("Exception", body);
        Assert.DoesNotContain("Host=", body);
    }

    [ApiFact]
    [Trait("Category", "KnownBug")]
    [Trait("Bug", "BUG-BE-007")]
    public async Task ErrorResponses_KeepCorrelationIdHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/apartments/{Guid.NewGuid()}");
        request.Headers.Add(Header, "qa-err-404");

        var response = await api.ClientFor(null).SendAsync(request);

        // Expected: the id is returned so support can find the failing request in Loki.
        // Actual: UseExceptionHandler calls Response.Clear(), the header set by CorrelationIdMiddleware is lost.
        Assert.True(response.Headers.TryGetValues(Header, out var values), "X-Correlation-ID header is missing on error response");
        Assert.Equal("qa-err-404", values!.Single());
    }

    [ApiFact]
    public async Task UnknownRoute_Returns404()
    {
        var response = await api.ClientFor(null).GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [ApiFact]
    public async Task MalformedGuidInRoute_DoesNotMatchEndpoint()
    {
        var response = await api.ClientFor(null).GetAsync("/api/apartments/123");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
