using BookingGuardian.Data;
using BookingGuardian.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookingGuardian.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class HeartbeatController : ControllerBase
    {
        private readonly BookingDbContext _dbContext;
        private readonly ILogger<HeartbeatController> _logger;
        private readonly IConfiguration _configuration;

        public HeartbeatController(BookingDbContext dbContext, ILogger<HeartbeatController> logger, IConfiguration configuration)
        {
            _dbContext = dbContext;
            _logger = logger;
            _configuration = configuration;
        }

        [HttpGet]
        [AllowAnonymous] // Needed for Pre-Login Pulse
        public async Task<IActionResult> GetHeartbeat()
        {
            var openCount = await _dbContext.Anomalies.CountAsync(a => a.Status == "OPEN");
            
            // Performance Audit: Only pull health checks from the last 48 hours to determine current pulse
            var runTime = DateTime.UtcNow;
            var pulseWindow = runTime.AddHours(-48);
            
            // Get Configured Endpoints to avoid "Ghost" status from old/renamed records
            var configEndpoints = _configuration.GetSection("HealthCheck:Endpoints")
                .GetChildren()
                .Select(c => c.GetValue<string>("Name"))
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();

            if (!configEndpoints.Any()) configEndpoints.Add("Payment Gateway");

            var endpointHealthHistory = await _dbContext.EndpointHealths
                .Where(e => e.CheckedAt >= pulseWindow && configEndpoints.Contains(e.Name))
                .OrderByDescending(e => e.CheckedAt)
                .ThenByDescending(e => e.Id)
                .ToListAsync();

            var currentHealth = endpointHealthHistory
                .GroupBy(e => e.Name)
                .Select(g => g.First())
                .ToList();

            // NEW: Simulator Priority Logic
            // If any record in the current window is a "Manual Simulator Toggle", 
            // the LATEST manual toggle takes total control of the system status.
            var latestManual = endpointHealthHistory
                .FirstOrDefault(e => e.CheckDetails == "Manual Simulator Toggle");

            bool outageDetected;
            if (latestManual != null)
            {
                outageDetected = latestManual.Status == "DOWN";
                _logger.LogInformation("Dashboard: Simulator Override Active. Status: {Status}", latestManual.Status);
            }
            else
            {
                outageDetected = currentHealth.Any(h => h.Status == "DOWN");
            }

            var status = "SAFE";
            var pulseColor = "green";
            var message = "Your bookings are protected.";

            if (outageDetected)
            {
                status = "CRITICAL";
                pulseColor = "red";
                message = "Active connection issues detected. TicketGuard is intervening.";
            }
            else if (openCount > 0)
            {
                status = "WARNING";
                pulseColor = "amber";
                message = $"Attention required for {openCount} pending bookings.";
            }

            // Narrative Summary for the \"Magic Timeline\"
            var today = DateTime.UtcNow.Date;
            var recentActions = await _dbContext.AuditLogs
                .Where(l => l.CreatedAt >= today.AddDays(-7))
                .OrderByDescending(l => l.CreatedAt)
                .Take(15)
                .ToListAsync();

            var recoveredToday = await _dbContext.Anomalies
                .CountAsync(a => a.Status == "RESOLVED" && a.ResolvedAt >= today);
            
            var revenueToday = await _dbContext.Anomalies
                .Include(a => a.Booking)
                .Where(a => a.Status == "RESOLVED" && a.ResolvedAt >= today)
                .SumAsync(a => (decimal?)a.Booking.Amount) ?? 0;

            var revenueAtRisk = await _dbContext.Anomalies
                .Include(a => a.Booking)
                .Where(a => a.Status == "OPEN")
                .SumAsync(a => (decimal?)a.Booking.Amount) ?? 0;

            return Ok(new
            {
                status,
                pulseColor,
                message,
                openCount,
                stats = new {
                    recoveredToday,
                    revenueToday = $"฿{revenueToday:N0}",
                    revenueAtRisk = $"฿{revenueAtRisk:N0}"
                },
                timeline = TranslateLogsToNarrative(recentActions)
            });
        }

        private List<string> TranslateLogsToNarrative(List<AuditLog> logs)
        {
            var narrative = new List<string>();
            foreach (var log in logs)
            {
                string time = log.CreatedAt.ToString("HH:mm");
                string action = log.Action;
                
                if (action == "RECOVER")
                {
                    narrative.Add($"{time} — Automatically recovered booking. Flow restored.");
                }
                else if (action == "SMS_NOTIFICATION_SENT")
                {
                    narrative.Add($"{time} — SMS alert delivered to terminal management.");
                }
                else if (action == "SYSTEM_INIT")
                {
                    narrative.Add($"{time} — System heartbeat online. All nodes active.");
                }
                else if (action == "IGNORE")
                {
                    narrative.Add($"{time} — Minor anomaly resolved by operator.");
                }
            }
            return narrative;
        }
    }
}
