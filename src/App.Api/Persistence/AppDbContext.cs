using Microsoft.EntityFrameworkCore;
using App.Api.Domain;

namespace App.Api.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<InstrumentModel> Models => Set<InstrumentModel>();

    public DbSet<Instrument> Instruments => Set<Instrument>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Reservation> Reservations => Set<Reservation>();

    public DbSet<ReservationItem> ReservationItems => Set<ReservationItem>();

    public DbSet<IssueRecord> IssueRecords => Set<IssueRecord>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<InstrumentModel>(e =>
        {
            e.ToTable("models");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(32);
            e.Property(x => x.Title).HasMaxLength(128).IsRequired();
            e.Property(x => x.TotalCount);
            e.Property(x => x.AvailableCount);

            // Токен конкурентності. Оновлення, що прочитало стару версію, отримує
            // DbUpdateConcurrencyException замість тихого перезапису.
            e.Property(x => x.Version).IsConcurrencyToken();
        });

        b.Entity<Instrument>(e =>
        {
            e.ToTable("instruments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(32);
            e.Property(x => x.ModelId).HasMaxLength(32).IsRequired();
            e.Property(x => x.SerialNumber).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.ModelId);
        });

        b.Entity<Customer>(e =>
        {
            e.ToTable("customers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(32);
            e.Property(x => x.Name).HasMaxLength(128).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(32).IsRequired();
        });

        b.Entity<Reservation>(e =>
        {
            e.ToTable("reservations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(32);
            e.Property(x => x.CustomerId).HasMaxLength(32).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(200);

            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);

            e.HasMany(x => x.Items)
             .WithOne()
             .HasForeignKey(i => i.ReservationId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ReservationItem>(e =>
        {
            e.ToTable("reservation_items");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.ReservationId).HasMaxLength(32).IsRequired();
            e.Property(x => x.ModelId).HasMaxLength(32).IsRequired();
            e.Property(x => x.Count);
            e.HasIndex(x => x.ReservationId);
        });

        b.Entity<IssueRecord>(e =>
        {
            e.ToTable("issue_records");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(32);
            e.Property(x => x.ReservationId).HasMaxLength(32).IsRequired();
            e.Property(x => x.IssuedBy).HasMaxLength(64).IsRequired();
            e.Property(x => x.IssuedAt);
            e.HasIndex(x => x.ReservationId);
        });
    }
}
