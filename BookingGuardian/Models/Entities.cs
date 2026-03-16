using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BookingGuardian.Models
{
    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [EmailAddress]
        [MaxLength(100)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string PasswordHash { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        public string Role { get; set; } = "Supporter"; // Reserved for future use (Admin, Supporter)

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Booking
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(20)]
        public string ReferenceNo { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string CustomerName { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        public string Route { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string OperatorName { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? PhoneNumber { get; set; }

        [MaxLength(100)]
        public string? CustomerEmail { get; set; }

        public byte PassengerCount { get; set; } = 1;

        public DateTime TravelDate { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal Amount { get; set; }

        [Required]
        public string PaymentStatus { get; set; } = "PENDING"; // PENDING, SUCCESS, FAILED

        [Required]
        public string BookingStatus { get; set; } = "PENDING"; // PENDING, CONFIRMED, CANCELLED, RECOVERED

        public DateTime? PaymentAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public virtual Anomaly? Anomaly { get; set; }
    }

    public class Anomaly
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int BookingId { get; set; }

        public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

        [Required]
        public DateTime DetectionRunAt { get; set; }

        [Required]
        public string Status { get; set; } = "OPEN"; // OPEN, RESOLVED, IGNORED

        public DateTime? ResolvedAt { get; set; }

        [MaxLength(100)]
        public string? ResolvedBy { get; set; }

        public string? Note { get; set; }

        public int? EndpointHealthId { get; set; }

        [ForeignKey("BookingId")]
        public virtual Booking Booking { get; set; } = null!;

        [ForeignKey("EndpointHealthId")]
        public virtual EndpointHealth? EndpointHealth { get; set; }
    }

    public class AuditLog
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Action { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string EntityType { get; set; } = string.Empty;

        [Required]
        public int EntityId { get; set; }

        [Required]
        [MaxLength(100)]
        public string PerformedBy { get; set; } = string.Empty;

        [MaxLength(45)]
        public string? IpAddress { get; set; }

        [MaxLength(512)]
        public string? UserAgent { get; set; }

        public string? Note { get; set; }

        public string? Detail { get; set; } // JSON string

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class EndpointHealth
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string Url { get; set; } = string.Empty;

        [Required]
        public string Status { get; set; } = "UP"; // UP, DEGRADED, DOWN

        public int? ResponseMs { get; set; }

        public int? HttpCode { get; set; }

        public string? CheckDetails { get; set; }

        public DateTime CheckedAt { get; set; } = DateTime.UtcNow;

        public virtual ICollection<Anomaly> Anomalies { get; set; } = new List<Anomaly>();
    }
}
