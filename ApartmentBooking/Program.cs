using System.Text.Json.Serialization;
using ApartmentBooking.Auth;
using ApartmentBooking.Infrastructure;
using ApartmentBookingApplication;
using ApartmentBookingApplication.Abstractions;
using ApartmentBookingApplication.Options;
using ApartmentBookingInfrastructure;
using ApartmentBookingInfrastructure.Persistence;

namespace ApartmentBooking
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Structured JSON logs to stdout (collected by Promtail -> Loki), including the CorrelationId scope.
            builder.Logging.ClearProviders();
            builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);

            builder.Services
                .AddControllers()
                .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
            builder.Services.AddOpenApi();
            builder.Services.AddProblemDetails();
            builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

            builder.Services.Configure<BookingOptions>(builder.Configuration.GetSection(BookingOptions.SectionName));
            builder.Services.Configure<FrontendOptions>(builder.Configuration.GetSection(FrontendOptions.SectionName));

            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
            builder.Services.AddKeycloakAuthentication(builder.Configuration);

            builder.Services.AddApplication();
            builder.Services.AddInfrastructure(builder.Configuration);

            builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("postgres");

            // Only needed for local development (in production the SPA is served by Nginx from the same origin).
            var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
                .WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .WithExposedHeaders(CorrelationIdMiddleware.HeaderName)));

            var app = builder.Build();

            if (builder.Configuration.GetValue("Database:ApplyMigrationsOnStartup", false))
            {
                await DatabaseInitializer.InitializeAsync(
                    app.Services, builder.Configuration.GetValue("Database:SeedDemoData", false));
            }

            app.UseMiddleware<CorrelationIdMiddleware>();
            app.UseExceptionHandler();

            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
            }

            // TLS is terminated by Nginx; redirect only when the app is exposed directly.
            if (!builder.Configuration.GetValue("BehindReverseProxy", false))
            {
                app.UseHttpsRedirection();
            }

            app.UseCors();
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();
            app.MapHealthChecks("/health").AllowAnonymous();

            await app.RunAsync();
        }
    }
}
