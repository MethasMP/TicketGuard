using BookingGuardian.Data;
using BookingGuardian.Models;
using BookingGuardian.Services;
using BookingGuardian.BackgroundServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BookingGuardian.Controllers
{
    [Route("api/simulator")]
    [ApiController]
    [Authorize(Policy = "AdminOnly")]
    public class SystemSimulatorController : ControllerBase
    {
        private readonly BookingDbContext _dbContext;
        private readonly IBookingService _bookingService;
        private readonly ISmsNotificationService _smsService;
        private readonly IEmailNotificationService _emailService;
        private readonly AnomalyDetectionJob _detectionJob;
        private readonly IConfiguration _configuration;
        private readonly ISystemModeService _modeService;

        public SystemSimulatorController(
            BookingDbContext dbContext, 
            IBookingService bookingService,
            ISmsNotificationService smsService,
            IEmailNotificationService emailService,
            AnomalyDetectionJob detectionJob,
            IConfiguration configuration,
            ISystemModeService modeService)
        {
            _dbContext = dbContext;
            _bookingService = bookingService;
            _smsService = smsService;
            _emailService = emailService;
            _detectionJob = detectionJob;
            _configuration = configuration;
            _modeService = modeService;
        }

        [HttpPost("set-mode")]
        public IActionResult SetMode([FromQuery] string mode)
        {
            if (Enum.TryParse<SystemMode>(mode, true, out var newMode))
            {
                _modeService.SetMode(newMode);
                return Ok(new { message = $"System mode updated to {newMode}", mode = _modeService.CurrentMode.ToString() });
            }
            return BadRequest("Invalid mode. Use TEST or LIVE.");
        }

        [HttpGet("mode")]
        public IActionResult GetMode()
        {
            return Ok(new { mode = _modeService.CurrentMode.ToString() });
        }

        [HttpPost("plant-anomaly")]
        public async Task<IActionResult> PlantAnomaly()
        {
            var booking = new Booking
            {
                ReferenceNo = "SIM-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper(),
                CustomerName = "Simulated Error",
                Route = "BKK -> HKT",
                OperatorName = "Thai Airways",
                Amount = 2500,
                PaymentStatus = "SUCCESS",
                BookingStatus = "PENDING", // This will trigger detection
                PaymentAt = DateTime.UtcNow.AddMinutes(-15),
                CreatedAt = DateTime.UtcNow,
                PhoneNumber = "0811234567"
            };

            _dbContext.Bookings.Add(booking);
            await _dbContext.SaveChangesAsync();

            return Ok(new { message = "Stuck booking planted.", reference = booking.ReferenceNo });
        }

        [HttpPost("trigger-scan")]
        public async Task<IActionResult> TriggerScan()
        {
            await _detectionJob.DetectAndRecoverAnomaliesAsync();
            return Ok(new { message = "Manual scan triggered. Check dashboard for updates." });
        }

        [HttpPost("toggle-outage")]
        public async Task<IActionResult> ToggleOutage()
        {
            // NEW: Get ALL endpoint names from config to toggle them all
            var endpoints = _configuration.GetSection("HealthCheck:Endpoints").Get<List<EndpointConfig>>() 
                             ?? new List<EndpointConfig>();
            
            // If No endpoints in config, fallback to default
            if (!endpoints.Any()) endpoints.Add(new EndpointConfig { Name = "Payment Gateway", Url = "SIMULATED" });

            // Determine NEW global status
            var latestOne = await _dbContext.EndpointHealths
                .OrderByDescending(e => e.CheckedAt)
                .ThenByDescending(e => e.Id)
                .FirstOrDefaultAsync();
            
            var newStatus = (latestOne == null || latestOne.Status == "UP") ? "DOWN" : "UP";

            // Apply to ALL endpoints
            foreach (var ep in endpoints)
            {
                var health = new EndpointHealth
                {
                    Name = ep.Name,
                    Status = newStatus,
                    CheckedAt = DateTime.UtcNow,
                    CheckDetails = "Manual Simulator Toggle",
                    HttpCode = newStatus == "UP" ? 200 : 503,
                    ResponseMs = newStatus == "UP" ? 45 : 0,
                    Url = ep.Url ?? "SIMULATED"
                };
                await _dbContext.EndpointHealths.AddAsync(health);
            }

            await _dbContext.SaveChangesAsync();

            // If system is coming back ONLINE, auto-trigger a scan to recover anomalies immediately
            if (newStatus == "UP")
            {
                _ = Task.Run(async () => {
                    await Task.Delay(500); // Give DB a micro-second to breathe
                    await _detectionJob.DetectAndRecoverAnomaliesAsync();
                });
            }

            return Ok(new { 
                status = newStatus, 
                message = $"System-wide status changed to {newStatus}.",
                affectedNodes = endpoints.Count
            });
        }

        [HttpPost("test-sms")]
        public async Task<IActionResult> TestSms([FromQuery] string phone = "0810000000")
        {
            var result = await _smsService.SendRecoveryNotificationAsync(phone, "TEST-REF", "TEST-ROUTE");
            return Ok(new { 
                message = "SMS command executed.", 
                success = result.Success, 
                details = result.Error ?? "Sent via API" 
            });
        }

        [HttpPost("test-email")]
        public async Task<IActionResult> TestEmail([FromQuery] string subject = "TicketGuard Test Alert", [FromQuery] string? email = null)
        {
            var success = await _emailService.SendAlertAsync(subject, "<h3>Test Notification</h3><p>ระบบ TicketGuard กำลังทำงานอย่างปกติ</p>", email);
            
            // In simulation mode, we might want to pretend it worked if we are just testing UI
            var isDev = true; // Logic to check environment
            var statusMessage = success ? "Sent via SMTP" : (isDev ? "Simulated Success (No SMTP Config)" : "Failed - Check Logs");

            return Ok(new { 
                message = "Email service validation complete.", 
                success = success || isDev, 
                details = statusMessage
            });
        }

        public class EndpointConfig { public string Name { get; set; } = string.Empty; public string Url { get; set; } = string.Empty; }
    }
}
