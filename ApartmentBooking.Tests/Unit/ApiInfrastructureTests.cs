using System.Text.Json;
using ApartmentBooking.Infrastructure;
using ApartmentBookingApplication.Exceptions;
using ApartmentBookingDomain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApartmentBooking.Tests.Unit;

[Trait("Level", "Unit")]
public class CorrelationIdMiddlewareTests
{
    private static async Task<HttpContext> InvokeAsync(string? incoming)
    {
        var context = new DefaultHttpContext();
        if (incoming is not null)
            context.Request.Headers[CorrelationIdMiddleware.HeaderName] = incoming;

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        return context;
    }

    [Fact]
    public async Task ExistingId_IsReused_AsTraceIdentifierAndResponseHeader()
    {
        var context = await InvokeAsync("abc-123");

        Assert.Equal("abc-123", context.TraceIdentifier);
        Assert.Equal("abc-123", context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingOrBlankId_IsGenerated(string? incoming)
    {
        var context = await InvokeAsync(incoming);

        Assert.True(Guid.TryParse(context.Response.Headers[CorrelationIdMiddleware.HeaderName], out _));
    }

    [Fact]
    public async Task IdLongerThan100Chars_IsReplaced_ToProtectLogs()
    {
        var context = await InvokeAsync(new string('a', 101));

        Assert.True(Guid.TryParse(context.TraceIdentifier, out _));
    }

    [Fact]
    public async Task IdOfExactly100Chars_IsKept()
    {
        var id = new string('b', 100);

        var context = await InvokeAsync(id);

        Assert.Equal(id, context.TraceIdentifier);
    }
}

[Trait("Level", "Unit")]
public class GlobalExceptionHandlerTests
{
    public static TheoryData<Exception, int, string> Mappings => new()
    {
        { new DomainException("rule"), 400, "Business rule violation" },
        { new ValidationException("bad"), 400, "Validation error" },
        { new InvalidWebhookSignatureException("sig"), 400, "Invalid signature" },
        { new NotFoundException("Booking", Guid.Empty), 404, "Not found" },
        { new ConflictException("taken"), 409, "Conflict" },
        { new DbUpdateConcurrencyException("xmin"), 409, "Conflict" },
        { new ForbiddenException(), 403, "Forbidden" },
        { new UnauthorizedAccessException("no sub"), 401, "Unauthorized" },
        { new PaymentGatewayException("stripe down"), 502, "Payment provider error" },
        { new InvalidOperationException("Host=db;Password=secret"), 500, "Internal server error" },
    };

    private static async Task<(int Status, JsonElement Body)> HandleAsync(Exception exception)
    {
        var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();

        var handler = new GlobalExceptionHandler(
            services.GetRequiredService<IProblemDetailsService>(), NullLogger<GlobalExceptionHandler>.Instance);
        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);
        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body));
    }

    [Theory]
    [MemberData(nameof(Mappings))]
    public async Task Exception_IsMappedToStatusAndTitle(Exception exception, int status, string title)
    {
        var (actualStatus, body) = await HandleAsync(exception);

        Assert.Equal(status, actualStatus);
        Assert.Equal(title, body.GetProperty("title").GetString());
        Assert.Equal(status, body.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task UnexpectedException_DoesNotLeakMessage()
    {
        var (_, body) = await HandleAsync(new InvalidOperationException("Host=db;Password=secret"));

        var json = body.GetRawText();
        Assert.Equal("An unexpected error occurred.", body.GetProperty("detail").GetString());
        Assert.DoesNotContain("Password", json);
        Assert.DoesNotContain("InvalidOperationException", json);
    }

    [Fact]
    public async Task ConcurrencyConflict_HasFriendlyDetail()
    {
        var (_, body) = await HandleAsync(new DbUpdateConcurrencyException("row xmin changed"));

        Assert.Equal("The resource was modified by another request. Please reload and try again.", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task PaymentProviderError_KeepsMessageForSupport()
    {
        var (_, body) = await HandleAsync(new PaymentGatewayException("Stripe timeout"));

        Assert.Equal("Stripe timeout", body.GetProperty("detail").GetString());
    }
}
