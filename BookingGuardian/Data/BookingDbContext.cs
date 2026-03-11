using BookingGuardian.Models;
using Microsoft.EntityFrameworkCore;

namespace BookingGuardian.Data
{
    public class BookingDbContext : DbContext
    {
        public BookingDbContext(DbContextOptions<BookingDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Booking> Bookings { get; set; }
        public DbSet<Anomaly> Anomalies { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<EndpointHealth> EndpointHealths { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>(entity => {
                entity.ToTable("users");
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.Email).HasColumnName("email");
                entity.Property(e => e.PasswordHash).HasColumnName("password_hash");
                entity.Property(e => e.FullName).HasColumnName("full_name");
                entity.Property(e => e.Role).HasColumnName("role");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at");
                entity.HasIndex(u => u.Email).IsUnique();
            });

            modelBuilder.Entity<Booking>(entity => {
                entity.ToTable("bookings");
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.ReferenceNo).HasColumnName("reference_no");
                entity.Property(e => e.CustomerName).HasColumnName("customer_name");
                entity.Property(e => e.Route).HasColumnName("route");
                entity.Property(e => e.OperatorName).HasColumnName("operator_name");
                entity.Property(e => e.PhoneNumber).HasColumnName("phone_number");
                entity.Property(e => e.PassengerCount).HasColumnName("passenger_count");
                entity.Property(e => e.TravelDate).HasColumnName("travel_date");
                entity.Property(e => e.Amount).HasColumnName("amount");
                entity.Property(e => e.PaymentStatus).HasColumnName("payment_status");
                entity.Property(e => e.BookingStatus).HasColumnName("booking_status");
                entity.Property(e => e.PaymentAt).HasColumnName("payment_at");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at");
                entity.HasIndex(b => b.ReferenceNo).IsUnique();
                entity.HasIndex(b => new { b.PaymentStatus, b.BookingStatus, b.PaymentAt }).HasDatabaseName("idx_anomaly_detection");
            });

            modelBuilder.Entity<Anomaly>(entity => {
                entity.ToTable("anomalies");
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.BookingId).HasColumnName("booking_id");
                entity.Property(e => e.EndpointHealthId).HasColumnName("endpoint_health_id");
                entity.Property(e => e.DetectedAt).HasColumnName("detected_at");
                entity.Property(e => e.DetectionRunAt).HasColumnName("detection_run_at");
                entity.Property(e => e.Status).HasColumnName("status");
                entity.Property(e => e.ResolvedAt).HasColumnName("resolved_at");
                entity.Property(e => e.ResolvedBy).HasColumnName("resolved_by");
                entity.Property(e => e.Note).HasColumnName("note");
                entity.HasIndex(a => a.BookingId).IsUnique();
                entity.HasIndex(a => a.EndpointHealthId).HasDatabaseName("idx_anomaly_endpoint_health");
                entity.HasOne(a => a.EndpointHealth)
                      .WithMany(e => e.Anomalies)
                      .HasForeignKey(a => a.EndpointHealthId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<AuditLog>(entity => {
                entity.ToTable("audit_logs");
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.Action).HasColumnName("action");
                entity.Property(e => e.EntityType).HasColumnName("entity_type");
                entity.Property(e => e.EntityId).HasColumnName("entity_id");
                entity.Property(e => e.PerformedBy).HasColumnName("performed_by");
                entity.Property(e => e.IpAddress).HasColumnName("ip_address");
                entity.Property(e => e.UserAgent).HasColumnName("user_agent");
                entity.Property(e => e.Note).HasColumnName("note"); // Matching entities property
                entity.Property(e => e.Detail).HasColumnName("detail");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at");
                entity.HasIndex(a => new { a.EntityType, a.EntityId }).HasDatabaseName("idx_entity");
                entity.HasIndex(a => a.CreatedAt).HasDatabaseName("idx_performed_at");
            });

            modelBuilder.Entity<EndpointHealth>(entity => {
                entity.ToTable("endpoint_health");
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.Name).HasColumnName("name");
                entity.Property(e => e.Url).HasColumnName("url");
                entity.Property(e => e.Status).HasColumnName("status");
                entity.Property(e => e.ResponseMs).HasColumnName("response_ms");
                entity.Property(e => e.HttpCode).HasColumnName("http_code");
                entity.Property(e => e.CheckedAt).HasColumnName("checked_at");
                entity.HasIndex(e => new { e.Name, e.CheckedAt }).HasDatabaseName("idx_name_time");
            });
        }
    }
}
