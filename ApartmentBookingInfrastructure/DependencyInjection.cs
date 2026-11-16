using ApartmentBookingApplication.Abstractions;
using ApartmentBookingInfrastructure.BackgroundJobs;
using ApartmentBookingInfrastructure.Email;
using ApartmentBookingInfrastructure.Payments;
using ApartmentBookingInfrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApartmentBookingInfrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("Default")
                ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

            services.AddDbContext<AppDbContext>(options => options
                .UseNpgsql(connectionString)
                .UseSnakeCaseNamingConvention());
            services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

            services.Configure<StripeOptions>(configuration.GetSection(StripeOptions.SectionName));
            services.AddSingleton<IPaymentGateway, StripePaymentGateway>();

            services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
            services.AddSingleton<IEmailSender, SmtpEmailSender>();

            services.AddHostedService<ExpiredLocksCleanupService>();

            return services;
        }
    }
}
