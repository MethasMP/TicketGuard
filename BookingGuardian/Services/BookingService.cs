using BookingGuardian.Data;
using BookingGuardian.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace BookingGuardian.Services
{
    public interface IBookingService
    {
        /// <summary>
        /// Attempts to recover an anomaly by confirming the booking.
        /// </summary>
        /// <param name="anomalyId">ID of the anomaly.</param>
        /// <param name="note">Justification note.</param>
        /// <param name="performedBy">Email of the user performing the action.</param>
        /// <param name="ipAddress">Requester's IP address.</param>
        /// <param name="userAgent">Requester's Browser Agent.</param>
        /// <returns>Execution result status.</returns>
        Task<AnomalyResponse> RecoverAnomalyAsync(int anomalyId, string note, string performedBy, string? ipAddress, string? userAgent);
        /// <summary>
        /// Marks an anomaly as ignored.
        /// </summary>
        /// <param name="anomalyId">ID of the anomaly.</param>
        /// <param name="note">Justification note.</param>
        /// <param name="performedBy">Email of the user performing the action.</param>
        /// <param name="ipAddress">Requester's IP address.</param>
        /// <param name="userAgent">Requester's Browser Agent.</param>
        /// <returns>Execution result status.</returns>
        Task<AnomalyResponse> IgnoreAnomalyAsync(int anomalyId, string note, string performedBy, string? ipAddress, string? userAgent);

        /// <summary>
        /// Recovers multiple anomalies in one atomic transaction.
        /// </summary>
        /// <param name="anomalyIds">List of anomaly IDs to recover.</param>
        /// <param name="note">Shared justification note.</param>
        /// <param name="performedBy">Email of the user performing the action.</param>
        /// <param name="ipAddress">Requester's IP address.</param>
        /// <param name="userAgent">Requester's Browser Agent.</param>
        /// <returns>Execution result status.</returns>
        Task<AnomalyResponse> BulkRecoverAnomaliesAsync(IReadOnlyCollection<int> anomalyIds, string note, string performedBy, string? ipAddress, string? userAgent);
    }

    public class BookingService : IBookingService
    {
        private readonly BookingDbContext _dbContext;
        private readonly ILogger<BookingService> _logger;
        private readonly ISmsNotificationService _smsNotificationService;

        public BookingService(BookingDbContext dbContext, ILogger<BookingService> logger, ISmsNotificationService smsNotificationService)
        {
            _dbContext = dbContext;
            _logger = logger;
            _smsNotificationService = smsNotificationService;
        }

        /// <summary>
        /// Recovers an anomaly by updating the booking status to CONFIRMED.
        /// </summary>
        /// <param name="anomalyId">The unique identifier of the anomaly.</param>
        /// <param name="note">Justification note (min 10 chars).</param>
        /// <param name="performedBy">User identifying string.</param>
        /// <param name="ipAddress">IP address of the requester.</param>
        /// <param name="userAgent">User agent of the requester.</param>
        /// <returns>AnomalyResponse with status code and message.</returns>
        public async Task<AnomalyResponse> RecoverAnomalyAsync(int anomalyId, string note, string performedBy, string? ipAddress, string? userAgent)
        {
            if (string.IsNullOrEmpty(note) || note.Length < 10)
            {
                return AnomalyResponse.Unprocessable("Note must be at least 10 characters long.");
            }

            var strategy = _dbContext.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _dbContext.Database.BeginTransactionAsync();
                try
                {
                    var anomaly = await _dbContext.Anomalies
                        .Include(a => a.Booking)
                        .FirstOrDefaultAsync(a => a.Id == anomalyId);

                    if (anomaly == null) return AnomalyResponse.Fail("Booking issue not found.");
                    if (anomaly.Status != "OPEN") return AnomalyResponse.Conflict($"Booking issue is already {anomaly.Status}.");
                    if (anomaly.Booking.PaymentStatus != "SUCCESS") return AnomalyResponse.Fail("Booking payment status is not SUCCESS.");

                    // Update Booking
                    anomaly.Booking.BookingStatus = "CONFIRMED";

                    // Update Anomaly
                    anomaly.Status = "RESOLVED";
                    anomaly.ResolvedAt = DateTime.UtcNow;
                    anomaly.ResolvedBy = performedBy;
                    anomaly.Note = note;

                    // Create Audit Log
                    var auditLog = new AuditLog
                    {
                        Action = "RECOVER",
                        EntityType = "Booking",
                        EntityId = anomaly.Booking.Id,
                        PerformedBy = performedBy,
                        IpAddress = ipAddress,
                        UserAgent = userAgent,
                        Note = note,
                        CreatedAt = DateTime.UtcNow
                    };
                    
                    await _dbContext.AuditLogs.AddAsync(auditLog);
                    await _dbContext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    _logger.LogInformation("Booking recovered. BookingId={BookingId} Reference={Ref} RecoveredBy={User}", 
                        anomaly.Booking.Id, anomaly.Booking.ReferenceNo, performedBy);

                    await TrySendRecoverySmsAuditAsync(anomaly.Booking, performedBy, ipAddress, userAgent);

                    return AnomalyResponse.Ok($"Booking {anomaly.Booking.ReferenceNo} successfully recovered.");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, "Error recovering anomaly {Id}", anomalyId);
                    return AnomalyResponse.Error("An unexpected error occurred while recovering the booking. Please contact support.");
                }
            });
        }

        /// <summary>
        /// Marks an anomaly as IGNORED.
        /// </summary>
        /// <param name="anomalyId">The unique identifier of the anomaly.</param>
        /// <param name="note">Justification note (min 10 chars).</param>
        /// <param name="performedBy">User identifying string.</param>
        /// <param name="ipAddress">IP address of the requester.</param>
        /// <param name="userAgent">User agent of the requester.</param>
        /// <returns>AnomalyResponse with status code and message.</returns>
        public async Task<AnomalyResponse> IgnoreAnomalyAsync(int anomalyId, string note, string performedBy, string? ipAddress, string? userAgent)
        {
            if (string.IsNullOrEmpty(note) || note.Length < 10)
            {
                return AnomalyResponse.Unprocessable("Note must be at least 10 characters long.");
            }

            var anomaly = await _dbContext.Anomalies.FindAsync(anomalyId);
            if (anomaly == null) return AnomalyResponse.Fail("Booking issue not found.");
            if (anomaly.Status != "OPEN") return AnomalyResponse.Conflict($"Booking issue is already {anomaly.Status}.");

            anomaly.Status = "IGNORED";
            anomaly.ResolvedAt = DateTime.UtcNow;
            anomaly.ResolvedBy = performedBy;
            anomaly.Note = note;

            await _dbContext.AuditLogs.AddAsync(new AuditLog
            {
                Action = "IGNORE",
                EntityType = "Anomaly",
                EntityId = anomaly.Id,
                PerformedBy = performedBy,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Note = note,
                CreatedAt = DateTime.UtcNow
            });

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Anomaly ignored. AnomalyId={Id} IgnoredBy={User}", anomalyId, performedBy);

            return AnomalyResponse.Ok($"Booking issue #{anomalyId} marked as ignored.");
        }

        public async Task<AnomalyResponse> BulkRecoverAnomaliesAsync(IReadOnlyCollection<int> anomalyIds, string note, string performedBy, string? ipAddress, string? userAgent)
        {
            if (string.IsNullOrWhiteSpace(note) || note.Length < 10)
            {
                return AnomalyResponse.Unprocessable("Note must be at least 10 characters long.");
            }

            var distinctIds = anomalyIds.Distinct().ToList();
            if (!distinctIds.Any())
            {
                return AnomalyResponse.Fail("No booking issues selected for bulk recovery.");
            }

            var strategy = _dbContext.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _dbContext.Database.BeginTransactionAsync();
                try
                {
                    var anomalies = await _dbContext.Anomalies
                        .Include(a => a.Booking)
                        .Where(a => distinctIds.Contains(a.Id))
                        .ToListAsync();

                    if (anomalies.Count != distinctIds.Count)
                    {
                        return AnomalyResponse.Fail("One or more booking issues were not found.");
                    }

                    if (anomalies.Any(a => a.Status != "OPEN"))
                    {
                        return AnomalyResponse.Conflict("One or more booking issues are no longer OPEN.");
                    }

                    if (anomalies.Any(a => a.Booking.PaymentStatus != "SUCCESS"))
                    {
                        return AnomalyResponse.Fail("One or more bookings have payment status other than SUCCESS.");
                    }

                    var now = DateTime.UtcNow;
                    var auditLogs = new List<AuditLog>(anomalies.Count);

                    foreach (var anomaly in anomalies)
                    {
                        anomaly.Booking.BookingStatus = "CONFIRMED";
                        anomaly.Status = "RESOLVED";
                        anomaly.ResolvedAt = now;
                        anomaly.ResolvedBy = performedBy;
                        anomaly.Note = note;

                        auditLogs.Add(new AuditLog
                        {
                            Action = "RECOVER",
                            EntityType = "Booking",
                            EntityId = anomaly.Booking.Id,
                            PerformedBy = performedBy,
                            IpAddress = ipAddress,
                            UserAgent = userAgent,
                            Note = note,
                            CreatedAt = now
                        });
                    }

                    await _dbContext.AuditLogs.AddRangeAsync(auditLogs);
                    await _dbContext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    _logger.LogInformation("Bulk recovery complete. Count={Count} RecoveredBy={User}", anomalies.Count, performedBy);
                    return AnomalyResponse.Ok($"Recovered {anomalies.Count} bookings.", anomalies.Count);
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, "Bulk recovery failed. Count={Count}", distinctIds.Count);
                    return AnomalyResponse.Error("Bulk recovery failed.");
                }
            });
        }

        private async Task TrySendRecoverySmsAuditAsync(Booking booking, string performedBy, string? ipAddress, string? userAgent)
        {
            if (string.IsNullOrWhiteSpace(booking.PhoneNumber))
            {
                return;
            }

            try
            {
                var smsResult = await _smsNotificationService.SendRecoveryNotificationAsync(booking.PhoneNumber, booking.ReferenceNo, booking.Route);
                if (!smsResult.Attempted)
                {
                    return;
                }

                var maskedPhone = MaskPhoneNumber(booking.PhoneNumber);
                var detail = JsonSerializer.Serialize(new
                {
                    phone = maskedPhone,
                    referenceNo = booking.ReferenceNo,
                    route = booking.Route,
                    error = smsResult.Error
                });

                await _dbContext.AuditLogs.AddAsync(new AuditLog
                {
                    Action = smsResult.Success ? "SMS_NOTIFICATION_SENT" : "SMS_NOTIFICATION_FAILED",
                    EntityType = "Booking",
                    EntityId = booking.Id,
                    PerformedBy = performedBy,
                    IpAddress = ipAddress,
                    UserAgent = userAgent,
                    Note = smsResult.Success ? "Recovery SMS sent." : "Recovery SMS failed.",
                    Detail = detail,
                    CreatedAt = DateTime.UtcNow
                });
                await _dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Post-recovery SMS audit write failed for booking {BookingId}", booking.Id);
            }
        }

        private static string MaskPhoneNumber(string phoneNumber)
        {
            var digits = new string(phoneNumber.Where(char.IsDigit).ToArray());
            if (digits.Length >= 10)
            {
                return $"{digits.Substring(0, 2)}X-XXX-XX{digits.Substring(digits.Length - 2)}";
            }

            if (digits.Length >= 4)
            {
                return $"{digits.Substring(0, 2)}XX{digits.Substring(digits.Length - 2)}";
            }

            return "***";
        }
    }

    public class AnomalyResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int StatusCode { get; set; }
        public int? AffectedCount { get; set; }

        public static AnomalyResponse Ok(string message, int? affectedCount = null) => new AnomalyResponse { Success = true, Message = message, StatusCode = 200, AffectedCount = affectedCount };
        public static AnomalyResponse Fail(string message) => new AnomalyResponse { Success = false, Message = message, StatusCode = 400 };
        public static AnomalyResponse Unprocessable(string message) => new AnomalyResponse { Success = false, Message = message, StatusCode = 422 };
        public static AnomalyResponse Conflict(string message) => new AnomalyResponse { Success = false, Message = message, StatusCode = 409 };
        public static AnomalyResponse Error(string message) => new AnomalyResponse { Success = false, Message = message, StatusCode = 500 };
    }
}
