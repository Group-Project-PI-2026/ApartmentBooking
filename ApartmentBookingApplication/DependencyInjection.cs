using ApartmentBookingApplication.Apartments;
using ApartmentBookingApplication.Bookings;
using ApartmentBookingApplication.OwnerRequests;
using Microsoft.Extensions.DependencyInjection;

namespace ApartmentBookingApplication
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplication(this IServiceCollection services)
        {
            services.AddScoped<ApartmentService>();
            services.AddScoped<BookingService>();
            services.AddScoped<PaymentWebhookService>();
            services.AddScoped<OwnerRequestService>();
            return services;
        }
    }
}
