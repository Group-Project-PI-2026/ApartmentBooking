using ApartmentBookingDomain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApartmentBookingInfrastructure.Persistence.Configurations
{
    internal static class Money
    {
        public static PropertyBuilder<decimal> HasMoneyPrecision(this PropertyBuilder<decimal> property) =>
            property.HasPrecision(12, 2);
    }

    internal class DistrictConfiguration : IEntityTypeConfiguration<District>
    {
        public void Configure(EntityTypeBuilder<District> builder)
        {
            builder.ToTable("districts");
            builder.HasKey(d => d.Id);
            builder.Property(d => d.Id).ValueGeneratedNever();
            builder.Property(d => d.Name).HasMaxLength(100).IsRequired();

            // Districts of Kyiv
            builder.HasData(
                new District(1, "Голосіївський"),
                new District(2, "Дарницький"),
                new District(3, "Деснянський"),
                new District(4, "Дніпровський"),
                new District(5, "Оболонський"),
                new District(6, "Печерський"),
                new District(7, "Подільський"),
                new District(8, "Святошинський"),
                new District(9, "Солом'янський"),
                new District(10, "Шевченківський"));
        }
    }

    internal class ApartmentConfiguration : IEntityTypeConfiguration<Apartment>
    {
        public void Configure(EntityTypeBuilder<Apartment> builder)
        {
            builder.ToTable("apartments");
            builder.HasKey(a => a.Id);
            builder.Property(a => a.Id).ValueGeneratedNever();
            builder.Property(a => a.Title).HasMaxLength(200).IsRequired();
            builder.Property(a => a.Description).HasMaxLength(4000);
            builder.Property(a => a.Address).HasMaxLength(300).IsRequired();
            builder.Property(a => a.CheckInInstructions).HasMaxLength(4000);
            builder.Property(a => a.PricePerNight).HasMoneyPrecision();

            builder.HasOne<District>().WithMany().HasForeignKey(a => a.DistrictId).OnDelete(DeleteBehavior.Restrict);
            builder.HasIndex(a => new { a.DistrictId, a.IsActive });
        }
    }

    internal class BookingConfiguration : IEntityTypeConfiguration<Booking>
    {
        public void Configure(EntityTypeBuilder<Booking> builder)
        {
            builder.ToTable("bookings");
            builder.HasKey(b => b.Id);
            builder.Property(b => b.Id).ValueGeneratedNever();

            builder.ComplexProperty(b => b.Dates, d =>
            {
                d.Property(x => x.CheckIn).HasColumnName("check_in");
                d.Property(x => x.CheckOut).HasColumnName("check_out");
            });

            builder.Property(b => b.TenantEmail).HasMaxLength(200);
            builder.Property(b => b.TotalPrice).HasMoneyPrecision();
            builder.Property(b => b.PaidAmount).HasMoneyPrecision();
            builder.Property(b => b.PenaltyAmount).HasMoneyPrecision();
            builder.Property(b => b.RefundAmount).HasMoneyPrecision();
            builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(30);
            builder.Property(b => b.RefundStatus).HasConversion<string>().HasMaxLength(30);

            // Optimistic concurrency on PostgreSQL's system column xmin (e.g. two parallel cancellations).
            builder.Property<uint>("xmin").IsRowVersion();

            builder.HasOne<Apartment>().WithMany().HasForeignKey(b => b.ApartmentId).OnDelete(DeleteBehavior.Restrict);
            builder.HasIndex(b => b.ApartmentId);
            builder.HasIndex(b => b.TenantId);
        }
    }

    internal class BookingLockConfiguration : IEntityTypeConfiguration<BookingLock>
    {
        public void Configure(EntityTypeBuilder<BookingLock> builder)
        {
            builder.ToTable("booking_locks");
            builder.HasKey(l => l.Id);
            builder.Property(l => l.Id).ValueGeneratedNever();

            builder.ComplexProperty(l => l.Dates, d =>
            {
                d.Property(x => x.CheckIn).HasColumnName("check_in");
                d.Property(x => x.CheckOut).HasColumnName("check_out");
            });

            builder.Property(l => l.TenantEmail).HasMaxLength(200);
            builder.Property(l => l.CheckoutSessionId).HasMaxLength(255);
            builder.Property(l => l.TotalPrice).HasMoneyPrecision();
            builder.Property(l => l.AmountToPay).HasMoneyPrecision();

            builder.HasOne<Apartment>().WithMany().HasForeignKey(l => l.ApartmentId).OnDelete(DeleteBehavior.Cascade);
            builder.HasIndex(l => new { l.ApartmentId, l.ExpiresAt });
            builder.HasIndex(l => l.ExpiresAt);
        }
    }

    internal class PaymentConfiguration : IEntityTypeConfiguration<Payment>
    {
        public void Configure(EntityTypeBuilder<Payment> builder)
        {
            builder.ToTable("payments");
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
            builder.Property(p => p.Type).HasConversion<string>().HasMaxLength(30);
            builder.Property(p => p.Amount).HasMoneyPrecision();
            builder.Property(p => p.RefundedAmount).HasMoneyPrecision();
            builder.Property(p => p.CheckoutSessionId).HasMaxLength(255).IsRequired();
            builder.Property(p => p.PaymentIntentId).HasMaxLength(255);

            // Guarantees a webhook is never applied twice.
            builder.HasIndex(p => p.CheckoutSessionId).IsUnique();

            builder.HasOne<Booking>().WithMany().HasForeignKey(p => p.BookingId).OnDelete(DeleteBehavior.Restrict);
        }
    }

    internal class OwnerRequestConfiguration : IEntityTypeConfiguration<OwnerRequest>
    {
        public void Configure(EntityTypeBuilder<OwnerRequest> builder)
        {
            builder.ToTable("owner_requests");
            builder.HasKey(r => r.Id);
            builder.Property(r => r.Id).ValueGeneratedNever();

            builder.Property(r => r.Number).UseIdentityByDefaultColumn();
            builder.HasIndex(r => r.Number).IsUnique();

            builder.Property(r => r.ContactName).HasMaxLength(200).IsRequired();
            builder.Property(r => r.ContactEmail).HasMaxLength(200).IsRequired();
            builder.Property(r => r.ContactPhone).HasMaxLength(50);
            builder.Property(r => r.Address).HasMaxLength(300).IsRequired();
            builder.Property(r => r.Title).HasMaxLength(200).IsRequired();
            builder.Property(r => r.Description).HasMaxLength(4000);
            builder.Property(r => r.ExpectedPricePerNight).HasMoneyPrecision();
            builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(30);

            builder.HasOne<District>().WithMany().HasForeignKey(r => r.DistrictId).OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(r => r.History)
                .WithOne()
                .HasForeignKey(h => h.OwnerRequestId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(r => r.OwnerId);
            builder.HasIndex(r => r.Status);
        }
    }

    internal class OwnerRequestHistoryEntryConfiguration : IEntityTypeConfiguration<OwnerRequestHistoryEntry>
    {
        public void Configure(EntityTypeBuilder<OwnerRequestHistoryEntry> builder)
        {
            builder.ToTable("owner_request_history");
            builder.HasKey(h => h.Id);
            builder.Property(h => h.Id).ValueGeneratedNever();
            builder.Property(h => h.Status).HasConversion<string>().HasMaxLength(30);
            builder.Property(h => h.Message).HasMaxLength(2000);
        }
    }
}
