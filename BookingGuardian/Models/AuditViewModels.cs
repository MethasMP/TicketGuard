namespace BookingGuardian.Models
{
    public class AuditLogViewModel
    {
        public int Id { get; set; }
        public string Action { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public int EntityId { get; set; }
        public string PerformedBy { get; set; } = string.Empty;
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public string? Note { get; set; }
        public string? Detail { get; set; }
        public DateTime CreatedAt { get; set; }

        // Extra info joined for UI clarity
        public string? ReferenceNo { get; set; }
        public string? CustomerName { get; set; }
        public decimal? Amount { get; set; }
    }
}
