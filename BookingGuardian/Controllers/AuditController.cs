using System.Text;
using BookingGuardian.Data;
using BookingGuardian.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookingGuardian.Controllers
{
    [AllowAnonymous]
    [Authorize(Policy = "SupportOrAdmin")]
    [Route("audit")]
    public class AuditController : Controller
    {
        private readonly BookingDbContext _dbContext;

        public AuditController(BookingDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index([FromQuery] string? action, [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            if (page < 1) page = 1;
            if (pageSize < 5) pageSize = 5;
            if (pageSize > 100) pageSize = 100;

            var query = _dbContext.AuditLogs.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(action))
            {
                query = query.Where(x => x.Action == action.ToUpper());
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                var lowerQ = q.ToLower();
                query = query.Where(x => 
                    x.PerformedBy.ToLower().Contains(lowerQ) ||
                    (x.Note != null && x.Note.ToLower().Contains(lowerQ)) ||
                    x.Action.ToLower().Contains(lowerQ) ||
                    x.EntityType.ToLower().Contains(lowerQ) ||
                    (x.IpAddress != null && x.IpAddress.ToLower().Contains(lowerQ)) ||
                    (x.EntityType == "Booking" && _dbContext.Bookings.Any(b => b.Id == x.EntityId && (b.ReferenceNo.ToLower().Contains(lowerQ) || b.CustomerName.ToLower().Contains(lowerQ)))) ||
                    (x.EntityType == "Anomaly" && _dbContext.Anomalies.Any(a => a.Id == x.EntityId && (a.Booking.ReferenceNo.ToLower().Contains(lowerQ) || a.Booking.CustomerName.ToLower().Contains(lowerQ))))
                );
            }

            var total = await query.CountAsync();
            var logs = await query
                .OrderByDescending(x => x.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // DATA ENRICHMENT PHASE
            // Identify related bookings and anomalies to show references
            var bookingIds = logs.Where(l => l.EntityType == "Booking").Select(l => l.EntityId).Distinct().ToList();
            var anomalyIds = logs.Where(l => l.EntityType == "Anomaly").Select(l => l.EntityId).Distinct().ToList();

            var relevantBookings = await _dbContext.Bookings
                .AsNoTracking()
                .Where(b => bookingIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id);

            var relevantAnomalies = await _dbContext.Anomalies
                .AsNoTracking()
                .Include(a => a.Booking)
                .Where(a => anomalyIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id);

            var items = logs.Select(log => {
                var vm = new AuditLogViewModel
                {
                    Id = log.Id,
                    Action = log.Action,
                    EntityType = log.EntityType,
                    EntityId = log.EntityId,
                    PerformedBy = log.PerformedBy,
                    IpAddress = log.IpAddress,
                    UserAgent = log.UserAgent,
                    Note = log.Note,
                    Detail = log.Detail,
                    CreatedAt = log.CreatedAt
                };

                if (log.EntityType == "Booking" && relevantBookings.TryGetValue(log.EntityId, out var b))
                {
                    vm.ReferenceNo = b.ReferenceNo;
                    vm.CustomerName = b.CustomerName;
                    vm.Amount = b.Amount;
                }
                else if (log.EntityType == "Anomaly" && relevantAnomalies.TryGetValue(log.EntityId, out var a))
                {
                    vm.ReferenceNo = a.Booking.ReferenceNo;
                    vm.CustomerName = a.Booking.CustomerName;
                    vm.Amount = a.Booking.Amount;
                }

                return vm;
            }).ToList();

            ViewBag.Total = total;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.Action = action ?? string.Empty;
            ViewBag.Query = q ?? string.Empty;
            return View(items);
        }

        [HttpGet("export")]
        public async Task<IActionResult> ExportCsv([FromQuery] string? action, [FromQuery] string? q)
        {
            var query = _dbContext.AuditLogs.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(action))
            {
                query = query.Where(x => x.Action == action.ToUpper());
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                var lowerQ = q.ToLower();
                query = query.Where(x => 
                    x.PerformedBy.ToLower().Contains(lowerQ) ||
                    (x.Note != null && x.Note.ToLower().Contains(lowerQ)) ||
                    x.Action.ToLower().Contains(lowerQ) ||
                    x.EntityType.ToLower().Contains(lowerQ) ||
                    (x.IpAddress != null && x.IpAddress.ToLower().Contains(lowerQ)) ||
                    (x.EntityType == "Booking" && _dbContext.Bookings.Any(b => b.Id == x.EntityId && (b.ReferenceNo.ToLower().Contains(lowerQ) || b.CustomerName.ToLower().Contains(lowerQ)))) ||
                    (x.EntityType == "Anomaly" && _dbContext.Anomalies.Any(a => a.Id == x.EntityId && (a.Booking.ReferenceNo.ToLower().Contains(lowerQ) || a.Booking.CustomerName.ToLower().Contains(lowerQ))))
                );
            }

            var logs = await query
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();

            // Enrichment for export
            var bookingIds = logs.Where(l => l.EntityType == "Booking").Select(l => l.EntityId).Distinct().ToList();
            var anomalyIds = logs.Where(l => l.EntityType == "Anomaly").Select(l => l.EntityId).Distinct().ToList();

            var relevantBookings = await _dbContext.Bookings
                .AsNoTracking()
                .Where(b => bookingIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id);

            var relevantAnomalies = await _dbContext.Anomalies
                .AsNoTracking()
                .Include(a => a.Booking)
                .Where(a => anomalyIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id);

            var csv = new StringBuilder();
            csv.AppendLine("id,timestamp_utc,action,entity_type,reference_no,customer,amount,performed_by,ip_address,user_agent,note");

            foreach (var log in logs)
            {
                string? refNo = null;
                string? cust = null;
                string? amt = null;

                if (log.EntityType == "Booking" && relevantBookings.TryGetValue(log.EntityId, out var b))
                {
                    refNo = b.ReferenceNo;
                    cust = b.CustomerName;
                    amt = b.Amount.ToString("F2");
                }
                else if (log.EntityType == "Anomaly" && relevantAnomalies.TryGetValue(log.EntityId, out var a))
                {
                    refNo = a.Booking.ReferenceNo;
                    cust = a.Booking.CustomerName;
                    amt = a.Booking.Amount.ToString("F2");
                }

                csv.AppendLine(string.Join(",",
                    log.Id,
                    log.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    EscapeCsv(log.Action),
                    EscapeCsv(log.EntityType),
                    EscapeCsv(refNo ?? $"ID:{log.EntityId}"),
                    EscapeCsv(cust),
                    amt ?? "0.00",
                    EscapeCsv(log.PerformedBy),
                    EscapeCsv(log.IpAddress),
                    EscapeCsv(log.UserAgent),
                    EscapeCsv(log.Note)
                ));
            }

            // Prefix UTF-8 BOM so Excel opens UTF-8 CSV reliably.
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
            var fileName = $"audit-logs-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
            return File(bytes, "text/csv; charset=utf-8", fileName);
        }

        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var escaped = value.Replace("\"", "\"\"");
            return $"\"{escaped}\"";
        }
    }
}
