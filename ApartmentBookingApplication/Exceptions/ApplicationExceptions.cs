namespace ApartmentBookingApplication.Exceptions
{
    /// <summary>Base class for expected application errors that are mapped to HTTP status codes.</summary>
    public abstract class AppException : Exception
    {
        protected AppException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>HTTP 404.</summary>
    public class NotFoundException : AppException
    {
        public NotFoundException(string entity, object id) : base($"{entity} '{id}' was not found.") { }
    }

    /// <summary>HTTP 400 — invalid input that is not a domain invariant.</summary>
    public class ValidationException : AppException
    {
        public ValidationException(string message) : base(message) { }
    }

    /// <summary>HTTP 409 — e.g. selected dates are already taken.</summary>
    public class ConflictException : AppException
    {
        public ConflictException(string message) : base(message) { }
    }

    /// <summary>HTTP 403.</summary>
    public class ForbiddenException : AppException
    {
        public ForbiddenException(string message = "You do not have access to this resource.") : base(message) { }
    }

    /// <summary>HTTP 502 — the payment provider failed.</summary>
    public class PaymentGatewayException : AppException
    {
        public PaymentGatewayException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>HTTP 400 — webhook signature could not be verified.</summary>
    public class InvalidWebhookSignatureException : AppException
    {
        public InvalidWebhookSignatureException(string message, Exception? inner = null) : base(message, inner) { }
    }
}
