using ApartmentBookingInfrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ApartmentBookingInfrastructure.BackgroundJobs
{
    /// <summary>
    /// Periodically deletes expired rows from booking_locks.
    /// Availability checks already ignore expired locks, so this is housekeeping, not a correctness requirement.
    /// </summary>
    public class ExpiredLocksCleanupService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

        // Keep expired locks a little longer so a late Stripe webhook can still read the tenant's email from the lock.
        private static readonly TimeSpan Grace = TimeSpan.FromMinutes(30);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ExpiredLocksCleanupService> _logger;

        public ExpiredLocksCleanupService(IServiceScopeFactory scopeFactory, ILogger<ExpiredLocksCleanupService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(Interval);
            do
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var threshold = DateTime.UtcNow - Grace;

                    var deleted = await db.BookingLocks
                        .Where(l => l.ExpiresAt < threshold)
                        .ExecuteDeleteAsync(stoppingToken);

                    if (deleted > 0)
                        _logger.LogInformation("Removed {Count} expired booking locks", deleted);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Expired booking locks cleanup failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
    }
}
