using ApartmentBookingDomain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ApartmentBookingInfrastructure.Persistence
{
    public static class DatabaseInitializer
    {
        /// <summary>Applies pending migrations and optionally seeds demo apartments (development only).</summary>
        public static async Task InitializeAsync(IServiceProvider services, bool seedDemoData, CancellationToken ct = default)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DatabaseInitializer));

            await db.Database.MigrateAsync(ct);

            if (!seedDemoData || await db.Apartments.AnyAsync(ct))
                return;

            var demoOwner = Guid.Parse("00000000-0000-0000-0000-000000000001");
            db.Apartments.AddRange(
                new Apartment(demoOwner, 6, "Студія біля Печерської лаври", "Затишна студія з видом на Дніпро.",
                    "вул. Лаврська, 10", 2, 1800m, "Код від домофону: 1234. Ключі в боксі біля дверей, код 5678.",
                    new[] { "https://picsum.photos/seed/apt1/800/600" }, new[] { "Wi-Fi", "Кондиціонер", "Кухня" }),
                new Apartment(demoOwner, 10, "Двокімнатна на Хрещатику", "Простора квартира в центрі міста.",
                    "вул. Хрещатик, 25", 4, 3200m, "Зустріч з адміністратором біля входу.",
                    new[] { "https://picsum.photos/seed/apt2/800/600" }, new[] { "Wi-Fi", "Пральна машина", "Балкон" }),
                new Apartment(demoOwner, 5, "Квартира біля Оболонської набережної", "Тиха квартира поруч із парком.",
                    "просп. Героїв Сталінграду, 4", 3, 1500m, "Ключі в сейфі, код 4321.",
                    new[] { "https://picsum.photos/seed/apt3/800/600" }, new[] { "Wi-Fi", "Паркінг" }));

            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded demo apartments");
        }
    }
}
