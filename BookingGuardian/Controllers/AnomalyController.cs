using BookingGuardian.Data;
using BookingGuardian.Models;
using BookingGuardian.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BookingGuardian.Controllers
{
    [Authorize(Policy = "SupportOrAdmin")]
    [Route("api/[controller]")]
    [ApiController]
    public class AnomalyController : ControllerBase
    {
        private readonly BookingDbContext _dbContext;
        private readonly IBookingService _bookingService;
        private readonly ILogger<AnomalyController> _logger;

        public AnomalyController(BookingDbContext dbContext, IBookingService bookingService, ILogger<AnomalyController> logger)
        {
            _dbContext = dbContext;
            _bookingService = bookingService;
            _logger = logger;
        }

        /// <summary>
        /// Retrieves a paginated list of anomalies with optional filters.
        /// </summary>
        /// <param name="status">Optional status filter (e.g., OPEN, RESOLVED, IGNORED).</param>
        /// <param name="page">The page number for pagination.</param>
        /// <param name="pageSize">Number of items per page.</param>
        /// <param name="todayOnly">If true, only return anomalies resolved today.</param>
        /// <returns>A paged response containing anomaly data and metadata.</returns>
        [HttpGet]
        public async Task<IActionResult> GetAnomalies(
            [FromQuery] string? status = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] bool todayOnly = false,
            [FromQuery] int? outageEndpointHealthId = null)
        {
            _logger.LogInformation("GetAnomalies called. Status: {Status}, PageSize: {PageSize}, TodayOnly: {TodayOnly}", status ?? "NULL", pageSize, todayOnly);

            var query = _dbContext.Anomalies
                .Include(a => a.Booking)
                .Include(a => a.EndpointHealth)
                .AsQueryable();

            if (!string.IsNullOrEmpty(status) && !status.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                var targetStatus = status.ToUpper();
                query = query.Where(a => a.Status == targetStatus);
            }

            if (todayOnly)
            {
                var today = DateTime.UtcNow.Date;
                query = query.Where(a => a.ResolvedAt != null && a.ResolvedAt >= today);
            }

            object? outageContext = null;
            if (outageEndpointHealthId.HasValue)
            {
                var context = await ResolveOutageContextAsync(outageEndpointHealthId.Value);
                if (context == null)
                {
                    return Ok(new { data = Array.Empty<object>(), total = 0, page, pageSize, outageContext = (object?)null });
                }

                query = query.Where(a =>
                    a.EndpointHealthId != null &&
                    a.EndpointHealth != null &&
                    a.EndpointHealth.Name == context.EndpointName &&
                    a.DetectedAt >= context.OutageStart);

                outageContext = new
                {
                    endpointName = context.EndpointName,
                    outageStart = context.OutageStart,
                    outageEndpointHealthId = context.OutageEndpointHealthId
                };
            }

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(a => a.DetectedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            _logger.LogInformation("Query returned {ItemCount} of {TotalCount} total anomalies.", items.Count, total);

            var now = DateTime.UtcNow;
            var data = items.Select(a => new
            {
                a.Id,
                a.DetectedAt,
                MinutesSinceDetected = (int)(now - a.DetectedAt).TotalMinutes,
                a.Status,
                bookingReference = a.Booking?.ReferenceNo,
                customerName = a.Booking?.CustomerName,
                route = a.Booking?.Route,
                operatorName = a.Booking?.OperatorName,
                passengerCount = a.Booking?.PassengerCount,
                travelDate = a.Booking?.TravelDate,
                amount = a.Booking?.Amount,
                paymentAt = a.Booking?.PaymentAt,
                endpointHealthId = a.EndpointHealthId,
                cause = a.EndpointHealth != null ? $"{a.EndpointHealth.Name} DOWN" : "Platform Delay / Other"
            }).ToList();

            return Ok(new { data, total, page, pageSize, outageContext });
        }

        [HttpGet("trend")]
        public async Task<IActionResult> GetTrend([FromQuery] int days = 7)
        {
            if (days < 1) days = 1;
            if (days > 30) days = 30;

            var startDate = DateTime.UtcNow.Date.AddDays(-(days - 1));
            var grouped = await _dbContext.Anomalies
                .Where(a => a.DetectedAt >= startDate)
                .GroupBy(a => a.DetectedAt.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .ToListAsync();

            var trend = Enumerable.Range(0, days)
                .Select(offset => startDate.AddDays(offset))
                .Select(d => new
                {
                    date = d.ToString("yyyy-MM-dd"),
                    count = grouped.FirstOrDefault(x => x.Date == d)?.Count ?? 0
                })
                .ToList();

            return Ok(new { data = trend });
        }

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary()
        {
            var totalOpen = await _dbContext.Anomalies.CountAsync(a => a.Status == "OPEN");
            var resolvedAnomalies = await _dbContext.Anomalies
                .Include(a => a.Booking)
                .Where(a => a.Status == "RESOLVED")
                .ToListAsync();

            var savedBookings = resolvedAnomalies.Count;
            var recoveredRevenue = resolvedAnomalies.Sum(a => a.Booking.Amount);
            var autoRecovered = resolvedAnomalies.Count(a => a.ResolvedBy == "SYSTEM_GUARDIAN");

            return Ok(new
            {
                openIssues = totalOpen,
                savedBookings,
                recoveredRevenue,
                autoRecovered
            });
        }

        /// <summary>
        /// Recovers an anomaly, marking the booking as CONFIRMED.
        /// </summary>
        /// <param name="id">The anomaly unique identifier.</param>
        /// <param name="request">Request containing a mandatory note (min 10 chars).</param>
        /// <returns>A status response indicating success or failure reasons.</returns>
        [HttpPost("{id}/recover")]
        [Authorize(Policy = "AdminOnly")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Recover(int id, [FromBody] NoteRequest request)
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
            var ipAddress = GetClientIpAddress();
            var userAgent = GetUserAgent();
            var result = await _bookingService.RecoverAnomalyAsync(id, request.Note, userEmail, ipAddress, userAgent);

            if (result.Success) return Ok(result);
            return StatusCode(result.StatusCode, result);
        }

        [HttpPost("bulk-recover")]
        [Authorize(Policy = "AdminOnly")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkRecover([FromBody] BulkRecoverRequest request)
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
            var ipAddress = GetClientIpAddress();
            var userAgent = GetUserAgent();

            List<int> targetAnomalyIds;
            if (request.AnomalyIds?.Any() == true)
            {
                targetAnomalyIds = request.AnomalyIds.Distinct().ToList();
            }
            else if (request.OutageEndpointHealthId.HasValue)
            {
                var context = await ResolveOutageContextAsync(request.OutageEndpointHealthId.Value);
                if (context == null)
                {
                    return BadRequest(new { success = false, message = "Outage context not found." });
                }

                targetAnomalyIds = await _dbContext.Anomalies
                    .Include(a => a.EndpointHealth)
                    .Where(a =>
                        a.Status == "OPEN" &&
                        a.EndpointHealthId != null &&
                        a.EndpointHealth != null &&
                        a.EndpointHealth.Name == context.EndpointName &&
                        a.DetectedAt >= context.OutageStart)
                    .Select(a => a.Id)
                    .ToListAsync();
            }
            else
            {
                return BadRequest(new { success = false, message = "Provide anomalyIds or outageEndpointHealthId." });
            }

            var result = await _bookingService.BulkRecoverAnomaliesAsync(targetAnomalyIds, request.Note, userEmail, ipAddress, userAgent);
            if (result.Success) return Ok(result);
            return StatusCode(result.StatusCode, result);
        }

        /// <summary>
        /// Marks an anomaly as IGNORED.
        /// </summary>
        /// <param name="id">The anomaly unique identifier.</param>
        /// <param name="request">Request containing a mandatory note (min 10 chars).</param>
        /// <returns>A status response indicating success or failure reasons.</returns>
        [HttpPost("{id}/ignore")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Ignore(int id, [FromBody] NoteRequest request)
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
            var ipAddress = GetClientIpAddress();
            var userAgent = GetUserAgent();
            var result = await _bookingService.IgnoreAnomalyAsync(id, request.Note, userEmail, ipAddress, userAgent);

            if (result.Success) return Ok(result);
            return StatusCode(result.StatusCode, result);
        }

        public class NoteRequest
        {
            public string Note { get; set; } = string.Empty;
        }

        public class BulkRecoverRequest
        {
            public List<int>? AnomalyIds { get; set; }
            public int? OutageEndpointHealthId { get; set; }
            public string Note { get; set; } = string.Empty;
        }

        private async Task<OutageContext?> ResolveOutageContextAsync(int outageEndpointHealthId)
        {
            var anchor = await _dbContext.EndpointHealths.FirstOrDefaultAsync(e => e.Id == outageEndpointHealthId);
            if (anchor == null)
            {
                return null;
            }

            var endpointHistory = await _dbContext.EndpointHealths
                .Where(e => e.Name == anchor.Name && e.CheckedAt <= anchor.CheckedAt)
                .OrderByDescending(e => e.CheckedAt)
                .ToListAsync();

            if (!endpointHistory.Any() || endpointHistory[0].Status != "DOWN")
            {
                return null;
            }

            var outageStart = endpointHistory[0].CheckedAt;
            foreach (var item in endpointHistory)
            {
                if (item.Status != "DOWN")
                {
                    break;
                }
                outageStart = item.CheckedAt;
            }

            return new OutageContext(anchor.Name, outageStart, anchor.Id);
        }

        private string? GetClientIpAddress()
        {
            var forwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwardedFor))
            {
                return forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            }

            var realIp = Request.Headers["X-Real-IP"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(realIp))
            {
                return realIp;
            }

            return HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString();
        }

        private string GetUserAgent()
        {
            var userAgent = Request.Headers.UserAgent.ToString();
            return string.IsNullOrWhiteSpace(userAgent) ? "Unknown" : userAgent;
        }


        private sealed record OutageContext(string EndpointName, DateTime OutageStart, int OutageEndpointHealthId);
    }
}
