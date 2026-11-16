using ApartmentBookingApplication.Exceptions;
using ApartmentBookingDomain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApartmentBooking.Infrastructure
{
    /// <summary>Maps exceptions to RFC 7807 ProblemDetails responses.</summary>
    public class GlobalExceptionHandler : IExceptionHandler
    {
        private readonly IProblemDetailsService _problemDetails;
        private readonly ILogger<GlobalExceptionHandler> _logger;

        public GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
        {
            _problemDetails = problemDetails;
            _logger = logger;
        }

        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            var (status, title) = exception switch
            {
                DomainException => (StatusCodes.Status400BadRequest, "Business rule violation"),
                ValidationException => (StatusCodes.Status400BadRequest, "Validation error"),
                InvalidWebhookSignatureException => (StatusCodes.Status400BadRequest, "Invalid signature"),
                NotFoundException => (StatusCodes.Status404NotFound, "Not found"),
                ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
                DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Conflict"),
                ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden"),
                UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
                PaymentGatewayException => (StatusCodes.Status502BadGateway, "Payment provider error"),
                _ => (StatusCodes.Status500InternalServerError, "Internal server error")
            };

            if (status >= 500)
                _logger.LogError(exception, "Unhandled exception");
            else
                _logger.LogInformation("Request failed with {Status}: {Message}", status, exception.Message);

            var detail = exception switch
            {
                DbUpdateConcurrencyException => "The resource was modified by another request. Please reload and try again.",
                _ when status >= 500 && status != StatusCodes.Status502BadGateway => "An unexpected error occurred.",
                _ => exception.Message
            };

            httpContext.Response.StatusCode = status;
            return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail }
            });
        }
    }
}
