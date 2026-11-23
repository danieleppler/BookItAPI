using BookIt.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookIt.Api.Data;

public class BookItDbContext : DbContext
{
    public BookItDbContext(DbContextOptions<BookItDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<Appointment> Appointments => Set<Appointment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Lets a GiST index combine "=" on ServiceId with "&&" on time ranges (used by the no-overlap constraint)
        modelBuilder.HasPostgresExtension("btree_gist");

        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Email).IsRequired().HasMaxLength(256);
            e.Property(u => u.PasswordHash).IsRequired();
            e.Property(u => u.FullName).IsRequired().HasMaxLength(200);
            e.Property(u => u.Role).HasConversion<string>().HasMaxLength(30);
        });

        modelBuilder.Entity<Business>(e =>
        {
            e.Property(b => b.Name).IsRequired().HasMaxLength(200);
            e.Property(b => b.Category).IsRequired().HasMaxLength(100);
            e.HasIndex(b => b.Category);
            e.HasOne(b => b.Owner)
                .WithMany(u => u.OwnedBusinesses)
                .HasForeignKey(b => b.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Service>(e =>
        {
            e.Property(s => s.Name).IsRequired().HasMaxLength(200);
            e.Property(s => s.Price).HasPrecision(10, 2);
            e.HasOne(s => s.Business)
                .WithMany(b => b.Services)
                .HasForeignKey(s => s.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Appointment>(e =>
        {
            // The no-overlap exclusion constraint is created in raw SQL in the AddAppointmentEndTime migration
            // (EF can't model exclusion constraints).
            e.ToTable(t => t.HasCheckConstraint("CK_Appointments_EndAfterStart", "\"EndTime\" > \"StartTime\""));
            e.Property(a => a.Status).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(a => a.ClientId);
            e.HasIndex(a => new { a.ServiceId, a.StartTime });
            e.HasOne(a => a.Service)
                .WithMany(s => s.Appointments)
                .HasForeignKey(a => a.ServiceId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.Client)
                .WithMany(u => u.Appointments)
                .HasForeignKey(a => a.ClientId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
